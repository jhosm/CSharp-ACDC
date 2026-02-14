using System.Collections.Immutable;
using CSharpAcdc.Configuration;

namespace CSharpAcdc.Builder;

public record AcdcClientBuilder
{
    internal Action<AcdcAuthOptions>? AuthConfigure { get; private init; }
    internal Action<AcdcCacheOptions>? CacheConfigure { get; private init; }
    internal Action<AcdcLoggingOptions>? LoggingConfigure { get; private init; }
    internal ImmutableList<Type> CustomHandlerTypes { get; private init; } = [];
    internal TimeSpan? Timeout { get; private init; }
    internal Uri? BaseAddress { get; private init; }
    internal string ClientName { get; private init; } = "acdc";

    private AcdcClientBuilder() { }

    public static AcdcClientBuilder Create() => new();

    public AcdcClientBuilder WithAuth(Action<AcdcAuthOptions> configure) =>
        this with { AuthConfigure = configure };

    public AcdcClientBuilder WithCache(Action<AcdcCacheOptions> configure) =>
        this with { CacheConfigure = configure };

    public AcdcClientBuilder WithLogging(Action<AcdcLoggingOptions> configure) =>
        this with { LoggingConfigure = configure };

    public AcdcClientBuilder WithCustomHandler<T>() where T : DelegatingHandler =>
        this with { CustomHandlerTypes = CustomHandlerTypes.Add(typeof(T)) };

    public AcdcClientBuilder WithTimeout(TimeSpan timeout) =>
        this with { Timeout = timeout };

    public AcdcClientBuilder WithBaseAddress(Uri baseAddress) =>
        this with { BaseAddress = baseAddress };

    public AcdcClientBuilder WithClientName(string clientName) =>
        this with { ClientName = clientName };

    internal bool HasAuth => AuthConfigure is not null;
    internal bool HasCache => CacheConfigure is not null;

    internal IReadOnlyList<Type> GetCustomHandlerTypes() => CustomHandlerTypes;

    internal AcdcClientOptions BuildOptions()
    {
        var options = new AcdcClientOptions
        {
            BaseAddress = BaseAddress,
            Timeout = Timeout,
            ClientName = ClientName,
        };

        if (AuthConfigure is not null)
        {
            var auth = new AcdcAuthOptions { RefreshEndpoint = "", ClientId = "" };
            AuthConfigure(auth);
            options.Auth = auth;
        }

        if (CacheConfigure is not null)
        {
            var cache = new AcdcCacheOptions();
            CacheConfigure(cache);
            options.Cache = cache;
        }

        if (LoggingConfigure is not null)
        {
            LoggingConfigure(options.Logging);
        }

        return options;
    }
}
