namespace CSharpAcdc.Configuration;

public sealed record AcdcAuthOptions
{
    public required string RefreshEndpoint { get; set; }
    public required string ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public TimeSpan RefreshThreshold { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public string? RevocationEndpoint { get; set; }
}
