using AutoGithubChangelogPoster.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AutoGithubChangelogPoster.Functions;

public class NotifierFunction
{
    private const string StateFileName = "github-changelog-last-processed-id.txt";
    private const int MaxPostedIdHistory = 200;

    private enum PostingMode
    {
        Thread,
        Single,
        Premium
    }

    private readonly ILogger<NotifierFunction> _logger;
    private readonly FeedService _feedService;
    private readonly TwitterApiClient _twitterApiClient;
    private readonly MastodonApiClient _mastodonApiClient;
    private readonly TweetFormatterService _tweetFormatterService;
    private readonly StateTrackingService _stateTrackingService;

    public NotifierFunction(
        ILogger<NotifierFunction> logger,
        FeedService feedService,
        TwitterApiClient twitterApiClient,
        MastodonApiClient mastodonApiClient,
        TweetFormatterService tweetFormatterService,
        StateTrackingService stateTrackingService)
    {
        _logger = logger;
        _feedService = feedService;
        _twitterApiClient = twitterApiClient;
        _mastodonApiClient = mastodonApiClient;
        _tweetFormatterService = tweetFormatterService;
        _stateTrackingService = stateTrackingService;
    }

    [Function("Notifier")]
    public async Task Run([TimerTrigger("0 */15 * * * *")] TimerInfo timerInfo)
    {
        _logger.LogInformation("Notifier started at: {Time}", DateTime.UtcNow);

        if (!_twitterApiClient.IsConfigured && !_mastodonApiClient.IsConfigured)
        {
            _logger.LogWarning("Neither X nor Mastodon credentials are configured. Skipping.");
            return;
        }

        if (!_twitterApiClient.IsConfigured)
        {
            _logger.LogInformation("X credentials are not configured. Posting to Mastodon only.");
        }

        if (!_mastodonApiClient.IsConfigured)
        {
            _logger.LogInformation("Mastodon credentials are not configured. Posting to X only.");
        }

        try
        {
            var entries = await _feedService.GetEntriesAsync();
            if (entries.Count == 0)
            {
                _logger.LogInformation("No GitHub changelog entries found.");
                return;
            }

            var state = await _stateTrackingService.GetStateAsync(
                StateFileName,
                PostingState.FromLegacyId);
            var newEntries = GetNewEntries(entries, state);
            if (newEntries.Count == 0)
            {
                _logger.LogInformation("No new GitHub changelog entries to process.");
                return;
            }

            _logger.LogInformation("Found {Count} new GitHub changelog entries.", newEntries.Count);
            
            var postingMode = GetPostingMode();
            foreach (var entry in newEntries.OrderBy(entry => entry.Updated))
            {
                bool success;

                if (postingMode == PostingMode.Premium)
                {
                    var post = await _tweetFormatterService.FormatPremiumPostForXAsync(entry, useAi: true);
                    LogPremiumPost(entry, post);
                    var xSuccess = _twitterApiClient.IsConfigured && await _twitterApiClient.PostTweetAsync(post);
                    var mastodonSuccess = _mastodonApiClient.IsConfigured && await _mastodonApiClient.PostAsync(post);
                    success = xSuccess || mastodonSuccess;
                }
                else if (postingMode == PostingMode.Single)
                {
                    var post = await _tweetFormatterService.FormatSinglePostForXAsync(entry, useAi: true);
                    LogSinglePost(entry, post);
                    var xSuccess = _twitterApiClient.IsConfigured && await _twitterApiClient.PostTweetAsync(post);
                    var mastodonSuccess = _mastodonApiClient.IsConfigured && await _mastodonApiClient.PostAsync(post);
                    success = xSuccess || mastodonSuccess;
                }
                else
                {
                    var thread = await _tweetFormatterService.FormatThreadForXAsync(entry, useAi: true);
                    LogThread(entry, thread);
                    var xSuccess = _twitterApiClient.IsConfigured && await _twitterApiClient.PostTweetThreadAsync(thread);
                    var mastodonSuccess = _mastodonApiClient.IsConfigured && await _mastodonApiClient.PostThreadAsync(thread);
                    success = xSuccess || mastodonSuccess;
                }

                if (success)
                {
                    state ??= new PostingState();
                    state.RecordPostedId(entry.Id, MaxPostedIdHistory);
                    await _stateTrackingService.SetStateAsync(state, StateFileName);
                    _logger.LogInformation("Successfully posted GitHub changelog entry: {Title}", entry.Title);
                }
                else
                {
                    _logger.LogWarning("Failed to post GitHub changelog entry: {Title}", entry.Title);
                }

                if (newEntries.Count > 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Notifier");
        }

        _logger.LogInformation("Notifier completed at: {Time}", DateTime.UtcNow);
    }

    private List<ChangelogEntry> GetNewEntries(
        IReadOnlyList<ChangelogEntry> entries,
        PostingState? state)
    {
        var postedIds = new HashSet<string>(
            state?.PostedIds?.Where(id => !string.IsNullOrWhiteSpace(id)) ?? [],
            StringComparer.OrdinalIgnoreCase);
        var lastProcessedId = state?.LastProcessedId;

        if (string.IsNullOrWhiteSpace(lastProcessedId))
        {
            _logger.LogInformation("First GitHub changelog run detected. Processing only the most recent entry.");
            var latestUnpostedEntry = entries
                .OrderByDescending(entry => entry.Updated)
                .FirstOrDefault(entry => !postedIds.Contains(entry.Id));

            return latestUnpostedEntry == null ? [] : [latestUnpostedEntry];
        }

        var newEntries = new List<ChangelogEntry>();
        foreach (var entry in entries)
        {
            if (string.Equals(entry.Id, lastProcessedId, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (postedIds.Contains(entry.Id))
            {
                _logger.LogInformation("Skipping already-posted GitHub changelog entry: {Title}", entry.Title);
                continue;
            }

            newEntries.Add(entry);
        }

        return newEntries;
    }

    private static bool IsEnabled(string envVar)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        return bool.TryParse(value, out var enabled) && enabled;
    }

    private PostingMode GetPostingMode()
    {
        var premiumMode = IsEnabled("X_GITHUB_CHANGELOG_PREMIUM_MODE");
        var singleMode = IsEnabled("X_GITHUB_CHANGELOG_SINGLE_POST_MODE");

        if (premiumMode && singleMode)
        {
            _logger.LogWarning("Both X_GITHUB_CHANGELOG_PREMIUM_MODE and X_GITHUB_CHANGELOG_SINGLE_POST_MODE are enabled. Premium mode will take precedence.");
        }

        if (premiumMode)
        {
            return PostingMode.Premium;
        }

        if (singleMode)
        {
            return PostingMode.Single;
        }

        return PostingMode.Thread;
    }

    private void LogSinglePost(ChangelogEntry entry, SocialMediaPost post)
    {
        var weightedLength = XPostLengthHelper.GetWeightedLength(post.Text);
        _logger.LogInformation(
            "GitHub changelog single post to send for {Title} (raw={RawLength}, weighted={WeightedLength}, media={MediaCount}):\n{Post}",
            entry.Title,
            post.Text.Length,
            weightedLength,
            post.MediaUrlsOrEmpty.Count,
            post.Text);

        if (post.MediaUrlsOrEmpty.Count > 0)
        {
            _logger.LogInformation(
                "GitHub changelog single post media for {Title}: {MediaUrls}",
                entry.Title,
                string.Join(", ", post.MediaUrlsOrEmpty));
        }
    }

    private void LogPremiumPost(ChangelogEntry entry, SocialMediaPost post)
    {
        var weightedLength = XPostLengthHelper.GetWeightedLength(post.Text);
        _logger.LogInformation(
            "GitHub changelog premium post to send for {Title} (raw={RawLength}, weighted={WeightedLength}, media={MediaCount}):\n{Post}",
            entry.Title,
            post.Text.Length,
            weightedLength,
            post.MediaUrlsOrEmpty.Count,
            post.Text);

        if (post.MediaUrlsOrEmpty.Count > 0)
        {
            _logger.LogInformation(
                "GitHub changelog premium post media for {Title}: {MediaUrls}",
                entry.Title,
                string.Join(", ", post.MediaUrlsOrEmpty));
        }
    }

    private void LogThread(ChangelogEntry entry, IReadOnlyList<SocialMediaPost> thread)
    {
        var renderedThread = string.Join(
            "\n\n",
            thread.Select((post, index) =>
                $"[Post {index + 1}/{thread.Count}] (raw={post.Text.Length}, weighted={XPostLengthHelper.GetWeightedLength(post.Text)}, media={post.MediaUrlsOrEmpty.Count})\n{post.Text}"));

        _logger.LogInformation(
            "GitHub changelog thread to send for {Title} ({Count} post(s)):\n{Thread}",
            entry.Title,
            thread.Count,
            renderedThread);

        var firstPostMedia = thread.FirstOrDefault()?.MediaUrlsOrEmpty ?? [];
        if (firstPostMedia.Count > 0)
        {
            _logger.LogInformation(
                "GitHub changelog thread first-post media for {Title}: {MediaUrls}",
                entry.Title,
                string.Join(", ", firstPostMedia));
        }
    }

    private sealed class PostingState
    {
        public string? LastProcessedId { get; set; }
        public List<string> PostedIds { get; set; } = [];

        public static PostingState? FromLegacyId(string? legacyId)
        {
            if (string.IsNullOrWhiteSpace(legacyId))
            {
                return null;
            }

            var trimmedId = legacyId.Trim();
            return new PostingState
            {
                LastProcessedId = trimmedId,
                PostedIds = [trimmedId]
            };
        }

        public void RecordPostedId(string id, int maxPostedIdHistory)
        {
            LastProcessedId = id;
            PostedIds = PostedIds
                .Where(existingId => !string.IsNullOrWhiteSpace(existingId))
                .Where(existingId => !string.Equals(existingId, id, StringComparison.OrdinalIgnoreCase))
                .Append(id)
                .TakeLast(maxPostedIdHistory)
                .ToList();
        }
    }
}
