# AtomBox.Core 端口设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的 Core 第一版跨模块稳定端口、端口分类、依赖边界和使用规则
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的端口边界，必须先说明原因，再同步更新相关 Core、Application、Transfer、Providers、Infrastructure、Presentation 文档。

本文档定义 `AtomBox.Core` 的跨模块稳定端口。

端口不是“所有 interface”，也不是“所有 class”。端口是 Core 对外声明的一条稳定能力边界，通常表现为一个 interface，并配套使用 Core 模型、Core 值对象、Core 错误模型和明确的生命周期约束。

## 1. 基本原则

- 端口必须表达跨模块稳定能力边界。
- 端口方法签名只能使用 Core 类型和 .NET BCL 类型。
- 端口不能泄漏 UI、SDK、数据库、配置文件、日志框架或 DI 容器类型。
- 端口定义在 Core，端口实现放在 Transfer、Providers、Infrastructure 或测试项目。
- Application 只消费 Core 端口，不定义跨模块稳定端口。
- Desktop 作为组合根负责装配端口实现，但普通 ViewModel 不直接持有底层运行时对象。

不要把 Core 写成 interface 垃圾场。一个 interface 如果只服务某个实现细节、某个 UI 控件或某个局部策略，它就不该进 Core。

## 2. 端口与模型的区别

端口示例：

```text
IStorageAccountRepository
IStorageProviderFactory
ITransferTaskScheduler
```

这些类型表达“系统必须具备某种能力”。

模型和值对象示例：

```text
StorageAccount
StorageAccountId
RemoteItem
RemotePath
TransferTask
StorageError
```

这些类型表达“系统内部统一语言中的事实、快照或值”。

`StorageAccount` 不是端口，`IStorageAccountRepository` 才是端口。把这两者混为一谈，代码很快会变成一团软泥。

## 3. Provider 端口

Provider 端口表达统一远程存储能力。

第一版端口：

```text
IStorageProvider
IStorageProviderFactory
IStorageProviderRegistry
```

`IStorageProvider` 表达单个短生命周期 provider 实例可执行的远程操作。

第一版建议能力：

- 列出远程资源。
- 获取基础能力。
- 删除远程文件或对象。
- 打开文件夹语义由列表和路径表达，不在 Core 中定义 UI 行为。

`IStorageProvider` 必须返回 Core 模型，例如 `RemoteItem`、`RemotePath`、`StorageError`、`OperationResult<T>`。它不能返回 SDK DTO。

`IStorageProviderFactory` 负责创建短生命周期 provider 实例。

Factory 边界：

- 可以基于 `StorageAccount`、`CredentialRef` 和 provider 配置创建 provider。
- 可以在内部通过凭据端口读取 secret material。
- 不能缓存 provider 实例。
- 不能把 secret material 暴露给 Application、Transfer 或 Presentation。
- 不能承担 provider 类型目录查询职责。

`IStorageProviderRegistry` 负责 provider 类型、描述、能力声明和注册信息。

Registry 边界：

- 可以登记和查询 provider 描述。
- 可以返回 provider 能力声明。
- 不能创建 provider 实例。
- 不能连接远程服务。
- 不能刷新 token。

## 4. 账号与设置端口

账号与设置端口表达本地业务事实的读取和保存能力。

第一版端口：

```text
IStorageAccountRepository
IApplicationSettingsRepository
```

`IStorageAccountRepository` 负责账号配置事实的持久化读写。

它可以提供：

- 根据 `StorageAccountId` 读取账号。
- 列出账号。
- 新增账号。
- 更新账号。
- 删除账号。

它不能负责：

- 保存 secret material。
- 创建 provider。
- 测试远程连接。
- 判断 UI 是否允许点击某个按钮。

`IApplicationSettingsRepository` 负责应用设置快照的读写。

它不能负责：

- 弹确认框。
- 自动决定何时保存设置。
- 读取 Avalonia 控件状态。
- 保存 provider SDK 原始配置对象。

