namespace CSharpAcdc.Configuration;

public sealed record AcdcAuthOptions
{
    public required string RefreshEndpoint { get; init; }
    public required string ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public TimeSpan RefreshThreshold { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan QueueTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public string? RevocationEndpoint { get; init; }
}
