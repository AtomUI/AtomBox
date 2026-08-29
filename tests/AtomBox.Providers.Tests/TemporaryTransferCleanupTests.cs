using AtomBox.Core.Errors;
using AtomBox.Providers.Common;
using AtomBox.Providers.FileTransfer.WebDav;

namespace AtomBox.Providers.Tests;

public sealed class TemporaryTransferCleanupTests
{
    [Fact]
    public async Task RunAsync_TransientFailuresThenSuccess_CompletesAllConfirmationAttempts()
    {
        var attempts = 0;

        var result = await TemporaryTransferCleanup.RunAsync(
            _ =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException(new IOException("Remote upload is still finalizing."))
                    : Task.CompletedTask;
            },
            "/remote/.atombox-upload-test.part",
            confirmationAttempts: 4);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, attempts);
    }

    [Fact]
    public async Task RunAsync_AllAttemptsFail_ReturnsMappedCleanupFailure()
    {
        var attempts = 0;

        var result = await TemporaryTransferCleanup.RunAsync(
            _ =>
            {
                attempts++;
                return Task.FromException(new WebDavHttpException(423, "Locked"));
            },
            "/remote/.atombox-upload-test.part",
            confirmationAttempts: 3);

        Assert.True(result.IsFailure);
        Assert.Equal(3, attempts);
        Assert.Equal(StorageErrorCategory.Authorization, result.Error?.Category);
        Assert.Contains("cleanup failed", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
    }
}
