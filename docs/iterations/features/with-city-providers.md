# AtomBox.Providers × AtomUI.City 迁移审计

> 状态：Decision Complete / No Migration
>
> Matrix ID：`AB-PROVIDERS × CITY-*`
>
> 实施分支：`feature/with-city`

> 后续实现说明（2026-10-20）：本审计记录的 SFTP 与 Aliyun OSS 运行中不可取消债务，已在 Providers 自身边界内独立治理。SFTP 现使用 SSH.NET 异步上传/下载并贯穿 `CancellationToken`；Aliyun OSS V1 SDK 没有原生 token，普通上传以可取消流/回调中断请求，大文件改为显式 multipart 编排并在取消时 abort 会话。两项改动均不采用 City 能力，也不改变本审计的 City 决策。
>
> 创建时间：2026-10-13

## 结论摘要

`AtomBox.Providers` 是外部存储后端适配层。它拥有具体 SDK、HTTP/FTP/SFTP 协议适配、短生命周期 provider、凭据材料的临时使用以及 SDK/API/协议错误到 Core `StorageError` 的转换；它不拥有 UI、应用级状态、传输任务调度或持久化。

最终审计结论如下：

1. `AtomBox.Providers` 本轮不采用任何 City 能力，不修改生产代码，不新增 City 项目引用。厂商 SDK、自研 HTTP、S3Compatible、FTP 和 SFTP 实现全部保持现状。
2. `City.Data` 曾是原生 HTTP 路径的候选，但统一 transport 带来的收益不足以抵消厂商错误语义、多账号认证、stream replay、幂等和传递依赖成本。本轮明确不采用，也不再把 City.Data 能力增强列为 AtomBox.Provider 的迁移前置条件。
3. `City.Security` 表达 City Host 当前用户、权限和 access token；AtomBox Provider 凭据则属于多个外部存储账号，包含密码、私钥、AccessKey secret 和网盘 token。二者不是同一安全边界，不能用 City.Security 替换 Core `ICredentialStore` / `CredentialMaterialLease`。
4. `City.PluginSystem` 对未来第三方 Provider 插件可能有价值，但当前产品设计没有 Provider 插件化目标。本轮不采用；未来只有在产品正式立项后才能重新审计。
5. Providers 当前没有适合 City.State 或 City.EventBus 的应用级事实。Provider 是短生命周期请求对象，操作结果已经同步返回调用方；为相同事实再发布 State/Event 会制造双通道。
6. Providers 自有的异步、取消、连接复用、内存和 dispose 问题保留为独立工程债，不以 City 迁移名义处理。

## 能力所有权

依照迁移总纲的“能力归属铁律”，以下能力即使最先由 Desktop 页面触发，也仍属于 Providers：

- HTTP、SDK、FTP、SFTP 请求的发起和取消；
- provider 内部连接和 client 的生命周期；
- provider 专属认证头、签名和临时凭据使用；
- provider 响应 DTO、错误码、分页 token 和路径语义的转换；
- SDK/API/协议异常到 Core `StorageError` 的映射；
- Provider 类型目录、descriptor 和 creator 的注册。

Desktop 只能消费 Application 返回的 Core 结果并展示错误，不得在 UI 层补充 Provider retry、缓存、认证、错误映射或连接恢复。

## 当前实现事实

### 模块边界

- `AtomBox.Providers` 当前只直接引用 `AtomBox.Core`、DI abstractions、具体厂商 SDK 和协议库。
- `StorageProviderRegistry` 是应用级静态目录；`StorageProviderFactory` 根据账号和 creator 创建短生命周期 `IStorageProvider`。
- `ProviderCredentialResolver` 通过 Core `ICredentialStore` 获取 `CredentialMaterialLease`；具体 Provider 随自身释放该 lease。
- Application 和 Transfer 均通过 Core `IStorageProviderFactory` 创建并以 `await using` 释放 provider。
- 15 个内置 Provider 在 `AddAtomBoxProviders()` 中静态注册；当前不存在运行时动态贡献和撤销。

### 三类传输实现

| 实现族 | 当前代表 | City.Data 适用性 |
| --- | --- | --- |
| 厂商 SDK | Aliyun OSS、Tencent COS、Qiniu、Huawei OBS、Baidu BOS、Volcengine TOS、AWS SDK S3 | 不应为了“统一”再包一层 HTTP transport。SDK 自己拥有协议、签名和部分 retry；只能按具体缺口评估共享调度或诊断。 |
| 原生 HTTP | WebDAV、Upyun、S3CompatibleAwsV4、AliyunDrive、BaiduNetDisk、Qiniu 部分下载 | `City.Data` 的主要候选范围。可受益于真正异步取消、HttpClientFactory、统一 timeout/resilience/diagnostics 和大载荷能力。 |
| 非 HTTP 协议 | FTP、SFTP | City.Data 当前不是通用任意协议执行器，不适用。连接、取消和异步问题必须在具体协议 adapter 中解决。 |

