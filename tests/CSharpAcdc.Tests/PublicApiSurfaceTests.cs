using System.Reflection;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests;

public class PublicApiSurfaceTests
{
    private static readonly HashSet<string> ExpectedPublicTypes =
    [
        // Exceptions
        "CSharpAcdc.Exceptions.AcdcException",
        "CSharpAcdc.Exceptions.AcdcAuthException",
        "CSharpAcdc.Exceptions.AcdcClientException",
        "CSharpAcdc.Exceptions.AcdcServerException",
        "CSharpAcdc.Exceptions.AcdcNetworkException",
        "CSharpAcdc.Exceptions.AcdcCacheException",
        "CSharpAcdc.Exceptions.NetworkErrorType",
        "CSharpAcdc.Exceptions.CacheOperation",

        // Configuration
        "CSharpAcdc.Configuration.AcdcAuthOptions",
        "CSharpAcdc.Configuration.AcdcCacheOptions",
        "CSharpAcdc.Configuration.AcdcLoggingOptions",
        "CSharpAcdc.Configuration.AcdcDeduplicationOptions",
        "CSharpAcdc.Configuration.AcdcClientOptions",

        // Auth
        "CSharpAcdc.Auth.AcdcAuthManager",
        "CSharpAcdc.Auth.BackoffManager",
        "CSharpAcdc.Auth.ITokenProvider",
        "CSharpAcdc.Auth.ITokenRefreshStrategy",
        "CSharpAcdc.Auth.InMemoryTokenProvider",
        "CSharpAcdc.Auth.OAuthTokenRefreshStrategy",
        "CSharpAcdc.Auth.CustomTokenRefreshStrategy",
        "CSharpAcdc.Auth.TokenRefreshResult",
        "CSharpAcdc.Auth.UserIdExtractor",

        // Cache
        "CSharpAcdc.Cache.IAcdcCacheManager",
        "CSharpAcdc.Cache.AcdcCacheManager",
        "CSharpAcdc.Cache.CacheKeyBuilder",
        "CSharpAcdc.Cache.CacheKeyStrategy",
        "CSharpAcdc.Cache.CachedResponse",

        // Cancellation
        "CSharpAcdc.Cancellation.ActiveRequestTracker",

        // Handlers
        "CSharpAcdc.Handlers.LoggingHandler",
        "CSharpAcdc.Handlers.ErrorHandler",
        "CSharpAcdc.Handlers.CancellationHandler",
        "CSharpAcdc.Handlers.AuthHandler",
        "CSharpAcdc.Handlers.CacheHandler",
        "CSharpAcdc.Handlers.DeduplicationHandler",

        // Extensions
        "CSharpAcdc.Extensions.AcdcRequestOptions",
        "CSharpAcdc.Extensions.HttpRequestMessageExtensions",
        "CSharpAcdc.Extensions.ServiceCollectionExtensions",

        // Builder
        "CSharpAcdc.Builder.AcdcClientBuilder",

        // Client
        "CSharpAcdc.Client.AcdcHttpClient",

        // Logging
        "CSharpAcdc.Logging.SensitiveDataRedactor",
    ];

    [Fact]
    public void Assembly_ContainsAllExpectedPublicTypes()
    {
        var assembly = typeof(CSharpAcdc.Client.AcdcHttpClient).Assembly;
        var actualPublicTypes = GetPublicTypeNames(assembly);

        foreach (var expected in ExpectedPublicTypes)
        {
            actualPublicTypes.Should().Contain(expected,
                because: $"'{expected}' is part of the public API contract");
        }
    }

    [Fact]
    public void Assembly_DoesNotExposeUnexpectedPublicTypes()
    {
        var assembly = typeof(CSharpAcdc.Client.AcdcHttpClient).Assembly;
        var actualPublicTypes = GetPublicTypeNames(assembly);

        var unexpected = actualPublicTypes.Except(ExpectedPublicTypes).ToList();

        unexpected.Should().BeEmpty(
            because: "only types in the public API contract should be public. " +
                     $"Unexpected types found: {string.Join(", ", unexpected)}");
    }

    private static HashSet<string> GetPublicTypeNames(Assembly assembly)
    {
        return assembly.GetExportedTypes()
            .Where(t => !t.IsNested) // exclude nested types (private test doubles, etc.)
            .Select(t => t.FullName!)
            .ToHashSet();
    }
}
