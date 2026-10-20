# AtomBox.Transfer Runtime 设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-15
>
> 冻结范围：当前文档定义的 Transfer 第一版运行时对象、调度边界、worker 生命周期、provider 创建边界和 Phase 9 收口规则
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的 Transfer Runtime 边界，必须先说明原因，再同步更新相关 Transfer、Core、Application、Presentation 文档。

本文档定义 `AtomBox.Transfer` 第一版运行时设计。Transfer 是调度器，不是 provider，不是 Application，也不是 UI。

## 1. 运行时定位

Transfer Runtime 负责执行已经创建好的 `TransferTask`。

它负责：

- 接收待执行任务。
- 以持久化 Store 为队列事实源，并用容量 1 的内部信号唤醒扫描。
- 通过 Revision、预期状态和 CAS 原子领取任务。
- 创建和释放单任务 `TransferOperationExecutor`。
- 管理并发。
- 处理取消、重试和失败状态。
- 聚合进度和速度。
- 更新任务状态存储。

它不负责：

- 创建账号。
- 测试连接。
- 保存凭据。
- 读取配置文件。
- 弹窗或更新 UI。
- 调用具体 provider 实现类。
- 暴露内部队列给 Application 或 Presentation。

## 2. 主要运行时对象

当前生产运行时对象：

| 对象 | 生命周期 | 职责 |
| --- | --- | --- |
| `TransferRuntimeController` | 应用级运行时服务 | 管理启停、准入、Store 扫描、CAS 领取、并发、取消、恢复、健康和有限关闭。 |
| `AtomBoxTransferModule` | City Module 生命周期 | 注册 Transfer，并把 Runtime 接到 ApplicationScope 和 Host shutdown。 |
| `TransferOperationExecutor` | 单任务执行期 | 创建临时 provider，执行一次上传或下载并释放资源。 |
| `ActiveExecution` | Runtime 内部执行租约 | 持有执行体、取消来源、进度快照、序号和迟到提交围栏。 |
| `TransferStateProjector` | 应用级投影器 | 只在事实成功持久化后发布 City.State 队列投影。 |
| `TransferSpeedMeter` | 执行期对象 | 使用单调时钟、最小采样窗口和 EWMA 平滑计算速度快照，隔离 Provider 高频突发回调。 |

这些对象都不能被 ViewModel 直接持有，也不能进入 Application 结果对象。Desktop 只观察 `IAtomBoxTransferState` 的只读快照。

## 3. 调度入口

Core 端口 `ITransferTaskScheduler` 是 Transfer 对外暴露的调度入口。

公开语义：

- 提交任务。
- 取消任务。
- 重试任务。

调度唤醒是 Runtime 内部实现细节。`SubmitAsync` 和 `RetryAsync` 在持久化成功后自行发送合并信号，公开 `WakeAsync` 已删除。`ITransferTaskScheduler` 不能返回 Controller、执行租约或其他可变运行时对象。

Application 可以调用 `ITransferTaskScheduler`，但只能表达用户用例，例如创建任务后提交、取消、重试。Application 不能指挥 worker 如何执行，也不能决定 provider 生命周期。

## 4. 执行单元

当前版本一个 `TransferTask` 对应一个 `TransferOperationExecutor` 和一个 provider 生命周期。Application 的一次批量提交会创建多个独立任务；Runtime 可以并发执行，但不把它们合并成共享 provider 的隐式批次。

执行开始时，Transfer 通过 Core 端口读取账号快照并创建 provider；执行结束时释放 provider。未来如果要跨任务复用，必须先设计明确的 `ProviderSessionPool` 或 `ProviderClientPool`，不能暗中延长 SDK client 生命周期。

## 5. Provider 创建边界

Transfer 执行任务时使用：

```text
IStorageAccountRepository
IStorageProviderFactory
IStorageProvider
```

正确流程：

```text
TransferOperationExecutor
  -> IStorageAccountRepository.Get(StorageAccountId)
  -> IStorageProviderFactory.Create(StorageAccount)
  -> IStorageProvider 执行上传或下载
```