## `AB-PROVIDERS × CITY-DATA`

最终标记：`否`。

### 可获得的收益

- 使用 `IHttpClientFactory`，避免短生命周期 provider 每次创建并销毁自己的 `HttpClient`，保留连接池和 DNS 生命周期治理。
- HTTP 请求真正异步执行并贯穿 `CancellationToken`，而不是只在同步阻塞调用前检查一次取消。
- 对适合重试的查询请求统一使用 timeout、retry、circuit breaker、rate limit、concurrency policy 和 diagnostics。
- 对 HTTP 上传/下载复用固定缓冲区、进度节流、range/resume 和临时文件所有权能力。
- 让请求 correlation、attempt 和失败类型具有统一可观测合同。

### City 当前合同冲突

第一，`HttpDataTransport` 在调用 response mapper 之前处理非 2xx 响应，只返回按 HTTP status 映射的通用 `DataError`。AtomBox 当前需要读取阿里云盘、百度网盘、S3 compatible、WebDAV 等响应体中的 provider error code，再映射为 Core 的 Authentication、Authorization、NotFound、Conflict、Network、Provider 和 retryable 语义。直接迁移会丢失 `ProviderErrorCode` 和更精确的可重试判断。

第二，`IDataCredentialProvider` 收到的 `DataAuthenticationContext` 只有 `ClientId`、`OperationName` 和 authentication options，没有显式账号/credential identity 或安全上下文。AtomBox 可以同时操作多个 OSS、网盘、WebDAV 账号；不能依赖一个“当前全局账号”选择凭据，也不能把 secret 放入 `ClientId`。如果绕过 credential phase，在 request factory 中手工追加认证头，则 cache/security identity、认证诊断和“认证先于 transport”的框架合同都会被削弱。

第三，`AtomUI.City.Data` 当前同时引用 Security，并整体携带 HTTP、gRPC 和 SignalR 能力；Security 又引用 Routing。Providers 即使只使用 HTTP，也会承担与 provider 业务无关的传递依赖。这不是功能正确性的绝对阻断项，但在决定直接引用前必须明确接受，或在 City 中形成更细的包/合同边界。

第四，City retry 不能自动使上传、删除、move、multipart complete 等 mutation 变得幂等。非可重放 stream 也不能在失败后直接重试。即使采用 City.Data，Provider 仍必须逐操作声明 retry、idempotency 和 stream replay 规则，默认禁止 mutation retry。

### AtomBox 当前实现缺口

这些问题是审计发现的 Providers 自有工程债，不能错误归因给 City：

- WebDAV、Upyun、S3CompatibleAwsV4、FTP、SFTP 以及多个 SDK provider 通过同步 client 执行网络 IO，再用 `Task.FromResult` 包装结果。公开方法看似异步，实际会阻塞调用线程。
- 多数同步 provider 只在 IO 前调用一次 `ThrowIfCancellationRequested()`；IO 开始后不能被 token 中断。
- 多个 HTTP client 由短生命周期 provider 直接 `new HttpClient()` 并随 provider 释放，无法获得共享连接池治理；接受外部 `HttpClient` 的实现也没有区分 client 所有权。
- Upyun、S3CompatibleAwsV4、Qiniu 和 Tencent COS 的部分下载会先把完整响应读入 byte array，再复制到调用方 stream；大文件内存上限不受控。
- 现有 `ObjectStorageContract_DisposedProviderRejectsOperations` 只验证测试内的内存 fake，没有验证生产 provider 的 dispose 后行为。
- 当前单元测试主要验证结果映射和 happy path；真实取消、超时、并发 dispose、HTTP 连接复用、retry 幂等和非可重放 stream 仍缺专项门禁。

### 最终决策

不迁移。上述收益与冲突作为历史评估依据保留，但不再形成 City 或 AtomBox 的待实施方案：

1. 厂商 SDK 始终优先保留，不为了统一外观改写为 City.Data。
2. 自研 HTTP、S3Compatible、FTP 和 SFTP 同样维持现有 adapter；其工程问题在 Providers 内按协议和 SDK 特性分别治理。
3. 不为 AtomBox.Provider 增加 City.Data 的非成功响应 mapper、多账号 credential identity 或 HTTP-only 包边界需求。
4. 未来若出现独立于本轮迁移的新产品需求，必须重新建立矩阵单元和专项设计，不能沿用本次已否决方案直接施工。

