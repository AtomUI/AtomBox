# 阿里云 OSS 传输精确取消与 SDK 迁移

> 状态：Implemented
>
> 目标版本：TBD
>
> 创建时间：2026-11-22
>
> 影响模块：Providers / Transfer（验收）

## 背景

迁移前，AtomBox 的阿里云 OSS Provider 使用 `Aliyun.OSS.SDK.NetCore`。该 SDK 的同步上传、下载和分片接口不能把 AtomBox 的 `CancellationToken` 贯穿到实际 HTTP 请求，因此运行中的任务只能通过可取消流、进度回调和分片边界进行尽力中断。

这种实现无法稳定满足传输队列的取消语义：用户点击取消后，任务可能长时间停留在 `Canceling`，也可能在取消请求后继续传输并最终成功。任务状态虽然记录了用户意图，但 Provider 执行体没有可靠的异步取消边界。

阿里云 OSS 已提供 S3 兼容 Endpoint，AWS 官方 .NET S3 SDK 的异步数据操作接受 `CancellationToken`。本次迭代将阿里云 OSS 的主要数据访问迁移到未经修改的 `AWSSDK.S3`，并在 AtomBox Providers 内建立阿里云适配层；S3 兼容接口确实不覆盖的能力，再由适配层直接调用阿里云 OSS 原生 HTTP API。

## 目标

- 用户取消运行中的阿里云 OSS 上传或下载后，底层执行体必须观察取消并退出。
- 任务状态必须从 `Running` 经短暂 `Canceling` 收敛到 `Canceled`，随后离开当前传输队列并出现在传输历史中。
- 普通上传、分片上传、下载及相关流复制都必须贯穿同一个 Runtime `CancellationToken`。
- 完全移除阿里云官方 C# OSS SDK 依赖，不再保留新旧 SDK 双路径。
- 使用官方、未经修改的 `AWSSDK.S3` 作为阿里云 OSS 的主要数据访问实现。
- 在 Providers 内提供阿里云 OSS 专用适配层，隔离 Endpoint、寻址、请求参数、签名、错误和兼容差异。
- 保持现有 `aliyun-oss` Provider ID、账号配置、凭据和用户可见能力兼容。
- 保持现有阿里云 OSS Provider 的 Bucket 列表、Bucket 创建、对象列表、上传、下载、删除、复制、目录标记、移动和重命名能力。
- 第一阶段支持阿里云标准外网和内网地域 Endpoint。

## 非目标

- 本次不实现暂停、恢复或断点续传。
- 本次不改变通用 `s3-compatible` Provider 的用户配置和兼容承诺。
- 本次不支持 CNAME、自定义域名、传输加速、双栈或非标准 OSS Endpoint。
- 本次不修改、fork、patch、复制或通过反射介入 AWS SDK 内部实现。
- 本次不在 AWS S3 路径失败后自动切换到原生 OSS HTTP API。
- 本次不全面重写阿里云 OSS 协议；原生 HTTP 只补充已确认的 S3 兼容缺口。
- 本次不承诺回滚取消生效前远端已经明确完成的操作，也不讨论请求恰在远端完成瞬间发生取消的极端竞态。

## 用户体验与业务语义

用户在当前传输队列中取消阿里云 OSS 任务时：

1. AtomBox 持久化用户取消意图，并把运行中任务置为 `Canceling`。
2. Transfer Runtime 取消该任务的执行令牌。
3. Provider 把同一令牌传入 AWS SDK、原生 HTTP 请求和本地流读写。
4. 执行体确认退出后，任务进入 `Canceled`。
5. `Canceled` 任务从当前传输队列消失，并可在传输历史中查询。

`Canceling` 是执行体退出前的短暂过渡状态，不能成为永久终态。重复点击取消必须保持幂等，不得创建额外取消流程或把任务恢复为运行状态。

取消表示停止后续传输，不等于删除远端对象或本地部分文件。对于 Multipart，Provider 必须尝试 Abort 未完成会话；Abort 失败只保留为诊断信息，不得把用户取消改写为失败或成功。

## 兼容性要求

### Provider 与持久化契约

