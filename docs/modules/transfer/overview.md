# AtomBox.Transfer 模块设计

> 文档状态：第一版发布基线冻结
>
> 冻结时间：2026-06-22
>
> 冻结范围：当前文档定义的架构、模块边界、依赖关系、目录规划或实现约束
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的设计边界，必须先说明原因，再同步更新相关设计文档。

## 1. 模块定位

`AtomBox.Transfer` 是 AtomBox 的传输任务调度层。

它负责管理上传、下载等传输任务的生命周期，包括任务队列、并发调度、运行中取消、重试、进度聚合、速度统计和任务状态更新。

Transfer 不是具体协议实现层。它不关心阿里云 OSS 怎么上传对象，不关心 SFTP 怎么写文件，也不关心网盘 API 怎么提交分片。把具体 provider SDK 或协议细节塞进 Transfer，是严重的边界错误。

## 2. 边界规则

Transfer 允许包含：

- 传输任务队列、worker、调度策略。
- 并发控制、取消、失败重试。
- 进度聚合、速度统计、任务状态更新。
- 传输策略，例如重试策略、限速策略、优先级策略。
- Core 传输端口的运行时实现，例如任务调度端口、任务存储协作和状态快照查询协作。
- 执行传输任务时，通过 Core 中的账号仓储和 provider factory 解析运行时所需的 `IStorageProvider`。

Transfer 禁止包含：

- Avalonia、AtomUI、View、ViewModel、Command、Binding 等 UI 类型。
- 阿里云 OSS、腾讯 COS、七牛、FTP/SFTP、网盘 API 等具体 SDK 类型。
- 具体 provider 实现引用。
- Application 引用或用户用例编排逻辑。
- 配置文件读写、SQLite 访问、凭据加密、日志落盘等 Infrastructure 实现。

Transfer 可以调用 Core 中定义的 provider 抽象，但不能知道具体 provider 是谁。Provider 是驱动器，Transfer 是调度器，别把这两个角色混在一起。

Transfer 接收的是任务描述，不是 provider 实例。任务描述只保存账号 ID、本地路径、远程路径、方向和策略选项；Transfer 执行任务时再通过 Core 端口获取账号并调用 provider factory 创建 provider。凭据读取、`CredentialLease` 和 secret material 处理属于 provider factory / provider 操作链路内部细节，Transfer 不理解凭据载荷结构。

## 3. 推荐目录

```text
src/AtomBox.Transfer/
  AtomBox.Transfer.csproj
  Queue/
  Workers/
  Scheduling/
  Progress/
  Policies/
  Persistence/
```

## 4. 目录职责

| 目录 | 职责 |
| --- | --- |
| `Queue/` | 定义传输任务队列、任务入队、出队、状态跟踪。 |
| `Workers/` | 定义传输 worker 和任务执行循环。 |
| `Scheduling/` | 定义并发调度、优先级、任务选择策略。 |
| `Progress/` | 定义进度聚合、速度统计、进度事件转换。 |
| `Policies/` | 定义重试、限速、并发等策略；暂停/恢复策略仅作为后续版本候选，不作为第一版对外调度能力。 |
| `Persistence/` | 协同 Core 中定义的传输持久化端口，不定义新的跨模块端口，不实现具体存储。 |

## 5. 首批核心对象范围

第一阶段 Transfer 只定义支撑基础上传下载队列的运行时对象，不提前实现复杂断点续传和跨 provider 同步。

队列与调度：

- `TransferManager`
- `TransferQueue`
- `TransferWorker`
- `TransferScheduler`

任务控制：

- `TransferTaskHandle`
- `TransferCancellation`

进度与状态：

- `TransferProgressReporter`
- `TransferSpeedMeter`
- `TransferStateSnapshot`

策略：

- `TransferRetryPolicy`
- `TransferConcurrencyOptions`
- `TransferThrottleOptions`

Core 端口实现：

- `TransferTaskScheduler`
- `TransferStateStoreCoordinator`

这些对象属于任务调度语言，不是具体 provider 实现，也不是 UI 展示对象。

## 6. 设计约束

- Transfer 只能依赖 Core；不能依赖 Application、Desktop、具体 Provider 实现或 Infrastructure 实现。
- Transfer 调用 provider 抽象执行实际读写，但不直接引用 provider 实现类。
- Transfer 使用 Core 中定义的 `ITransferTaskStore`、`ITransferStateStore`、`IStorageAccountRepository`、`IStorageProviderFactory` 等端口，但不定义这些跨模块端口。
- Transfer 不保存敏感凭据，不读取配置文件，不访问数据库。
- Transfer 不读取、不解析、不缓存 secret material；凭据解析和 secret material 使用必须封装在 provider factory / provider 操作链路内部。
- Transfer 不处理 UI 状态。第一版不暴露传输状态发布端口，传输状态进入 UI 的正式路径是：Transfer Runtime 更新状态存储，Application 通过 Core 查询端口组织不可变快照，Presentation 消费 Application 结果。
- Transfer 不接收、保存或传播长期 provider 实例，provider 生命周期由 Transfer 执行批次临时创建和释放。
- Provider 可以暴露可选能力；Transfer 只能基于 Core 能力模型决定任务级调度、重试和并发策略，具体协议分片、SDK 调用细节仍由 Provider 内部负责。
- Transfer 不负责账号管理和连接测试，这些是 Application 用例。
- 传输任务状态必须基于 Core 的统一模型，不能在 Transfer 内部发明另一套状态语言。

## 7. 生命周期约束

Transfer Runtime 是应用级运行时服务，拥有任务队列、并发调度、取消、重试、进度聚合和任务状态更新。当前第一版通过设置项中的最大并发数控制同时上传/下载任务数量。`Paused` / `Interrupted` 状态用于关闭、中断和后续恢复设计，第一版不承诺对外暂停/恢复调度能力。

- `TransferManager` / `ITransferTaskScheduler` 可以作为应用级长期服务存在。
- `TransferQueue` 是 Transfer 内部状态，不直接暴露给 UI 或 Application。
- `TransferWorker` 是单个执行批次生命周期对象，由 Transfer Runtime 创建和释放。
- `TransferProgressReporter`、`TransferSpeedMeter` 属于任务执行期对象，输出进度快照，不作为应用级单例。
- 传输执行开始时，Transfer 通过 Core 端口读取账号，并形成本次执行的内存快照。
- `TransferWorker` 的执行批次是传输 provider 的主要生命周期边界；同账号、同方向、同目标、同策略的一组可合并任务可以在批次内复用 provider 或 provider session。
- Transfer 不能按单个分片、单个 HTTP 请求、单个 SDK 方法调用创建 provider；这会把 provider 生命周期切得过碎，是错误设计。
- 不同用户提交批次第一版不跨批次共享 provider 实例。未来如果需要跨批次复用，必须先设计 `ProviderSessionPool` 或 `ProviderClientPool`。
- 任务重启或恢复时，Transfer 通过 `StorageAccountId` 重新读取当前账号和当前 `CredentialRef`。
- TransferWorker 执行期间如占用某个 `CredentialRef`，必须通过 `CredentialLease` 或等价机制声明占用；TransferTask 持久化数据仍然不能保存 secret material。
- Transfer 启动时可以读取未完成任务或队列状态，供 Application 后续查询并组织不可变快照；第一版不自动恢复执行历史未完成任务。
- 应用关闭时，Transfer 必须停止接收新任务，请求 worker 停止，把运行中任务保存为 Interrupted 或等价可解释状态，并 flush 关键传输状态。

Transfer 停止必须有有限超时。无法及时停止的 worker，应把任务标记为 Interrupted，然后允许应用继续退出。
