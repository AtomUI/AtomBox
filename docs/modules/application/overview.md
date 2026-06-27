# AtomBox.Application 模块设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的架构、模块边界、依赖关系、目录规划或实现约束
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的设计边界，必须先说明原因，再同步更新相关设计文档。

## 1. 模块定位

`AtomBox.Application` 是 AtomBox 的用户用例编排层。

它负责把 Desktop 层传入的用户动作转换为明确的应用流程，例如账号管理、远程目录浏览、连接测试、创建传输任务、删除远程资源、刷新状态等。

Application 不是 UI 层，也不是具体 provider 实现层。把 ViewModel、Avalonia 控件、阿里云 OSS SDK、SFTP 客户端直接塞进 Application，是非常糟糕的设计，会让用例层同时污染 UI 和基础设施细节。

## 2. 边界规则

Application 允许包含：

- 用户用例服务，例如账号、浏览、传输、设置相关的 app service。
- 用例输入对象和输出对象，例如请求、结果、查询条件。
- 应用级流程编排，例如通过 Core 端口读取账号、检查 capability、创建传输任务描述。
- 为连接测试、远程浏览、能力探测等短流程创建短生命周期 provider。
- 应用级错误结果组织，例如用 Core 的 `OperationResult<T>` 和 `StorageError` 表达用例结果；不转换具体 SDK/API/协议异常。
- 面向 DI 的服务注册扩展。

Application 禁止包含：

- Avalonia、AtomUI、View、ViewModel、Command、Binding 等 UI 类型。
- 具体云厂商 SDK、FTP/SFTP 库、网盘 API 客户端类型。
- JSON 配置读写、SQLite 访问、凭据加密、文件系统持久化等技术实现。
- 传输 worker、队列调度、并发控制等 Transfer Engine 运行时实现。
- SDK DTO 到 UI DTO 的直接透传。
- 捕获、判断或转换具体 SDK/API/协议异常。
- 直接 `new` 具体 provider 实现。
- 持有长期 provider 实例，或把 provider 实例塞进传输任务。
- 跨模块稳定端口定义，例如仓储、provider factory、transfer store、credential store。

Application 可以编排业务流程，但不能亲自执行外部系统细节。只要这段代码开始关心某个 SDK 怎么调用、某个窗口怎么显示、某个配置文件怎么落盘，它就不该在 Application。

## 3. 推荐目录

```text
src/AtomBox.Application/
  AtomBox.Application.csproj
  Accounts/
  Browsing/
  Transfers/
  Settings/
  DependencyInjection/
```

## 4. 目录职责

| 目录 | 职责 |
| --- | --- |
| `Accounts/` | 定义账号管理相关用例，例如添加账号、更新账号、删除账号、连接测试、列出账号。 |
| `Browsing/` | 定义远程资源浏览相关用例，例如列目录、刷新目录、删除远程资源、获取路径上下文。 |
| `Transfers/` | 定义传输相关用例入口，例如创建上传任务、创建下载任务、取消任务、重试任务；不实现调度器。 |
| `Settings/` | 定义应用设置相关用例，例如读取设置、保存设置、更新偏好。 |
| `DependencyInjection/` | 定义 Application 层服务注册扩展，例如 `AddAtomBoxApplication()`。 |

## 5. 首批核心对象范围

第一阶段 Application 只冻结支撑基础用例的服务和请求/结果对象；不冻结后续版本的完整业务流程。

账号用例：

- `AccountAppService`
- `AddStorageAccountRequest`
- `UpdateStorageAccountRequest`
- `DeleteStorageAccountRequest`
- `ListStorageAccountsRequest`
- `TestConnectionRequest`
- `TestConnectionResult`
- `StorageAccountSummary`

浏览用例：

- `RemoteBrowserAppService`
- `ResolveRemoteEntryRequest`
- `RemoteEntryResult`
- `ListRemoteItemsRequest`
- `ListRemoteItemsResult`
- `DeleteRemoteItemRequest`
- `GetRemotePathContextRequest`
- `RemotePathContextResult`

传输用例：

- `TransferAppService`
- `CreateUploadTasksRequest`
- `CreateDownloadTasksRequest`
- `CreateTransferTasksResult`
- `GetTransferQueueRequest`
- `TransferQueueSnapshot`
- `GetTransferHistoryRequest`
- `TransferHistoryPage`
- `CancelTransferTaskRequest`
- `RetryTransferTaskRequest`

