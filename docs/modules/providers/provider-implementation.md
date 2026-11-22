# Provider 实现设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-15
>
> 冻结范围：当前文档定义的具体 provider 实现边界、生命周期、凭据处理、异常处理、能力处理和 Phase 9 收口规则
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的设计边界，必须先说明原因，再同步更新相关设计文档。

## 1. 模块定位

具体 provider 是外部存储系统到 AtomBox Core 契约的适配器。

它的任务是把阿里云 OSS、腾讯 COS、七牛 Kodo、又拍云 USS、S3 兼容对象存储、FTP/SFTP/WebDAV、阿里云盘、百度网盘等外部系统的协议、SDK、API、异常、分页、路径规则和能力差异，收敛成 Core 中定义的统一对象和统一操作语义。

第一版发布基线以对象存储、SFTP、FTP、WebDAV 为主路径；阿里云盘、百度网盘 provider 代码已存在，但完整 OAuth、token refresh 和产品级体验不作为第一版发布承诺。

Provider 不是业务用例，不是 UI 控件，不是传输调度器，不是账号仓库。

## 2. 实现原则

具体 provider 必须遵守：

- 对外只暴露 Core 中定义的 `IStorageProvider` 和 Core 模型。
- 内部可以使用 SDK DTO、HTTP response、协议库对象，但不得返回给外层。
- 所有远程操作返回 `OperationResult<T>` 或 Core 约定的结果类型。
- 所有 SDK/API/协议异常必须在 Providers 内部转换为 Core 错误。
- 不读取普通配置文件。
- 不保存账号配置。
- 不持久化凭据。
- 不启动后台线程。
- 不实现任务队列、重试调度、并发调度。

Provider 的尊严来自边界清楚，不来自把所有事情都做了。

## 3. 生命周期

具体 provider 是短生命周期运行对象。

默认生命周期：

- Application 短用例创建一个 provider，操作结束后释放。
- Transfer worker 为一次传输批次创建 provider，批次结束后释放。
- Provider 内部可以在该生命周期内复用 SDK client、HTTP client wrapper、FTP session 或 SFTP session。
- Provider 不能跨批次缓存为账号级全局单例。

第一版不承诺 provider 线程安全。

如果同一个账号下需要并发执行多个传输 worker，工程上应创建多个 provider 实例，而不是让多个线程共享一个 provider 实例。

## 4. 凭据处理

Provider 不持久化、不缓存、不跨生命周期保存 secret material。

正确链路：

1. `StorageAccount` 保存 `CredentialRef`。
2. Factory 在真实远程操作开始时通过 Core 凭据端口解析 `CredentialRef`。
3. Factory 把必要的临时凭据材料传给具体 provider 构造逻辑。
4. Provider 在自己的短生命周期内使用凭据完成操作。
5. Provider 释放后，不再持有凭据材料。

这不表示 provider 在内存中完全不能接触凭据。OSS 分片上传、SFTP session、网盘 token refresh 等真实远程操作，可能需要 provider 在单次 Application 短用例或单次 Transfer 执行批次窗口内临时持有或间接使用认证材料。禁止的是把 secret material 持久化、日志化、缓存成全局状态，或跨 provider 生命周期保存。

禁止行为：

- Registry 保存 `CredentialRef` 或 secret material。
- Provider 把 access key、client secret、refresh token 写入日志。
- Provider 把 secret material 放进异常 message。
- Provider 把 secret material 缓存在静态字段或全局单例中。
- TransferTask 保存 secret material。

OAuth refresh 如后续需要，应发生在 Factory 或 provider 操作链路中，并通过 Core 端口更新凭据状态。这个过程不能让 Application 直接接触 SDK token 对象。

## 5. 能力处理

Registry 提供 provider 类型的静态能力声明。

具体 provider 在运行时仍然必须尊重真实账号、真实后端和真实协议限制。

示例：

- Registry 可以声明 `aliyun-oss` 支持对象存储列表、上传、下载、删除。
- 某个具体账号因为权限不足无法删除对象时，Provider 必须返回 Core 权限错误，而不是伪装成不支持删除。
- Registry 可以声明 `sftp` 支持目录语义。
- 某个 SFTP 服务器因为权限或服务端限制无法创建目录时，Provider 必须返回远端错误。

静态能力用于生成 UI、约束明显不可用的操作、帮助 Application 做基本决策。真实远端操作仍以 provider 返回结果为准。

## 6. 路径与列表