设置保存流程必须由 Application 用例显式触发。

## 5. 凭据端口

凭据端口表达凭据引用和 secret material 之间的安全边界。

第一版端口：

```text
ICredentialStore
```

`ICredentialStore` 负责保存、读取、轮换和清理凭据材料，但 Core 只定义端口，不定义具体加密、平台密钥链、文件格式或数据库结构。

凭据端口规则：

- Core 可见的是 `CredentialRef`。
- secret material 不进入 Core 模型。
- secret material 不进入日志、ViewModel、TransferTask 或普通配置文件。
- 读取 secret 的结果只允许进入 provider factory / provider 操作链路。
- 凭据轮换必须支持 `CredentialLease` 或等价占用声明，避免运行中任务仍使用旧凭据时被物理删除。

`ICredentialStore` 的实现属于 Infrastructure。

## 6. 传输端口

传输端口表达传输任务调度、状态保存和状态发布能力。

第一版端口：

```text
ITransferTaskScheduler
ITransferTaskStore
ITransferStateStore
```

`ITransferTaskScheduler` 表达传输任务调度入口。

它可以提供：

- 提交任务。
- 取消任务。
- 重试任务。
- 唤醒调度器处理待执行任务。

它不能把 `TransferQueue`、`TransferManager`、worker 或任何可变运行时对象暴露给 Application 或 Presentation。

`ITransferTaskStore` 负责传输任务持久化。

它可以保存和读取 `TransferTask`，但不能执行传输，也不能创建 provider。

`ITransferStateStore` 负责传输状态快照查询。

Application 查询传输队列和历史时，只能通过该类端口获得不可变快照或可被 Application 组织成不可变结果的数据，不能拿到 Transfer Runtime 内部状态。

第一版不把 `ITransferStatePublisher` 作为 Core 公共端口暴露。

如果 Transfer 内部需要事件发布机制，只能作为 Transfer 模块内部实现细节，或在后续版本经过重新设计后再进入 Core。第一版传输状态进入 UI 的唯一正式路径是：Transfer Runtime 更新状态存储，Application 通过 Core 查询端口组织不可变快照，Presentation 消费 Application 结果。

## 7. 结果与错误规则

端口返回失败时必须使用 Core 统一错误模型。

第一版规则：

- 跨模块 fallible 操作返回 `OperationResult` 或 `OperationResult<T>`。
- 错误信息使用 `StorageError` 或后续统一错误对象。
- Provider SDK/API/协议异常必须在 Providers 内转换为 Core 错误模型。
- Application 可以组织错误结果，但不能判断具体 SDK 异常类型。
- Core 不捕获、不转换具体 SDK/API/协议异常。

不要为每个模块发明自己的 `ApplicationResult<T>`、`DesktopResult<T>`、`ProviderResult<T>`。这会把错误语义撕碎。

## 8. 生命周期规则

端口定义不等于端口实现生命周期。

- Core 只定义端口，不决定 DI 注册。
- Registry、Factory、Repository、Store、Scheduler 等实现通常可以是应用级服务。
- provider 实例是短生命周期运行对象。
- Transfer worker 是任务执行期对象。
- 快照对象和值对象不由 DI 管理。

具体生命周期必须遵守 `Docs/architecture/lifecycle.md`。

## 9. 禁止事项

Core 端口禁止包含：

- `IServiceProvider`。
- Avalonia / AtomUI 类型。
- View / ViewModel / Command。
- 具体 SDK client 或 SDK DTO。
- `DbContext`、SQLite connection、文件流等具体持久化实现。
- Serilog、NLog 或其他日志框架类型。
- secret material。
- provider 实例缓存语义。
- Transfer Runtime 内部队列或 worker。
- 面向 UI 直接订阅 Transfer Runtime 的状态发布端口。

任何接口只要需要这些东西，说明它不是 Core 端口，或者它被设计坏了。
