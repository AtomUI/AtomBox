using COSXML.Callback;
using COSXML.Model;
using COSXML.Model.Object;

namespace AtomBox.Providers.ObjectStorage.TencentCos;

internal interface ITencentCosAsyncRequestClient
{
    void PutObject(PutObjectRequest request, OnSuccessCallback<CosResult> success, OnFailedCallback failed);

    void GetObject(GetObjectBytesRequest request, OnSuccessCallback<CosResult> success, OnFailedCallback failed);

    void InitMultipartUpload(
        InitMultipartUploadRequest request,
        OnSuccessCallback<CosResult> success,
        OnFailedCallback failed);

    void UploadPart(UploadPartRequest request, OnSuccessCallback<CosResult> success, OnFailedCallback failed);

    void CompleteMultipartUpload(
        CompleteMultipartUploadRequest request,
        OnSuccessCallback<CosResult> success,
        OnFailedCallback failed);

    void AbortMultipartUpload(
        AbortMultipartUploadRequest request,
        OnSuccessCallback<CosResult> success,
        OnFailedCallback failed);

    void Cancel(CosRequest request);
}
