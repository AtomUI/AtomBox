# 测试报告：Application 模块

> 报告状态：已完成
> 创建日期：2026-06-17
> 最近验证：2026-10-13
> 测试命令：`dotnet test tests/AtomBox.Application.Tests`
> 测试框架：xUnit

## 1. 总体结果

- **总计：162 项测试，0 失败，0 跳过**
- 最近全仓运行中的 Application 测试用时：~761ms
- 构建验证：`dotnet build` — 0 warning / 0 error
- 全仓 Release 验证：677/677 测试通过，0 warning / 0 error

## 2. 测试范围

当前 162 项基线包含下述历史两轮 132 项测试、后续新增的 Application 用例测试，以及 5 项 City.State 迁移专项测试。历史分轮表保留用于追溯，不再作为当前测试文件总数统计。

### 2026-06 历史 Round 1 — 契约测试（89 项，5 个文件）

| 测试文件 | 测试数 | 覆盖方法 | 覆盖内容 |
|---|---|---|---|
| `AccountAppServiceContractTests` | 24 | Add/Update/Delete/List/TestConnection/TestConnectionDraft | 空 ID、空凭据、空显示名、仓库失败传播、更新传播、存在性检查、任务冲突、分类过滤、提供商失败、探测成功/失败 |
| `RemoteBrowserAppServiceContractTests` | 14 | ResolveEntry/ListRemoteItems/DeleteRemoteItem/GetPathContext | 空/单个/多个账号状态、分页、文件夹拒绝、提供商失败路径、根/桶根/嵌套路径上下文、游标导航 |
| `TransferAppServiceContractTests` | 27 | CreateUpload/CreateDownload/GetQueue/GetHistory/Cancel/Retry/ClearHistory | 全部验证路径、调度器提交/唤醒失败、翻页钳位、空队列/历史、多项清理、删除停止 |
| `SettingsAppServiceContractTests` | 6 | Get/Update/Reset | 正常获取、更新返回、重置返回默认值、仓库失败传播 |
| `ApplicationServiceCollectionExtensionsContractTests` | 3 | AddAtomBoxApplication | 注册全部 4 个服务（单例）、空参数异常 |

**共计：89 项契约测试，全部通过**（含 16 项原有 `ApplicationValidationTests`）

### 2026-06 历史 Round 2 — 数据测试（43 项，4 个文件）

| 测试文件 | 测试数 | 测试方式 | 覆盖内容 |
|---|---|---|---|
| `AccountAppServiceDataTests` | 7 | 真实 StorageAccountRepository + 真实文件系统 | 持久化 JSON、GetById 匹配、Update 修改文件、删除（完成/未完成任务）、分类过滤 TestConnection 探测 |
| `RemoteBrowserAppServiceDataTests` | 6 | 真实 StorageAccountRepository + 真实文件系统 + 假 Provider | ResolveEntry（0/1/多个）、ListRemoteItems 通过持久化账号、DeleteRemoteItem、GetPathContext |
| `TransferAppServiceDataTests` | 7 | 真实 TransferTaskStore + TransferStateStore + 真实文件系统 + 假调度器 | CreateUpload/Download 创建文件、Queue/History 从持久化读取、Cancel/Retry 路由、ClearHistory 从文件删除 |
| `SettingsAppServiceDataTests` | 7 | 真实 ApplicationSettingsRepository + 真实文件系统 | Get 默认、Update 持久化、UpdateThenGet 一致性、Reset 写入默认值、多次更新覆盖、损坏文件回退 |

**共计：43 项数据测试，全部通过** — 全部经过从 Application 服务到真实文件系统 JSON 的端到端流程。

### 全模块覆盖汇总

