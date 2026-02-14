using CSharpAcdc.Auth;
using CSharpAcdc.Builder;
using CSharpAcdc.Cache;
using CSharpAcdc.Cancellation;
using CSharpAcdc.Client;
using CSharpAcdc.Configuration;
using CSharpAcdc.Handlers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace CSharpAcdc.Extensions;

public static class ServiceCollectionExtensions
{
    public static IHttpClientBuilder AddAcdcHttpClient(this IServiceCollection services)
        => services.AddAcdcHttpClient("acdc", null);

    public static IHttpClientBuilder AddAcdcHttpClient(
        this IServiceCollection services,
        Func<AcdcClientBuilder, AcdcClientBuilder> configure)
        => services.AddAcdcHttpClient("acdc", configure);

    public static IHttpClientBuilder AddAcdcHttpClient(
        this IServiceCollection services,
        string clientName,
        Func<AcdcClientBuilder, AcdcClientBuilder>? configure = null)
    {
        var builder = AcdcClientBuilder.Create();
        if (configure is not null)
            builder = configure(builder);
        return services.AddAcdcHttpClientCore(
            clientName, builder.BuildOptions(), builder.GetCustomHandlerTypes());
    }

    public static IHttpClientBuilder AddAcdcHttpClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new AcdcClientOptions();
        configuration.Bind(options);

        if (options.Auth is not null)
        {
            if (string.IsNullOrWhiteSpace(options.Auth.RefreshEndpoint))
                throw new InvalidOperationException(
                    "Configuration section 'Auth' is present but 'Auth:RefreshEndpoint' is missing or empty.");
            if (string.IsNullOrWhiteSpace(options.Auth.ClientId))
                throw new InvalidOperationException(
                    "Configuration section 'Auth' is present but 'Auth:ClientId' is missing or empty.");
        }

