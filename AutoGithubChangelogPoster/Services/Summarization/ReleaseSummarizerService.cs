using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.RegularExpressions;

namespace AutoGithubChangelogPoster.Services;

public class ReleaseSummarizerService
{
    private const int DefaultSummaryPlanTimeoutSeconds = 60;
    private const int MaxAiContentLength = 4000;

    private readonly IChatClient _chatClient;
    private readonly ILogger<ReleaseSummarizerService> _logger;
    private readonly string _endpointHost;
    private readonly string _deploymentModel;

    private static readonly Regex HtmlTagPattern = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex ContributorLinePattern = new(
        @"(?:by\s+@\S+\s*(?:in\s+)?)?(?:https?://\S+/pull/\d+|#\d+)\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches common prompt-injection phrases that could hijack model behaviour.
    private static readonly Regex PromptInjectionPattern = new(
        @"ignore\s+(previous|prior|all)\s+(instructions?|rules?|directives?|prompts?|constraints?)|" +
        @"disregard\s+(previous|prior|all)\s+(instructions?|rules?|directives?)|" +
        @"forget\s+(everything|all\s*(?:previous|prior)?|previous|prior)(\s+instructions?)?|" +
        @"you\s+are\s+now\s+(?:a\s+|an\s+)?|" +
        @"new\s+instructions?\s*[:\-]|" +
        @"<\s*/?(?:system|instruction|prompt)\s*>|" +
        @"\[/?(?:INST|SYS|SYSTEM|OVERRIDE)\]|" +
        @"pretend\s+(?:you\s+are|to\s+be)\s+|" +
        @"act\s+as\s+(?:a\s+|an\s+)?(?:different|new|another|uncensored|jailbroken)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches content that must never appear in AI output destined for social posting.
    private static readonly Regex UnsafeOutputPattern = new(
        @"https?://\S+|" +
        @"(?<!\w)@[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?|" +
        @"(?<!\w)#[A-Za-z][A-Za-z0-9_]*|" +
        @"\bsystem\s+prompt\b|" +
        @"\bignore\s+(previous|prior|all)\s+(instructions?|rules?)\b|" +
        @"\bas\s+an?\s+(AI|language\s+model|LLM)\b|" +
        @"\bmy\s+(instructions?|guidelines?|rules?|constraints?|system\s+prompt)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ReleaseSummarizerService(
        ILogger<ReleaseSummarizerService> logger,
        string endpoint,
        string apiKey,
        string deploymentModel)
    {
        _logger = logger;
        _endpointHost = new Uri(endpoint).Host;
        _deploymentModel = deploymentModel;
        _chatClient = CreateClient(endpoint, apiKey, deploymentModel);
    }

    private static IChatClient CreateClient(string endpoint, string apiKey, string deploymentModel)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(apiKey);
        ArgumentNullException.ThrowIfNull(deploymentModel);

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey));

