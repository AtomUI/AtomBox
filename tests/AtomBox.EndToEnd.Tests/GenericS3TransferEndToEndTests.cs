using AtomBox.Application.Accounts;
using AtomBox.Application.Transfers;
using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Fingerprints;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using System.Security.Cryptography;
using System.Text;

namespace AtomBox.EndToEnd.Tests;

public sealed class GenericS3TransferEndToEndTests
{
    [Fact]
    [Trait("Category", "ManualIntegration")]
    public async Task Transfer_InfrastructureFingerprintBatchCancelAndRetry_WorkAgainstMinio()
    {
        await using var context = await GenericS3EndToEndContext.CreateAsync();
        if (context is null)
        {
            return;
        }

        var transfers = context.GetRequiredService<TransferAppService>();
        var settings = context.GetRequiredService<IApplicationSettingsRepository>();
        GenericS3EndToEndContext.AssertSuccess(
            await settings.SaveAsync(new ApplicationSettings(
                6,
                TransferOverwritePolicy.Overwrite,
                KeepCompletedTransfers: true,
                EnableUploadFingerprintIndex: false)),
            "Disable fingerprint index for baseline transfer");

        var roundtripPayload = Encoding.UTF8.GetBytes($"AtomBox Generic S3 transfer {context.RunId}\n中文内容\n");
        var uploadLocalPath = Path.Combine(context.ArtifactRoot, "roundtrip-source.txt");
        var downloadLocalPath = Path.Combine(context.ArtifactRoot, "roundtrip-downloaded.txt");
        await File.WriteAllBytesAsync(uploadLocalPath, roundtripPayload);
        var remotePath = context.ObjectPath("transfers/roundtrip.txt");

        var disabledPreparation = await transfers.PrepareBatchUploadTasksAsync(new PrepareBatchUploadTasksRequest(
            context.Account.Id,
            [new UploadTaskTarget(new LocalPath(uploadLocalPath), remotePath)]));
        GenericS3EndToEndContext.AssertSuccess(disabledPreparation, "Prepare upload with fingerprint disabled");
        Assert.False(disabledPreparation.GetValueOrThrow().IsFingerprintIndexEnabled);
        Assert.Null(disabledPreparation.GetValueOrThrow().Targets[0].Fingerprint);

        var upload = await transfers.CreateBatchUploadTasksAsync(new CreateBatchUploadTasksRequest(
            context.Account.Id,
            [new UploadTaskTarget(new LocalPath(uploadLocalPath), remotePath)],
            TransferOverwritePolicy.Overwrite));
        GenericS3EndToEndContext.AssertSuccess(upload, "Upload through TransferAppService");
        await AssertTasksSucceededAsync(transfers, upload.GetValueOrThrow().Tasks.Select(item => item.Id));

        var download = await transfers.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
            context.Account.Id,
            [new DownloadTaskTarget(remotePath, new LocalPath(downloadLocalPath))],
            TransferOverwritePolicy.Overwrite));
        GenericS3EndToEndContext.AssertSuccess(download, "Download through TransferAppService");
        await AssertTasksSucceededAsync(transfers, download.GetValueOrThrow().Tasks.Select(item => item.Id));
        Assert.Equal(SHA256.HashData(roundtripPayload), SHA256.HashData(await File.ReadAllBytesAsync(downloadLocalPath)));

        await VerifyFingerprintLifecycleAsync(context, transfers, settings, uploadLocalPath);
        await VerifyEightTaskBatchDrainsAsync(context, transfers);
        await VerifyPendingCancelAsync(context, transfers);
        await VerifyFailedTaskRetryAfterCredentialCorrectionAsync(context, transfers);

        var queue = await transfers.GetQueueAsync(new GetTransferQueueRequest());
        GenericS3EndToEndContext.AssertSuccess(queue, "Read final transfer queue");
        Assert.Empty(queue.GetValueOrThrow().Tasks);

        var history = await transfers.GetHistoryAsync(new GetTransferHistoryRequest());
        GenericS3EndToEndContext.AssertSuccess(history, "Read final transfer history");
        Assert.Contains(history.GetValueOrThrow().Tasks, item => item.Task.Status == TransferStatus.Succeeded);
        Assert.Contains(history.GetValueOrThrow().Tasks, item => item.Task.Status == TransferStatus.Canceled);

        var fingerprintStats = await context.GetRequiredService<IFileFingerprintIndexStore>().GetStatisticsAsync();
        GenericS3EndToEndContext.AssertSuccess(fingerprintStats, "Read fingerprint index statistics");
        Assert.True(fingerprintStats.GetValueOrThrow().RecordCount > 0);

        await context.WriteEvidenceManifestAsync(new Dictionary<string, object?>
        {
            ["transferRoundtrip"] = "passed",
            ["batchTaskCount"] = 8,
            ["pendingCancellation"] = "passed",
            ["failedTaskRetry"] = "passed",
            ["fingerprintRecords"] = fingerprintStats.GetValueOrThrow().RecordCount
        });
    }

    private static async Task VerifyFingerprintLifecycleAsync(
        GenericS3EndToEndContext context,
        TransferAppService transfers,
        IApplicationSettingsRepository settings,
        string localPath)
    {
        GenericS3EndToEndContext.AssertSuccess(
            await settings.SaveAsync(new ApplicationSettings(
                6,
                TransferOverwritePolicy.Overwrite,
                KeepCompletedTransfers: true,
                EnableUploadFingerprintIndex: true)),
            "Enable fingerprint index");

        var remotePath = context.ObjectPath("transfers/fingerprint-indexed.txt");
        var request = new PrepareBatchUploadTasksRequest(
            context.Account.Id,
            [new UploadTaskTarget(new LocalPath(localPath), remotePath)]);
        var prepared = await transfers.PrepareBatchUploadTasksAsync(request);
        GenericS3EndToEndContext.AssertSuccess(prepared, "Calculate upload fingerprint");
        Assert.True(prepared.GetValueOrThrow().IsFingerprintIndexEnabled);
        var target = Assert.Single(prepared.GetValueOrThrow().Targets);
        Assert.NotNull(target.Fingerprint);
        Assert.Empty(target.HistoricalRecords);

        var upload = await transfers.CreateBatchUploadTasksAsync(new CreateBatchUploadTasksRequest(
            context.Account.Id,
            [new UploadTaskTarget(target.LocalPath, target.RemotePath, target.Fingerprint)],
            TransferOverwritePolicy.Overwrite));
        GenericS3EndToEndContext.AssertSuccess(upload, "Upload with fingerprint metadata");

        var duplicate = await transfers.PrepareBatchUploadTasksAsync(request);
        GenericS3EndToEndContext.AssertSuccess(duplicate, "Query prior upload fingerprint");
        var duplicateTarget = Assert.Single(duplicate.GetValueOrThrow().Targets);
        Assert.Contains(duplicateTarget.HistoricalRecords, item => item.RemotePath == remotePath);
    }

    private static async Task VerifyEightTaskBatchDrainsAsync(
        GenericS3EndToEndContext context,
        TransferAppService transfers)
    {
        var targets = new List<UploadTaskTarget>();
        for (var index = 0; index < 8; index++)
        {
            var local = Path.Combine(context.ArtifactRoot, $"batch-{index:D2}.bin");
            var payload = new byte[128 * 1024];
            new Random(index + 500).NextBytes(payload);
            await File.WriteAllBytesAsync(local, payload);
            targets.Add(new UploadTaskTarget(
                new LocalPath(local),
                context.ObjectPath($"transfers/batch/batch-{index:D2}.bin")));
        }

        var result = await transfers.CreateBatchUploadTasksAsync(new CreateBatchUploadTasksRequest(
            context.Account.Id,
            targets,
            TransferOverwritePolicy.Overwrite));
        GenericS3EndToEndContext.AssertSuccess(result, "Run eight uploads with concurrency six");
        Assert.Equal(8, result.GetValueOrThrow().Tasks.Count);
        await AssertTasksSucceededAsync(transfers, result.GetValueOrThrow().Tasks.Select(item => item.Id));
    }

    private static async Task VerifyPendingCancelAsync(
        GenericS3EndToEndContext context,
        TransferAppService transfers)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = new TransferTask(
            TransferTaskId.New(),
            context.Account.Id,
            TransferDirection.Upload,
            new LocalPath(Path.Combine(context.ArtifactRoot, "not-opened-pending.bin")),
            context.ObjectPath("transfers/canceled-before-start.bin"),
            TransferStatus.Pending,
            new TransferOptions(TransferOverwritePolicy.Overwrite),
            now,
            now);
        var scheduler = context.GetRequiredService<ITransferTaskScheduler>();
        GenericS3EndToEndContext.AssertSuccess(await scheduler.SubmitAsync(pending), "Persist pending transfer");
        GenericS3EndToEndContext.AssertSuccess(
            await transfers.CancelAsync(new CancelTransferTaskRequest(pending.Id)),
            "Cancel pending transfer");

        var history = await transfers.GetHistoryAsync(new GetTransferHistoryRequest());
        GenericS3EndToEndContext.AssertSuccess(history, "Read canceled transfer history");
        Assert.Contains(history.GetValueOrThrow().Tasks, item =>
            item.Task.Id == pending.Id && item.Task.Status == TransferStatus.Canceled);
    }

    private static async Task VerifyFailedTaskRetryAfterCredentialCorrectionAsync(
        GenericS3EndToEndContext context,
        TransferAppService transfers)
    {
        var credentials = context.GetRequiredService<ICredentialStore>();
        var wrongCredential = await credentials.SaveAsync(new CredentialSecretMaterial(
            new Dictionary<string, string>
            {
                ["accessKeyId"] = context.Environment.AccessKeyId,
                ["accessKeySecret"] = context.Environment.AccessKeySecret + "-invalid"
            }));
        GenericS3EndToEndContext.AssertSuccess(wrongCredential, "Persist invalid retry credential");

        var accounts = context.GetRequiredService<AccountAppService>();
        var originalCredential = context.Account.CredentialRef;
        var invalidUpdate = await accounts.UpdateAsync(new UpdateStorageAccountRequest(
            context.Account.Id,
            context.Account.DisplayName,
            context.Account.Endpoint,
            context.Account.Region,
            wrongCredential.GetValueOrThrow(),
            context.Account.ProviderConfig));
        GenericS3EndToEndContext.AssertSuccess(invalidUpdate, "Switch account to invalid credential");
        await RefreshAccountAsync(context);

        var local = Path.Combine(context.ArtifactRoot, "retry-source.txt");
        await File.WriteAllTextAsync(local, "retry-after-credential-correction", Encoding.UTF8);
        var remote = context.ObjectPath("transfers/retry-after-auth-failure.txt");
        var failedCreate = await transfers.CreateBatchUploadTasksAsync(new CreateBatchUploadTasksRequest(
            context.Account.Id,
            [new UploadTaskTarget(new LocalPath(local), remote)],
            TransferOverwritePolicy.Overwrite));
        Assert.True(failedCreate.IsFailure);

        var failedHistory = await transfers.GetHistoryAsync(new GetTransferHistoryRequest());
        GenericS3EndToEndContext.AssertSuccess(failedHistory, "Read failed transfer history");
        var failedTask = Assert.Single(
            failedHistory.GetValueOrThrow().Tasks,
            item => item.Task.RemotePath == remote && item.Task.Status == TransferStatus.Failed);

        var validUpdate = await accounts.UpdateAsync(new UpdateStorageAccountRequest(
            context.Account.Id,
            context.Account.DisplayName,
            context.Account.Endpoint,
            context.Account.Region,
            originalCredential,
            context.Account.ProviderConfig));
        GenericS3EndToEndContext.AssertSuccess(validUpdate, "Restore valid credential");
        await RefreshAccountAsync(context);

        GenericS3EndToEndContext.AssertSuccess(
            await transfers.RetryAsync(new RetryTransferTaskRequest(failedTask.Task.Id)),
            "Retry failed transfer after credential correction");
        var retriedHistory = await transfers.GetHistoryAsync(new GetTransferHistoryRequest());
        GenericS3EndToEndContext.AssertSuccess(retriedHistory, "Read retried transfer history");
        Assert.Contains(retriedHistory.GetValueOrThrow().Tasks, item =>
            item.Task.Id == failedTask.Task.Id && item.Task.Status == TransferStatus.Succeeded);
    }

    private static async Task RefreshAccountAsync(GenericS3EndToEndContext context)
    {
        var result = await context.GetRequiredService<IStorageAccountRepository>().GetByIdAsync(context.Account.Id);
        GenericS3EndToEndContext.AssertSuccess(result, "Refresh account after update");
        context.Account = result.GetValueOrThrow();
    }

    private static async Task AssertTasksSucceededAsync(
        TransferAppService transfers,
        IEnumerable<TransferTaskId> taskIds)
    {
        var expected = taskIds.ToHashSet();
        var history = await transfers.GetHistoryAsync(new GetTransferHistoryRequest());
        GenericS3EndToEndContext.AssertSuccess(history, "Read transfer history");
        var actual = history.GetValueOrThrow().Tasks
            .Where(item => expected.Contains(item.Task.Id) && item.Task.Status == TransferStatus.Succeeded)
            .Select(item => item.Task.Id)
            .ToHashSet();
        Assert.True(expected.SetEquals(actual), "All submitted transfer tasks must finish successfully.");
    }
}
