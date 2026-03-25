using AutoGithubChangelogPoster.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
	.ConfigureFunctionsWebApplication()
	.ConfigureServices(services =>
	{
		services.AddHttpClient();

		services.AddTransient<TransientRetryHandler>();

		// Named client for the GitHub changelog RSS feed.
		// Only GET requests are made, so retry on transient errors is safe.
		services.AddHttpClient("feed")
			.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30))
			.AddHttpMessageHandler<TransientRetryHandler>();

		// Named client for the X (Twitter) API.
		// POST requests must not be retried automatically to prevent duplicate tweets.
		services.AddHttpClient("twitter")
			.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

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
