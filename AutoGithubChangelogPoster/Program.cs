using AutoGithubChangelogPoster.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
	.ConfigureFunctionsWebApplication()
	.ConfigureServices(services =>
	{
		services.AddHttpClient();

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
