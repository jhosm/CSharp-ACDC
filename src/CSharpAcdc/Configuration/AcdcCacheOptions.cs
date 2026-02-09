using CSharpAcdc.Cache;

namespace CSharpAcdc.Configuration;

public record AcdcCacheOptions
{
    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan? FailSafeMaxDuration { get; init; }
    public TimeSpan? FactorySoftTimeout { get; init; }
    public bool AllowTimedOutFactoryBackgroundCompletion { get; init; } = true;
    public CacheKeyStrategy CacheKeyStrategy { get; init; } = CacheKeyStrategy.Shared;
    public bool ETagEnabled { get; init; } = true;
}
