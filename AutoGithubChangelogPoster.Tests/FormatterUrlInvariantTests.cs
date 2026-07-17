using System.Text.RegularExpressions;
using AutoGithubChangelogPoster.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoGithubChangelogPoster.Tests;

public partial class FormatterUrlInvariantTests
{
    private const string CanonicalLink = "https://github.blog/changelog/2026-07-09-example-update/";
    private const string MediaUrl = "https://github.blog/wp-content/uploads/2026/07/example.png";

    [Fact]
    public async Task SinglePost_AllowsOnlyCanonicalChangelogUrl()
    {
        var formatter = CreateFormatter(singleSummary:
            """
            Devs get a cleaner workflow at https://example.com/docs and [docs](https://docs.example.com).
            HTML links like <a href="https://html.example/docs">HTML docs</a> are stripped.

            • Try github.blog/extra/details before rollout
            • See https://github.blog/changelog/2026-07-09-example-update/ for details
            """);

        var post = await formatter.FormatSinglePostForXAsync(CreateEntry(), useAi: true);

        AssertOnlyCanonicalUrl(post.Text);
        Assert.DoesNotContain("example.com", post.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("html.example", post.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a", post.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("github.blog/extra", post.Text, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(CanonicalLink, post.Text);
        Assert.Empty(post.MediaUrlsOrEmpty);
    }

    [Fact]
    public async Task Thread_AllowsOnlyCanonicalChangelogUrl_AndKeepsMediaAttachments()
    {
        var formatter = CreateFormatter(plan: new ChangelogSummaryPlan
        {
            TopThingsToKnow =
            [
                "Cleaner setup at https://example.com/setup",
                "Markdown [guide](https://docs.example.com/guide)",
                "Bare domain github.blog/not-the-entry"
            ],
            Paragraphs =
            [
                "Teams can configure the update from https://example.org/config while reading example.net/help.",
                $"The canonical link may be repeated by AI: {CanonicalLink}"
            ]
        });

        var thread = await formatter.FormatThreadForXAsync(CreateEntry(), useAi: true);

        AssertOnlyCanonicalUrl(string.Join("\n\n", thread.Select(post => post.Text)));
        Assert.DoesNotContain("example.com", string.Join("\n", thread.Select(post => post.Text)), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(CanonicalLink, thread[^1].Text);
        Assert.Contains(MediaUrl, thread[0].MediaUrlsOrEmpty);
    }

    [Fact]
    public async Task PremiumPost_AllowsOnlyCanonicalChangelogUrl()
    {
        var formatter = CreateFormatter(plan: new ChangelogSummaryPlan
        {
            TopThingsToKnow =
            [
                "Use the new flow at https://example.com",
                "Read [background](https://docs.example.com/background)"
            ],
            Paragraphs =
            [
                "The update references github.blog/generated and https://example.org/details in generated copy.",
                $"AI repeated the main URL here too: {CanonicalLink}"
            ]
        });

        var post = await formatter.FormatPremiumPostForXAsync(CreateEntry(), useAi: true);

        AssertOnlyCanonicalUrl(post.Text);
        Assert.DoesNotContain("example.org", post.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("github.blog/generated", post.Text, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(CanonicalLink, post.Text);
    }

    private static TweetFormatterService CreateFormatter(
        string? singleSummary = null,
        ChangelogSummaryPlan? plan = null)
        => new(
            NullLogger<TweetFormatterService>.Instance,
            new FakeSummarizer(singleSummary, plan));

    private static ChangelogEntry CreateEntry()
        => new()
        {
            Id = CanonicalLink,
            Title = "Example changelog update",
            Link = CanonicalLink,
            SummaryHtml = string.Empty,
            SummaryText = "Summary text with https://summary.example and github.blog/summary.",
            ContentHtml = "<p>Content with <a href=\"https://content.example\">content link</a>.</p>",
            ContentText = "Content text with https://content.example.",
            Labels = ["copilot"],
            Media = [new ChangelogMediaItem(MediaUrl, ChangelogMediaType.Image)],
            ChangelogType = "feature",
            Updated = new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero)
        };

    private static void AssertOnlyCanonicalUrl(string text)
    {
        var urls = UrlPattern()
            .Matches(text)
            .Select(match => match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']'))
            .ToList();

        var url = Assert.Single(urls);
        Assert.Equal(CanonicalLink, url);
    }

    [GeneratedRegex(@"https?://[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    private sealed class FakeSummarizer(
        string? singleSummary,
        ChangelogSummaryPlan? plan) : IReleaseSummarizerService
    {
        public Task<ChangelogSummaryPlan?> PlanSummaryAsync(
            string releaseTitle,
            string releaseContent,
            string summaryText,
            IReadOnlyList<string> labels,
            bool premiumMode,
            bool isWeekly,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ChangelogSummaryPlan?>(plan);

        public Task<string?> SummarizeSinglePostAsync(
            string releaseTitle,
            string releaseContent,
            int maxLength,
            CancellationToken cancellationToken = default)
            => Task.FromResult(singleSummary);
    }
}
