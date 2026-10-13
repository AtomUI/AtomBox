# AtomBox.Application × AtomUI.City.State 迁移

> 状态：In Progress
>
> Matrix ID：`AB-APPLICATION × CITY-STATE`、`AB-APPLICATION × CITY-EVENTBUS`
>
> 实施分支：`feature/with-city`
>
> 创建时间：2026-10-13

## 结论

`AtomBox.Application` 直接引用 `AtomUI.City.State`，负责把账号目录和应用设置投影为应用级、只读、可观察的运行时状态。Repository 继续是持久化事实源；City.State 不替代持久化，也不保存凭据明文。

`AtomBox.Application` 当前没有独立的业务事件流需求。账号和设置的“当前事实 + 后续变化”统一由 State 表达，不引入 EventBus，不为同一事实建立双通道。

## City 前置能力

当前 City 的 `Value/Version` 读取和 `OnChange` 注册是两个独立动作，调用间存在漏失更新窗口；`ApplicationStateRegistry.CreateWriter(...)` 也没有抽象工厂进入 DI。因此本迁移先在 City 正式仓库完成：

- `AUC-STATE-009` 原子 `Observe(...)`：同一同步边界捕获当前快照并注册订阅，锁外先投递初始值，再按版本顺序投递更新。
- `IApplicationStateWriterFactory`：模块通过 `StateWriteAuthority.Module("AtomBox.Application")` 获取受 OwnerWrite 约束的 writer。

City 完成、测试并同步远端前，本单元状态为 `City Blocked`；AtomBox 更新外部 City 引用后进入 `Implementing`。

## 状态模型与所有权

- `IAtomBoxApplicationState` 对外只暴露 `IReadOnlyState<AccountCatalogState>` 和 `IReadOnlyState<ApplicationSettingsState>`。
- 两个 State 均为 Application lifetime、OwnerWrite、Transient snapshot；Owner 固定为 `AtomBox.Application`。
- 状态显式区分 `Uninitialized`、`Ready`、`Faulted`。读取失败不得发布伪造的空账号或默认设置。
- `AccountCatalogState` 只包含 `StorageAccountSummary` 不可变快照和安全错误摘要。
- `ApplicationSettingsState` 只包含 Core `ApplicationSettings` 快照和安全错误摘要。
- Desktop、未来 CLI 或其他前端都是只读消费者；写入只发生在 Application 初始化器和用例提交成功之后。

## 数据流

```text
Application initializer
  -> repositories
  -> publish Ready / Faulted state

AccountAppService / SettingsAppService successful mutation
  -> repository remains source of truth
  -> reload committed snapshot
  -> owner writer publishes state

Desktop activation
  -> State.Observe(... Dispatcher ...)
  -> initial snapshot
  -> ordered subsequent updates
```

账号添加、更新、删除和草稿连接测试中的凭据保存/清理由 `AccountAppService` 编排。Desktop 对话框只采集 `CredentialSecretMaterial` 并作为一次请求的瞬时输入传入 Application。明文不得进入 State、结果对象、缓存、日志或长期字段。

## Desktop 消费调整

- 删除 `IAccountDialogWorkflow.AccountsChanged` 及其本地 Channel 合并刷新实现。
- 账号管理、Shell、Home 和远程浏览通过账号 State 建立展示投影。
- Settings 页面通过设置 State 获得初始值和后续保存/重置结果。
- Dialog、确认框、文件选择器和本地化继续属于 Desktop；它们不下沉到 Application。

## 非目标

- 不把远端目录、传输队列或 Provider session 放入本轮 State。
- 不引入 City.EventBus。
- 不让 State 成为第二套持久化或恢复机制。
- 不在本轮处理 Transfer 内部批量提交、worker 或历史分页状态。

## 验收

- City 原子观察、集合观察、并发顺序、Dispose 竞态和 Writer authority 测试通过。
- Application 初始化成功/失败、账号增删改、设置保存/重置、凭据补偿和 State 更新测试通过。
- Desktop 不再声明或订阅 `AccountsChanged`，账号/设置投影使用 `Observe(...)`。
- State、返回 DTO 和诊断文本中均无凭据明文。
- AtomBox 全量测试和 Release 构建通过；真实 UI 烟测由用户最终执行。

