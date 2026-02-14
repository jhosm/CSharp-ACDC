using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace CSharpAcdc.IntegrationTests.Helpers;

/// <summary>
/// WireMock-backed fake OAuth server exposing /token and /revoke endpoints.
/// </summary>
public sealed class FakeOAuthServer : IDisposable
{
    private readonly WireMockServer _server;

    public FakeOAuthServer()
    {
        _server = WireMockServer.Start();
    }

    public string Url => _server.Url!;
    public string TokenEndpoint => $"{Url}/token";
    public string RevokeEndpoint => $"{Url}/revoke";

    /// <summary>
    /// Configure /token to return a success response with the given tokens.
    /// </summary>
    public void ConfigureTokenSuccess(
        string accessToken = "new-access-token",
        string refreshToken = "new-refresh-token",
        int expiresIn = 3600)
    {
        _server.Given(
            Request.Create()
                .WithPath("/token")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(new
                    {
                        access_token = accessToken,
                        refresh_token = refreshToken,
                        expires_in = expiresIn,
                        token_type = "Bearer",
                    })));
    }

    /// <summary>
    /// Configure /token to return an invalid_grant error (auth failure — clears tokens).
    /// </summary>
    public void ConfigureTokenInvalidGrant()
    {
        _server.Given(
            Request.Create()
                .WithPath("/token")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(400)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(new
                    {
                        error = "invalid_grant",
                        error_description = "The refresh token is expired",
                    })));
    }

    /// <summary>
    /// Configure /token to return a server error (transient — preserves tokens).
    /// </summary>
    public void ConfigureTokenServerError()
    {
        _server.Given(
            Request.Create()
                .WithPath("/token")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(500)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("{\"error\": \"server_error\"}"));
    }

    /// <summary>
    /// Configure /revoke to return 200 OK.
    /// </summary>
    public void ConfigureRevokeSuccess()
    {
        _server.Given(
            Request.Create()
                .WithPath("/revoke")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200));
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
    /// Returns captured request bodies for the given path.
    /// </summary>
    public IReadOnlyList<string?> GetRequestBodies(string path)
    {
        return _server.LogEntries
            .Where(e => e.RequestMessage.Path?.Equals(path, StringComparison.OrdinalIgnoreCase) == true)
            .Select(e => e.RequestMessage.Body)
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
