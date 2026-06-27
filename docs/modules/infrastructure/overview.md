# AtomBox.Infrastructure 模块设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的架构、模块边界、依赖关系、目录规划或实现约束
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的设计边界，必须先说明原因，再同步更新相关设计文档。

## 1. 模块定位

`AtomBox.Infrastructure` 是 AtomBox 的本地技术实现层。

它负责配置、凭据、日志、本地持久化、缓存、系统文件访问等基础能力。Infrastructure 实现 Core 中定义的技术端口，并由 Desktop 组合根注入给 Application、Transfer、Providers 使用。

Infrastructure 不是业务用例层。把账号管理流程、远程浏览流程、上传下载调度流程写进 Infrastructure，是非常糟糕的设计，会让技术实现层反过来控制业务。

## 2. 边界规则

Infrastructure 允许包含：

- 配置文件读写和配置模型持久化。
- 凭据存储、加密、系统 keychain / DPAPI / credential manager 适配。
- 日志框架适配和日志初始化。
- 本地缓存、本地数据库、本地文件存储实现。
- Core 端口实现，例如账号仓储、设置仓储、凭据存储、传输任务仓储、传输状态存储。
- 面向 DI 的 `IServiceCollection` 基础设施注册扩展。

Infrastructure 禁止包含：

- Avalonia、AtomUI、View、ViewModel、Command、Binding 等 UI 类型。
- 具体用户用例编排，例如添加账号、刷新目录、创建上传任务。
- 传输队列 worker、并发调度、重试策略等 Transfer Engine 逻辑。
- 具体 provider 的业务适配逻辑。
- Core 领域规则的重复实现。
- 明文长期保存 AccessKey、密码、refresh token 等敏感信息。
- 持有、注入或直接解析 `IServiceProvider`。

Infrastructure 可以执行外部 IO，但不能决定业务流程。它提供能力，不当指挥官。

## 3. 推荐目录

```text
src/AtomBox.Infrastructure/
  AtomBox.Infrastructure.csproj
  Configuration/
  Credentials/
  Logging/
  Storage/
  Caching/
  DependencyInjection/
```

## 4. 目录职责

| 目录 | 职责 |
| --- | --- |
| `Configuration/` | 保存应用配置读写、配置迁移、配置默认值。 |
| `Credentials/` | 保存凭据存储、加密、系统凭据服务适配。 |
| `Logging/` | 保存日志框架适配和日志初始化。 |
| `Storage/` | 保存本地持久化实现，例如账号仓储、设置仓储、传输任务仓储。 |
| `Caching/` | 保存本地缓存、缩略图缓存、元数据缓存等实现。 |
| `DependencyInjection/` | 定义基础设施注册扩展，例如 `AddAtomBoxInfrastructure()`。 |

## 5. 首批核心对象范围

第一阶段 Infrastructure 只定义必要的本地技术能力，不提前引入复杂数据库和缓存体系。

配置：

- `AppConfigurationStore`
- `ApplicationSettingsRepository`
- `ConfigurationMigration`

凭据：

- `CredentialStore`
- `ProtectedCredentialPayload`
- `CredentialProtectionService`

日志：

- `LoggingInitializer`
- `LogOptions`

本地持久化：

- `StorageAccountRepository`
- `TransferTaskStore`
- `TransferStateStore`

缓存：

- `MetadataCache`
- `ThumbnailCache`

注册入口：

- `InfrastructureServiceCollectionExtensions`

这些对象属于技术实现语言，不是 Core 领域模型，也不是 Application 用例对象。

## 6. 设计约束

- Infrastructure 可以依赖 Core，但不能依赖 Desktop、ViewModel 或 UI 类型。
- Infrastructure 可以依赖 `Microsoft.Extensions.DependencyInjection.Abstractions` 暴露 `IServiceCollection` 注册扩展，但不能在业务对象、repository、store、cache、credential service 中持有或直接解析 `IServiceProvider`。
- Infrastructure 不承载用户用例，不决定业务流程。
- Infrastructure 实现 Core 中定义的仓储、凭据、配置、传输任务存储等技术端口，并通过 Desktop 组合根注入给其他模块。
- Infrastructure 不依赖 Application、Transfer 或 Providers；不能实现定义在这些模块中的接口。
- 凭据必须通过专门的 credential store 保存，普通配置文件只保存凭据引用，不保存 secret 明文。
- `CredentialRef`、`CredentialLease` 相关契约定义在 Core；lease 计数、pending-delete、物理清理、并发锁和平台加密实现属于 Infrastructure。
- 配置文件损坏时不能静默覆盖用户配置，必须有备份、错误报告或迁移策略。
- 日志实现不能泄漏敏感凭据。
- 本地持久化模型不能直接替代 Core 模型，必要时应做映射。

## 7. 生命周期约束

Infrastructure 提供应用级技术服务，但不能因为服务长期存在就长期占用底层资源。

- `IStorageAccountRepository`、`IApplicationSettingsRepository`、`ITransferTaskStore`、`ITransferStateStore` 可以作为应用级服务存在。
- 文件句柄、数据库连接、事务、锁等内部资源必须按操作打开和释放。
- `ICredentialStore` 是应用级服务，但不得缓存 plaintext secret。
- `ICredentialStore` 必须支持 `CredentialLease` 或等价运行时占用机制；lease 存在期间，对应 `CredentialRef` 的 secret payload 不能被物理删除。
- 凭据轮换后，旧 `CredentialRef` 可以标记为 pending-delete，但必须等没有运行中的任务、provider 或 provider session lease 后再清理。
- `LoggingInitializer` 属于启动初始化对象，应用退出时必须 flush / release。
- `MetadataCache`、`ThumbnailCache` 是应用级缓存服务，必须有容量、时间、账号边界或清理策略。
- 第一版 Infrastructure 不启动后台缓存清理线程；缓存清理只能发生在读写路径、容量淘汰、启动维护或关闭维护中。
- 关闭阶段必须尽力保存账号配置、凭据引用、传输任务状态等关键状态。
- metadata cache、thumbnail cache 属于普通缓存，关闭时 best-effort flush 即可。

Infrastructure 失败可能阻断应用启动，但 Infrastructure 不能自己展示 UI。启动错误展示由 Desktop / Presentation 负责。
