using System.Net;
using AutoGithubChangelogPoster.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AutoGithubChangelogPoster.Functions;

public class TestFunction
{
    private readonly ILogger<TestFunction> _logger;
    private readonly FeedService _feedService;
    private readonly TweetFormatterService _tweetFormatterService;

    public TestFunction(
        ILogger<TestFunction> logger,
        FeedService feedService,
        TweetFormatterService tweetFormatterService)
    {
        _logger = logger;
        _feedService = feedService;
        _tweetFormatterService = tweetFormatterService;
    }

    [Function("Test")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "test")] HttpRequestData req)
    {
        var response = req.CreateResponse();

        try
        {
            var mode = (GetQueryParameter(req, "mode") ?? "single").Trim().ToLowerInvariant();
            if (mode is not ("thread" or "single" or "premium"))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Invalid mode. Use mode=thread, mode=single, or mode=premium.");
                return response;
            }

            var useAi = ParseBoolQuery(req, "ai", defaultValue: true);
            var entries = await _feedService.GetEntriesAsync();

            if (entries.Count == 0)
            {
                response.StatusCode = HttpStatusCode.NotFound;
                await response.WriteStringAsync("No changelog entries found.");
                return response;
            }

            var latestEntry = entries.OrderByDescending(entry => entry.Updated).First();
            IReadOnlyList<SocialMediaPost> posts = mode switch
            {
                "premium" => [await _tweetFormatterService.FormatPremiumPostForXAsync(latestEntry, useAi)],
                "single" => [await _tweetFormatterService.FormatSinglePostForXAsync(latestEntry, useAi)],
                _ => await _tweetFormatterService.FormatThreadForXAsync(latestEntry, useAi)
            };

            response.StatusCode = HttpStatusCode.OK;
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

            var output = $"Mode: {mode}\n";
            output += $"AI: {useAi}\n";
            output += $"Title: {latestEntry.Title}\n";
            output += $"Updated: {latestEntry.Updated:yyyy-MM-dd HH:mm:ss}\n";
            output += $"Link: {latestEntry.Link}\n";
            output += $"Labels: {string.Join(", ", latestEntry.Labels)}\n\n";
            output += $"Preview ({posts.Count} post(s)):\n";
            output += "═══════════════════════════════════════\n";

            for (var i = 0; i < posts.Count; i++)
            {
                var weightedLength = XPostLengthHelper.GetWeightedLength(posts[i].Text);
                output += $"[Post {i + 1}/{posts.Count}] (raw={posts[i].Text.Length}, weighted={weightedLength}";

                if (posts[i].MediaUrlsOrEmpty.Count > 0)
                {
                    output += $", media={posts[i].MediaUrlsOrEmpty.Count}";
                }

                output += "):\n";
                output += posts[i].Text;

                if (posts[i].MediaUrlsOrEmpty.Count > 0)
                {
                    output += $"\nMedia: {string.Join(", ", posts[i].MediaUrlsOrEmpty)}";
                }

                if (i < posts.Count - 1)
                {
                    output += "\n───────────────────────────────────────\n";
                }
            }

            await response.WriteStringAsync(output);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Test function");
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    private static string? GetQueryParameter(HttpRequestData req, string name)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        return query[name];
    }

    private static bool ParseBoolQuery(HttpRequestData req, string name, bool defaultValue)
    {
        var value = GetQueryParameter(req, name);
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}