using COSXML;
using COSXML.Auth;
using COSXML.Callback;
using COSXML.Model;
using COSXML.Model.Object;
using AtomBox.Core.Errors;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage;
using AtomBox.Providers.ObjectStorage.TencentCos;

namespace AtomBox.Providers.Tests;

public sealed class TencentCosSdkClientCancellationTests
{
    [Fact]
    public async Task PutObjectAsync_CancelsActiveCosRequest()
    {
        var cosServer = new RecordingAsyncRequestClient();
        var client = CreateClient(cosServer);
        using var source = new MemoryStream([1, 2, 3]);
        using var cancellation = new CancellationTokenSource();

        var operation = client.PutObjectAsync("bucket-1250000000", "file.bin", source, cancellationToken: cancellation.Token);
        await WaitForRequestOrFailureAsync(cosServer.RequestStarted.Task, operation);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Same(cosServer.ActiveRequest, cosServer.CanceledRequest);
    }

    [Fact]
    public async Task GetObjectAsync_CancelsActiveCosRequest()
    {
        var cosServer = new RecordingAsyncRequestClient { Operation = RecordedOperation.Download };
        var client = CreateClient(cosServer);
        using var cancellation = new CancellationTokenSource();

        var operation = client.GetObjectAsync("bucket-1250000000", "file.bin", cancellation.Token);
        await WaitForRequestOrFailureAsync(cosServer.RequestStarted.Task, operation);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Same(cosServer.ActiveRequest, cosServer.CanceledRequest);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_CancelsPartAndAbortsUpload()
    {
        var cosServer = new RecordingAsyncRequestClient { Operation = RecordedOperation.Multipart };
        var client = CreateClient(cosServer);
        using var source = new MemoryStream(new byte[ObjectStorageUploadPolicy.MultipartThreshold]);
        using var cancellation = new CancellationTokenSource();

        var operation = client.PutObjectMultipartAsync(
            "bucket-1250000000",
            "large.bin",
            source,
            source.Length,
            cancellationToken: cancellation.Token);
        await WaitForRequestOrFailureAsync(cosServer.RequestStarted.Task, operation);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, cosServer.AbortCount);
        Assert.False(cosServer.CompleteCalled);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_PreservesCanceledCategoryWhenAbortFails()
    {
        var cosServer = new RecordingAsyncRequestClient
        {
            Operation = RecordedOperation.Multipart,
            AbortFails = true
        };
        var client = CreateClient(cosServer);
        using var source = new MemoryStream(new byte[ObjectStorageUploadPolicy.MultipartThreshold]);
        using var cancellation = new CancellationTokenSource();

        var operation = client.PutObjectMultipartAsync(
            "bucket-1250000000",
            "large.bin",
            source,
            source.Length,
            cancellationToken: cancellation.Token);
        await WaitForRequestOrFailureAsync(cosServer.RequestStarted.Task, operation);
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<ProviderOperationCanceledException>(() => operation);
        var error = ProviderErrorMapper.FromException(exception);
        Assert.Equal(StorageErrorCategory.Canceled, error.Category);
        Assert.Contains("incomplete parts may remain", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PutObjectAsync_CompletedCallbackWinsLaterCancellationRequest()
    {
        var cosServer = new RecordingAsyncRequestClient { CompleteCanceledRequestWithFailure = false };
        var client = CreateClient(cosServer);
        using var source = new MemoryStream([1, 2, 3]);
        using var cancellation = new CancellationTokenSource();

        var operation = client.PutObjectAsync("bucket-1250000000", "file.bin", source, cancellationToken: cancellation.Token);
        await WaitForRequestOrFailureAsync(cosServer.RequestStarted.Task, operation);
        cosServer.CompleteActiveRequestSuccessfully();
        await operation;
        cancellation.Cancel();

        Assert.Null(cosServer.CanceledRequest);
    }

    private enum RecordedOperation
    {
        Upload,
        Download,
        Multipart
    }

    private static async Task WaitForRequestOrFailureAsync(Task requestStarted, Task operation)
    {
        var completed = await Task.WhenAny(requestStarted, operation).WaitAsync(TimeSpan.FromSeconds(2));
        if (ReferenceEquals(completed, operation))
        {
            await operation;
        }
    }

    private static TencentCosSdkClient CreateClient(ITencentCosAsyncRequestClient asyncClient)
    {
        var client = new CosXmlServer(
            new CosXmlConfig.Builder().SetRegion("ap-guangzhou").Build(),
            new DefaultQCloudCredentialProvider("secret-id", "secret-key", 600));
        return new TencentCosSdkClient(client, asyncClient, "ap-guangzhou");
    }

    private sealed class RecordingAsyncRequestClient : ITencentCosAsyncRequestClient
    {
        private OnSuccessCallback<CosResult>? _activeSuccess;
        private OnFailedCallback? _activeFailed;

        public RecordedOperation Operation { get; init; }

        public bool CompleteCanceledRequestWithFailure { get; init; } = true;

        public bool AbortFails { get; init; }

        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CosRequest? ActiveRequest { get; private set; }

        public CosRequest? CanceledRequest { get; private set; }

        public int AbortCount { get; private set; }

        public bool CompleteCalled { get; private set; }

        public void PutObject(
            PutObjectRequest request,
            OnSuccessCallback<CosResult> successCallback,
            OnFailedCallback failCallback)
        {
            SetActiveRequest(request, successCallback, failCallback);
        }

        public void GetObject(
            GetObjectBytesRequest request,
            OnSuccessCallback<CosResult> successCallback,
            OnFailedCallback failCallback)
        {
            SetActiveRequest(request, successCallback, failCallback);
        }

        public void InitMultipartUpload(
            InitMultipartUploadRequest request,
            OnSuccessCallback<CosResult> successCallback,
            OnFailedCallback failCallback)
        {
            var result = new InitMultipartUploadResult();
            typeof(InitMultipartUploadResult)
                .GetField("_data", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(result, new COSXML.Model.Tag.InitiateMultipartUpload { uploadId = "upload-1" });
            successCallback(result);
        }

        public void UploadPart(
            UploadPartRequest request,
            OnSuccessCallback<CosResult> successCallback,
            OnFailedCallback failCallback)
        {
            SetActiveRequest(request, successCallback, failCallback);
        }

        public void CompleteMultipartUpload(
            CompleteMultipartUploadRequest request,
            OnSuccessCallback<CosResult> successCallback,
            OnFailedCallback failCallback)
        {
            CompleteCalled = true;
            successCallback(new CompleteMultipartUploadResult());
        }

        public void AbortMultipartUpload(
            AbortMultipartUploadRequest request,
            OnSuccessCallback<CosResult> successCallback,
            OnFailedCallback failCallback)
        {
            AbortCount++;
            if (AbortFails)
            {
                failCallback(null!, null!);
                return;
            }

            successCallback(new AbortMultipartUploadResult());
        }

        public void Cancel(CosRequest request)
        {
            CanceledRequest = request;
            if (CompleteCanceledRequestWithFailure && ReferenceEquals(request, ActiveRequest))
            {
                _activeFailed?.Invoke(null!, null!);
            }
        }

        public void CompleteActiveRequestSuccessfully()
        {
            _activeSuccess?.Invoke(Operation switch
            {
                RecordedOperation.Download => new GetObjectBytesResult { content = [1, 2, 3] },
                RecordedOperation.Multipart => new UploadPartResult { eTag = "etag-1" },
                _ => new PutObjectResult()
            });
        }

        private void SetActiveRequest(
            CosRequest request,
            OnSuccessCallback<CosResult> successCallback,
            OnFailedCallback failCallback)
        {
            ActiveRequest = request;
            _activeSuccess = successCallback;
            _activeFailed = failCallback;
            RequestStarted.TrySetResult();
        }
    }

}
