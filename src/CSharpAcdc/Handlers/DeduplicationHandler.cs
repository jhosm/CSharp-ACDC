using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CSharpAcdc.Extensions;

namespace CSharpAcdc.Handlers;

public class DeduplicationHandler : DelegatingHandler
{
    private readonly ConcurrentDictionary<string, Lazy<Task<DeduplicatedResponse>>> _inFlight = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!IsDeduplicatable(request))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var key = BuildDeduplicationKey(request);

        var lazy = _inFlight.GetOrAdd(key, _ => new Lazy<Task<DeduplicatedResponse>>(
            () => ExecuteAndBufferAsync(request, cancellationToken)));

        try
        {
            var dedupResponse = await lazy.Value.ConfigureAwait(false);
            return dedupResponse.Clone();
        }
        finally
        {
            // Only the thread whose Lazy instance is stored removes the entry.
            // This avoids removing a replacement entry added by a later wave.
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<DeduplicatedResponse>>>(key, lazy));
        }
    }

    private async Task<DeduplicatedResponse> ExecuteAndBufferAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await DeduplicatedResponse.FromResponseAsync(response).ConfigureAwait(false);
    }

    private static bool IsDeduplicatable(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            return false;

        if (request.Options.TryGetValue(AcdcRequestOptions.Deduplicate, out var deduplicate) && !deduplicate)
            return false;

        return true;
    }

    internal static string BuildDeduplicationKey(HttpRequestMessage request)
    {
        var sb = new StringBuilder();
        sb.Append(request.Method);
        sb.Append(':');
        sb.Append(request.RequestUri?.ToString() ?? string.Empty);
        sb.Append(':');

        var sortedHeaders = request.Headers
            .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(h => h.Value.Select(v => $"{h.Key}:{v}"));

        var headerString = string.Join(",", sortedHeaders);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(headerString));
        sb.Append(Convert.ToHexString(hashBytes));

        return sb.ToString();
    }

    private sealed class DeduplicatedResponse
    {
        public required System.Net.HttpStatusCode StatusCode { get; init; }
        public required byte[]? ContentBytes { get; init; }
        public required List<KeyValuePair<string, IEnumerable<string>>> ResponseHeaders { get; init; }
        public required List<KeyValuePair<string, IEnumerable<string>>> ContentHeaders { get; init; }
        public required string? ReasonPhrase { get; init; }
        public required Version Version { get; init; }

        public static async Task<DeduplicatedResponse> FromResponseAsync(HttpResponseMessage response)
        {
            byte[]? contentBytes = null;
            List<KeyValuePair<string, IEnumerable<string>>> contentHeaders = [];

            if (response.Content is not null)
            {
                contentBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                contentHeaders = [.. response.Content.Headers];
            }

            return new DeduplicatedResponse
            {
                StatusCode = response.StatusCode,
                ContentBytes = contentBytes,
                ResponseHeaders = [.. response.Headers],
                ContentHeaders = contentHeaders,
                ReasonPhrase = response.ReasonPhrase,
                Version = response.Version,
            };
        }

        public HttpResponseMessage Clone()
        {
            var response = new HttpResponseMessage(StatusCode)
            {
                ReasonPhrase = ReasonPhrase,
                Version = Version,
            };

            foreach (var header in ResponseHeaders)
            {
                response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (ContentBytes is not null)
            {
                var content = new ByteArrayContent(ContentBytes);
                foreach (var header in ContentHeaders)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                response.Content = content;
            }

            return response;
        }
    }
}