        var chatClient = azureClient.GetChatClient(deploymentModel);
        return chatClient.AsIChatClient();
    }

    public async Task<ChangelogSummaryPlan?> PlanSummaryAsync(
        string releaseTitle,
        string releaseContent,
        string summaryText,
        IReadOnlyList<string> labels,
        bool premiumMode,
        bool isWeekly,
        CancellationToken cancellationToken = default)
    {
        const int maxRetries = 3;
        var cleaned = PrepareContentForAi($"{summaryText}\n\n{releaseContent}");
        var labelText = labels.Count > 0 ? string.Join(", ", labels) : "none";
        var sanitizedTitle = SanitizeInputField(releaseTitle);
        var sanitizedSummary = SanitizeInputField(summaryText);
        var sanitizedLabelText = SanitizeInputField(labelText);
        var prompt = ReleaseSummarizerPrompts.BuildChangelogPlanUserPrompt(sanitizedTitle, sanitizedSummary, cleaned, sanitizedLabelText, premiumMode, isWeekly);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, ReleaseSummarizerPrompts.GetChangelogPlanSystemPrompt()),
            new(ChatRole.User, prompt)
        };

        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json
        };

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(GetSummaryPlanTimeoutSeconds()));

                var response = await _chatClient.GetResponseAsync(messages, options, timeoutCts.Token);
                var json = StripCodeFences(response.Messages.LastOrDefault()?.Text?.Trim() ?? string.Empty);

                var plan = System.Text.Json.JsonSerializer.Deserialize<ChangelogSummaryPlan>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (plan == null)
                {
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                        continue;
                    }

                    return null;
                }

                plan.TopThingsToKnow ??= [];
                plan.Paragraphs ??= [];

                plan.TopThingsToKnow = plan.TopThingsToKnow
                    .Select(item => item.Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(SinglePostSummaryNormalizer.ShortenBullet)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Where(item => !SinglePostSummaryNormalizer.LooksLikeTitleEcho(item, releaseTitle))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .ToList();

                plan.Paragraphs = plan.Paragraphs
                    .Select(paragraph => paragraph.Trim())
                    .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();

                if (plan.TopThingsToKnow.Count == 0 && plan.Paragraphs.Count == 0)
                {
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                        continue;
                    }

                    return null;
                }

                var allPlanText = string.Join(" ", plan.TopThingsToKnow.Concat(plan.Paragraphs));
                if (!IsValidAiOutput(allPlanText))
                {
                    _logger.LogWarning(
                        "AI plan output for {Title} failed safety validation (attempt {Attempt}/{MaxRetries}).",
                        releaseTitle, attempt, maxRetries);

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                        continue;
                    }

                    return null;
                }

                return plan;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "Timed out generating GitHub changelog AI plan for {Title} (attempt {Attempt}/{MaxRetries}).",
                    releaseTitle, attempt, maxRetries);

                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                    continue;
                }

                return null;
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex,
                    "Azure OpenAI request failed for AI plan for {Title} (attempt {Attempt}/{MaxRetries}). Status={Status}, ErrorCode={ErrorCode}, EndpointHost={EndpointHost}, Deployment={Deployment}.",
                    releaseTitle,
                    attempt,
                    maxRetries,
                    ex.Status,
                    ex.ErrorCode,
                    _endpointHost,
                    _deploymentModel);

                if (!IsRetryableAzureAiError(ex))
                {
                    _logger.LogWarning(
                        "Stopping retries for AI plan for {Title} because this looks like a non-transient configuration/auth issue.",
                        releaseTitle);
                    return null;
                }

                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                    continue;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error generating GitHub changelog AI plan for {Title} (attempt {Attempt}/{MaxRetries}).",
                    releaseTitle, attempt, maxRetries);

                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                    continue;
                }

                return null;
            }
        }

        return null;
    }

    public async Task<string?> SummarizeSinglePostAsync(
        string releaseTitle,
        string releaseContent,
        int maxLength,
        CancellationToken cancellationToken = default)
    {
        const int maxRetries = 3;
        var cleaned = PrepareContentForAi(releaseContent);
        var sanitizedTitle = SanitizeInputField(releaseTitle);
        var prompt = ReleaseSummarizerPrompts.BuildSinglePostUserPrompt(sanitizedTitle, cleaned, maxLength);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, ReleaseSummarizerPrompts.GetSinglePostSystemPrompt()),
            new(ChatRole.User, prompt)
        };

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(GetSummaryPlanTimeoutSeconds()));

                var response = await _chatClient.GetResponseAsync(messages, cancellationToken: timeoutCts.Token);
                var summary = StripCodeFences(response.Messages.LastOrDefault()?.Text?.Trim() ?? string.Empty)
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Trim();
                summary = SinglePostSummaryNormalizer.Normalize(summary, maxLength);

                if (string.IsNullOrWhiteSpace(summary))
                {
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                        continue;
                    }

                    return null;
                }

                if (!IsValidAiOutput(summary))
                {
                    _logger.LogWarning(
                        "Single-post AI output for {Title} failed safety validation (attempt {Attempt}/{MaxRetries}).",
                        releaseTitle, attempt, maxRetries);

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                        continue;
                    }

                    return null;
                }

                return summary;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "Timed out generating GitHub changelog single-post summary for {Title} (attempt {Attempt}/{MaxRetries}).",
                    releaseTitle, attempt, maxRetries);

                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                    continue;
                }

                return null;
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex,
                    "Azure OpenAI request failed for single-post summary for {Title} (attempt {Attempt}/{MaxRetries}). Status={Status}, ErrorCode={ErrorCode}, EndpointHost={EndpointHost}, Deployment={Deployment}.",
                    releaseTitle,
                    attempt,
                    maxRetries,
                    ex.Status,
                    ex.ErrorCode,
                    _endpointHost,
                    _deploymentModel);

                if (!IsRetryableAzureAiError(ex))
                {
                    _logger.LogWarning(
                        "Stopping retries for single-post summary for {Title} because this looks like a non-transient configuration/auth issue.",
                        releaseTitle);
                    return null;
                }

                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                    continue;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error generating GitHub changelog single-post summary for {Title} (attempt {Attempt}/{MaxRetries}).",
                    releaseTitle, attempt, maxRetries);

                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                    continue;
                }

                return null;
            }
        }

        return null;
    }

    private static string PrepareContentForAi(string rawContent)
    {
        var decoded = WebUtility.HtmlDecode(rawContent);

        var cutoff = decoded.IndexOf("New Contributors", StringComparison.OrdinalIgnoreCase);
        if (cutoff < 0)
        {
            cutoff = decoded.IndexOf("Full Changelog", StringComparison.OrdinalIgnoreCase);
        }

        if (cutoff > 0)
        {
            decoded = decoded[..cutoff];
        }

        var cleaned = HtmlTagPattern.Replace(decoded, " ");
        cleaned = ContributorLinePattern.Replace(cleaned, " ");
        cleaned = WhitespacePattern.Replace(cleaned, " ").Trim();
        cleaned = PromptInjectionPattern.Replace(cleaned, "[filtered]");

        if (cleaned.Length > MaxAiContentLength)
        {
            cleaned = cleaned[..MaxAiContentLength] + "...[truncated]";
        }

        return cleaned;
    }

    /// <summary>
    /// Flattens a short input field to a single line and strips prompt injection patterns.
    /// Use this for fields that are embedded directly into the prompt (title, labels, summary).
    /// </summary>
    private static string SanitizeInputField(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var singleLine = WhitespacePattern.Replace(value.Replace('\r', ' ').Replace('\n', ' '), " ");
        return PromptInjectionPattern.Replace(singleLine, "[filtered]").Trim();
    }

    /// <summary>
    /// Returns true when the AI output contains only safe, expected content.
    /// Rejects text with URLs, @-handles, hashtags, or indicators of prompt hijacking.
    /// </summary>
    private static bool IsValidAiOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        return !UnsafeOutputPattern.IsMatch(output);
    }

    private static string StripCodeFences(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstNewline = text.IndexOf('\n');
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewline >= 0 && lastFence > firstNewline)
        {
            return text[(firstNewline + 1)..lastFence].Trim();
        }

        return text;
    }

    private static int GetSummaryPlanTimeoutSeconds()
    {
        var configured = Environment.GetEnvironmentVariable("AI_THREAD_PLAN_TIMEOUT_SECONDS");
        return int.TryParse(configured, out var seconds) && seconds > 0
            ? seconds
            : DefaultSummaryPlanTimeoutSeconds;
    }

    private static bool IsRetryableAzureAiError(RequestFailedException ex)
        => ex.Status is 408 or 429 or 500 or 502 or 503 or 504;
}

