using System.Net;
using System.Net.Http.Headers;
using CSharpAcdc.Auth;
using CSharpAcdc.Configuration;
using CSharpAcdc.Exceptions;
using CSharpAcdc.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CSharpAcdc.Handlers;

/// <summary>
/// Injects Bearer tokens into outgoing requests, performs proactive and reactive token refresh,
/// and retries once on 401 responses after a successful refresh.
/// </summary>
public sealed class AuthHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokenProvider;
    private readonly ITokenRefreshStrategy _refreshStrategy;
    private readonly BackoffManager _backoffManager;
    private readonly AcdcAuthOptions _options;
    private readonly ILogger<AuthHandler> _logger;

    // Leader/follower coordination via atomic CAS — no separate semaphore needed.
    // The first thread to CAS null → TCS becomes the leader; others wait on the TCS.
    private TaskCompletionSource<bool>? _pendingRefresh;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthHandler"/>.
    /// </summary>
    /// <param name="tokenProvider">Provides access and refresh tokens.</param>
    /// <param name="refreshStrategy">The strategy used to refresh expired tokens.</param>
    /// <param name="backoffManager">Manages exponential backoff after transient refresh failures.</param>
    /// <param name="options">Authentication configuration options.</param>
    /// <param name="logger">The logger instance.</param>
    public AuthHandler(
        ITokenProvider tokenProvider,
        ITokenRefreshStrategy refreshStrategy,
        BackoffManager backoffManager,
        IOptions<AcdcAuthOptions> options,
        ILogger<AuthHandler> logger)
    {
        _tokenProvider = tokenProvider;
        _refreshStrategy = refreshStrategy;
        _backoffManager = backoffManager;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Check skip auth option
        if (request.Options.TryGetValue(AcdcRequestOptions.SkipAuth, out var skipAuth) && skipAuth)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        // Wait for backoff if needed
        await _backoffManager.WaitIfNeededAsync(cancellationToken).ConfigureAwait(false);

        // Inject token
        var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // Proactive refresh if token is near expiry — uses the same leader/follower
            // coordination as 401 refresh so the two paths never race.
            await TryProactiveRefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        // Buffer content before first send so it can be re-read on 401 retry clone.
        // No-op for already-buffered types (StringContent, ByteArrayContent).
        if (request.Content is not null)
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        // 401 response — attempt refresh and retry once
        var refreshed = await TryRefreshOnUnauthorizedAsync(cancellationToken).ConfigureAwait(false);
        if (!refreshed)
            return response;

        // Retry with new token
        response.Dispose();
        var retryRequest = await CloneRequestAsync(request, cancellationToken).ConfigureAwait(false);
        var newToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (newToken is not null)
        {
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
        }

        return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
    }

    private async Task TryProactiveRefreshAsync(CancellationToken ct)
    {
        try
        {
            var expiry = await _tokenProvider.GetTokenExpiryAsync(ct).ConfigureAwait(false);
            if (expiry is null)
                return;

            var timeUntilExpiry = expiry.Value - DateTimeOffset.UtcNow;
            if (timeUntilExpiry > _options.RefreshThreshold)
                return;

            _logger.LogDebug("Token expires in {TimeUntilExpiry}, proactively refreshing", timeUntilExpiry);

            // Try to become the refresh leader — if another refresh is in flight, skip.
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref _pendingRefresh, tcs, null) is not null)
                return;

            // Fire-and-forget — don't block the current request, but coordinate
            // through the same TCS so a concurrent 401 refresh waits on our result.
            _ = Task.Run(async () =>
            {
                try
                {
                    var refreshed = await ExecuteRefreshAsync(CancellationToken.None).ConfigureAwait(false);
                    tcs.TrySetResult(refreshed);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Proactive token refresh failed");
                    tcs.TrySetException(ex);
                }
                finally
                {
                    Interlocked.CompareExchange(ref _pendingRefresh, null, tcs);
                }
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking token expiry for proactive refresh");
        }
    }

    private async Task<bool> TryRefreshOnUnauthorizedAsync(CancellationToken ct)
    {
        // Two attempts: if we're a follower and the leader fails with a transient
        // error (e.g., from a concurrent proactive refresh), loop back and attempt
        // our own refresh as a new leader instead of propagating the failure.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            // Atomic leader election: CAS null → our TCS.
            // If CAS succeeds we're the leader; if it returns an existing TCS we're a follower.
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var existing = Interlocked.CompareExchange(ref _pendingRefresh, tcs, null);

            if (existing is null)
            {
                // We're the leader — execute the refresh
                try
                {
                    var refreshed = await ExecuteRefreshAsync(ct).ConfigureAwait(false);
                    tcs.TrySetResult(refreshed);
                    return refreshed;
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                    return false;
                }
                finally
                {
                    Interlocked.CompareExchange(ref _pendingRefresh, null, tcs);
                }
            }

            // We're a follower — wait for the leader's refresh to complete
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_options.QueueTimeout);
                return await existing.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // Propagate caller cancellation instead of swallowing it
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new AcdcAuthException(
                    $"Token refresh queue timed out after {_options.QueueTimeout.TotalSeconds}s");
            }
            catch (AcdcAuthException)
            {
                throw;
            }
            catch
            {
                // Leader failed with a transient error (e.g., from a concurrent proactive
                // refresh). The leader already cleared _pendingRefresh in its finally block.
                // Loop to attempt our own refresh as a new leader.
            }
        }

        return false;
    }

    /// <returns><c>true</c> if tokens were refreshed; <c>false</c> if no refresh token was available.</returns>
    private async Task<bool> ExecuteRefreshAsync(CancellationToken ct)
    {
        var refreshToken = await _tokenProvider.GetRefreshTokenAsync(ct).ConfigureAwait(false);
        if (refreshToken is null)
        {
            _logger.LogWarning("No refresh token available");
            return false;
        }

        try
        {
            var result = await _refreshStrategy.RefreshAsync(refreshToken, ct).ConfigureAwait(false);
            await _tokenProvider.SaveTokensAsync(
                result.AccessToken, result.RefreshToken, result.ExpiresAt, ct).ConfigureAwait(false);
            await _backoffManager.ResetAsync(ct).ConfigureAwait(false);
            _logger.LogDebug("Token refresh succeeded, expires at {ExpiresAt}", result.ExpiresAt);
            return true;
        }
        catch (AcdcAuthException ex)
        {
            _logger.LogWarning(ex, "Auth error during token refresh, clearing tokens");
            await _tokenProvider.ClearTokensAsync(ct).ConfigureAwait(false);
            await _backoffManager.ResetAsync(ct).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transient error during token refresh");
            await _backoffManager.RecordFailureAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage original, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy,
        };

        // Clone content
        if (original.Content is not null)
        {
            var contentBytes = await original.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var newContent = new ByteArrayContent(contentBytes);

            foreach (var header in original.Content.Headers)
            {
                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = newContent;
        }

        // Clone headers
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Clone options
#pragma warning disable CS8620 // Argument type differs from parameter type due to nullability — IDictionary<string, object?> vs IDictionary<string, object?>
        foreach (var option in original.Options)
        {
            ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
        }
#pragma warning restore CS8620

        return clone;
    }
}
