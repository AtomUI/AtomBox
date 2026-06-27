# AtomBox.Core 模块设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的架构、模块边界、依赖关系、目录规划或实现约束
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的设计边界，必须先说明原因，再同步更新相关设计文档。

## 1. 模块定位

`AtomBox.Core` 是 AtomBox 的产品内核，不是运行流程中的普通步骤。

它定义 AtomBox 远程存储管理领域的统一语言、核心对象、跨模块稳定端口、能力模型、错误模型和纯领域规则。其他模块可以依赖 Core，但 Core 不依赖任何 AtomBox 外层模块。

Core 不是公共工具库。把杂七杂八的 `Utils`、SDK DTO、UI 绑定对象塞进 Core，是非常糟糕的设计，会直接污染整个系统的依赖中心。

## 2. 边界规则

Core 允许包含：

- 核心模型，例如账号、远程资源、传输任务。
- 值对象，例如远程路径、账号 ID、凭据引用。
- enum 和常量，但必须属于 AtomBox 产品语言。
- 抽象接口，例如 provider 抽象、仓储端口、凭据端口、传输端口和能力接口。
- 结果模型、错误模型、能力模型。
- 针对 AtomBox 核心对象的、无外部依赖的领域处理方法。

Core 禁止包含：

- Avalonia、AtomUI、View、ViewModel、Command、Binding 等 UI 类型。
- Serilog、NLog 等具体日志框架类型。
- 阿里云 OSS、腾讯 COS、七牛、FTP/SFTP、网盘 API 等具体 SDK 类型。
- HTTP API 具体调用、JSON 配置读写、SQLite 访问、文件系统持久化。
- 通用 `StringUtils`、`JsonUtils`、`FileUtils`、`HttpUtils` 这类杂物工具。
- DI 注册、应用启动、模块初始化流程。

Core 中可以有业务逻辑，但只能是纯领域规则。只要这段代码需要访问 UI、文件、网络、数据库、日志或具体 SDK，它就不该进 Core。

## 3. 推荐目录

```text
src/AtomBox.Core/
  AtomBox.Core.csproj
  Accounts/
  Capabilities/
  Credentials/
  Errors/
  Providers/
  RemoteItems/
  Results/
  Settings/
  Transfers/
  ValueObjects/
```

## 4. 目录职责

| 目录 | 职责 |
| --- | --- |
| `Accounts/` | 定义账号、连接配置、provider 类型、凭据引用等核心模型。 |
| `Capabilities/` | 定义 provider 能力模型和能力判断规则，例如上传、下载、删除、搜索、分享、分片上传等能力。 |
| `Credentials/` | 定义凭据引用、凭据存储端口和凭据相关核心模型，不保存 secret 明文。 |
| `Errors/` | 定义统一错误码、错误对象、可重试/不可重试语义。 |
| `Providers/` | 定义 provider 抽象接口、provider factory/registry 端口、provider 描述对象、可选能力接口。 |
| `RemoteItems/` | 定义远程资源对象，例如 `RemoteItem`、`RemoteItemKind`。 |
| `Results/` | 定义跨模块调用结果模型，例如 `OperationResult`、`OperationResult<T>`；不要和错误模型混在一起。 |
| `Settings/` | 定义应用设置模型和设置仓储端口。 |
| `Transfers/` | 定义传输任务核心状态模型，例如任务方向、状态、进度；不包含调度器实现。 |
| `ValueObjects/` | 定义 `RemotePath`、`LocalPath`、`StorageAccountId`、`StorageProviderId`、`CredentialRef` 等值对象。 |

Core 端口按能力族组织，例如 provider、账号仓储、凭据、传输、设置；不要按 Application、Presentation、Providers、Transfer、Infrastructure 这些外层消费者组织。Core 服务产品语言，不服务某个外层模块。

## 5. 首批核心对象范围

第一阶段 Core 只定义支撑基础架构的首批核心对象，不提前冻结所有字段细节。

账号与凭据：

- `StorageAccount`
- `StorageAccountId`
- `StorageProviderCategory`
- `StorageProviderId`
- `CredentialRef`

凭据运行时契约：

- `CredentialLease`

