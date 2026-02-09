using System.Net;
using System.Net.Http.Headers;
using CSharpAcdc.Auth;
using CSharpAcdc.Configuration;
using CSharpAcdc.Exceptions;
using CSharpAcdc.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CSharpAcdc.Handlers;

public sealed class AuthHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokenProvider;
    private readonly ITokenRefreshStrategy _refreshStrategy;
    private readonly BackoffManager _backoffManager;
    private readonly AcdcAuthOptions _options;
    private readonly ILogger<AuthHandler> _logger;

    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private volatile TaskCompletionSource<bool>? _pendingRefresh;

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

            // Proactive refresh: fire-and-forget if token is near expiry
            _ = TryProactiveRefreshAsync(cancellationToken);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        // 401 response — attempt refresh and retry once
        var refreshed = await TryRefreshOnUnauthorizedAsync(cancellationToken).ConfigureAwait(false);
        if (!refreshed)
            return response;

        // Retry with new token
        response.Dispose();
        var retryRequest = await CloneRequestAsync(request).ConfigureAwait(false);
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

            // Fire-and-forget — don't block the current request
            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteRefreshAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Proactive token refresh failed");
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
        // Try to acquire the refresh semaphore with immediate timeout
        if (await _refreshSemaphore.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            // We're the leader — execute the refresh
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRefresh = tcs;
            try
            {
                await ExecuteRefreshAsync(ct).ConfigureAwait(false);
                tcs.TrySetResult(true);
                return true;
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                return false;
            }
            finally
            {
                _pendingRefresh = null;
                _refreshSemaphore.Release();
            }
        }

        // We're a follower — wait for the leader's refresh to complete
        var pending = _pendingRefresh;
        if (pending is null)
            return false;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.QueueTimeout);
            return await pending.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
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
            return false;
        }
    }

    private async Task ExecuteRefreshAsync(CancellationToken ct)
    {
        var refreshToken = await _tokenProvider.GetRefreshTokenAsync(ct).ConfigureAwait(false);
        if (refreshToken is null)
        {
            _logger.LogWarning("No refresh token available");
            return;
        }

        try
        {
            var result = await _refreshStrategy.RefreshAsync(refreshToken, ct).ConfigureAwait(false);
            await _tokenProvider.SaveTokensAsync(
                result.AccessToken, result.RefreshToken, result.ExpiresAt, ct).ConfigureAwait(false);
            await _backoffManager.ResetAsync(ct).ConfigureAwait(false);
            _logger.LogDebug("Token refresh succeeded, expires at {ExpiresAt}", result.ExpiresAt);
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

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy,
        };

        // Clone content
        if (original.Content is not null)
        {
            var contentBytes = await original.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
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