Provider 必须把外部系统的路径语义转换为 Core 的统一路径对象。

对象存储和文件系统的路径不是一回事：

- OSS 的“文件夹”通常是 prefix 或约定对象。
- FTP/SFTP 的目录通常是真实目录。
- 网盘 API 的路径可能是 id、path、parent id 混合模型。

这些差异必须封装在 provider 内部。外层只看到 Core 的 `RemotePath`、`RemoteItem` 和目录/文件语义。

第一版远程列表必须支持：

- 当前路径列表。
- 文件和目录区分。
- 文件名。
- 文件大小。
- 更新时间。
- 下一页或分页游标语义。
- Provider 契约必须提供分页入口；具体 provider 如果还没有原生分页能力，可以先使用 Core 的兼容分页默认实现，但不能让外层绕过 Provider 自行分页。

如果某个 provider 的远程 API 不支持完整字段，必须用 Core 模型明确表达未知值，而不是用假数据糊弄外层。

## 7. 上传与下载

Provider 实现具体上传和下载动作。

Transfer 负责：

- 任务排队。
- worker 调度。
- 并发控制。
- 重试策略。
- 取消请求。
- 状态持久化。

Provider 负责：

- 调用具体 SDK/API/协议执行上传或下载。
- 把进度回调转换为 Core 约定的进度语义。
- 把远端错误转换为 Core 错误。
- 在自己的生命周期内管理 SDK client/session。

Provider 可以在内部使用对象存储 multipart、SFTP 流式写入、网盘分片上传等协议能力，但这些实现细节不能泄漏给 Transfer。Transfer 只根据 Core 能力模型做任务级调度，不理解某个 SDK 的分片对象。

## 8. 异常与日志

Provider 内部必须建立错误映射。

典型映射：

- 认证失败 -> Core 鉴权错误。
- 权限不足 -> Core 权限错误。
- 远端对象不存在 -> Core 不存在错误。
- 网络超时 -> Core 网络错误或超时错误。
- 服务端限流 -> Core 限流错误。
- SDK 参数错误 -> Core 配置错误或 provider 实现错误。

日志规则：

- 可以记录 provider id、账号 id、操作类型、远程路径、错误分类。
- 不记录 access key、secret、token、authorization header、cookie。
- 不记录 SDK 原始请求体中的敏感字段。
- 不把 SDK 异常原文无脑透传到 UI。

Phase 9 错误映射至少覆盖：

- 认证失败。
- 权限不足。
- 网络不可达或超时。
- 远端路径不存在。
- 远端对象已存在或冲突。
- provider 配置错误。
- 服务端限流或临时不可用。
- SDK / 协议层未知异常。

Provider 可以保留脱敏技术细节供 Application 和 Presentation 展示，例如 request id、HTTP status、协议状态码或 provider 错误码。

## 9. 第一版实现范围

第一版 Providers 只需要支持统一骨架和少量真实 provider 的最小闭环。

优先级：

1. Registry / Factory 基础设施。
2. 一个对象存储 provider，并逐步扩展阿里云 OSS、腾讯 COS、七牛 Kodo、又拍云 USS、华为云 OBS、百度智能云 BOS、京东云 OSS、青云 QingStor、火山引擎 TOS 等对象存储实现。
3. 一个 SFTP provider。
4. 远程列表、下载、上传、删除。
5. 错误转换和凭据边界。

文件传输 provider 第一版认证范围：

