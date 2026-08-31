using COSXML;
using COSXML.Callback;
using COSXML.CosException;
using COSXML.Model;
using COSXML.Model.Bucket;
using COSXML.Model.Object;
using COSXML.Model.Service;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage;

namespace AtomBox.Providers.ObjectStorage.TencentCos;

internal sealed class TencentCosSdkClient : ITencentCosClient
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);
    private static readonly Task NeverCompletes = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously).Task;
    private readonly CosXmlServer _client;
    private readonly ITencentCosAsyncRequestClient _asyncClient;
    private readonly string _region;

    public TencentCosSdkClient(CosXmlServer client, string region)
        : this(client, new TencentCosAsyncRequestClient(client), region)
    {
    }

    internal TencentCosSdkClient(
        CosXmlServer client,
        ITencentCosAsyncRequestClient asyncClient,
        string region)
    {
        _client = client;
        _asyncClient = asyncClient;
        _region = region.Trim();
    }

    public IReadOnlyList<TencentCosBucket> ListBuckets()
    {
        var result = _client.GetService(new GetServiceRequest());
        return result.listAllMyBuckets?.buckets?
            .Select(bucket => new TencentCosBucket(
                bucket.name,
                bucket.location,
                TryParseDate(bucket.createDate)))
            .Where(bucket => !string.IsNullOrWhiteSpace(bucket.Name))
            .ToArray() ?? [];
    }

    public void CreateBucket(string bucketName)
    {
        _client.PutBucket(new PutBucketRequest(bucketName));
    }

    public TencentCosObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000)
    {
        var request = new GetBucketRequest(bucketName);
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            request.SetPrefix(prefix);
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            request.SetMarker(cursor);
        }

        request.SetDelimiter("/");
        request.SetMaxKeys(maxKeys.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var result = _client.GetBucket(request);
        var listBucket = result.listBucket;

        var objects = listBucket?.contentsList?
            .Select(item => new TencentCosObjectSummary(
                item.key,
                item.size,
                TryParseDate(item.lastModified),
                item.eTag))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToArray() ?? [];

        var commonPrefixes = listBucket?.commonPrefixesList?
            .Select(item => item.prefix)
            .Where(prefixItem => !string.IsNullOrWhiteSpace(prefixItem))
            .ToArray() ?? [];

        return new TencentCosObjectListing(
            objects,
            commonPrefixes,
            listBucket is { isTruncated: true } && !string.IsNullOrWhiteSpace(listBucket.nextMarker)
                ? listBucket.nextMarker
                : null);
    }

    public void DeleteObject(string bucketName, string key)
    {
        _client.DeleteObject(new DeleteObjectRequest(bucketName, key));
    }

    public void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey)
    {
        var request = new CopyObjectRequest(destinationBucketName, destinationKey);
        request.SetCopySource(new COSXML.Model.Tag.CopySourceStruct(
            ExtractAppId(sourceBucketName),
            StripAppId(sourceBucketName),
            _region,
            sourceKey));
        _client.CopyObject(request);
    }

    public void PutObject(string bucketName, string key, Stream content, Action<long, long>? progress = null)
    {
        var request = new PutObjectRequest(bucketName, key, content);
        if (progress is not null)
        {
            request.SetCosProgressCallback((completed, total) => progress(completed, total));
        }

        _client.PutObject(request);
    }

    public async Task PutObjectAsync(
        string bucketName,
        string key,
        Stream content,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var cancellableContent = new CancellationReadStream(content, cancellationToken, leaveOpen: true);
        var request = new PutObjectRequest(bucketName, key, cancellableContent);
        if (progress is not null)
        {
            request.SetCosProgressCallback((completed, total) => progress(completed, total));
        }

        var operation = ExecuteAsync<PutObjectResult>(
            request,
            (success, failed) => _asyncClient.PutObject(request, success, failed),
            cancellationToken,
            completeOnCancellation: false);
        _ = await AwaitRequestStopAsync(
            operation,
            cancellationToken,
            cancellableContent.CancellationObserved).ConfigureAwait(false);
    }

    public void PutObjectMultipart(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null)
    {
        var initResult = _client.InitMultipartUpload(new InitMultipartUploadRequest(bucketName, key));
        var uploadId = initResult.initMultipartUpload?.uploadId;
        if (string.IsNullOrWhiteSpace(uploadId))
        {
            throw new InvalidOperationException("COS multipart upload id was empty.");
        }

        var parts = new Dictionary<int, string>();
        var transferred = 0L;
        var partNumber = 1;
        try
        {
            foreach (var part in ReadParts(content, ObjectStorageUploadPolicy.PartSize))
            {
                var request = new UploadPartRequest(bucketName, key, partNumber, uploadId, part);
                request.SetCosProgressCallback((completed, _) =>
                    progress?.Invoke(Math.Min(contentLength, transferred + completed), contentLength));
                var result = _client.UploadPart(request);
                if (string.IsNullOrWhiteSpace(result.eTag))
                {
                    throw new InvalidOperationException("COS multipart part etag was empty.");
                }

                parts[partNumber] = result.eTag;
                transferred += part.LongLength;
                progress?.Invoke(Math.Min(contentLength, transferred), contentLength);
                partNumber++;
            }

            var complete = new CompleteMultipartUploadRequest(bucketName, key, uploadId);
            foreach (var part in parts)
            {
                complete.SetPartNumberAndETag(part.Key, part.Value);
            }

            _client.CompleteMultiUpload(complete);
            progress?.Invoke(contentLength, contentLength);
        }
        catch
        {
            _client.AbortMultiUpload(new AbortMultipartUploadRequest(bucketName, key, uploadId));
            throw;
        }
    }

    public async Task PutObjectMultipartAsync(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var initRequest = new InitMultipartUploadRequest(bucketName, key);
        var initOperation = ExecuteAsync<InitMultipartUploadResult>(
            initRequest,
            (success, failed) => _asyncClient.InitMultipartUpload(initRequest, success, failed),
            cancellationToken,
            completeOnCancellation: false);
        var initResult = await AwaitRequestStopAsync(initOperation, cancellationToken).ConfigureAwait(false);
        var uploadId = initResult.initMultipartUpload?.uploadId;
        if (string.IsNullOrWhiteSpace(uploadId))
        {
            throw new InvalidOperationException("COS multipart upload id was empty.");
        }

        var parts = new Dictionary<int, string>();
        var transferred = 0L;
        var partNumber = 1;
        try
        {
            while (transferred < contentLength)
            {
                var expectedLength = (int)Math.Min(ObjectStorageUploadPolicy.PartSize, contentLength - transferred);
                var payload = await ReadPartAsync(content, expectedLength, cancellationToken).ConfigureAwait(false);
                if (payload.Length == 0)
                {
                    throw new EndOfStreamException("COS multipart input ended before contentLength bytes were read.");
                }

                var partRequest = new UploadPartRequest(bucketName, key, partNumber, uploadId, payload);
                partRequest.SetCosProgressCallback((completed, _) =>
                    progress?.Invoke(Math.Min(contentLength, transferred + completed), contentLength));
                var partOperation = ExecuteAsync<UploadPartResult>(
                    partRequest,
                    (success, failed) => _asyncClient.UploadPart(partRequest, success, failed),
                    cancellationToken,
                    completeOnCancellation: false);
                var partResult = await AwaitRequestStopAsync(partOperation, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(partResult.eTag))
                {
                    throw new InvalidOperationException("COS multipart part etag was empty.");
                }

                parts[partNumber] = partResult.eTag;
                transferred += payload.LongLength;
                progress?.Invoke(Math.Min(contentLength, transferred), contentLength);
                partNumber++;
            }

            var completeRequest = new CompleteMultipartUploadRequest(bucketName, key, uploadId);
            foreach (var part in parts)
            {
                completeRequest.SetPartNumberAndETag(part.Key, part.Value);
            }

            var completeOperation = ExecuteAsync<CompleteMultipartUploadResult>(
                completeRequest,
                (success, failed) => _asyncClient.CompleteMultipartUpload(completeRequest, success, failed),
                cancellationToken,
                completeOnCancellation: false);
            _ = await AwaitRequestStopAsync(completeOperation, cancellationToken).ConfigureAwait(false);
            progress?.Invoke(contentLength, contentLength);
        }
        catch (Exception exception)
        {
            try
            {
                await AbortMultipartAsync(bucketName, key, uploadId).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                if (exception is OperationCanceledException || cancellationToken.IsCancellationRequested)
                {
                    throw CreateCleanupWarning(cleanupException, cancellationToken);
                }
            }

            throw;
        }
    }

    public TencentCosDownloadObject GetObject(string bucketName, string key)
    {
        var result = _client.GetObject(new GetObjectBytesRequest(bucketName, key));
        var content = result.content ?? [];
        return new TencentCosDownloadObject(new MemoryStream(content, writable: false), content.LongLength);
    }

    public async Task<TencentCosDownloadObject> GetObjectAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        var request = new GetObjectBytesRequest(bucketName, key);
        var operation = ExecuteAsync<GetObjectBytesResult>(
            request,
            (success, failed) => _asyncClient.GetObject(request, success, failed),
            cancellationToken,
            completeOnCancellation: false);
        var result = await AwaitRequestStopAsync(operation, cancellationToken).ConfigureAwait(false);
        var content = result.content ?? [];
        return new TencentCosDownloadObject(new MemoryStream(content, writable: false), content.LongLength);
    }

    private async Task AbortMultipartAsync(string bucketName, string key, string uploadId)
    {
        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        var request = new AbortMultipartUploadRequest(bucketName, key, uploadId);
        var operation = ExecuteAsync<AbortMultipartUploadResult>(
            request,
            (success, failed) => _asyncClient.AbortMultipartUpload(request, success, failed),
            cleanup.Token);
        _ = await operation.WaitAsync(cleanup.Token).ConfigureAwait(false);
    }

    private async Task<TResult> ExecuteAsync<TResult>(
        CosRequest request,
        Action<OnSuccessCallback<CosResult>, OnFailedCallback> start,
        CancellationToken cancellationToken,
        bool completeOnCancellation = true)
        where TResult : CosResult
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                _asyncClient.Cancel(request);
            }
            catch
            {
                // Cancellation remains the task result even when the SDK rejects a duplicate cancel call.
            }

            if (completeOnCancellation)
            {
                completion.TrySetCanceled(cancellationToken);
            }
        });

        if (cancellationToken.IsCancellationRequested)
        {
            return await Task.FromCanceled<TResult>(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            start(
                result =>
                {
                    if (result is TResult typedResult)
                    {
                        completion.TrySetResult(typedResult);
                    }
                    else
                    {
                        completion.TrySetException(new InvalidOperationException(
                            $"COS returned {result.GetType().Name} instead of {typeof(TResult).Name}."));
                    }
                },
                (clientException, serverException) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                        return;
                    }

                    var exception = (Exception?)clientException ?? (Exception?)serverException ??
                        new InvalidOperationException("COS request failed without an exception.");
                    completion.TrySetException(exception);
                });
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }

        return await completion.Task.ConfigureAwait(false);
    }

    private static void ObserveLaterFailure(Task operation)
    {
        _ = operation.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static async Task<TResult> AwaitRequestStopAsync<TResult>(
        Task<TResult> operation,
        CancellationToken cancellationToken,
        Task? localStop = null)
    {
        var cancellationSignal = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var stoppedLocally = localStop ?? NeverCompletes;
        var first = await Task.WhenAny(operation, stoppedLocally, cancellationSignal).ConfigureAwait(false);
        if (ReferenceEquals(first, operation))
        {
            return await operation.ConfigureAwait(false);
        }

        if (ReferenceEquals(first, stoppedLocally))
        {
            ObserveLaterFailure(operation);
            throw new OperationCanceledException(cancellationToken);
        }

        var timeout = Task.Delay(CleanupTimeout);
        var stopped = await Task.WhenAny(operation, stoppedLocally, timeout).ConfigureAwait(false);
        if (ReferenceEquals(stopped, operation))
        {
            return await operation.ConfigureAwait(false);
        }

        if (ReferenceEquals(stopped, stoppedLocally))
        {
            ObserveLaterFailure(operation);
            throw new OperationCanceledException(cancellationToken);
        }

        ObserveLaterFailure(operation);
        throw new ProviderOperationCanceledException(
            "Provider operation was canceled, but COS request termination was not confirmed within 10 seconds; remote final state may be unknown.",
            new TimeoutException("COS request cancellation confirmation timed out."),
            cancellationToken);
    }

    private static async Task<byte[]> ReadPartAsync(
        Stream content,
        int expectedLength,
        CancellationToken cancellationToken)
    {
        var payload = new byte[expectedLength];
        var offset = 0;
        while (offset < payload.Length)
        {
            var read = await content.ReadAsync(
                payload.AsMemory(offset, payload.Length - offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset == payload.Length ? payload : payload[..offset];
    }

    private static ProviderOperationCanceledException CreateCleanupWarning(
        Exception cleanupException,
        CancellationToken cancellationToken)
    {
        var reason = cleanupException is OperationCanceledException
            ? "Remote multipart cleanup timed out; incomplete parts may remain."
            : "Remote multipart cleanup failed; incomplete parts may remain.";
        return new ProviderOperationCanceledException(
            $"Provider operation was canceled. {reason}",
            cleanupException,
            cancellationToken);
    }

    private static DateTimeOffset? TryParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string ExtractAppId(string bucketName)
    {
        var index = bucketName.LastIndexOf("-", StringComparison.Ordinal);
        return index >= 0 && index < bucketName.Length - 1 ? bucketName[(index + 1)..] : string.Empty;
    }

    private static string StripAppId(string bucketName)
    {
        var index = bucketName.LastIndexOf("-", StringComparison.Ordinal);
        return index > 0 ? bucketName[..index] : bucketName;
    }

    private static IEnumerable<byte[]> ReadParts(Stream content, long partSize)
    {
        var buffer = new byte[partSize];
        while (true)
        {
            var readTotal = 0;
            while (readTotal < buffer.Length)
            {
                var read = content.Read(buffer, readTotal, buffer.Length - readTotal);
                if (read == 0)
                {
                    break;
                }

                readTotal += read;
            }

            if (readTotal == 0)
            {
                yield break;
            }

            if (readTotal == buffer.Length)
            {
                yield return buffer;
                buffer = new byte[partSize];
            }
            else
            {
                var part = new byte[readTotal];
                Array.Copy(buffer, part, readTotal);
                yield return part;
                yield break;
            }
        }
    }
}
