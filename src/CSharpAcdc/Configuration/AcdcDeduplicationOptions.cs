namespace CSharpAcdc.Configuration;

public record AcdcDeduplicationOptions
{
    // Currently deduplication has no configurable options.
    // This record exists for forward compatibility and consistency
    // with the other handler option records (AcdcAuthOptions, etc.).
}