- SFTP 支持 host/IP、port、rootPath、Linux username、密码认证、SSH 私钥认证和可选私钥 passphrase。
- SFTP 支持 host key 策略：默认 `acceptAny`，也可配置 `hostKeyPolicy=fingerprint` 和 `hostKeyFingerprint` 做显式指纹校验。
- FTP 支持 host/IP、port、rootPath、匿名认证、用户名+密码认证。
- FTP 支持 `transferMode=passive|active` 和 `timeoutSeconds` 基础连接配置；FTPS/TLS 证书能力后置。
- WebDAV 支持完整 http/https endpoint URL、rootPath、匿名认证、用户名+密码 Basic 认证和 `timeoutSeconds` 基础连接配置。
- `authMode` 属于账号配置字段，用于表达认证方式；password、privateKey、privateKeyPassphrase 等 secret material 只能通过凭据端口进入 provider factory / provider 操作链路。
- FTP/WebDAV 匿名模式不得读取或要求 secret material；SFTP/FTP/WebDAV 非匿名模式必须通过 credential lease 获取临时凭据材料。
- SFTP/FTP 上传文件前应确保远端父目录存在；下载目录应返回 Validation；下载不存在文件应返回 NotFound；删除必须按文件/目录类型分别调用协议能力。
- SFTP 上传和下载必须使用 SSH.NET 的异步传输 API，并把 Runtime 提供的 `CancellationToken` 贯穿到协议调用。用户取消后，协议执行体必须退出并返回 Canceled 错误语义；不得只在传输开始前检查令牌、随后继续执行同步上传或下载。
- FTP 必须使用 FluentFTP `AsyncFtpClient`；连接、目录列表、存在性检查、父目录创建、上传、下载、删除和移动必须接收同一个 Runtime `CancellationToken`。不得退回同步 `FtpClient` 后用 `Task.Run` 伪装可取消异步传输。
- WebDAV 必须使用 `HttpClient.SendAsync` 和异步 Stream API；PROPFIND、MKCOL、DELETE、MOVE、PUT、GET、上传内容序列化和下载响应复制必须传递同一个 Runtime `CancellationToken`。自定义 `HttpContent` 必须覆盖带 Token 的 `SerializeToStreamAsync`，不得在无 Token 重载中丢失取消信号。
- SFTP、FTP 和 WebDAV 的上传/下载能力均声明 `Asynchronous + SupportsCooperativeCancellation=true + SupportsActiveAbort=false`。用户取消必须使协议执行体退出并返回 Canceled；非调用方 Token 导致的 WebDAV `OperationCanceledException` 必须映射为 Timeout/Network，不得误报用户取消。
- 文件传输取消只停止后续传输，不承诺删除远端已经产生的部分文件或本地部分下载。部分数据的清理由用户根据实际结果决定，Provider 不得在结果未知时自动删除路径。
- Aliyun OSS 不再使用 `Aliyun.OSS.SDK.NetCore`。主要 Bucket、Object 和 Multipart 操作使用官方且未经修改的 `AWSSDK.S3`，S3 兼容接口没有承诺的操作才允许由阿里云适配层调用原生 OSS HTTP API。
- 禁止 fork、patch、vendoring、反射注入或复制 AWS SDK 私有实现。阿里云兼容差异只能通过 AWS SDK 公共配置、公共 request 类型和 Providers 内部适配层解决。
- Aliyun OSS 使用固定操作路由：`ListBuckets` 走原生 OSS HTTP；`PutBucket`、`ListObjectsV2`、对象上传/下载/删除/复制及 Multipart 全流程走 AWS SDK。一次操作不得在失败后从 AWS 路径自动切换到原生 HTTP 路径。
- Aliyun OSS 的标准公网 S3 Endpoint 为 `https://s3.oss-{region}.aliyuncs.com`，标准内网 Endpoint 为 `https://s3.oss-{region}-internal.aliyuncs.com`；AWS 客户端必须使用虚拟主机寻址，不得启用 path-style。
- Aliyun OSS 的 `PutObjectRequest` 和 `UploadPartRequest` 必须设置 `UseChunkEncoding=false`、`DisablePayloadSigning=true`，请求与响应 checksum 策略固定为 `WHEN_REQUIRED`，所有数据传输使用 HTTPS。
- AWS SDK、原生 `HttpClient`、本地分片读取和下载响应复制必须接收同一个 Runtime `CancellationToken`。Aliyun OSS 声明异步上传/下载与 cooperative cancellation，不额外声明 active abort。
- Aliyun OSS Multipart 由 AtomBox 明确选择普通上传或分片上传；分片读取使用可取消异步读取。取消或失败后不得执行 Complete，并用独立、最长 10 秒的清理令牌 best-effort 调用 `AbortMultipartUpload`；清理失败不得覆盖原始取消或上传错误。
- 原生 OSS HTTP 第一阶段只实现 `ListBuckets`，使用独立 `OSS4-HMAC-SHA256` signer。Signer 和错误解析都属于 Providers 内部实现，不能泄漏凭据、Authorization、canonical request 或派生密钥。
- WebDAV 上传文件前应确保远端父目录存在；下载目录应返回 Validation；下载不存在文件应返回 NotFound；创建目录使用 MKCOL，移动/重命名使用 MOVE。
- Provider 契约提供 `CreateFolderAsync`、`RenameAsync`、`MoveAsync` 三个文件管理入口；不支持的 provider 必须返回 `OperationNotSupported`，不能伪装成功。
- SFTP/FTP/WebDAV 必须真实支持创建目录、重命名和移动；对象存储 provider 在未接入 SDK copy-object 前不得用“下载到内存再上传”的方式伪装 move/rename。

