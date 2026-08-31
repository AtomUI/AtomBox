using COSXML;
using COSXML.Callback;
using COSXML.Model;
using COSXML.Model.Object;

namespace AtomBox.Providers.ObjectStorage.TencentCos;

internal sealed class TencentCosAsyncRequestClient : ITencentCosAsyncRequestClient
{
    private readonly CosXmlServer _client;

    public TencentCosAsyncRequestClient(CosXmlServer client)
    {
        _client = client;
    }

    public void PutObject(PutObjectRequest request, OnSuccessCallback<CosResult> success, OnFailedCallback failed)
    {
        _client.PutObject(request, success, failed);
    }

    public void GetObject(GetObjectBytesRequest request, OnSuccessCallback<CosResult> success, OnFailedCallback failed)
    {
        _client.GetObject(request, success, failed);
    }

    public void InitMultipartUpload(
        InitMultipartUploadRequest request,
        OnSuccessCallback<CosResult> success,
        OnFailedCallback failed)
    {
        _client.InitMultipartUpload(request, success, failed);
    }

    public void UploadPart(UploadPartRequest request, OnSuccessCallback<CosResult> success, OnFailedCallback failed)
    {
        _client.UploadPart(request, success, failed);
    }

    public void CompleteMultipartUpload(
        CompleteMultipartUploadRequest request,
        OnSuccessCallback<CosResult> success,
        OnFailedCallback failed)
    {
        _client.CompleteMultiUpload(request, success, failed);
    }

    public void AbortMultipartUpload(
        AbortMultipartUploadRequest request,
        OnSuccessCallback<CosResult> success,
        OnFailedCallback failed)
    {
        _client.AbortMultiUpload(request, success, failed);
    }

    public void Cancel(CosRequest request)
    {
        _client.Cancel(request);
    }
}
