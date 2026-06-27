# 测试报告：Infrastructure 模块（存储层）

> 报告状态：已完成
> 创建日期：2026-06-17
> 测试命令：`dotnet test tests/AtomBox.Infrastructure.Tests`
> 测试框架：xUnit

## 1. 总体结果

- **总计：149 项测试，0 失败，0 跳过，0 警告**
- 测试用时：~468ms
- 构建验证：`dotnet build AtomBox.slnx` — 0 warning / 0 error

## 2. 测试范围

### Round 1 — 契约测试（79 项，8 个文件）

| 测试文件 | 测试数 | 覆盖内容 |
|---|---|---|
| `StorageAccountRepositoryContractTests` | 10 | CRUD、冲突、验证、更新 |
| `TransferTaskStoreContractTests` | 9 | CRUD、upsert、删除、列表、状态过滤 |
| `TransferStateStoreContractTests` | 9 | 队列/历史过滤、进度追踪、排序 |
| `LocalTransferFileStoreContractTests` | 11 | 读/写、取消令牌、空路径、零长度文件、目录路径、嵌套目录、冲突检测 |
| `ApplicationSettingsRepositoryContractTests` | 7 | 默认设置、持久化、备份、损坏文件、空 JSON |
| `CredentialStoreContractTests` | 12 | 保存/获取凭据、租约生命周期、待删除、多租约、索引秘密 |
| `CredentialProtectionServiceContractTests` | 9 | 加密/解密、空/null 负载、格式错误、前缀、跨实例 |
| `JsonFileStoreContractTests` | 12 | 读/写、损坏备份、目录创建、原子写入、并发 |

### Round 2 — 数据测试（70 项，7 个文件）

| 测试文件 | 测试数 | 覆盖内容 |
|---|---|---|
| `AtomBoxStoragePathsDataTests` | 5 | 路径构造、目录自动创建、空根目录异常 |
| `JsonFileStoreDataTests` | 11 | 备份、时间戳、空列表、大数据集（5000）、Unicode、并发写入（50 线程）、读后写完整性 |
| `CredentialStoreDataTests` | 10 | 密钥文件大小、跨实例解密、AES-GCM 前缀、Base64、索引结构 |
| `TransferTaskStoreDataTests` | 7 | JSON 数组、必填字段、多任务、删除、statusReason、原地替换 |
| `TransferStateStoreDataTests` | 7 | 进度文件创建、内容、替换、文件分离、未保存任务 upsert |
| `StorageAccountRepositoryDataTests` | 7 | JSON 数组、端点/区域、providerConfig 除外、原地更新、显示名修剪 |
| `ApplicationSettingsRepositoryDataTests` | 5 | 正确值、备份内容、默认设置、OverwritePolicy 枚举持久化 |

## 3. 生产代码修复

- **文件：** `src/AtomBox.Infrastructure/Storage/JsonFileStore.cs:39`
- **问题：** 空的 `{}` JSON 被反序列化为 `ApplicationSettings` 时，域构造函数抛出 `ArgumentOutOfRangeException`（因 concurrency=0 不合法），该异常未被 `catch` 捕获，导致程序崩溃。
- **修复：** 在 `ReadAsync` 的 catch 子句中新增 `ArgumentException`，将其视为损坏的存储数据，走备份 + 返回 `Failure` 路径（`ArgumentOutOfRangeException` 继承自 `ArgumentException`）。

## 4. 已移除的框架行为测试

移除了 10 项测试框架行为的测试：

| 测试名称 | 理由 |
|---|---|
| `WrittenFile_IsUtf8WithoutBom` | 测试 `JsonSerializerDefaults.Web` 的 BOM 配置 |
| `WrittenFile_IsIndented` | 测试缩进格式 |
| `WrittenFile_UsesCamelCasePropertyNames` | 测试 camelCase 命名策略 |
| `TransferTasksFile_UsesCamelCase` | 同上 |
| `AccountsFile_UsesCamelCase` | 同上 |
| `SettingsFile_UsesCamelCase` | 同上 |
| `SettingsFile_IsIndentedJson` | 测试缩进行数 |
| `ProgressFile_UsesCamelCase` | 测试 camelCase 命名策略 |
| `Status_IsPersistedAsInteger` | 测试枚举序列化方式 |
| `Options_IsPersistedWithOverwritePolicy` | 测试属性存在性 |

理由：这些测试验证的是 `JsonSerializerDefaults.Web` 的框架配置行为，不属于业务逻辑；在更换序列化配置时会产生误报。

## 5. 已知问题（TODO）

| 优先级 | 问题 | 描述 |
|---|---|---|
| **高** | `CredentialStore` TOCTOU 竞态条件 | `AcquireLeaseAsync` 在 `ExistsAsync` 和 `CreateLease` 之间存在窗口期，另一线程可调用 `MarkPendingDeleteAsync`。**未测试** — 尚无任何并发测试覆盖此路径，需新增测试并修复。 |
| **中** | 进入下一阶段开发 | 等待下一阶段指示。 |
| **低** | Azure 认证问题 | `az login` 设备代码流程在测试依赖中使用 Azure Identity 时存在认证问题。 |

## 6. 关键决策记录

1. 在 `JsonFileStore.ReadAsync` 的 catch 子句中新增 `ArgumentException` — 域构造函数验证失败应视为存储数据损坏，而非未处理的崩溃。
2. 移除框架序列化行为测试 — 测试框架配置而非业务逻辑。
3. `LocalTransferFileStore.OpenReadAsync` 在预取消的 `CancellationToken` 下返回 `StorageErrorCategory.Canceled`（`OperationCanceledException` 映射结果）。
4. `TransferStateStore.UpdateStatusAsync` 对未保存任务 upsert — 经确认为预期行为。
5. `CredentialStore.HasActiveLease` 通过 `InternalsVisibleTo` 暴露给测试。
