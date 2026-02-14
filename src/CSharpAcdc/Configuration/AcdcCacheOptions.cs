using CSharpAcdc.Cache;

namespace CSharpAcdc.Configuration;

public record AcdcCacheOptions
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan? FailSafeMaxDuration { get; set; }
    public TimeSpan? FactorySoftTimeout { get; set; }
    public bool AllowTimedOutFactoryBackgroundCompletion { get; set; } = true;
    public CacheKeyStrategy CacheKeyStrategy { get; set; } = CacheKeyStrategy.Shared;
    public bool ETagEnabled { get; set; } = true;
}