- Provider ID 继续使用 `aliyun-oss`。
- 现有 `StorageAccount.Endpoint`、`StorageAccount.Region` 和 `CredentialRef` 结构不变。
- 凭据字段继续使用 `accessKeyId`、`accessKeySecret` 和可选 `securityToken`。
- `securityToken` 在 AWS 客户端中映射为 session token。
- 已保存账号不得要求用户删除后重新创建。
- Endpoint 没有显式 scheme 时按 HTTPS 处理。
- `Region` 缺失时允许从标准 OSS Endpoint 推导；两者同时存在但地域不一致时返回配置错误。

### Endpoint 范围

适配层使用一个类型化 Endpoint Profile 同时产生 S3 兼容 Endpoint 和原生 OSS Endpoint：

| 网络类型 | S3 兼容 Endpoint | 原生 OSS Endpoint |
|---|---|---|
| 公网 | `https://s3.oss-{region}.aliyuncs.com` | `https://oss-{region}.aliyuncs.com` |
| 内网 | `https://s3.oss-{region}-internal.aliyuncs.com` | `https://oss-{region}-internal.aliyuncs.com` |

现有配置可以填写不含 scheme 的原生标准 Endpoint，也可以填写标准 S3 兼容 Endpoint。适配层必须先解析为 Endpoint Profile，禁止在业务调用过程中临时进行字符串替换。

## 设计方案

### 模块边界

- Core、Application 和 Desktop 的公共契约不变。
- Transfer 继续只负责取消意图、任务状态和执行令牌，不理解 AWS SDK、OSS 签名或 Multipart 细节。
- Providers 负责 Endpoint 解析、AWS 客户端配置、原生 HTTP 补充、签名、异常映射和资源释放。
- SDK、HTTP response、签名对象和阿里云专用 DTO 不得泄漏到 Providers 之外。

### 主要内部对象

| 对象 | 职责 |
|---|---|
| `AliyunOssEndpointProfile` | 校验标准 Endpoint，确定地域和公网/内网类型，产生 S3 与原生 Endpoint。 |
| `AliyunOssS3Adapter` | 实现 `IAsyncS3CompatibleClient`，按固定路由组合 AWS S3 客户端和原生 OSS 客户端。 |
| `AwsSdkS3CompatibleClient` | 通过 AWS SDK 执行 S3 兼容操作；接受请求策略以处理具体服务差异。 |
| `AwsSdkS3RequestPolicy` | 为阿里云请求固定 chunked、payload signing、checksum 和 Multipart 清理规则；通用 S3 使用默认策略。 |
| `AliyunOssNativeServiceClient` | 只实现原生 OSS HTTP 补充操作，第一阶段为 `ListBuckets`。 |
| `OssV4Signer` | 为原生 HTTP 请求生成 `OSS4-HMAC-SHA256` 签名。 |

`AliyunOssProviderCreator` 创建短生命周期的凭据 lease、Endpoint Profile、AWS 客户端和原生客户端，最后使用 `AliyunOssS3Adapter` 构造通用 `S3CompatibleProvider`。`S3CompatibleProvider` 需要接收 Provider 专属分页游标命名和传输执行能力，确保阿里云继续使用 `aliyun-oss` 游标命名，并声明真实异步取消能力。

现有同步 `IAliyunOssClient`、`AliyunOssSdkClient`、阿里云 SDK Multipart 包装和重复的阿里云对象存储运行时行为在迁移完成后删除，不保留兼容分支。

### 固定操作路由

| AtomBox 操作 | 实现路径 | 原因 |
|---|---|---|
| 列出全部 Bucket | 原生 OSS HTTP `ListBuckets` | 阿里云公开的 S3 兼容 API 清单没有承诺 Service 级 `ListBuckets`。 |
| 创建 Bucket | AWS SDK `PutBucket` | 阿里云明确列为兼容 API。 |
| 列出对象/前缀 | AWS SDK `ListObjectsV2` | 阿里云明确列为兼容 API。 |
| 上传对象 | AWS SDK `PutObject` | 数据操作必须使用可取消异步 API。 |
| 下载对象 | AWS SDK `GetObject` + 可取消流复制 | 请求和响应读取都必须观察令牌。 |
| 删除对象 | AWS SDK `DeleteObject` | 阿里云明确列为兼容 API。 |
| 复制对象 | AWS SDK `CopyObject` | 对应 OSS `PutObjectCopy`。 |
| Multipart 初始化/上传/完成/终止 | AWS SDK 对应 Multipart API | 阿里云明确列为兼容 API。 |

