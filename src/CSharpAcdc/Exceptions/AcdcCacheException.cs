namespace CSharpAcdc.Exceptions;

public class AcdcCacheException : AcdcException
{
    public CacheOperation CacheOperation { get; }

    public AcdcCacheException(
        string message,
        CacheOperation cacheOperation,
        Exception? innerException = null)
        : base(message, statusCode: null, responseBody: null, requestUrl: null, innerException: innerException)
    {
        CacheOperation = cacheOperation;
    }

    public static AcdcCacheException ReadFailed(Exception? innerException = null)
        => new("Cache read failed", CacheOperation.Read, innerException);

    public static AcdcCacheException WriteFailed(Exception? innerException = null)
        => new("Cache write failed", CacheOperation.Write, innerException);

    public static AcdcCacheException DeleteFailed(Exception? innerException = null)
        => new("Cache delete failed", CacheOperation.Delete, innerException);

    public static AcdcCacheException ClearFailed(Exception? innerException = null)
        => new("Cache clear failed", CacheOperation.Clear, innerException);

    public override Dictionary<string, object?> ToMap()
    {
        var map = base.ToMap();
        map["cacheOperation"] = CacheOperation.ToString();
        return map;
    }
}
