using System.Net;
using System.Net.Sockets;
using AutoGithubChangelogPoster.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
	.ConfigureFunctionsWebApplication()
	.ConfigureServices(services =>
	{
		services.AddHttpClient();

		// Named HttpClient used exclusively for downloading external media.
		// The SocketsHttpHandler ConnectCallback performs DNS resolution in-process so
		// that the resolved IP can be inspected before any connection is established,
		// preventing DNS-rebinding SSRF attacks. Redirects are capped at 3.
		services.AddHttpClient("MediaDownload")
			.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
			{
				MaxAutomaticRedirections = 3,
				AllowAutoRedirect = true,
				ConnectCallback = async (context, cancellationToken) =>
				{
					var addresses = await Dns.GetHostAddressesAsync(
						context.DnsEndPoint.Host, cancellationToken);

					foreach (var ip in addresses)
					{
						if (MediaSsrfGuard.IsPrivateOrReservedAddress(ip))
						{
							throw new InvalidOperationException(
								$"Media download blocked: resolved IP {ip} for host " +
								$"'{context.DnsEndPoint.Host}' is a private or reserved address.");
						}
					}

					Exception? lastException = null;
					foreach (var ip in addresses)
					{
						var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
						{
							// Disable Nagle's algorithm so the TLS handshake packets are sent
							// immediately rather than coalesced, reducing connection latency.
							NoDelay = true
						};
						try
						{
							await socket.ConnectAsync(ip, context.DnsEndPoint.Port, cancellationToken);
							return new NetworkStream(socket, ownsSocket: true);
						}
						catch (Exception ex)
						{
							socket.Dispose();
							lastException = ex;
						}
					}

					throw lastException ?? new InvalidOperationException(
						$"Unable to connect to '{context.DnsEndPoint.Host}'.");
				}
			});

		var aiEndpoint = Environment.GetEnvironmentVariable("AI_ENDPOINT");
		var aiApiKey = Environment.GetEnvironmentVariable("AI_API_KEY");
		var aiModel = Environment.GetEnvironmentVariable("AI_MODEL") ?? "gpt-4o-mini";

		if (!string.IsNullOrWhiteSpace(aiEndpoint) && !string.IsNullOrWhiteSpace(aiApiKey))
		{
			services.AddSingleton(sp =>
			{
				var logger = sp.GetRequiredService<ILogger<ReleaseSummarizerService>>();
				return new ReleaseSummarizerService(logger, aiEndpoint, aiApiKey, aiModel);
			});
		}

		services.AddSingleton<FeedService>();
		services.AddSingleton<OAuth1Helper>();
		services.AddSingleton<TwitterApiClient>();
		services.AddSingleton<TweetFormatterService>();
		services.AddSingleton<StateTrackingService>();
	})
	.Build();

host.Run();