路由在请求发出前已经确定。任何 AWS 路径的鉴权、配置、网络或服务端错误都按原路径返回，不得静默重试为原生 OSS API。

### AWS SDK 配置

阿里云 AWS 客户端固定使用：

- `ServiceURL`：Endpoint Profile 产生的 S3 兼容 HTTPS Endpoint。
- `AuthenticationRegion`：阿里云地域 ID。
- `ForcePathStyle = false`：OSS 只使用虚拟主机寻址。
- `RequestChecksumCalculation = WHEN_REQUIRED`。
- `ResponseChecksumValidation = WHEN_REQUIRED`。
- 不启用 accelerate、dualstack 或 S3 Express 能力。

`PutObjectRequest` 和 `UploadPartRequest` 固定设置：

- `UseChunkEncoding = false`。
- `DisablePayloadSigning = true`，使用 HTTPS 和 `UNSIGNED-PAYLOAD`。
- 不使用 AWS TransferUtility 自动决定上传策略；继续由 AtomBox 的对象存储上传策略明确选择普通上传或 Multipart。

AWS SDK 使用自己的 AWS Signature V4 实现为 S3 兼容 Endpoint 签名。AtomBox 不生成、不替换也不拦截这条路径的 AWS 签名。

### 原生 OSS HTTP 与签名

第一阶段原生 HTTP 只承担 `ListBuckets`。请求使用原生 OSS Endpoint，并由 `OssV4Signer` 生成：

- `Authorization: OSS4-HMAC-SHA256 ...`
- `x-oss-date`
- `x-oss-content-sha256: UNSIGNED-PAYLOAD`
- 使用 STS 时的 `x-oss-security-token`

签名实现必须独立于 AWS SDK，不得尝试复用 AWS 私有 signer。规范化 URI、查询参数、Header、Credential Scope 和派生密钥必须按照阿里云 OSS V4 规范实现，并使用官方测试向量验证。

### 取消与 Multipart

- `IAsyncS3CompatibleClient` 的每个异步方法都接收调用方令牌，并原样传入对应 AWS SDK 或 `HttpClient` API。
- Multipart 的本地分片读取必须使用 `ReadAsync(..., cancellationToken)`，不得继续使用同步 `Stream.Read`。
- 下载响应必须使用带令牌的异步复制，取消后及时释放 AWS response 和目标流所有权。
- 每个 part 前后都检查取消；取消后禁止进入 `CompleteMultipartUpload`。
- Multipart 取消或失败后使用独立清理令牌调用 `AbortMultipartUpload`，清理超时固定为 10 秒。
- Abort 失败不得覆盖原始取消或上传错误，也不得无限延长 `Canceling`。
- Provider 的传输能力声明为上传/下载均为 `Asynchronous`、支持 cooperative cancellation、支持进度、不额外声明 active abort。

### 错误映射与安全

- `AmazonS3Exception` 和原生 OSS XML 错误统一映射为 Core `StorageError`。
- 保留脱敏后的 HTTP status、provider error code 和 request id。
- S3 兼容鉴权未开启、签名不匹配、Endpoint/Region 不一致必须返回明确配置或鉴权错误，不触发路径切换。
- 日志不得包含 AccessKey Secret、security token、Authorization、规范化待签名字符串或派生签名密钥。
- 凭据只存在于短生命周期 Provider 和 credential lease 内，不进入账号普通配置、任务或历史记录。

## 测试与验收

### 单元测试

