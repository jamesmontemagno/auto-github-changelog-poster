using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AutoGithubChangelogPoster.Services;

public class MastodonApiClient
{
    private const int ThreadPostDelaySeconds = 2;

    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly string? _instanceUrl;
    private readonly string? _accessToken;

    /// <summary>
    /// Whether the Mastodon credentials are configured.
    /// </summary>
    public bool IsConfigured { get; }

    public MastodonApiClient(ILogger<MastodonApiClient> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _instanceUrl = Environment.GetEnvironmentVariable("MASTODON_INSTANCE_URL")?.Trim().TrimEnd('/');
        _accessToken = Environment.GetEnvironmentVariable("MASTODON_ACCESS_TOKEN")?.Trim();
        IsConfigured = !string.IsNullOrEmpty(_instanceUrl) && !string.IsNullOrEmpty(_accessToken);
    }

    public async Task<bool> PostAsync(SocialMediaPost post)
        => await PostAndGetIdAsync(post) != null;

    /// <summary>
    /// Posts a status and returns the status ID, or null on failure.
    /// </summary>
    public async Task<string?> PostAndGetIdAsync(SocialMediaPost post, string? replyToId = null)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Mastodon credentials not configured. Skipping post.");
            return null;
        }

        try
        {
            _logger.LogInformation(
                "Posting to Mastodon (raw={RawLength}, media={MediaCount}): {Preview}...",
                post.Text.Length,
                post.MediaUrlsOrEmpty.Count,
                post.Text.Length > 50 ? post.Text[..50] : post.Text);

            var mediaIds = await UploadMediaAsync(post.MediaUrlsOrEmpty);

            var statusesUrl = $"{_instanceUrl}/api/v1/statuses";

            using var request = new HttpRequestMessage(HttpMethod.Post, statusesUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            using var formData = new MultipartFormDataContent();
            formData.Add(new StringContent(post.Text), "status");

            if (!string.IsNullOrEmpty(replyToId))
            {
                formData.Add(new StringContent(replyToId), "in_reply_to_id");
            }

            foreach (var mediaId in mediaIds)
            {
                formData.Add(new StringContent(mediaId), "media_ids[]");
            }

            request.Content = formData;

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var statusResponse = JsonSerializer.Deserialize<MastodonStatusResponse>(responseContent);
                var statusId = statusResponse?.Id;
                _logger.LogInformation("Mastodon post created successfully. Status ID: {StatusId}", statusId);
                return statusId;
            }

            _logger.LogError("Failed to post to Mastodon. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, responseContent);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error posting to Mastodon");
            return null;
        }
    }

    /// <summary>
    /// Posts multiple statuses as a reply chain (thread). Returns true if all posts succeeded.
    /// </summary>
    public async Task<bool> PostThreadAsync(IReadOnlyList<SocialMediaPost> posts)
    {
        if (posts == null || posts.Count == 0)
        {
            return false;
        }

        if (posts.Count == 1)
        {
            return await PostAsync(posts[0]);
        }

        string? lastStatusId = null;
        var allSucceeded = true;

        for (var i = 0; i < posts.Count; i++)
        {
            var statusId = await PostAndGetIdAsync(posts[i], lastStatusId);
            if (statusId == null)
            {
                _logger.LogWarning("Mastodon thread post {Index}/{Total} failed. Stopping thread.", i + 1, posts.Count);
                allSucceeded = false;
                break;
            }

            lastStatusId = statusId;

            if (i < posts.Count - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(ThreadPostDelaySeconds));
            }
        }

        return allSucceeded;
    }

    private async Task<List<string>> UploadMediaAsync(IReadOnlyList<string> mediaUrls)
    {
        var mediaIds = new List<string>();

        foreach (var mediaUrl in mediaUrls.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var mediaId = await UploadSingleMediaAsync(mediaUrl);
            if (!string.IsNullOrWhiteSpace(mediaId))
            {
                mediaIds.Add(mediaId);
            }
        }

        return mediaIds;
    }

    private async Task<string?> UploadSingleMediaAsync(string mediaUrl)
    {
        using var mediaResponse = await _httpClient.GetAsync(mediaUrl);
        if (!mediaResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to download media from {MediaUrl}. Status: {StatusCode}",
                mediaUrl, mediaResponse.StatusCode);
            return null;
        }

        var bytes = await mediaResponse.Content.ReadAsByteArrayAsync();
        if (bytes.Length == 0)
        {
            _logger.LogWarning("Downloaded media from {MediaUrl} was empty.", mediaUrl);
            return null;
        }

        var contentType = mediaResponse.Content.Headers.ContentType?.MediaType
            ?? GuessMediaTypeFromUrl(mediaUrl);

        var uploadUrl = $"{_instanceUrl}/api/v2/media";

        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new(contentType);
        multipart.Add(fileContent, "file", "media");
        request.Content = multipart;

        var response = await _httpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Mastodon media upload failed. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, payload);
            return null;
        }

        var upload = JsonSerializer.Deserialize<MastodonMediaAttachment>(payload);
        return upload?.Id;
    }

    private static string GuessMediaTypeFromUrl(string mediaUrl)
    {
        var path = mediaUrl.Split('?', '#')[0];
        var extension = Path.GetExtension(path);

        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            _ => "application/octet-stream"
        };
    }
}

public class MastodonStatusResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

public class MastodonMediaAttachment
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
