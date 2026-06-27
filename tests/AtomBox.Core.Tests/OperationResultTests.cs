using AtomBox.Core.Errors;
using AtomBox.Core.Results;

namespace AtomBox.Core.Tests;

public sealed class OperationResultTests
{
    [Fact]
    public void GetValueOrThrow_ReturnsSuccessValue()
    {
        var result = OperationResult<string>.Success("ok");

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("ok", value);
        Assert.Equal("ok", result.GetValueOrThrow());
    }

    [Fact]
    public void GetValueOrThrow_ThrowsForFailure()
    {
        var result = OperationResult<string>.Failure(StorageError.Validation("bad input"));

        Assert.False(result.TryGetValue(out _));
        Assert.Throws<InvalidOperationException>(() => result.GetValueOrThrow());
    }
}
