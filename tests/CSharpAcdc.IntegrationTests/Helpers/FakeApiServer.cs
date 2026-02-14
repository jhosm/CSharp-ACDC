using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace CSharpAcdc.IntegrationTests.Helpers;

/// <summary>
/// WireMock-backed fake API server with dynamic request handlers,
/// ETag/304 support, and configurable latency.
/// </summary>
public sealed class FakeApiServer : IDisposable
{
    private readonly WireMockServer _server;
    private int _scenarioCounter;

    public FakeApiServer()
    {
        _server = WireMockServer.Start();
    }

    public string Url => _server.Url!;

    /// <summary>
    /// Configure a GET endpoint that returns JSON with a 200 status.
    /// </summary>
    public void ConfigureGetSuccess(string path, object body, string? etag = null)
    {
        var response = Response.Create()
            .WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(JsonSerializer.Serialize(body));

        if (etag is not null)
        {
            response = response.WithHeader("ETag", $"\"{etag}\"");
        }

        _server.Given(
            Request.Create()
                .WithPath(path)
                .UsingGet())
            .RespondWith(response);
    }

    /// <summary>
    /// Configure a GET endpoint that returns 304 Not Modified when If-None-Match matches.
    /// Also serves the full response when no If-None-Match header is present.
    /// </summary>
    public void ConfigureGetWithETag(string path, object body, string etag)
    {
        // First mapping: 304 when ETag matches
        _server.Given(
            Request.Create()
                .WithPath(path)
                .UsingGet()
                .WithHeader("If-None-Match", $"\"\\\"" + etag + "\\\"\"", WireMock.Matchers.MatchBehaviour.AcceptOnMatch))
            .AtPriority(1)
            .RespondWith(
                Response.Create()
                    .WithStatusCode(304)
                    .WithHeader("ETag", $"\"{etag}\""));

        // Second mapping: full response when no match
        _server.Given(
            Request.Create()
                .WithPath(path)
                .UsingGet())
            .AtPriority(2)
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithHeader("ETag", $"\"{etag}\"")
                    .WithBody(JsonSerializer.Serialize(body)));
    }

    /// <summary>
    /// Configure a GET endpoint that returns 200 with configurable latency.
    /// </summary>
    public void ConfigureGetWithDelay(string path, object body, TimeSpan delay)
    {
        _server.Given(
            Request.Create()
                .WithPath(path)
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(body))
                    .WithDelay(delay));
    }

    /// <summary>
    /// Configure a POST endpoint that returns the given status code.
    /// </summary>
    public void ConfigurePost(string path, int statusCode = 200, object? body = null)
    {
        var response = Response.Create()
            .WithStatusCode(statusCode)
            .WithHeader("Content-Type", "application/json");

        if (body is not null)
            response = response.WithBody(JsonSerializer.Serialize(body));

        _server.Given(
            Request.Create()
                .WithPath(path)
                .UsingPost())
            .RespondWith(response);
    }

    /// <summary>
    /// Configure a path to first return 401 Unauthorized, then 200 on subsequent calls.
    /// Useful for testing auth retry behavior.
    /// </summary>
    public void RespondWith401ThenSuccess(string path, object successBody)
    {
        var scenario = $"Auth-Retry-{Interlocked.Increment(ref _scenarioCounter)}-{path}";

        // WireMock scenario: first call returns 401, subsequent calls return 200
        _server.Given(
            Request.Create()
                .WithPath(path)
                .UsingGet())
            .InScenario(scenario)
            .WillSetStateTo("Authenticated")
            .RespondWith(
                Response.Create()
                    .WithStatusCode(401)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("{\"error\": \"unauthorized\"}"));

        _server.Given(
            Request.Create()
                .WithPath(path)
                .UsingGet())
            .InScenario(scenario)
            .WhenStateIs("Authenticated")
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(successBody)));
    }

    /// <summary>
    /// Configure a path to return a specific HTTP error status.
    /// </summary>
    public void ConfigureError(string path, int statusCode, string? body = null)
    {
        _server.Given(
            Request.Create()
                .WithPath(path)
                .UsingAnyMethod())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(statusCode)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(body ?? $"{{\"error\": \"{statusCode}\"}}"));
    }

    /// <summary>
    /// Returns the number of requests received at the given path.
    /// </summary>
    public int GetCallCount(string path)
    {
        return _server.LogEntries.Count(e =>
            e.RequestMessage.Path?.Equals(path, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Returns the number of requests for a given method+path.
    /// </summary>
    public int GetCallCount(string method, string path)
    {
        return _server.LogEntries.Count(e =>
            e.RequestMessage.Path?.Equals(path, StringComparison.OrdinalIgnoreCase) == true &&
            e.RequestMessage.Method.Equals(method, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns captured Authorization headers for requests at the given path.
    /// </summary>
    public IReadOnlyList<string?> GetAuthorizationHeaders(string path)
    {
        return _server.LogEntries
            .Where(e => e.RequestMessage.Path?.Equals(path, StringComparison.OrdinalIgnoreCase) == true)
            .Select(e =>
            {
                if (e.RequestMessage.Headers?.TryGetValue("Authorization", out var values) == true)
                    return values.FirstOrDefault();
                return null;
            })
            .ToList();
    }

    /// <summary>
    /// Returns captured If-None-Match headers for requests at the given path.
    /// </summary>
    public IReadOnlyList<string?> GetIfNoneMatchHeaders(string path)
    {
        return _server.LogEntries
            .Where(e => e.RequestMessage.Path?.Equals(path, StringComparison.OrdinalIgnoreCase) == true)
            .Select(e =>
            {
                if (e.RequestMessage.Headers?.TryGetValue("If-None-Match", out var values) == true)
                    return values.FirstOrDefault();
                return null;
            })
            .ToList();
    }

    /// <summary>
    /// Reset all mappings and log entries.
    /// </summary>
    public void Reset()
    {
        _server.Reset();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }
}
