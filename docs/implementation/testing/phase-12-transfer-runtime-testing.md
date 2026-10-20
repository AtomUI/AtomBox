# Phase 12 Transfer Runtime 测试与验收基线

> 文档状态：默认自动化门禁已通过；真实 Provider 与真实 UI 人工验收待执行
>
> 批准时间：2026-10-16
>
> 对应决策：`docs/iterations/features/with-city-transfer-runtime.md`
>
> 适用范围：AtomBox.Transfer 后台 Runtime、相关 Core 合同、Infrastructure 持久化、City Module 接入及 Desktop 人工烟测

## 1. 验收原则

- 测试必须证明 Runtime 的并发、恢复、故障和关闭语义，不以“代码能够编译”代替行为验收。
- 自动化测试与真实 UI 烟测缺一不可，但两者承担不同责任：自动化证明竞态与合同，人工烟测证明真实窗口、导航、托盘和退出体验。
- 默认自动化测试不得依赖公网、真实账号或人工交互；真实 Provider 使用明确的 opt-in 环境配置。
- 并发测试必须使用可控同步点和可控时间，不使用长时间休眠碰运气。
- 每个失败用例必须同时断言持久化事实、Runtime 健康状态、准入状态和公开 State，不能只断言一个返回值。

## 2. 默认自动化门禁

### 2.1 Core 合同

- `TransferStatus` 旧值 `Pending=0` 至 `Canceled=6` 保持稳定，`Canceling=7` 只追加不重排。
- `TransferTask.Revision` 持久化且单调递增；时间戳不参与并发胜负。
- 条件更新同时校验任务 ID、预期 Revision 与预期状态。
- 错误 Revision、错误状态或重复终态提交必须失败，且不得改变已保存事实。
- Provider 能力合同能够表达异步/阻塞 I/O、协作取消、主动中止和进度能力，不包含具体厂商类型。

### 2.2 Infrastructure 持久化合同

- 两个并发领取者对同一个 `Pending` 执行 CAS 时只能有一个成功。
- 批量进度更新不能因并发 Worker 的完整 JSON 读改写而互相覆盖。
- 临时文件写入、flush 或原子替换失败时，旧文件仍然可读且内容完整。
- 最终进度与任务终态按批准顺序提交；终态失败不能留下虚假的成功事实。
- 进程重启后 Revision、任务状态、最后持久化进度和错误字段保持一致。
- 单实例约束生效；若无法取得实例所有权，Host 不得启动第二个共享同一任务库的 Runtime。

### 2.3 Runtime 生命周期与调度

- 并发 `StartAsync` 共享一次启动，且只创建一个调度循环。
- `StartAsync`、`StopAsync` 幂等；`Stopped` 后同一 Host 不可再次启动。
- Runtime 只在 `Running` 且 `Healthy` 时开放准入，进入 `Stopping` 时原子关闭准入。
- 容量 1 信号可以合并；已经持久化的 `Pending` 即使没有对应内存信号，也能在启动或重试扫描时被发现。
- 并发槽位严格受限；领取失败立即归还槽位；Worker 结束一定释放槽位并再次请求调度。
- 每个活跃 Worker 都存在于受管活跃表中，执行结束后无论成功、失败、取消或异常都完成统一清理。

### 2.4 状态、取消与执行围栏

- `Pending -> Running` 只能通过 CAS 领取。
- 用户取消 Pending 直接进入 `Canceled`；取消 Running 先进入 `Canceling`，执行体退出后才进入 `Canceled`。
- `Canceled` 持久化成功后必须从活动队列移除并出现在传输历史；不能只更新 UI，也不能让协议调用在后台继续传输。
- 一旦持久化状态进入 `Canceling`，用户取消意图优先于 Provider 的迟到结果；即使不合作 Provider 最后返回 Success，终态仍必须是 `Canceled`，不得回写为 `Succeeded`。
- Host 关闭保存为 `Interrupted`；无 AtomBox 取消来源的 Provider 取消映射为 `Failed`。
- Provider 已明确超时失败映射为 `Failed`；Runtime 有限等待超时且结果未知映射为 `Interrupted`。
- Abandoned 执行的迟到进度、迟到成功、迟到失败和迟到异常都不能修改持久化终态或公开 State。
- 一个任务失败不影响其他任务，Runtime 保持 `Healthy` 并继续调度。

### 2.5 进度汇聚

- `IProgress.Report` 只执行线程安全的 O(1) 内存更新，不等待 Store。
- 高频和并发回调按任务汇聚，旧中间值可覆盖，最新值不得丢失；同一执行的字节数保持单调。
- 多任务脏进度可以批量 flush，且只在持久化成功后发布 City.State。
- 正常结束先强制 flush 最终进度，再 CAS 提交终态。
- 中间进度写入失败保留最新内存值并退避重试；迟到 ExecutionLease 不再接受进度。
- Desktop 收到同一 `TransferTaskId` 的连续进度快照时必须保持行 ViewModel 与行控件身份稳定，只更新可观察属性；自动化测试应拒绝集合 Reset/Replace，人工烟测应确认悬停不闪烁且一次点击即可触发取消。

### 2.6 Runtime 健康与异常

- 单任务 Provider/网络/文件错误只产生任务 `Failed`，不产生 Runtime `Faulted`。
- 可恢复 Store 或投影错误进入 `Degraded`、关闭新任务准入并保留最后可信队列；恢复成功后回到 `Healthy`。
- 重试耗尽、调度循环崩溃或内部不变量破坏进入 `Faulted`，并停止领取新任务。
- 首次队列初始化失败表示无可用快照；已有成功快照后的刷新失败保留任务并标记 `IsStale`。
- 启动异常保留原始 `InnerException` 与结构化错误；后台异常被观察并进入诊断；关闭异常在其他清理完成后聚合。

