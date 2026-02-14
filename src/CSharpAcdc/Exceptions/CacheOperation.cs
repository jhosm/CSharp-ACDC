namespace CSharpAcdc.Exceptions;

/// <summary>
/// Identifies the cache operation that failed.
/// </summary>
public enum CacheOperation
{
    /// <summary>
    /// A cache read (get) operation.
    /// </summary>
    Read,

    /// <summary>
    /// A cache write (set) operation.
    /// </summary>
    Write,

    /// <summary>
    /// A cache delete (remove) operation.
    /// </summary>
    Delete,

    /// <summary>
    /// A cache clear (purge all) operation.
    /// </summary>
    Clear,

    /// <summary>
    /// A cache serialization or deserialization operation.
    /// </summary>
    Serialize,
}