internal static class ReleaseSummarizerPrompts
{
        public static string GetChangelogPlanSystemPrompt() => @"You are an expert at turning GitHub changelog content into polished social post plans.

    You MUST respond with valid JSON only and no markdown fences.

    JSON schema:
    {
      ""topThingsToKnow"": string[],
      ""paragraphs"": string[]
    }

    Rules:
    - topThingsToKnow must contain short, high-signal bullets with no leading bullet characters
    - Keep topThingsToKnow compact and scannable; aim for 20-55 characters when possible
    - Do not repeat or paraphrase the release title in topThingsToKnow; the title is already shown separately
    - Prefer concrete capability/outcome phrases over complete sentences
    - paragraphs must be concise plain-text paragraphs
    - For non-premium/threaded output, paragraphs should stay under 200 characters
    - Avoid emoji; prefer plain text (use zero emoji unless absolutely necessary)
    - Never include links, URLs, raw domain names, the @ character, hashtags, usernames, issue numbers, or markdown headings
    - Focus on product impact, workflows, and why the update matters
    - Assume the post is published from an official GitHub account, so focus on what was announced rather than who announced it
    - Never say or imply that GitHub announced, launched, shared, or introduced something in the opening
    - If the content is negative or could be perceived as negative, keep the response shorter, flatter, and more succinct
    - For negative or sensitive updates, do not expand on bullet points unless essential to explain the recap
    - Keep tone neutral for negative or sensitive updates; avoid glowing positivity, hype, alarmist language, or dramatic framing
    - Avoid hype and repetition";