## `AB-PROVIDERS × CITY-SECURITY`

最终标记：`否`。

City.Security 的账号是 City Host 的认证主体；AtomBox 的 `StorageAccount` 是可同时存在、可并发使用的外部存储连接。AtomBox 凭据还包含 City.Security 明确不存储的密码和私钥。

建议本单元判定为 `否`：

- 不用 `IAccountSessionManager` 表达存储账号。
- 不用 City 全局活动 principal 选择 Provider 凭据。
- 不用 City `ICredentialStore` 替换 Core 同名但语义不同的存储凭据端口。
- 网盘 OAuth refresh 如后续实施，仍由 Provider/Factory 在短生命周期操作链路中完成，并通过 Core 端口原子更新凭据；不能把多个外部存储账号伪装成 City 当前登录用户。

City.Security 仍可在 Application/Presentation 层决定“当前用户是否允许管理账号或执行传输”，但那不属于 Providers 的凭据实现。

## `AB-PROVIDERS × CITY-PLUGINSYSTEM`

最终标记：`否`。

City.PluginSystem 可以提供插件发现、安装、manifest、依赖校验、加载、卸载和 runtime lease；它不会自动定义 AtomBox Provider 插件语义。

如果未来启用第三方 Provider 插件，AtomBox 仍必须负责：

- 在 Host 共享程序集定义稳定的 Provider contribution contract；
- 原子注册 descriptor 与 creator，拒绝重复 Provider ID；
- 用 immutable snapshot 对外发布动态 provider catalog；
- 在撤销 contribution 前拒绝新建 provider，并等待既有 provider 释放；
- 将 contribution lease 绑定 City `PluginRuntime`，卸载失败进入可诊断状态；
- 保证插件 SDK DTO、secret 和 City Plugin 类型不进入 Core 业务模型。

当前设计文档明确把“Provider 插件化独立发布”列为非第一版目标。因此建议保持“未来专项”，不在 Data 迁移中顺手实施，也不把 City.PluginSystem 的通用加载能力误写成 AtomBox Provider 插件已经可用。

## 其他 City 能力

| Matrix ID | 第一轮判断 | 原因 |
| --- | --- | --- |
| `AB-PROVIDERS × CITY-CORE` | `否` | Providers 不拥有 Host/module 生命周期；Desktop 组合根注册 `AddAtomBoxProviders()` 即可。短生命周期 provider 由调用方 `await using` 管理。 |
| `AB-PROVIDERS × CITY-MVVM` | `否` | 无 ViewModel、Command 或 UI 属性。 |
| `AB-PROVIDERS × CITY-ROUTING` | `否` | Provider 不拥有导航。 |
| `AB-PROVIDERS × CITY-PRESENTATION` | `否` | Provider 不拥有 View、窗口或 UI 线程。 |
| `AB-PROVIDERS × CITY-STATE` | `否` | 当前只有静态 provider catalog 和短生命周期运行对象，没有应用级可变事实。 |
| `AB-PROVIDERS × CITY-EVENTBUS` | `否` | Provider 操作通过返回值完成；事件会和调用结果形成双通道。 |
| `AB-PROVIDERS × CITY-LOCALIZATION` | `否` | Provider ID 和能力是业务合同，文化解析继续由 Application/Presentation 负责。 |
| `AB-PROVIDERS × CITY-BUILD/GENERATORS` | `否` | 当前内置 Provider 没有 City build-time metadata。 |
| `AB-PROVIDERS × CITY-TESTING` | `否` | Provider contract 与真实后端测试继续由 AtomBox 自己维护，不因本轮引入 City.Testing。 |
| `AB-PROVIDERS × CITY-CLI/TEMPLATES` | `否` | 当前没有 Provider 插件开发者工具需求。 |

## 实施结论

1. 不修改 `AtomBox.Providers` 生产代码、项目引用或 DI 注册。
2. 不修改 AtomUI.City，不创建 City.Data 前置任务。
3. Providers 自有工程债另行排期；Provider 插件化若未来立项，重新开展专项审计。

## 当前证据

- 阅读 Providers 三份正式设计文档、全部生产项目结构、Registry/Factory/credential/error mapping、DI 注册、原生 HTTP client、协议 adapter 和 Provider tests。
- `dotnet test tests/AtomBox.Providers.Tests/AtomBox.Providers.Tests.csproj -c Release --no-restore`：171/171 通过。
- 该绿色结果只代表现有自动化基线，不代表真实云端、FTP/SFTP/WebDAV 环境均完成本轮人工验收；多个 integration test 会在缺少本地凭据配置时提前返回。
- 本轮没有修改 City 或 Providers 生产代码。
