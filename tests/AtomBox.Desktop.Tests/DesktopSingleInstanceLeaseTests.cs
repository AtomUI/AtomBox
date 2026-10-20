using AtomBox.Desktop.Composition;

namespace AtomBox.Desktop.Tests;

public sealed class DesktopSingleInstanceLeaseTests
{
    [Fact]
    public void OnlyOneDesktopLeaseCanBeHeldAndReleaseAllowsNextOwner()
    {
        Assert.True(DesktopSingleInstanceLease.TryAcquire(out var first));
        Assert.NotNull(first);

        try
        {
            Assert.False(DesktopSingleInstanceLease.TryAcquire(out var competing));
            Assert.Null(competing);
        }
        finally
        {
            first.Dispose();
        }

        Assert.True(DesktopSingleInstanceLease.TryAcquire(out var next));
        Assert.NotNull(next);
        next.Dispose();
    }
}
