using CSharpAcdc.Exceptions;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Exceptions;

public class AcdcCacheExceptionTests
{
    [Fact]
    public void ReadFailed_SetsCacheOperationRead()
    {
        var ex = AcdcCacheException.ReadFailed();

        ex.CacheOperation.Should().Be(CacheOperation.Read);
        ex.Message.Should().Contain("read");
    }

    [Fact]
    public void WriteFailed_SetsCacheOperationWrite()
    {
        var ex = AcdcCacheException.WriteFailed();

        ex.CacheOperation.Should().Be(CacheOperation.Write);
        ex.Message.Should().Contain("write");
    }

    [Fact]
    public void DeleteFailed_SetsCacheOperationDelete()
    {
        var ex = AcdcCacheException.DeleteFailed();

        ex.CacheOperation.Should().Be(CacheOperation.Delete);
        ex.Message.Should().Contain("delete");
    }

    [Fact]
    public void ClearFailed_SetsCacheOperationClear()
    {
        var ex = AcdcCacheException.ClearFailed();

        ex.CacheOperation.Should().Be(CacheOperation.Clear);
        ex.Message.Should().Contain("clear");
    }

    [Fact]
    public void Factory_PreservesInnerException()
    {
        var inner = new Exception("inner");
        var ex = AcdcCacheException.ReadFailed(inner);

        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void ToMap_IncludesCacheOperation()
    {
        var ex = AcdcCacheException.WriteFailed();

        var map = ex.ToMap();

        map.Should().ContainKey("cacheOperation")
            .WhoseValue.Should().Be("Write");
        map["type"].Should().Be("AcdcCacheException");
    }

    [Fact]
    public void StatusCode_IsNull()
    {
        var ex = AcdcCacheException.ReadFailed();
        ex.StatusCode.Should().BeNull();
    }

    [Fact]
    public void IsAcdcException()
    {
        var ex = AcdcCacheException.ReadFailed();
        (ex is AcdcException).Should().BeTrue();
    }
}