- 公网原生 Endpoint、S3 Endpoint 和无 scheme 输入可以生成正确 Profile。
- 内网原生 Endpoint、S3 Endpoint 可以生成正确 Profile。
- 自定义域名、CNAME、路径、query、fragment、未知域名和地域冲突返回 Validation。
- Region 缺失时从标准 Endpoint 正确推导。
- 长期 AK/SK 生成基础 AWS 凭据；STS 凭据正确映射 `securityToken`。
- AWS 客户端使用虚拟主机寻址、正确地域和正确 S3 Endpoint。
- PutObject 和 UploadPart 禁用 chunked encoding、使用 `UNSIGNED-PAYLOAD`，checksum 策略为 `WHEN_REQUIRED`。
- 固定路由验证 `ListBuckets` 只调用原生客户端，其余操作只调用 AWS 客户端。
- AWS 操作失败时不调用原生客户端作为 fallback。
- OSS V4 signer 通过官方签名向量，并覆盖 query 排序、Header 规范化和 STS token。
- AWS 与原生错误映射不泄漏 secret。

### 取消测试

- 普通上传已经开始读取后取消，AWS 调用收到同一 token，结果不得为 Success。
- Multipart part 已经开始读取或发送后取消，停止后续 part，不调用 Complete，并尝试 Abort。
- Abort 超时或失败时仍保留原始取消结果，并在有限时间内退出。
- 下载在响应流读取期间取消，响应和目标写入停止，结果不得为 Success。
- 列表、创建、删除和复制操作也必须传入调用方 token。
- Provider 能力报告异步执行和 cooperative cancellation。
- Transfer Runtime 中任务从 `Canceling` 收敛到 `Canceled`，从当前队列移除并进入历史。
- 用户取消意图已经持久化后，Provider 的迟到成功或异常不得覆盖 `Canceled`。

### 真实 OSS Opt-in 验收

- 公网标准 Endpoint 完成 Bucket 列表、创建测试 Bucket（仅在显式允许时）、对象列表、普通上传、下载、复制、移动、重命名、删除和 Multipart 全链路测试。
- 使用限速流或受控大文件分别验证普通上传、Multipart 和下载的运行中取消。
- 取消后的 Multipart 不应继续完成；测试清理自己创建的未完成会话和对象。
- 内网 Endpoint 使用独立 opt-in 环境执行；没有阿里云内网环境时只跳过真实性测试，不跳过 Endpoint 和请求构造测试。
- 所有真实测试对象限定在测试前缀下，日志和失败输出必须脱敏。

## 完成条件

- `Aliyun.OSS.SDK.NetCore` 从集中包版本和 Providers 项目引用中删除。
- 生产代码和测试不再引用 `Aliyun.OSS` 命名空间。
- 阿里云 OSS 的现有功能契约和保存账号继续可用。
- 新增和更新的单元测试、Provider 契约测试、Transfer 取消测试通过。
- 公网真实 OSS 功能和取消 smoke test 通过。
- 全量 build/test 通过。
- 架构、Provider 实现设计和 Provider 测试矩阵与本文件一致。

## 参考资料

- [阿里云：使用 AWS SDK 访问 OSS](https://help.aliyun.com/zh/oss/developer-reference/use-aws-sdks-to-access-oss)
- [阿里云：OSS 兼容的 S3 API 及差异](https://help.aliyun.com/zh/oss/developer-reference/compatibility-with-amazon-s3)
- [阿里云：OSS V4 Header 签名](https://help.aliyun.com/zh/oss/developer-reference/recommend-to-use-signature-version-4)
- [AWS SDK for .NET：AmazonS3Config](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TS3Config.html)
- [AWS SDK for .NET：PutObjectRequest](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TPutObjectRequest.html)
- [AWS SDK for .NET：UploadPartRequest](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TUploadPartRequest.html)

## 文档同步

本需求进入实现前同步更新：

- `docs/iterations/decisions/2026-11-22-aliyun-oss-aws-sdk-adapter.md`
- `docs/architecture/overview.md`
- `docs/modules/providers/provider-implementation.md`
- `docs/implementation/testing/phase-11-provider-test-matrix.md`

本文件的阿里云专项设计已实现，并纳入更广的[对象存储精确取消与 SDK 收敛](object-storage-precise-cancellation-and-sdk-convergence.md)。

现有 `docs/modules/transfer/tasks-and-state.md` 和 `docs/modules/transfer/runtime.md` 已经定义 `Canceling -> Canceled`、活动队列与历史语义，本次只补充 Provider 侧贯彻和专项验收，不改变 Transfer 状态模型。