禁止流程：

```text
TransferOperationExecutor
  -> new AliyunOssProvider(...)
```

或者：

```text
Application
  -> 提前创建 provider
  -> 塞进 TransferTask
```

这两种都直接破坏架构边界。

## 6. 状态更新

不把 `ITransferStatePublisher` 作为 Core 公共端口暴露。

Transfer 使用 City.State 作为已持久化队列事实的应用级运行时投影。写入权限属于 `AtomBox.Transfer`，外部只能获得 `IReadOnlyState<TransferQueueState>`；不向 Core 暴露 City 类型。

正式状态路径：

```text
Transfer Runtime
  -> 更新 ITransferTaskStore / ITransferStateStore 对应实现
  -> 持久化成功后原子更新 City.State 队列投影
  -> Desktop 只读 Observe
```

历史分页不进入 State，仍按以下路径查询：

```text
Application 查询用例
  -> ITransferStateStore.ListHistoryAsync
  -> TransferHistoryPage
  -> Desktop 展示
```

State 不取代 Store，也不改变 Application 的命令入口。

Provider 的进度回调只能更新线程安全的最新内存快照并立即返回，禁止在 `IProgress.Report` 中同步等待异步 Store。Runtime 通过统一异步循环合并、节流和批量持久化进度，成功落盘后再更新 City.State；执行结束时先强制 flush 最终进度，再通过 Revision/预期状态条件提交终态。

速度不能按相邻两次 Provider 回调的瞬时时间差直接计算。Provider SDK 可能在极短时间内突发报告多个累计字节快照，这类回调反映缓冲区推进，不代表稳定网络吞吐量。`TransferSpeedMeter` 必须使用单调时钟，在至少一秒的有效采样窗口上按累计字节增量计算，并在窗口之间进行平滑；窗口内回调沿用最近一次可信速度。字节倒退的迟到或乱序回调必须在进入测速器前拒绝，不能污染后续采样基线。

## 7. 启动与关闭

Transfer 通过 `AtomBoxTransferModule` 接入 City Host。Module 在 `ConfigureServices` 注册 Transfer 服务，在 `OnPostApplicationInitializationAsync` 启动应用级 Runtime，在 `OnApplicationShutdownAsync` 执行有限关闭；Desktop 只显式加载并依赖该 Module，不再手工注册或调用 Transfer 初始化器。

启动时：

- 初始化 Transfer Runtime。
- 读取未完成任务并重建运行时队列与 City.State 投影。
- 自动调度已经可靠受理、但尚未被领取的 `Pending`。
- 将上次运行遗留的 `Running` 保守地转换为 `Interrupted`；已记录用户取消意图的 `Canceling` 在重启确认本地执行体消失后转换为 `Canceled`，从活动队列移入历史；`Paused` / `Interrupted` 不自动执行，只能由用户显式重试并从任务起点重新开始。

关闭时：

- 停止接受新任务。
- 请求 worker 停止。
- 有限超时等待 worker。
- 未完成任务保存为 `Paused` 或 `Interrupted`。
- flush 关键传输状态。

Transfer 停止不能无限等待。无法及时停止的 worker，应把任务标记为 `Interrupted`，然后允许应用继续退出。

这里的有限等待只约束 `TransferRuntime.StopAsync`，不承诺强制终止任意第三方 SDK。超过宽限期的执行体必须通过执行围栏停止发布进度和提交终态，Runtime 将任务保守地保存为 `Interrupted` 后继续关闭 Host；仍不退出的同步 SDK 最终随 AtomBox 进程退出由操作系统回收。除非 Provider 明确提供并保证安全的 abort/disconnect 合同，否则 Runtime 不得跨线程强制释放其正在使用的 Provider 或 Stream。

## 8. Runtime 健康与故障边界

单个传输任务失败不等于 Runtime 失败。Provider、网络、文件或单任务业务错误只把对应任务保存为 `Failed`，Runtime 继续执行其他任务。只有调度循环退出、持久化重试耗尽、执行所有权冲突或内部不变量被破坏等系统性问题，才允许把 Runtime 转入 `Faulted`。

