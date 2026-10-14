# AtomBox.Transfer × AtomUI.City.State 迁移

> Matrix ID：`AB-TRANSFER × CITY-STATE`
>
> 状态：Automated / Manual UI Pending
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

队列成员判定与现有 `ITransferStateStore.ListQueueAsync` 保持一致：`Pending` / `Running` / `Paused` / `Interrupted` 在队列中，`Succeeded` / `Failed` / `Canceled` 移出队列。

发布顺序必须是：

```text
业务变更
  -> 持久化写入成功
  -> Transfer State 原子更新
  -> Desktop Observe
```

持久化失败时不得发布一个并未落盘的新事实。并发 worker 之间的投影合并使用 City.State Writer 的原子 `Update`，不采用“先 Get 再 Set”。

## 4. 启动与重同步

`TransferRuntimeInitializer` 首先沿用现有逻辑，将上次运行留下的 `Running` 任务持久化为 `Interrupted`；然后从 `ITransferStateStore.ListQueueAsync` 读取一次完整队列并发布 `Ready`。这是应用启动时的明确重同步路径。

任务提交、取消、重试、worker 状态变化和进度持久化成功后，都必须同步更新投影。

## 5. Desktop 消费

- `TransferQueueViewModel` 观察 State，删除一秒轮询。首次 `IsInitial` 通知只渲染，不生成“任务完成”提示。
- 后续变化通过 old/new 快照识别从队列消失的任务，再使用现有历史查询确认是否成功，保留现有成功通知语义。
- `StatusBarViewModel` 观察同一 State 计算活动数、运行数和上下行速度，删除定时器和手工刷新通道。
- `HomeViewModel` 使用 State 计算队列摘要；最近历史仍在激活时查询。
- `TransferHistoryViewModel` 保留分页查询，不把无界历史集合投影到 City.State。

## 6. 本轮明确不处理

以下是审计已识别的 Transfer 自有工程问题，不是 City.State 引入所造成，且按本轮决策延后：

- Runtime 还没有完整的停止接收、有限等待和关停 flush 实现。
- `WakeAsync` 和 worker 任务所有权、取消与重试竞态需要单独治理。
- 进度持久化存在同步阻塞与读-改-写并发等技术债。

本轮不用 State 掩盖这些问题，也不将它们与状态订阅迁移捆绑重构。

## 7. 验收证据

- Transfer State 初始化、原子并发合并、队列成员转换和持久化失败不发布的单元测试。
- Desktop 测试验证 ViewModel 消费 State，不再依赖周期轮询。
- `dotnet test` 覆盖 Transfer、Application 和 Desktop 相关项目。
- `dotnet build AtomBox.slnx -c Release`。
- 真实 UI 验收由人工完成，在此之前状态不记为 Done。

2026-10-13 自动化结果：

- `AtomBox.Transfer.Tests`：106/106 通过，包含本次新增的初始投影、原子并发合并、重同步不覆盖并发提交、进度保留、终态移出、持久化失败不发布和启动修复测试。
- `AtomBox.Application.Tests`：163/163 通过。
- `AtomBox.Desktop.Tests`：32/32 通过，验证队列页与状态栏能观察已提交状态，并将原“每次激活拥有一个周期 worker”门禁更新为“观察 State 且不拥有周期 worker”。
- 整体回归：693/693 通过。
- `dotnet build AtomBox.slnx -c Release --no-restore -v:minimal`：0 warning / 0 error。
