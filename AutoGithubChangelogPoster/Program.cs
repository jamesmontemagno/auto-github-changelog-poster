using AutoTweetRss.Services;
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

		services.AddSingleton<RssFeedService>();
		services.AddSingleton<GitHubChangelogFeedService>();
		services.AddSingleton<OAuth1Helper>();
		services.AddSingleton<TwitterApiClient>();
		services.AddSingleton(sp =>
		{
			var logger = sp.GetRequiredService<ILogger<GitHubChangelogTwitterApiClient>>();
			var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
			var oauth = new OAuth1Helper("TWITTER_GITHUB_CHANGELOG_");
			return new GitHubChangelogTwitterApiClient(logger, httpFactory, oauth);
		});
		services.AddSingleton<TweetFormatterService>();
		services.AddSingleton<StateTrackingService>();
	})
	.Build();

host.Run();
