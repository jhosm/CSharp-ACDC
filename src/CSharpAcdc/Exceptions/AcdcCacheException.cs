namespace CSharpAcdc.Exceptions;

/// <summary>
/// Exception thrown when a cache operation fails.
/// </summary>
public class AcdcCacheException : AcdcException
{
    /// <summary>
    /// Gets the cache operation that failed.
    /// </summary>
    public CacheOperation CacheOperation { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AcdcCacheException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="cacheOperation">The cache operation that failed.</param>
    /// <param name="innerException">The inner exception that caused this error.</param>
    public AcdcCacheException(
        string message,
        CacheOperation cacheOperation,
        Exception? innerException = null)
        : base(message, statusCode: null, responseBody: null, requestUrl: null, innerException: innerException)
    {
        CacheOperation = cacheOperation;
    }

    /// <summary>
    /// Creates an <see cref="AcdcCacheException"/> for a failed cache read.
    /// </summary>
    /// <param name="innerException">The inner exception that caused the read failure.</param>
    /// <returns>A new <see cref="AcdcCacheException"/> for a read failure.</returns>
    public static AcdcCacheException ReadFailed(Exception? innerException = null)
        => new("Cache read failed", CacheOperation.Read, innerException);

    /// <summary>
    /// Creates an <see cref="AcdcCacheException"/> for a failed cache write.
    /// </summary>
    /// <param name="innerException">The inner exception that caused the write failure.</param>
    /// <returns>A new <see cref="AcdcCacheException"/> for a write failure.</returns>
    public static AcdcCacheException WriteFailed(Exception? innerException = null)
        => new("Cache write failed", CacheOperation.Write, innerException);

    /// <summary>
    /// Creates an <see cref="AcdcCacheException"/> for a failed cache delete.
    /// </summary>
    /// <param name="innerException">The inner exception that caused the delete failure.</param>
    /// <returns>A new <see cref="AcdcCacheException"/> for a delete failure.</returns>
    public static AcdcCacheException DeleteFailed(Exception? innerException = null)
        => new("Cache delete failed", CacheOperation.Delete, innerException);

    /// <summary>
    /// Creates an <see cref="AcdcCacheException"/> for a failed cache clear.
    /// </summary>
    /// <param name="innerException">The inner exception that caused the clear failure.</param>
    /// <returns>A new <see cref="AcdcCacheException"/> for a clear failure.</returns>
    public static AcdcCacheException ClearFailed(Exception? innerException = null)
        => new("Cache clear failed", CacheOperation.Clear, innerException);

    /// <inheritdoc />
    public override Dictionary<string, object?> ToMap()
    {
        var map = base.ToMap();
        map["cacheOperation"] = CacheOperation.ToString();
        return map;
    }
}