| 服务 | 方法 | 契约测试 | 数据测试 | 状态 |
|---|---|---|---|---|
| `AccountAppService` | AddAsync | ✓ | ✓ | 全部覆盖 |
| `AccountAppService` | UpdateAsync | ✓ | ✓ | 全部覆盖 |
| `AccountAppService` | DeleteAsync | ✓ | ✓ | 全部覆盖 |
| `AccountAppService` | ListAsync | ✓ | ✓ | 全部覆盖 |
| `AccountAppService` | TestConnectionAsync | ✓ | ✓ | 全部覆盖 |
| `AccountAppService` | TestConnectionDraftAsync | ✓ | - | 仅契约（无需持久化） |
| `RemoteBrowserAppService` | ResolveEntryAsync | ✓ | ✓ | 全部覆盖 |
| `RemoteBrowserAppService` | ListRemoteItemsAsync | ✓ | ✓ | 全部覆盖 |
| `RemoteBrowserAppService` | DeleteRemoteItemAsync | ✓ | ✓ | 全部覆盖 |
| `RemoteBrowserAppService` | GetPathContext | ✓ | ✓ | 全部覆盖 |
| `TransferAppService` | CreateUploadTasksAsync | ✓ | ✓ | 全部覆盖 |
| `TransferAppService` | CreateDownloadTasksAsync | ✓ | ✓ | 全部覆盖 |
| `TransferAppService` | GetQueueAsync | ✓ | ✓ | 全部覆盖 |
| `TransferAppService` | GetHistoryAsync | ✓ | ✓ | 全部覆盖 |
| `TransferAppService` | CancelAsync | ✓ | ✓ | 全部覆盖 |
| `TransferAppService` | RetryAsync | ✓ | ✓ | 全部覆盖 |
| `TransferAppService` | ClearHistoryAsync | ✓ | ✓ | 全部覆盖 |
| `SettingsAppService` | GetAsync | ✓ | ✓ | 全部覆盖 |
| `SettingsAppService` | UpdateAsync | ✓ | ✓ | 全部覆盖 |
| `SettingsAppService` | ResetAsync | ✓ | ✓ | 全部覆盖 |
| DI 扩展 | AddAtomBoxApplication | ✓ | - | 注册验证 |
| Application State | Initialize / Observe / Fault | ✓ | - | 初始快照、版本顺序、失败状态 |
| 账号凭据事务 | Add / Update / Delete | ✓ | - | 瞬时明文边界、旧凭据待清理、State 发布 |

## 3. 发现的代码问题

本轮迁移发现并修复了账号/设置多消费者重复读取、Desktop 直连凭据仓库以及账号变更事件与当前事实分离的问题。当前自动化基线未发现未处理的回归。

当前 Application 层的关键防护包括：
- 所有公共方法在入站时验证 ID 和必填字段
- 验证失败在调用仓库或工厂之前即返回
- 依赖方失败会正确传播
- Repository 保持持久化事实源，City.State 只发布运行时投影
- 凭据明文仅作为一次请求的瞬时输入，不进入 State 或结果对象
- State 显式区分 `Uninitialized / Ready / Faulted`

## 4. 已移除的测试

无。现有 `ApplicationValidationTests.cs`（16 项测试）保持完好。

## 5. 已知问题（TODO）

| 优先级 | 问题 | 描述 |
|---|---|---|
| **低** | TestConnectionDraft 数据测试 | TestConnectionDraft 创建临时 StorageAccount 对象但不持久化，因此数据测试无额外价值。仅通过契约测试覆盖。 |
| **低** | GetAsync 损坏文件返回 Failure | 当前实现在 JSON 损坏时返回 Failure。行为是预期的，但桌面层可能需要处理此失败并优雅地回退。 |

## 6. 关键决策记录

1. Round 2 数据测试使用真实 `AtomBoxStoragePaths` + 真实仓库实现，但不使用真实 `ITransferTaskScheduler`（传输层更高级别）或真实 `IStorageProviderFactory`（需要实际云提供商）。
2. `StoreBackedScheduler` 和 `CompletingScheduler` 是在数据测试内部创建的桥接假实现，用于连接 Application 调度流程与真实存储——它们模拟调度器将任务持久化到真实仓库。
3. 2026-10-13 新增 City.State 原子观察、状态投影和凭据事务测试；真实 UI 验收由用户另行人工执行。
