using AtomBox.Application.Transfers;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using System.Diagnostics;

namespace AtomBox.EndToEnd.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class HeadlessTransferCancellationCollection
{
    public const string CollectionName = "Headless transfer cancellation";
}

[Collection(HeadlessTransferCancellationCollection.CollectionName)]
public sealed class HeadlessTransferCancellationEndToEndTests
{
    private const int RegularUploadLength = 4 * 1024 * 1024;
    private const int MultipartUploadLength = 12 * 1024 * 1024;
    private const int DownloadLength = 8 * 1024 * 1024;

    [Theory]
    [InlineData("generic-s3")]
    [InlineData("tencent-cos")]
    [InlineData("qiniu-kodo")]
    [InlineData("ftp")]
    [InlineData("sftp")]
    [InlineData("webdav")]
    [Trait("Category", "RealQueueCancellationIntegration")]
    public async Task QueueCancellation_TraversesApplicationPersistenceSchedulerWorkerAndRealProvider(string providerKey)
    {
        await using var context = await HeadlessTransferCancellationContext.CreateAsync(providerKey);
        if (context is null)
        {
            return;
        }

        var transfers = context.GetRequiredService<TransferAppService>();
        await VerifyPendingCancellationAsync(context, transfers);
        await VerifyDownloadCancellationAndSlotHandoffAsync(context, transfers);

        if (context.Environment.SupportsUploadCancellation)
        {
            await VerifyUploadCancellationAsync(context, transfers, RegularUploadLength, "regular-upload");
        }

        if (context.Environment.SupportsMultipartCancellation)
        {
            await VerifyUploadCancellationAsync(context, transfers, MultipartUploadLength, "multipart-upload");
        }

        var queue = await transfers.GetQueueAsync(new GetTransferQueueRequest());
        HeadlessTransferCancellationContext.AssertSuccess(queue, "Read final transfer queue");
        Assert.Empty(queue.GetValueOrThrow().Tasks);

        await context.WriteEvidenceAsync(new Dictionary<string, object?>
        {
            ["pendingCancellation"] = "passed",
            ["downloadCancellation"] = "passed",
            ["slotHandoff"] = "passed",
            ["uploadCancellation"] = context.Environment.SupportsUploadCancellation ? "passed" : "not-supported",
            ["multipartCancellation"] = context.Environment.SupportsMultipartCancellation ? "passed" : "not-supported"
        });
    }

    private static async Task VerifyPendingCancellationAsync(
        HeadlessTransferCancellationContext context,
        TransferAppService transfers)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = new TransferTask(
            TransferTaskId.New(),
            context.Account.Id,
            TransferDirection.Upload,
            new LocalPath(context.LocalFile("pending-not-opened.bin")),
            context.RemoteObject("pending-not-created.bin"),
            TransferStatus.Pending,
            new TransferOptions(TransferOverwritePolicy.Overwrite),
            now,
            now);
        var scheduler = context.GetRequiredService<ITransferTaskScheduler>();
        HeadlessTransferCancellationContext.AssertSuccess(await scheduler.SubmitAsync(pending), "Persist pending task");
        HeadlessTransferCancellationContext.AssertSuccess(
            await transfers.CancelAsync(new CancelTransferTaskRequest(pending.Id)),
            "Cancel pending task through Application");

