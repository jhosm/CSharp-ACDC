using System.Collections.Frozen;

namespace CSharpAcdc.Configuration;

/// <summary>
/// Configuration options for structured HTTP request/response logging.
/// </summary>
public record AcdcLoggingOptions
{
    /// <summary>
    /// Gets or sets the threshold for slow request warnings. Defaults to 3 seconds.
    /// </summary>
    public TimeSpan SlowRequestThreshold { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Gets or sets the threshold for large payload alerts in bytes. Defaults to 1 MiB.
    /// </summary>
    public long LargePayloadThreshold { get; set; } = 1_048_576; // 1 MiB

    /// <summary>
    /// Gets or sets the set of header/field names to redact in logs.
    /// </summary>
    public IReadOnlySet<string> SensitiveFields { get; set; } = DefaultSensitiveFields;

    /// <summary>
    /// The default set of sensitive field names that are redacted in logs.
    /// </summary>
    public static readonly IReadOnlySet<string> DefaultSensitiveFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "password",
        "token",
        "secret",
        "key",
        "credential",
        "access_token",
        "refresh_token",
        "client_secret",
        "api_key",
        "private_key",
        "session_id",
        "x-csrf-token",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}
