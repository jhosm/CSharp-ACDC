using System.Collections.Immutable;
using CSharpAcdc.Configuration;

namespace CSharpAcdc.Builder;

/// <summary>
/// Immutable fluent builder for configuring the ACDC HTTP client pipeline.
/// Each method returns a new instance with the updated configuration.
/// </summary>
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

    /// <summary>
    /// Creates a new builder instance with default settings.
    /// </summary>
    /// <returns>A new <see cref="AcdcClientBuilder"/>.</returns>
    public static AcdcClientBuilder Create() => new();

    /// <summary>
    /// Configures OAuth 2.1 authentication for the client.
    /// </summary>
    /// <param name="configure">Action to configure authentication options.</param>
    /// <returns>A new builder with auth configured.</returns>
    public AcdcClientBuilder WithAuth(Action<AcdcAuthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return this with { AuthConfigure = configure };
    }

    /// <summary>
    /// Configures response caching with FusionCache.
    /// </summary>
    /// <param name="configure">Action to configure cache options.</param>
    /// <returns>A new builder with caching configured.</returns>
    public AcdcClientBuilder WithCache(Action<AcdcCacheOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return this with { CacheConfigure = configure };
    }

    /// <summary>
    /// Configures structured logging options.
    /// </summary>
    /// <param name="configure">Action to configure logging options.</param>
    /// <returns>A new builder with logging configured.</returns>
    public AcdcClientBuilder WithLogging(Action<AcdcLoggingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return this with { LoggingConfigure = configure };
    }

    /// <summary>
    /// Adds a custom <see cref="DelegatingHandler"/> to the pipeline.
    /// </summary>
    /// <typeparam name="T">The handler type, which must extend <see cref="DelegatingHandler"/>.</typeparam>
    /// <returns>A new builder with the custom handler registered.</returns>
    public AcdcClientBuilder WithCustomHandler<T>() where T : DelegatingHandler =>
        this with { CustomHandlerTypes = CustomHandlerTypes.Add(typeof(T)) };

    /// <summary>
    /// Sets the request timeout for the HTTP client.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    /// <returns>A new builder with the timeout configured.</returns>
    public AcdcClientBuilder WithTimeout(TimeSpan timeout) =>
        this with { Timeout = timeout };

    /// <summary>
    /// Sets the base address for all requests.
    /// </summary>
    /// <param name="baseAddress">The base URI.</param>
    /// <returns>A new builder with the base address configured.</returns>
    public AcdcClientBuilder WithBaseAddress(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        return this with { BaseAddress = baseAddress };
    }

    /// <summary>
    /// Sets the named client identifier used with <see cref="IHttpClientFactory"/>.
    /// </summary>
    /// <param name="clientName">The client name.</param>
    /// <returns>A new builder with the client name configured.</returns>
    public AcdcClientBuilder WithClientName(string clientName)
    {
        ArgumentNullException.ThrowIfNull(clientName);
        return this with { ClientName = clientName };
    }

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

            if (string.IsNullOrWhiteSpace(auth.RefreshEndpoint))
                throw new InvalidOperationException(
                    "AcdcAuthOptions.RefreshEndpoint is required when auth is configured.");
            if (string.IsNullOrWhiteSpace(auth.ClientId))
                throw new InvalidOperationException(
                    "AcdcAuthOptions.ClientId is required when auth is configured.");
            if (!Uri.TryCreate(auth.RefreshEndpoint, UriKind.Absolute, out _))
                throw new InvalidOperationException(
                    $"AcdcAuthOptions.RefreshEndpoint must be a valid absolute URI, got: '{auth.RefreshEndpoint}'");

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