### 2.7 启动恢复与关闭

- 启动自动调度持久化 `Pending`。
- 遗留 `Running` 通过 CAS 转换为 `Interrupted`；遗留 `Canceling` 通过 CAS 转换为 `Canceled` 并进入历史；`Paused`、`Interrupted` 和终态不自动执行。
- 关闭先停止准入，再等待已进入受理阶段的提交，随后取消 Worker、有限等待并执行最终 flush。
- 协作 Provider 在宽限期内退出；不合作 Provider 超时后不阻止 Host 继续关闭。
- 不合作 Worker 被标记 Abandoned 后即使测试主动放行返回，也不能覆盖 `Interrupted`。
- Runtime 的关闭耗时不得超过配置预算与小幅调度余量，测试不能无限等待。

### 2.8 City Module 与 Host 集成

- 组合根显式注册 `AtomBoxTransferModule`，`AtomBoxDesktopModule` 显式依赖它。
- Transfer 服务只由 Transfer Module 注册一次，Desktop 不再重复调用 `AddAtomBoxTransfer`。
- Runtime 在 `OnPostApplicationInitializationAsync` 启动，并使用 ApplicationScope token 作为运行期生命令牌。
- 启动完成时 Runtime 为 `Running/Healthy`；启动失败导致 Host 回滚且不遗留调度循环。
- Host 关闭先广播 ApplicationScope 取消，再以 shutdown token 调用有限收尾。
- 关闭失败由 City 聚合；`TransferRuntimeInitializer` 不再注册或由 Desktop 调用。

## 3. 无 UI 端到端门禁

使用临时目录中的真实 JSON Store、正式 DI 图和可控 Fake Provider，验证以下全链路：

```text
Application 提交
  -> Pending 持久化
  -> Runtime 自动发现并 CAS 领取
  -> Provider 执行
  -> 进度汇聚与批量保存
  -> 终态提交
  -> City.State 只读投影
```

必须包含成功上传/下载、认证失败、网络失败、用户取消、Host 关闭、重启恢复、投影暂时失败与不合作 Provider。测试结束后检查 Runtime 活跃表、可释放资源和临时目录，不允许隐藏泄漏。

## 4. 真实 Provider opt-in 验收

- 日常 CI 不要求任何真实凭据，也不能因未配置真实 Provider 而伪装成执行成功；测试应明确显示 skipped/未启用。
- 发布或重大迁移验收至少选取一个真实 Provider，验证上传、下载、运行中取消和“单任务失败不拖垮 Runtime”。
- 每种 Provider 的取消与中止能力按其真实能力断言；不支持取消的 SDK 应验证有限等待和 `Interrupted`，不能强行要求 `Canceled`。
- 真实测试产生的远端对象使用独立前缀并记录清理结果；不得操作用户已有对象。

## 5. 真实 AtomBox UI 人工烟测

人工测试必须运行本次提交对应的真实编译或发布产物：

1. 启动 AtomBox，确认 Host 与 Runtime 正常进入就绪状态。
2. 提交一个足够观察进度的真实任务，确认队列和状态栏持续刷新。
3. 切换到其他页面，确认 ViewModel 停用不会取消应用级传输。
4. 最小化到托盘，确认 Runtime 继续执行。
5. 鼠标持续悬停在正在更新进度的任务行，确认行和按钮不闪烁；单击一次取消，确认立即进入取消中，再显示已取消或能力受限时的中断结果。
6. 制造一个任务失败，再提交可成功任务，确认 Runtime 没有整体停摆。
7. 传输中真正退出应用并重新启动，确认旧任务显示 `Interrupted` 且不会自动重传。
8. 手动重试该任务，确认从起点重新执行，不宣称断点续传。
9. 正常退出，确认没有无限等待、残留窗口或明显未释放资源。

烟测报告至少记录日期、Git 提交号、构建配置/产物、操作系统、Provider、各步骤结果、发现的问题和必要截图。

## 6. 完成定义

- Core、Infrastructure、Transfer、Application/Desktop 集成测试全部通过，才能声明“代码重构完成，具备人工验收条件”。
- 至少一次批准范围内的真实 Provider 验证和真实 UI 烟测通过并留下报告，才能声明“Transfer Runtime 迁移正式验收完成”。
- 任一自动化用例偶发超时或需要重复执行才能通过，都视为未通过；必须修复测试同步或生产竞态，不能以重跑掩盖。

## 7. 2026-10-16 自动化执行记录

执行命令：

```powershell
dotnet test AtomBox.slnx --no-restore -v:minimal
```

结果：全部通过，共 664 项。

同一工作树执行 `dotnet build AtomBox.slnx -c Release --no-restore -v:minimal` 成功，结果为 0 warning / 0 error。

| 测试项目 | 通过 | 失败 |
| --- | ---: | ---: |
| AtomBox.Core.Tests | 61 | 0 |
| AtomBox.Infrastructure.Tests | 164 | 0 |
| AtomBox.Providers.Tests | 171 | 0 |
| AtomBox.Application.Tests | 161 | 0 |
| AtomBox.Transfer.Tests | 71 | 0 |
| AtomBox.EndToEnd.Tests | 4 | 0 |
| AtomBox.Desktop.Tests | 34 | 0 |

本轮新增的无 UI 端到端门禁使用临时真实 JSON Store、正式 Transfer DI 图和可控 Fake Provider，已走通 Application 受理、Pending 持久化、Runtime CAS 领取、Provider 执行、进度保存、终态提交与 City.State 投影。自动化通过仅表示代码重构具备人工验收条件；真实 Provider 和真实 UI 烟测仍不得标记为已完成。
