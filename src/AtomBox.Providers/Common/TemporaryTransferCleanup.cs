using AtomBox.Core.Errors;
using AtomBox.Core.Results;

namespace AtomBox.Providers.Common;

internal static class TemporaryTransferCleanup
{
    public static async Task<OperationResult> RunAsync(
        Func<CancellationToken, Task> cleanupAsync,
        string temporaryPath,
        int confirmationAttempts = 1,
        TimeSpan? confirmationDelay = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(confirmationAttempts, 1);
        var delay = confirmationDelay ?? TimeSpan.Zero;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            Exception? lastException = null;
            // A canceled upload can become visible remotely shortly after the client request exits.
            // Repeating the idempotent cleanup closes that server-side finalization race.
            for (var attempt = 0; attempt < confirmationAttempts; attempt++)
            {
                try
                {
                    await cleanupAsync(cleanup.Token).ConfigureAwait(false);
                    lastException = null;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                }

                if (attempt < confirmationAttempts - 1 && delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cleanup.Token).ConfigureAwait(false);
                }
            }

            if (lastException is not null)
            {
                throw lastException;
            }

            return OperationResult.Success();
        }
        catch (Exception exception)
        {
            var mapped = ProviderErrorMapper.FromException(exception);
            return OperationResult.Failure(new StorageError(
                mapped.Code,
                $"Temporary remote file cleanup failed for '{temporaryPath}': {mapped.Message}",
                mapped.Category,
                mapped.IsRetryable,
                mapped.ProviderErrorCode));
        }
    }

    public static OperationResult AppendWarning(
        OperationResult operationResult,
        OperationResult cleanupResult)
    {
        if (cleanupResult.IsSuccess)
        {
            return operationResult;
        }

        var original = operationResult.Error ?? new StorageError(
            StorageErrorCode.OperationCanceled,
            "Provider operation was canceled.",
            StorageErrorCategory.Canceled,
            isRetryable: true);
        return OperationResult.Failure(new StorageError(
            original.Code,
            $"{original.Message} {cleanupResult.Error?.Message}",
            original.Category,
            original.IsRetryable,
            original.ProviderErrorCode));
    }
}
