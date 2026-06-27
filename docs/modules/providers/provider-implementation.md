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
- WebDAV 上传文件前应确保远端父目录存在；下载目录应返回 Validation；下载不存在文件应返回 NotFound；创建目录使用 MKCOL，移动/重命名使用 MOVE。
- Provider 契约提供 `CreateFolderAsync`、`RenameAsync`、`MoveAsync` 三个文件管理入口；不支持的 provider 必须返回 `OperationNotSupported`，不能伪装成功。
- SFTP/FTP/WebDAV 必须真实支持创建目录、重命名和移动；对象存储 provider 在未接入 SDK copy-object 前不得用“下载到内存再上传”的方式伪装 move/rename。

S3 compatible 对象存储分层：

- `S3CompatibleProvider` 承载通用对象存储运行时行为：bucket/key 路径解析、prefix 伪目录列表、上传、下载、删除和统一错误映射。
- `IS3CompatibleClient` 隔离具体 SDK/API，返回中性 `S3Compatible*` 数据模型。
- `S3CompatibleAwsV4Client` 用于 AWS Signature V4 且 S3 API 兼容的服务，例如华为云 OBS、百度智能云 BOS、京东云 OSS、青云 QingStor 当前适配路径。
- 火山引擎 TOS 使用官方 `Volcengine.TOS.SDK.NetCore`，通过 `VolcengineTosSdkClient` 适配到 `IS3CompatibleClient`，运行时仍复用 `S3CompatibleProvider`。
- 具体云厂商 creator 负责校验自身必填配置、读取 credential material、选择合适的 `IS3CompatibleClient`，但不复制通用对象存储 provider 行为。

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
