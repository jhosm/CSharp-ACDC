namespace CSharpAcdc.Configuration;

public class AcdcLoggingOptions
{
    public TimeSpan SlowRequestThreshold { get; set; } = TimeSpan.FromSeconds(3);

    public long LargePayloadThreshold { get; set; } = 1_048_576; // 1 MiB

    public IReadOnlySet<string> SensitiveFields { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
    };
}