        public static string BuildChangelogPlanUserPrompt(
            string releaseTitle,
            string summaryText,
            string cleanedContent,
            string labelText,
            bool premiumMode,
            bool isWeekly)
        {
            return $@"Create a social post plan for this GitHub Changelog {(isWeekly ? "weekly recap" : "entry")}: {releaseTitle}

    Labels:
    {labelText}

    Summary:
    {summaryText}

    Content:
    {cleanedContent}

    Requirements:
    - Return JSON only
    - Produce 2-4 short bullet highlights under topThingsToKnow
    - Produce 1-2 concise paragraphs under paragraphs
    - Bullets should be plain text without bullet characters
    - Keep bullets very short: prefer 20-55 characters, fragments over full sentences
    - Do not repeat or closely paraphrase the changelog title; assume the title is already shown in the post header
    - Focus each bullet on a distinct capability, change, or outcome
    - Paragraphs should explain what changed and why it matters
    - Assume this will be posted from an official GitHub account, so frame the copy around the update itself instead of attributing it to GitHub
    - Do not say that GitHub announced, launched, shared, or introduced something
    - If the update is negative or could be perceived as negative, keep the response shorter and more succinct
    - For negative or sensitive updates, avoid expanding on bullet points unless needed for accuracy
    - Keep the tone neutral and recap-focused; avoid glowing positivity, hype, heavy negativity, or dramatic framing
    - Avoid emoji; prefer plain text (use zero emoji unless absolutely necessary)
    - {(premiumMode ? "Premium paragraphs can use richer detail, but still stay concise." : "Each paragraph must stay under 200 characters for thread follow-up posts.")}
    - Never include URLs, links, raw domain names, the @ character, hashtags, usernames, issue numbers, or markdown headings
    - Keep wording concrete and helpful for developers
    - {(premiumMode ? "Use slightly richer detail because this can be a Premium X post." : "Keep paragraphs concise enough to fit a social thread follow-up post.")}
    - {(isWeekly ? "Synthesize themes across the week instead of repeating every title." : "Focus on the single changelog entry and its key takeaways.")}";
        }

        public static string GetSinglePostSystemPrompt() => "You write concise GitHub changelog social posts for an official GitHub account. Focus on what was announced, not who announced it. Never say or imply that GitHub announced, launched, shared, or introduced something. If the content is negative or could be perceived as negative, keep the post shorter, more neutral, and more succinct, avoid expanding on bullets unless essential, and avoid both glowing positivity and dramatic negativity. Return plain text only, with no markdown code fences, no @ character, and no URLs, links, or raw domain names.";

        public static string BuildSinglePostUserPrompt(string releaseTitle, string cleanedContent, int maxLength) =>
            $@"Summarize the given GitHub changelog entry.

    Output shape:
    - Start with ONE short sentence summary on the first line.
    - Leave ONE blank line after that first sentence.
    - Only add 1-2 bullets if they are truly needed for the most important extra takeaways.
    - When using bullets, put each one on its own line and prefix it with •.

    STRICT RULES:
    - Total length MUST be less than {maxLength} characters. Don't cut off a sentence in the middle.
    - Keep wording concise, direct, and useful for devs.
    - NO emoji ever
    - NO hashtags ever
    - NO @ character ever
    - Never include raw handles, commands with reviewer handles, or tagged account names
    - Never include URLs, links, or raw domain names
    - NO filler words
    - Instead of using ""and"" use + or & when natural
    - Active voice only
    - Simple words only
    - Shorten ""administrators"" to ""admins"", ""developers"" to ""devs"", ""organizations"" to ""orgs"", ""repositories"" to ""repos"", ""pull requests"" to ""PRs"", when helpful.
    - Focus on what devs can do now + what's now possible
    - Assume this post comes from an official GitHub account, so the opening must focus on what changed, not on GitHub as the announcer
    - Never say that GitHub announced, launched, shared, or introduced this update
    - If the update is negative or could be perceived as negative, keep it shorter, more succinct, and more neutral
    - For negative or sensitive updates, do not expand on bullet points unless essential for accuracy
    - Avoid glowing positivity, hype, harsh negativity, or dramatic framing; stay focused on the recap
    - Implicit second person perspective
    - Use Oxford commas
    - NO em dashes
    - Do NOT mention or tag any account
    - ONLY summarize what is in the existing content - do NOT make anything up or use outside information
    - NEVER include any preface or preamble
    - Return plain text only

    Title:
    {releaseTitle}

    Content:
    {cleanedContent}";
    }

internal static class SinglePostSummaryNormalizer
{
        private static readonly Regex GitHubHandlePattern = new(@"(?<![\w/])@[A-Za-z0-9][A-Za-z0-9-]*", RegexOptions.Compiled);
        private static readonly Regex HashtagPattern = new(@"(?<!\w)#[A-Za-z0-9_]+", RegexOptions.Compiled);
        private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex SpaceBeforePunctuationPattern = new(@"\s+([,.;:!?])", RegexOptions.Compiled);

        public static string Normalize(string summary, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(summary))
            {
                return string.Empty;
            }

            var lines = summary
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (lines.Count == 0)
            {
                return string.Empty;
            }

