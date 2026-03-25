using System.Net;
using System.Runtime.ExceptionServices;

namespace AutoGithubChangelogPoster.Services;

/// <summary>
/// A <see cref="DelegatingHandler"/> that retries idempotent HTTP requests on transient failures.
/// Only GET and HEAD requests are retried to prevent duplicate side effects.
/// </summary>
internal sealed class TransientRetryHandler : DelegatingHandler
{
    private const int MaxRetries = 3;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Only retry safe, idempotent methods to avoid duplicate side effects.
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        HttpResponseMessage? lastResponse = null;
        ExceptionDispatchInfo? lastException = null;

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken);
            }

            try
            {
                lastResponse?.Dispose();
                lastResponse = await base.SendAsync(request, cancellationToken);
                lastException = null;

                if (lastResponse.IsSuccessStatusCode || !IsTransient(lastResponse.StatusCode))
                {
                    return lastResponse;
                }
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries - 1)
            {
                lastException = ExceptionDispatchInfo.Capture(ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < MaxRetries - 1)
            {
                // Per-request timeout fired; will retry.
                lastException = ExceptionDispatchInfo.Capture(ex);
            }
        }

        // All retries exhausted. Return the last transient response if available,
        // otherwise rethrow the last captured exception.
        if (lastResponse != null)
        {
            return lastResponse;
        }

        lastException?.Throw();

        // Unreachable, but satisfies the compiler.
        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout
        || statusCode == HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;
}