S3 compatible 对象存储分层：

- `S3CompatibleProvider` 承载通用对象存储运行时行为：bucket/key 路径解析、prefix 伪目录列表、上传、下载、删除、复制、分页游标和统一错误映射。
- `IAsyncS3CompatibleClient` 是迁移后对象存储的数据面合同；实现必须把调用方 `CancellationToken` 传入网络 I/O 和流 I/O，不能用 `Task.FromResult` 包装同步传输伪装异步。
- `AwsSdkS3CompatibleClient` 是阿里云、腾讯云、七牛、又拍云、华为云、百度云、京东云、火山引擎和通用 S3 的共享异步内核。它通过 `AwsSdkS3ClientOptions` 选择 ListObjects V1/V2、chunk/payload 策略、单次复制上限和 Multipart copy part 大小。
- `AwsSdkS3ProviderFactory` 只负责凭据到 AWS credential 的映射、公共 checksum 配置、client 生命周期和通用 Provider 装配。各供应商 Creator 仍负责独立 Endpoint/Profile 校验，禁止复用一份没有差异的 Generic S3 配置。
- `AliyunOssS3Adapter` 组合共享 AWS client 与最小原生 OSS service client；固定由原生路径处理 `ListBuckets`，其余操作委托 AWS client，失败后不 fallback。
- `S3CompatibleAwsV4Client` 是冻结的同步兼容实现，只允许当前后置的青云 QingStor 使用。它不能作为新增供应商或迁移供应商的默认实现。
- `IS3CompatibleClient` 与 `SynchronousS3CompatibleClientAdapter` 只为青云冻结路径保留；任何新对象存储能力必须使用真实异步合同。
- 具体云厂商 Creator 负责地域、寻址、Endpoint、Bucket 创建限制、单 Bucket root 和凭据迁移，不复制通用对象存储 Provider 行为。

供应商适配矩阵：

| Provider | 寻址 / 列表 | 特殊约束 |
| --- | --- | --- |
| 阿里云 OSS | virtual-host / V2 | 原生 `ListBuckets`；公网/内网配对 Endpoint。 |
| 腾讯云 COS | virtual-host / V2 | Bucket 保留 APPID 后缀。 |
| 七牛 Kodo | path-style / V2 | 地域别名映射，在账号配置的地域创建私有 Bucket。 |
| 又拍云 USS | path-style / V2 | 固定 Endpoint、单 Bucket、独立 S3 凭据，空间由厂商控制台创建。 |
| 华为云 OBS | virtual-host / V1 | Region 可从标准 Endpoint 推导。 |
| 百度 BOS | virtual-host / V2 | 原生 Endpoint 映射到 S3 Endpoint；支持单 Bucket root。 |
| 京东云 OSS | path-style / V2 | 使用账号地域 Endpoint。 |
| 火山引擎 TOS | virtual-host / V2 | `tos-*` 映射到 `tos-s3-*`。 |
| 青云 QingStor | 冻结旧路径 | 本轮不迁移、不扩展。 |

暂不追求：

- 账号级 provider 缓存。
- 自动 token 后台刷新。
- 跨 provider 复制。
- 高级断点续传策略。
- provider 插件化独立发布。
- 每个 provider 单独 csproj。

## 10. Phase 9 收口规则

连接测试：

- 连接测试必须尽量执行真实后端可验证的轻量操作，例如列根目录、获取 bucket 列表、检查 token 当前用户或探测默认路径。
- 连接测试不得启动长期 session、后台刷新或缓存 provider。
- 连接测试失败必须返回 Core 错误模型，不得把 SDK exception 直接穿透到 Application。

真实 provider 手工验收：

- 每个 Phase 8 已接入 provider 都应有最小手工验收步骤：测试连接、列表、上传小文件、下载小文件、删除测试对象、错误凭据测试。
- 手工验收可以记录在发布清单或 provider 验收文档中，不要求自动化真实云端测试。
- 手工验收不得要求提交真实密钥、token 或私有 endpoint 到仓库。

Phase 9 仍不做：

- 新 provider。
- 完整 OAuth 授权流程。
- 后台 token refresh。
- provider session pool。
- 跨 provider 复制。