        var canceled = await WaitForHistoryStatusAsync(transfers, pending.Id, TransferStatus.Canceled);
        Assert.Equal("用户取消了传输任务。", canceled.Task.StatusReason);
        Assert.False(await context.RemoteObjectExistsAsync(pending.RemotePath));
    }

    private static async Task VerifyDownloadCancellationAndSlotHandoffAsync(
        HeadlessTransferCancellationContext context,
        TransferAppService transfers)
    {
        var firstRemote = context.RemoteObject("download/first-source.bin");
        var secondRemote = context.RemoteObject("download/second-source.bin");
        await context.UploadRemoteAsync(firstRemote, DownloadLength);
        await context.UploadRemoteAsync(secondRemote, DownloadLength);

        var firstLocal = context.LocalFile("first-canceled-download.bin");
        var secondLocal = context.LocalFile("second-completed-download.bin");
        var create = Task.Run(() => transfers.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
            context.Account.Id,
            [
                new DownloadTaskTarget(firstRemote, new LocalPath(firstLocal)),
                new DownloadTaskTarget(secondRemote, new LocalPath(secondLocal))
            ],
            TransferOverwritePolicy.Overwrite)));

        var snapshots = await WaitForQueueAsync(transfers, tasks =>
            tasks.Count(item => item.Task.RemotePath == firstRemote || item.Task.RemotePath == secondRemote) == 2 &&
            tasks.Any(item => item.Task.RemotePath == firstRemote && item.Task.Status == TransferStatus.Running));
        var first = snapshots.Single(item => item.Task.RemotePath == firstRemote);
        var second = snapshots.Single(item => item.Task.RemotePath == secondRemote);
        Assert.Equal(TransferStatus.Pending, second.Task.Status);

        var handoff = Stopwatch.StartNew();
        HeadlessTransferCancellationContext.AssertSuccess(
            await transfers.CancelAsync(new CancelTransferTaskRequest(first.Task.Id)),
            "Cancel running download through Application");

        await WaitForTaskToLeavePendingAsync(transfers, second.Task.Id, TimeSpan.FromSeconds(15));
        handoff.Stop();
        Assert.True(handoff.Elapsed < TimeSpan.FromSeconds(15), $"Pending task slot handoff took {handoff.Elapsed}.");

        var createResult = await create.WaitAsync(TimeSpan.FromMinutes(2));
        HeadlessTransferCancellationContext.AssertSuccess(createResult, "Complete download batch after cancellation");
        var canceled = await WaitForHistoryStatusAsync(transfers, first.Task.Id, TransferStatus.Canceled);
        Assert.False(string.IsNullOrWhiteSpace(canceled.Task.StatusReason));
        await WaitForHistoryStatusAsync(transfers, second.Task.Id, TransferStatus.Succeeded);

        Assert.False(File.Exists(firstLocal));
        Assert.True(File.Exists(secondLocal));
        Assert.Equal(DownloadLength, new FileInfo(secondLocal).Length);
        Assert.Empty(Directory.EnumerateFiles(context.ArtifactRoot, ".atombox-download-*.part"));
    }

    private static async Task VerifyUploadCancellationAsync(
        HeadlessTransferCancellationContext context,
        TransferAppService transfers,
        int length,
        string label)
    {
        var local = context.LocalFile($"{label}-source.bin");
        await context.WritePatternFileAsync(local, length);
        var remote = context.RemoteObject($"upload/{label}-canceled.bin");
        var create = Task.Run(() => transfers.CreateBatchUploadTasksAsync(new CreateBatchUploadTasksRequest(
            context.Account.Id,
            [new UploadTaskTarget(new LocalPath(local), remote)],
            TransferOverwritePolicy.Overwrite)));

        var snapshots = await WaitForQueueAsync(transfers, tasks =>
            tasks.Any(item => item.Task.RemotePath == remote && item.Task.Status == TransferStatus.Running));
        var running = snapshots.Single(item => item.Task.RemotePath == remote);
        if (length >= MultipartUploadLength)
        {
            await WaitForProgressAsync(transfers, running.Task.Id, TimeSpan.FromSeconds(30));
        }
        else
        {
            await Task.Delay(100);
        }

        HeadlessTransferCancellationContext.AssertSuccess(
            await transfers.CancelAsync(new CancelTransferTaskRequest(running.Task.Id)),
            $"Cancel running {label} through Application");
        var createResult = await create.WaitAsync(TimeSpan.FromMinutes(2));
        HeadlessTransferCancellationContext.AssertSuccess(createResult, $"Complete {label} cancellation batch");
        var canceled = await WaitForHistoryStatusAsync(transfers, running.Task.Id, TransferStatus.Canceled);
        Assert.False(string.IsNullOrWhiteSpace(canceled.Task.StatusReason));
        Assert.False(await context.RemoteObjectExistsAsync(remote));
    }

    private static async Task<IReadOnlyList<TransferStateSnapshot>> WaitForQueueAsync(
        TransferAppService transfers,
        Func<IReadOnlyList<TransferStateSnapshot>, bool> predicate)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            var result = await transfers.GetQueueAsync(new GetTransferQueueRequest());
            HeadlessTransferCancellationContext.AssertSuccess(result, "Poll transfer queue");
            if (predicate(result.GetValueOrThrow().Tasks))
            {
                return result.GetValueOrThrow().Tasks;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("Expected transfer queue state was not observed within 30 seconds.");
    }

    private static async Task<TransferStateSnapshot> WaitForHistoryStatusAsync(
        TransferAppService transfers,
        TransferTaskId taskId,
        TransferStatus status)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            var result = await transfers.GetHistoryAsync(new GetTransferHistoryRequest());
            HeadlessTransferCancellationContext.AssertSuccess(result, "Poll transfer history");
            var match = result.GetValueOrThrow().Tasks.FirstOrDefault(item => item.Task.Id == taskId && item.Task.Status == status);
            if (match is not null) return match;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Task {taskId} did not reach {status} within 30 seconds.");
    }

    private static async Task WaitForTaskToLeavePendingAsync(
        TransferAppService transfers,
        TransferTaskId taskId,
        TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            var queue = await transfers.GetQueueAsync(new GetTransferQueueRequest());
            HeadlessTransferCancellationContext.AssertSuccess(queue, "Poll pending slot handoff");
            var queued = queue.GetValueOrThrow().Tasks.FirstOrDefault(item => item.Task.Id == taskId);
            if (queued is null || queued.Task.Status != TransferStatus.Pending) return;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Task {taskId} remained Pending after cancellation released its slot.");
    }

    private static async Task WaitForProgressAsync(
        TransferAppService transfers,
        TransferTaskId taskId,
        TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            var queue = await transfers.GetQueueAsync(new GetTransferQueueRequest());
            HeadlessTransferCancellationContext.AssertSuccess(queue, "Poll multipart upload progress");
            var task = queue.GetValueOrThrow().Tasks.FirstOrDefault(item => item.Task.Id == taskId);
            if (task?.Progress?.BytesTransferred > 0) return;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Task {taskId} did not report multipart progress before cancellation.");
    }
}
