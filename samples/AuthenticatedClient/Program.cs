using CSharpAcdc.Auth;
using CSharpAcdc.Client;
using CSharpAcdc.Extensions;
using Microsoft.Extensions.DependencyInjection;

// Register ACDC with OAuth 2.1 authentication
var services = new ServiceCollection();
services.AddLogging();
services.AddAcdcHttpClient(b => b
    .WithAuth(auth =>
    {
        auth.RefreshEndpoint = "https://auth.example.com/oauth/token";
        auth.ClientId = "my-client-id";
        auth.ClientSecret = "my-client-secret";
        auth.RefreshThreshold = TimeSpan.FromSeconds(60);
        auth.RevocationEndpoint = "https://auth.example.com/oauth/revoke";
    })
    .WithBaseAddress(new Uri("https://api.example.com")));

// Also register the plain auth client (used internally for token refresh)
services.AddHttpClient("acdc-auth");

var sp = services.BuildServiceProvider();

// Seed initial tokens (in production, these come from a login flow)
var tokenProvider = sp.GetRequiredKeyedService<ITokenProvider>("acdc");
await tokenProvider.SaveTokensAsync(
    "initial-access-token",
    "initial-refresh-token",
    DateTimeOffset.UtcNow.AddHours(1),
    CancellationToken.None);

// Make an authenticated request — the Bearer token is injected automatically
var client = sp.GetRequiredService<AcdcHttpClient>();
var response = await client.GetAsync("/api/protected-resource");

Console.WriteLine($"Status: {response.StatusCode}");

// Logout clears tokens and optionally revokes on the server
await client.Auth!.LogoutAsync(CancellationToken.None);
Console.WriteLine("Logged out");