Runtime 除生命周期状态外还公开 `Healthy / Degraded / Faulted` 健康状态。可恢复的 Store、进度或队列读取故障进入 `Degraded`，暂时关闭新任务准入并退避重试；成功恢复后重新进入 `Healthy`。不可恢复故障进入 `Faulted`，关闭本 Host 的准入、停止领取新任务并有限收尾活跃 Worker。

Runtime 创建的所有后台任务都必须被 Controller 持有和观察。启动异常保留原始异常链向 City Module 抛出；运行期后台异常由顶层边界转换为健康状态并写入开发者诊断；关闭异常在完成其他尽力清理后聚合抛出。完整异常不得进入任务 JSON 或公开 State，UI 只消费脱敏错误摘要；City 诊断只承担进程内可观测性，不负责磁盘日志。

队列投影与健康投影必须分离。队列读取在已经成功过至少一次后发生故障时，继续保留最后可信任务列表并标记为过期，不能清空列表；Runtime 健康投影独立提供生命周期、健康等级、准入状态、活跃/等待数量、最后脱敏故障和更新时间。

## 9. Phase 9 收口规则

Phase 9 不改变 Transfer 的职责边界，但要求传输结果具备可解释性。

状态规则：

- 失败任务必须保存失败原因摘要和错误类别。
- 用户取消任务必须与网络失败、权限失败、provider 错误区分。
- 应用关闭或 worker 超时导致的未确认结果必须保存为 `Interrupted` 或等价状态。
- 运行中用户取消先进入 `Canceling`，只有执行体确认退出后才能保存为 `Canceled`；重复取消必须幂等。
- `Canceling` 是已持久化的用户意图：执行体退出后它必须收敛到 `Canceled`；Provider 的迟到 Success、Failure 或异常均不得覆盖该意图。
- Provider 返回取消时必须结合用户、Host 和 Runtime 实际取消来源判定终态，不能仅凭异常类型把任务记为用户取消。
- `Interrupted` 不能自动恢复执行；后续只能由用户通过 Application 重试用例显式恢复。

重试规则：

- Transfer 只接收重试调度请求，不由 UI 直接操作内部队列。
- 可重试条件由任务状态和错误类别共同决定，再通过 Application 快照暴露给 UI。
- 第一版不提供断点续传；`Failed`、`Interrupted` 或用户手动重试一律从任务起点重新执行。持久化进度只用于展示和诊断，不保存或恢复厂商分片会话、下载偏移及协议私有恢复令牌。

展示规则：

- Transfer 不生成 UI 文案。
- Transfer 可以保存结构化错误类别、脱敏技术码、最后更新时间和可重试标志所需状态。
- Presentation 通过 Application 查询结果展示失败详情、重试按钮和中断原因。

## 10. 禁止事项

- 不引用 Application。
- 不引用 Desktop、Avalonia、AtomUI、ViewModel。
- 不引用具体 provider 实现。
- 不引用具体云 SDK、FTP/SFTP client、网盘 API client。
- 不读取配置文件。
- 不访问数据库具体实现。
- 不保存 secret material。
- 不暴露 `TransferRuntimeController`、活跃执行租约或内部调度信号给 Application 或 Presentation。
- 不把内部事件发布机制做成 UI 可直接订阅的公共端口。

## 11. 实现验收

Transfer Runtime 的自动化与人工验收以 `docs/implementation/testing/phase-12-transfer-runtime-testing.md` 为权威基线。旧的同步式 `WakeAsync`、7 状态模型和独立 `TransferRuntimeInitializer` 测试合同不再适用。

自动化通过只表示实现具备人工验收条件；必须在真实编译或发布产物上完成 UI 烟测，才能宣告 Runtime 迁移正式完成。UI 烟测不能替代并发、CAS、故障注入和关闭竞态自动化测试，自动化测试也不能替代真实窗口、导航、托盘和退出行为验证。
