namespace CSharpAcdc.Configuration;

/// <summary>
/// Configuration options for OAuth 2.1 authentication.
/// </summary>
public sealed record AcdcAuthOptions
{
    /// <summary>
    /// Gets or sets the OAuth token endpoint URL (required).
    /// </summary>
    public required string RefreshEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the OAuth client ID (required).
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the OAuth client secret.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the time before token expiry to trigger proactive refresh. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan RefreshThreshold { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the maximum wait time for the concurrent refresh queue. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the OAuth token revocation endpoint for logout.
    /// </summary>
    public string? RevocationEndpoint { get; set; }
}