`CredentialLease` 不是账号模型，不是值对象，不进入持久化；它只是 Core 暴露给外层实现的 lease 抽象句柄或端口契约。

远程资源：

- `RemotePath`
- `RemoteItem`
- `RemoteItemKind`

能力与错误：

- `StorageCapability`
- `StorageCapabilitySet`
- `StorageError`
- `OperationResult<T>`

Provider 抽象：

- `IStorageProvider`
- `IStorageProviderFactory`
- `IStorageProviderRegistry`
- 可选 capability interfaces

Factory / Registry 分工：

- `IStorageProviderRegistry` 负责登记、枚举、查询 provider 描述、provider 类型和能力声明，不创建 provider 实例。
- `IStorageProviderFactory` 负责基于 `StorageAccount`、凭据引用和 provider 配置创建 `IStorageProvider` 实例，不承担 provider 目录查询职责。

跨模块端口：

- `IStorageAccountRepository`
- `IApplicationSettingsRepository`
- `ICredentialStore`
- `ITransferTaskStore`
- `ITransferStateStore`
- `ITransferTaskScheduler`

传输核心模型：

- `TransferTask`
- `TransferTaskId`
- `TransferStatus`
- `TransferDirection`
- `TransferProgress`
- `TransferOptions`

这些对象是 AtomBox 内部统一语言的一部分，不是某个 provider SDK 的包装对象，也不是 UI 展示 DTO。

## 6. 设计约束

- Provider 返回的统一远程对象必须使用 Core 中定义的 `RemoteItem`，不能把 SDK DTO 泄漏给 Application、Transfer 或 Desktop。
- 远程路径必须使用 `RemotePath`，本地路径必须使用 `LocalPath`；不能用裸 `string` 长期贯穿系统。
- OSS、FTP/SFTP、阿里云盘、百度网盘的差异必须被封装在 Provider 内部，不能污染 Core 模型。
- Core 中的公共方法必须围绕 AtomBox 核心对象表达领域规则，不能退化成普通工具函数。
- Core 不做 DI 注册，不承载运行时初始化流程。
- Core 不读取配置，不保存凭据，不访问数据库，不写日志。
- Core 只定义统一错误模型和错误语义，不捕获、不判断、不转换任何具体 SDK/API/协议异常。
- Core 不依赖任何外层项目；任何外层项目反向进入 Core 都是架构错误。
- 跨模块稳定端口必须定义在 Core，端口方法签名只能使用 Core 类型和 .NET BCL 类型。
- UI、SDK、数据库、配置文件、日志框架相关接口不得伪装成 Core 端口。

## 7. 生命周期约束

Core 中的模型和值对象大多是业务事实或业务快照，不由 DI 管理。

- `StorageAccountId` 是稳定不可变标识，账号改名、endpoint 修改、region 修改、凭据轮换都不能改变它。
- `StorageAccount` 第一版不设计 enabled / disabled 状态；账号删除必须先检查是否仍被未完成传输任务引用。
- `CredentialRef` 是凭据引用，不是凭据明文；Core 不定义、保存、传递 secret material。
- `CredentialLease` 是运行时占用声明，用来表达某个 worker、provider 或 provider session 正在使用某个 `CredentialRef`；它不是持久化业务对象。
- Core 只能定义 `CredentialLease` 的抽象句柄或端口契约；lease 计数、pending-delete、物理清理和并发锁必须由 Infrastructure 实现。
- `TransferTask` 是持久化业务对象，保存账号 ID、本地路径、远程路径、方向、选项和状态；不能保存 provider 实例、SDK client、secret material 或完整账号配置快照。
- `RemoteItem` 是远程资源快照，不是远程文件的活动代理对象。
- `RemotePath` 和 `LocalPath` 都是不可变值对象；远程路径不能用裸 `string` 长期替代，本地路径也不能用 `FileInfo`、`DirectoryInfo` 或裸 `string` 长期替代。
- `StorageError` 是结构化错误结果快照，不长期持有原始 SDK Exception。

Core 中定义的跨模块端口可以被长期服务实现，但 Core 本身不决定 DI 注册。具体生命周期必须遵守 `Docs/architecture/lifecycle.md`。