            var summarySentence = lines[0].StartsWith("•", StringComparison.Ordinal)
                ? lines[0].TrimStart('•', ' ', '\t').Trim()
                : lines[0];
            summarySentence = EnsureSentence(SanitizeLine(summarySentence));
            if (string.IsNullOrWhiteSpace(summarySentence))
            {
                return string.Empty;
            }

            var bullets = new List<string>();
            for (var index = 1; index < lines.Count; index++)
            {
                var bulletText = SanitizeLine(lines[index].TrimStart('•', ' ', '\t').Trim());
                if (!LooksLikeMeaningfulBullet(bulletText))
                {
                    continue;
                }

                bullets.Add($"• {bulletText}");
            }

            return FitSummary(summarySentence, bullets, maxLength);
        }

        public static string ShortenBullet(string bullet)
        {
            var clean = Regex.Replace(bullet, @"\s+", " ").Trim();
            return clean.Length <= 55
                ? clean
                : clean[..52].TrimEnd(' ', ',', ';', ':', '-', '.', '!', '?') + "...";
        }

        public static bool LooksLikeTitleEcho(string bullet, string releaseTitle)
        {
            var normalizedBullet = NormalizeForComparison(bullet);
            var normalizedTitle = NormalizeForComparison(releaseTitle);

            if (string.IsNullOrEmpty(normalizedBullet) || string.IsNullOrEmpty(normalizedTitle))
            {
                return false;
            }

            if (normalizedTitle.Contains(normalizedBullet, StringComparison.Ordinal) ||
                normalizedBullet.Contains(normalizedTitle, StringComparison.Ordinal))
            {
                return true;
            }

            var bulletWords = normalizedBullet.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var titleWords = normalizedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (bulletWords.Length == 0 || titleWords.Length == 0)
            {
                return false;
            }

            var titleWordSet = titleWords.ToHashSet(StringComparer.Ordinal);
            var overlapCount = bulletWords.Count(titleWordSet.Contains);
            return overlapCount >= Math.Max(2, bulletWords.Length - 1);
        }

        private static string FitSummary(string summarySentence, IReadOnlyList<string> bullets, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(summarySentence) || maxLength <= 0)
            {
                return string.Empty;
            }

            if (!XPostLengthHelper.FitsWithinLimit(summarySentence, maxLength))
            {
                return TruncateSentence(summarySentence, maxLength);
            }

            if (bullets.Count == 0)
            {
                return summarySentence;
            }

            var includedBullets = new List<string>();
            foreach (var bullet in bullets.Take(2))
            {
                var candidateBullets = includedBullets.Concat([bullet]).ToList();
                var candidate = $"{summarySentence}\n\n{string.Join("\n", candidateBullets)}";
                if (!XPostLengthHelper.FitsWithinLimit(candidate, maxLength))
                {
                    break;
                }

                includedBullets = candidateBullets;
            }

            return includedBullets.Count == 0
                ? summarySentence
                : $"{summarySentence}\n\n{string.Join("\n", includedBullets)}";
        }

        private static string SanitizeLine(string text)
        {
            var clean = GitHubHandlePattern.Replace(text, string.Empty);
            clean = HashtagPattern.Replace(clean, string.Empty);
            clean = WhitespacePattern.Replace(clean, " ").Trim();
            clean = SpaceBeforePunctuationPattern.Replace(clean, "$1");
            return clean.Trim();
        }

        private static bool LooksLikeMeaningfulBullet(string text)
            => !string.IsNullOrWhiteSpace(text)
                && text.Any(char.IsLetterOrDigit)
                && text.Count(char.IsLetterOrDigit) >= 8;

        private static string TruncateSentence(string sentence, int maxLength)
        {
            var truncated = XPostLengthHelper.TruncateToWeightedLength(sentence, maxLength);
            if (!truncated.EndsWith("...", StringComparison.Ordinal))
            {
                return truncated;
            }

            var withoutEllipsis = truncated[..^3].TrimEnd();
            var lastSpace = withoutEllipsis.LastIndexOf(' ');
            if (lastSpace <= 0)
            {
                return truncated;
            }

            var candidate = withoutEllipsis[..lastSpace].TrimEnd(' ', ',', ';', ':') + "...";
            return XPostLengthHelper.FitsWithinLimit(candidate, maxLength)
                ? candidate
                : truncated;
        }

        private static string EnsureSentence(string text)
        {
            var clean = text.Trim();
            if (string.IsNullOrWhiteSpace(clean))
            {
                return string.Empty;
            }

            return clean[^1] is '.' or '!' or '?'
                ? clean
                : $"{clean}.";
        }

        private static string NormalizeForComparison(string value)
            => Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
    }