传输用例只创建任务描述。任务描述保存 `StorageAccountId`、本地路径、远程路径、传输方向、覆盖策略等必要信息，不保存 provider 实例，也不复制完整账号配置快照。具体 provider 在 Transfer 执行任务时通过 Core 端口解析。

设置用例：

- `SettingsAppService`
- `GetApplicationSettingsRequest`
- `ApplicationSettingsResult`
- `UpdateApplicationSettingsRequest`
- `ResetApplicationSettingsRequest`

Application 消费的 Core 端口：

- `IStorageAccountRepository`
- `IApplicationSettingsRepository`
- `IStorageProviderFactory`
- `ITransferTaskScheduler`
- `ITransferTaskStore`
- `ITransferStateStore`

这些对象属于应用流程语言，不是 UI 展示 DTO，也不是 provider SDK DTO。

Application 消费 `IStorageProviderFactory` 的边界必须非常窄：它可以为了连接测试、远程浏览、能力探测创建短生命周期 provider，但不能为 Transfer 创建 provider，不能持有长期 provider，也不能把 provider 实例放入传输任务。

## 6. 设计约束

- Application 只能依赖 Core，以及必要的抽象接口；不能依赖 Desktop、具体 Provider 实现或 Infrastructure 实现。
- Application 不定义跨模块稳定端口，只消费 Core 中定义的端口。
- ViewModel 只能调用 Application 暴露的用例服务，不能绕过 Application 直接调用 Provider 或 Infrastructure。
- Application 不直接保存凭据，不直接读取配置文件，不直接访问数据库。
- Application 不接触具体 SDK/API/协议异常；这些异常必须先在 Providers 内转换为 Core 统一错误模型。
- Application 不负责传输调度细节，只负责创建、操作或查询传输任务。
- Application 不能替 Transfer 预创建 provider；传输执行阶段的 provider 创建由 Transfer 通过 Core 端口触发。
- Application 可以通过 Core 的传输状态存储/查询端口组织传输状态结果，供 Presentation 使用；但它只能拿到不可变快照，不能拿到 `TransferQueue`、`TransferManager`、worker 或其他可变运行时对象。
- ViewModel 不直接订阅 Transfer 运行时对象。
- Application 返回的结果对象必须使用 Core 统一模型，例如 `RemoteItem`、`RemotePath`、`StorageError`。
- Application 第一版不定义 `ApplicationResult<T>`；用例 API 直接返回 Core 的 `OperationResult<T>`。结果对象自身承载分页、警告、能力提示等用例上下文。
- Application 中的用例接口必须表达用户流程，不能退化成对 Infrastructure、Transfer 或 Provider 的机械转发。
- Application 可以做流程编排和规则校验，但具体外部 IO 必须交给 Provider、Transfer 或 Infrastructure。

## 7. 生命周期约束

Application Service 是应用级无状态服务，可以作为长期服务实例存在。

- `AccountAppService`、`RemoteBrowserAppService`、`TransferAppService`、`SettingsAppService` 不持有 provider、SDK client、secret material、ViewModel 或 UI 状态。
- Application 返回的结果对象是一次用例调用的短生命周期结果快照。
- Application 可以为了连接测试、远程浏览、能力探测创建短生命周期 provider，但不能为 Transfer 创建 provider，不能保存 provider 实例。
- Application 短用例可以一次用例创建一个 provider；这里的用例包括连接测试、目录列表、能力探测、远程对象删除等短流程。
- Application 可以触发 provider 创建，但不直接读取 secret material，不直接创建或释放 `CredentialLease`；lease 生命周期由 provider factory / provider 操作链路封装。
- Application 不能把 UI 点击次数等同于 provider 生命周期，不能把 provider 生命周期扩大到多个独立用户提交批次。
- 账号删除用例必须检查未完成传输任务引用，不能允许删除仍被任务引用的账号。
- 账号修改只影响后续操作，不影响已经运行中的传输任务。
- 设置保存必须通过 Application 用例显式触发，不能由 ViewModel 或 Infrastructure 私自决定业务流程。

Application 的服务生命周期只表达用例编排能力，不代表它拥有底层资源。资源生命周期归 Provider、Transfer 或 Infrastructure 管。