        return services.AddAcdcHttpClientCore(options.ClientName, options, []);
    }

    private static IHttpClientBuilder AddAcdcHttpClientCore(
        this IServiceCollection services,
        string clientName,
        AcdcClientOptions options,
        IReadOnlyList<Type> customHandlerTypes)
    {
        var hasAuth = options.Auth is not null;
        var hasCache = options.Cache is not null;

        // -- Per-client supporting services (keyed by client name) --

        services.TryAddKeyedSingleton<ActiveRequestTracker>(clientName);

        if (hasAuth)
        {
            services.TryAddKeyedSingleton<BackoffManager>(clientName);
            services.TryAddKeyedSingleton<ITokenProvider, InMemoryTokenProvider>(clientName);
            services.TryAddKeyedSingleton<UserIdExtractor>(clientName,
                (sp, _) => new UserIdExtractor(sp.GetService<IHttpContextAccessor>()));
            services.TryAddKeyedSingleton<ITokenRefreshStrategy>(clientName,
                (sp, _) => new OAuthTokenRefreshStrategy(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    Options.Create(options.Auth!)));
            services.TryAddKeyedSingleton(clientName,
                (sp, _) => new AcdcAuthManager(
                    sp.GetRequiredKeyedService<ITokenProvider>(clientName),
                    sp.GetRequiredKeyedService<ITokenRefreshStrategy>(clientName),
                    sp.GetRequiredKeyedService<BackoffManager>(clientName),
                    sp.GetRequiredService<IHttpClientFactory>(),
                    Options.Create(options.Auth!),
                    sp.GetRequiredService<ILogger<AcdcAuthManager>>(),
                    sp.GetRequiredKeyedService<UserIdExtractor>(clientName)));
        }

        if (hasCache)
        {
            services.AddFusionCache(clientName)
                .WithDefaultEntryOptions(BuildDefaultCacheEntryOptions(options.Cache!));

            services.TryAddKeyedSingleton<IAcdcCacheManager>(clientName,
                (sp, _) =>
                {
                    var cacheProvider = sp.GetRequiredService<IFusionCacheProvider>();
                    return new AcdcCacheManager(
                        cacheProvider.GetCache(clientName),
                        sp.GetRequiredService<ILogger<AcdcCacheManager>>());
                });
        }

        // -- AcdcHttpClient factory (keyed + non-keyed convenience) --

        services.TryAddKeyedTransient(clientName,
            (sp, _) =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
                var authManager = hasAuth
                    ? sp.GetRequiredKeyedService<AcdcAuthManager>(clientName) : null;
                var cacheManager = hasCache
                    ? sp.GetRequiredKeyedService<IAcdcCacheManager>(clientName) : null;
                var tracker = sp.GetRequiredKeyedService<ActiveRequestTracker>(clientName);
                return new AcdcHttpClient(httpClient, authManager, cacheManager, tracker);
            });

        // Non-keyed forwarding for default resolution: only for the default "acdc" client
        if (string.Equals(clientName, "acdc", StringComparison.OrdinalIgnoreCase))
        {
            services.TryAddTransient(sp =>
                sp.GetRequiredKeyedService<AcdcHttpClient>(clientName));
        }

        // -- Named HttpClient with handler pipeline --

        var httpClientBuilder = services.AddHttpClient(clientName, httpClient =>
        {
            if (options.BaseAddress is not null)
                httpClient.BaseAddress = options.BaseAddress;
            if (options.Timeout is not null)
                httpClient.Timeout = options.Timeout.Value;
        });

        // Pipeline order: Logging → Error → Cancellation → Auth → Cache → Custom → Dedup

        httpClientBuilder.AddHttpMessageHandler(sp =>
            new LoggingHandler(
                sp.GetRequiredService<ILogger<LoggingHandler>>(),
                Options.Create(options.Logging)));

        httpClientBuilder.AddHttpMessageHandler(_ => new ErrorHandler());

        httpClientBuilder.AddHttpMessageHandler(sp =>
            new CancellationHandler(
                sp.GetRequiredKeyedService<ActiveRequestTracker>(clientName)));

        if (hasAuth)
        {
            httpClientBuilder.AddHttpMessageHandler(sp =>
                new AuthHandler(
                    sp.GetRequiredKeyedService<ITokenProvider>(clientName),
                    sp.GetRequiredKeyedService<ITokenRefreshStrategy>(clientName),
                    sp.GetRequiredKeyedService<BackoffManager>(clientName),
                    Options.Create(options.Auth!),
                    sp.GetRequiredService<ILogger<AuthHandler>>()));
        }

        if (hasCache)
        {
            httpClientBuilder.AddHttpMessageHandler(sp =>
            {
                var cacheProvider = sp.GetRequiredService<IFusionCacheProvider>();
                var fusionCache = cacheProvider.GetCache(clientName);

                Func<HttpRequestMessage, string?>? userIdProvider = hasAuth
                    ? req => sp.GetRequiredKeyedService<UserIdExtractor>(clientName)
                        .ExtractUserId(req)
                    : null;

                return new CacheHandler(
                    fusionCache,
                    Options.Create(options.Cache!),
                    sp.GetRequiredService<ILogger<CacheHandler>>(),
                    userIdProvider,
                    sp.GetRequiredKeyedService<IAcdcCacheManager>(clientName));
            });
        }

        foreach (var handlerType in customHandlerTypes)
        {
            httpClientBuilder.AddHttpMessageHandler(sp =>
            {
                try
                {
                    return (DelegatingHandler)ActivatorUtilities.CreateInstance(sp, handlerType);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to create custom handler '{handlerType.FullName}' registered via " +
                        $"WithCustomHandler<{handlerType.Name}>() in the ACDC pipeline for client '{clientName}'. " +
                        "Ensure all constructor dependencies are registered in DI.", ex);
                }
            });
        }

        httpClientBuilder.AddHttpMessageHandler(_ => new DeduplicationHandler());

        return httpClientBuilder;
    }

    private static FusionCacheEntryOptions BuildDefaultCacheEntryOptions(AcdcCacheOptions cacheOptions)
    {
        var entryOptions = new FusionCacheEntryOptions
        {
            Duration = cacheOptions.Duration,
            AllowTimedOutFactoryBackgroundCompletion =
                cacheOptions.AllowTimedOutFactoryBackgroundCompletion,
        };

        if (cacheOptions.FailSafeMaxDuration.HasValue)
        {
            entryOptions.IsFailSafeEnabled = true;
            entryOptions.FailSafeMaxDuration = cacheOptions.FailSafeMaxDuration.Value;
        }

        if (cacheOptions.FactorySoftTimeout.HasValue)
        {
            entryOptions.FactorySoftTimeout = cacheOptions.FactorySoftTimeout.Value;
        }

        return entryOptions;
    }
}
