# AtomBox.Transfer × AtomUI.City.State 迁移

> Matrix ID：`AB-TRANSFER × CITY-STATE`
>
> 状态：State 阶段已完成；Runtime 后续设计已由 `with-city-transfer-runtime.md` 接续并完成默认自动化门禁；Manual UI Pending
>
> 实施分支：`feature/with-city`
>
> 决策时间：2026-10-13

## 1. 决策摘要

`AtomBox.Transfer` 直接采用 City.State，拥有传输队列的应用级运行时投影。`ITransferTaskStore` / `ITransferStateStore` 仍然是持久化事实源；City.State 只保存已就绪队列及进度的瞬时、不可变快照，不取代存储，不承担恢复。

Desktop 不再为队列和状态栏每秒读取持久化文件，而是通过 City.State 的原子 `Observe`“立即快照 + 后续变化”。历史分页是无界数据，仍由 Application 查询，不整体常驻 State。

## 2. 能力归属与依赖方式

- 业务所有者：`AtomBox.Transfer`。移除 Avalonia 和 ViewModel 后，队列、进度、取消和重试仍然存在，因此不属于 Desktop。
- 实施位置：`AtomBox.Transfer` 新增 State 定义与投影器，直接引用 `AtomUI.City.State`。
- 对外合同：只暴露 `IReadOnlyState<TransferQueueState>`，写入器保持在 Transfer 内部。
- 消费位置：Desktop 只订阅并转换成 UI 行模型，不维护第二份业务事实。
- 不采用 EventBus：队列 UI 需要的是“当前事实 + 后续变化”，不是只发生一次的消息。State 已经覆盖该语义，不建立双通道。

## 3. 投影合同

`TransferQueueState` 具有三种显式状态：

- `Uninitialized`：Transfer Runtime 尚未完成启动投影。
- `Ready`：包含当前队列的不可变快照。
- `Faulted`：初始查询失败，仅暴露结构化、脱敏的存储错误摘要。

队列成员判定与现有 `ITransferStateStore.ListQueueAsync` 保持一致：`Pending` / `Running` / `Paused` / `Interrupted` / `Canceling` 在队列中，`Succeeded` / `Failed` / `Canceled` / `Abandoned` 移出队列。`Interrupted` 等待用户选择重试或放弃，`Abandoned` 才进入历史。读取暂时失败时保留最后可信快照并标记 stale，不得把故障错误投影成空队列。

发布顺序必须是：

```text
业务变更
  -> 持久化写入成功
  -> Transfer State 原子更新
  -> Desktop Observe
```

持久化失败时不得发布一个并未落盘的新事实。并发 worker 之间的投影合并使用 City.State Writer 的原子 `Update`，不采用“先 Get 再 Set”。

## 4. 启动与重同步

当前由 `TransferRuntimeController.StartAsync` 将上次运行留下的 `Running` 任务通过 Revision/预期状态条件更新为 `Interrupted`，随后从持久化 Store 重建队列并发布 `Ready`。旧 `TransferRuntimeInitializer` 已退出生产路径并删除；完整启动、恢复和关闭合同以 [Transfer Runtime 专项](with-city-transfer-runtime.md) 为准。

任务提交、取消、重试、worker 状态变化和进度持久化成功后，都必须同步更新投影。

## 5. Desktop 消费

- `TransferQueueViewModel` 观察 State，删除一秒轮询。首次 `IsInitial` 通知只渲染，不生成“任务完成”提示。
- 后续变化通过 old/new 快照识别从队列消失的任务，再使用现有历史查询确认是否成功，保留现有成功通知语义。
- `StatusBarViewModel` 观察同一 State 计算活动数、运行数和上下行速度，删除定时器和手工刷新通道。
- `HomeViewModel` 使用 State 计算队列摘要；最近历史仍在激活时查询。
- `TransferHistoryViewModel` 保留分页查询，不把无界历史集合投影到 City.State。

## 6. 后续 Runtime 专项的接续结果

State 阶段曾识别并延后的 Transfer 自有工程问题，已在后续 Runtime 专项中完成治理：

- Runtime 已实现停止准入、受管任务、有限等待、最终 flush 与迟到结果围栏。
- 公开同步 `WakeAsync`、旧 Scheduler、内存 Queue 和独立 Initializer 已退出生产路径；调度唤醒由 Runtime 内部管理。
- 进度改为异步汇聚、节流批量持久化，并以 Revision/预期状态 CAS 保护并发状态转换。

City.State 仍只承担已持久化事实的运行时投影，不承担队列执行、并发、恢复或持久化职责。

## 7. 验收证据

- Transfer State 初始化、原子并发合并、队列成员转换和持久化失败不发布的单元测试。
- Desktop 测试验证 ViewModel 消费 State，不再依赖周期轮询。
- `dotnet test` 覆盖 Transfer、Application 和 Desktop 相关项目。
- `dotnet build AtomBox.slnx -c Release`。
- 真实 UI 验收由人工完成，在此之前状态不记为 Done。

以下为 2026-10-13 State 阶段的历史自动化结果：

- `AtomBox.Transfer.Tests`：106/106 通过，包含本次新增的初始投影、原子并发合并、重同步不覆盖并发提交、进度保留、终态移出、持久化失败不发布和启动修复测试。
- `AtomBox.Application.Tests`：163/163 通过。
- `AtomBox.Desktop.Tests`：32/32 通过，验证队列页与状态栏能观察已提交状态，并将原“每次激活拥有一个周期 worker”门禁更新为“观察 State 且不拥有周期 worker”。
- 整体回归：693/693 通过。
- `dotnet build AtomBox.slnx -c Release --no-restore -v:minimal`：0 warning / 0 error。

当前权威证据以 [Phase 12 Transfer Runtime 测试矩阵](../../implementation/testing/phase-12-transfer-runtime-testing.md) 为准：2026-10-16 全解回归 666/666，Release 全解构建 0 warning / 0 error；真实 Provider 和真实 UI 仍待人工验收。
