# 对象存储统一采用 AWS SDK 与供应商适配层

> 状态：Accepted / Implemented
>
> 日期：2026-11-22

## 决策

除暂缓的青云 QingStor 外，AtomBox 已接入的对象存储统一采用未经修改的 `AWSSDK.S3` 作为数据面内核。每家供应商保留独立 Creator/Profile 适配层，负责 Endpoint、地域、寻址方式、列表版本、Bucket 限制、复制阈值和账号兼容；不得把供应商差异压成一套无差别的 Generic S3 配置。

通用运行时结构如下：

```text
<Vendor>ProviderCreator
  -> vendor endpoint/profile validation
  -> AmazonS3Client (public configuration only)
  -> AwsSdkS3CompatibleClient
  -> optional vendor adapter (Aliyun native ListBuckets only)
  -> S3CompatibleProvider
  -> IStorageProvider
```

AWS SDK 是不可修改的 NuGet 依赖。禁止 fork、patch、vendoring、反射 hook、复制私有 signer 或维护 AtomBox 专属分支。协议缺口只能在 AtomBox 的外层适配层中通过公开 API 或最小原生 HTTP 实现补齐。

## 供应商 Profile

| Provider | AWS SDK Endpoint / 寻址 | 列表与能力差异 | 原生补充 |
| --- | --- | --- | --- |
| 阿里云 OSS | `s3.oss-{region}[-internal].aliyuncs.com`；virtual-host | `ListObjectsV2`；标准公网/内网 Endpoint；5 GiB 后 multipart copy | 仅原生 `ListBuckets` + OSS V4 signer |
| 腾讯云 COS | 地域 Endpoint；标准桶域名归一为地域域名；virtual-host | 创建时校验完整 `bucket-appid` 名称，5 GiB 后 multipart copy | 无 |
| 七牛 Kodo | `s3-{region}.qiniucs.com`；path-style | 兼容旧地域别名，使用 AWS SDK 创建私有 Bucket，可配置单 Bucket root | 无 |
| 又拍云 USS | `s3.api.upyun.com`；path-style；签名地域 `us-east-1` | 单 Bucket root，创建空间通过厂商控制台，500 MiB 后 multipart copy、10 MiB copy part | 无 |
| 华为云 OBS | 账号 Endpoint；virtual-host | 使用 ListObjects V1；地域可从标准 Endpoint 推导 | 无 |
| 百度 BOS | 原生 Endpoint 映射为 `s3.<region>.bcebos.com`；virtual-host | 可配置单 Bucket root；5 GiB 后 multipart copy | 无 |
| 京东云 OSS | 账号 Endpoint；path-style | 5 GiB 后 multipart copy | 无 |
| 火山引擎 TOS | `tos-*` 映射为 `tos-s3-*`；virtual-host | 只接受地域 S3 Endpoint；5 GiB 后 multipart copy | 无 |
| 青云 QingStor | 本轮不迁移 | 继续原 `S3CompatibleAwsV4Client`；冻结、后置审计 | 无 |

具体 Endpoint 与兼容规则属于供应商 Profile，不能在请求失败后临时猜测或切换。独立的 `CreateBucket` 能力同时控制桌面创建入口和 Provider 分派。又拍云的建桶请求返回 `OperationNotSupported`，桶内目录能力独立声明。

## 取消与 Multipart

`AwsSdkS3CompatibleClient` 是真实异步 transport：

- 所有 AWS SDK async 方法接收调用方 token。
- 普通上传使用带 token 的 `ProgressReadStream`。
- Multipart part 使用 `ReadAsync(..., cancellationToken)`，每个 part 前检查取消。
- 下载请求和响应流复制都观察同一个 token。
- 对大对象复制先读取 metadata；超过供应商单次 copy 上限时使用 `CopyPartAsync`。
- Multipart 取消或失败后使用独立 10 秒 token 调用 `AbortMultipartUploadAsync`，然后重新抛出原始取消或错误。
- Abort 失败只用于诊断，不覆盖原始结果。

所有迁移供应商的传输能力声明为异步、支持 cooperative cancellation 和进度，不声明 active abort。这里的“精确取消”指执行体可被同一 token 可靠终止；不讨论远端恰好已经完成请求的极端竞态，也不承诺删除取消前已经写入的远端数据。

## 请求兼容策略

供应商 S3 客户端统一使用 Signature V4、`RequestChecksumCalculation=WHEN_REQUIRED` 与 `ResponseChecksumValidation=WHEN_REQUIRED`。流式上传固定设置：

- `UseChunkEncoding=false`
- `DisablePayloadSigning=true`
- HTTPS + `UNSIGNED-PAYLOAD`

这样避免把 AWS `aws-chunked` 默认行为发送到未实现该扩展的兼容服务。AtomBox 继续自行按明确阈值选择普通上传或 Multipart，不使用 TransferUtility 隐式决策。

## 依赖废弃

下列包已经从集中版本和 `AtomBox.Providers.csproj` 删除，生产代码和测试不得再引用其命名空间或能力：

- `Aliyun.OSS.SDK.NetCore`
- `Tencent.QCloud.Cos.Sdk`
- `Qiniu`
- `HuaweiCloud.Obs`
- `BceSdkDotNetCore`
- `Volcengine.TOS.SDK.NetCore`
- 仅由旧 SDK 链路遗留的 `log4net`、`Newtonsoft.Json` 直接引用

又拍云原生 REST 客户端也已删除。`S3CompatibleAwsV4Client` 只允许青云 QingStor 使用，避免继续扩散自研 S3 签名实现。

## 风险与门禁

- AWS SDK 升级必须重跑请求策略、Endpoint、取消、Multipart Abort 和真实厂商 opt-in 测试。
- 各家兼容声明可能随服务端变化；Profile 变更必须有官方文档依据和针对性测试。
- 单元测试验证 Profile、OSS V4 canonical request、Provider token 传播和能力声明；真实账号测试验证普通上传、Multipart、下载、复制、删除和运行中取消。
- 青云未完成同等审计前不得宣称已具备本轮精确取消保证。
