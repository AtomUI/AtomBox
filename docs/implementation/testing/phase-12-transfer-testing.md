# Phase 12 Transfer 测试矩阵

> 文档状态： 第一版发布基线冻结
>
> 创建时间： 2026-06-17
>
> 冻结时间： 2026-06-22
>
> 阶段定位： Phase 11 子阶段 — Transfer 模块无 UI 独立验证，分两轮推进：
>
> Round 1（本轮核心）：使用 fake provider + in-memory store 验证 Transfer 自身状态机、调度器、worker、队列/历史快照
>
> Round 2（扩展）：使用真实 `IStorageProvider` 实现验证 Transfer 端到端集成，含 opt-in 真实 provider 路径
>
> 详细执行清单见 `docs/implementation/phases/phase-11-headless-core-validation.md` §8
>
> 发布基线说明：当前 Transfer 已支持基于设置项的并发上传/下载、运行中取消、失败/中断重试、速度统计和队列/历史查询。本文保留测试矩阵作为回归依据。

## 1. 阶段目标

对 `AtomBox.Transfer` 模块进行独立、完备的功能性测试。Round 1 用 fake provider + in-memory store 覆盖全部 7 种状态流转、调度器编排、worker 执行路径和队列/历史快照，不依赖 Application 层和真实 provider。Round 2 接入真实 `IStorageProvider` 实现验证 Transfer 端到端集成。

Transfer 是调度器，不是 Application，不是 Provider。测试必须围绕调度语义设计，不能退化为 Application scenario 或 provider 集成测试。

## 2. 范围边界

### 2.1 Round 1 必须覆盖（本轮核心）

- TransferTask 模型校验与状态机规则（7 种状态，合法/非法流转）
- TransferQueue 队列选择逻辑（排序、过滤）
- TransferWorker 完整执行路径（upload + download + 失败 + 取消 + 中断）
- TransferTaskScheduler 编排逻辑（submit / cancel / retry / wake）
- TransferRuntimeInitializer 启动时 Running→Interrupted
- 状态快照队列和历史查询一致性
- Cancel 路径（Pending / Running / 终态拒绝）
- Failed 状态持久化（StatusReason + ErrorCategory + IsRetryable）
- Interrupted 路径（OperationCanceledException / 应用关闭）
- Retry 语义（Failed→Pending，Interrupted→Pending，终态拒绝）
- Progress 汇报捕获
- TransferSpeedMeter 计算逻辑（如已有实现，否则标记待实现）
- TransferServiceCollectionExtensions DI 注册校验
- MemoryTransferStore 契约行为（ITransferTaskStore + ITransferStateStore 的 in-memory 实现）

### 2.2 Round 2 扩展范围

- TransferWorker + FakeObjectStorageProvider（Phase 11 已建的 fake provider）
  验证 provider 契约走通时 worker 能正确执行、汇报进度、映射错误
- TransferWorker + Aliyun OSS（opt-in 环境变量）
  真实端到端：创建任务 → Transfer 执行上传/下载 → 队列/历史查询
- TransferWorker + SFTP/FTP（opt-in 环境变量）
  文件传输 provider 路径

### 2.3 本轮明确后置

- 更复杂的并发策略（全局限速、优先级、多队列策略）—— 留给后续 Phase
- 自动恢复历史未完成任务
- 断点续传 / 分片续传恢复
- 跨批次 provider session 复用
- ProviderSessionPool / ProviderClientPool
- Application TransferAppService 端到端 full scenario（属于 Phase 11 Application 阶段）
- Infrastructure TransferStateStore JSON 持久化测试（属于 Phase 11 Infrastructure 阶段）
- FTPS provider、阿里云盘 / 百度网盘真实账号验收和 OAuth/token refresh 场景

## 3. 测试分层

| 层级 | 默认运行 | 目标 |
| --- | --- | --- |
| TransferTask 模型测试 | 是 | 验证值对象不变性、状态机规则、WithStatus 边界 |
| TransferQueue 测试 | 是 | 验证 Pending 任务排序和选择逻辑 |
| TransferWorker 测试 | 是 | 验证单任务 upload/download 执行路径及失败场景 |
| TransferTaskScheduler 测试 | 是 | 验证 submit/cancel/retry/wake 编排 |
| TransferRuntimeInitializer 测试 | 是 | 验证启动时 Running→Interrupted |
| Progress 汇报测试 | 是 | 验证 IProgress\<TransferProgress\> 回调逻辑 |
| 队列/历史快照测试 | 是 | 验证 Task + Progress 合并后的快照一致性 |
| DI 注册校验测试 | 是 | 验证 AddAtomBoxTransfer 注册完整性 |
| 真实 provider 集成测试（Round 2） | 否 | 验证 Transfer + ObjectStorageProvider / OSS / SFTP 端到端，需 opt-in |

## 4. Fake Transfer 测试基础设施

Round 1 推荐创建以下 In-Memory 测试替身（「已有」表示在 TransferTaskSchedulerTests.cs 中已存在可复用或需提取为共享的 helper）：

| 替身 | 职责 | 状态 |
| --- | --- | --- |
| `MemoryTransferTaskStore` | 实现 `ITransferTaskStore`（内存 List） | 已有，可提取为共享 fixture |
| `MemoryTransferStateStore` | 实现 `ITransferStateStore`（快照查询 + 进度合并） | 已有，可提取为共享 fixture |
| `FakeProviderFactory` / `FixedProviderFactory` | 实现 `IStorageProviderFactory`，返回指定 provider | 已有 |
| `FakeProvider` | 实现 `IStorageProvider`，upload/download 成功 | 已有 |
| `FailingUploadProvider` / `FailingDownloadProvider` | 注入指定 StorageError | 已有 |
| `AnyAccountRepository` | 实现 `IStorageAccountRepository`，返回固定账号 | 已有 |
| `MemoryLocalTransferFileStore` | 实现 `ILocalTransferFileStore`（内存 byte[]） | 已有 |

Round 2 需要：

| 替身 | 职责 | 状态 |
| --- | --- | --- |
| `FakeObjectStorageProvider` | Phase 11 已建的 fake provider，走 IStorageProvider 正式契约 | 复用 Phase 11 测试 |
| `AliyunOssProvider` | 真实 OSS provider（opt-in） | 复用 Phase 11 |
| `SftpProvider` | 真实 SFTP provider（opt-in） | 复用 Phase 11 |

## 5. TransferTask 模型测试矩阵

### 5.1 值对象校验

| Case | 期望 |
| --- | --- |
| `TransferTask_Constructor_RequiresNonNullFields` | Id / StorageAccountId / Direction / LocalPath / RemotePath 不可为 null |
| `TransferTask_DefaultOptions_AreReasonable` | OverwritePolicy、MaxRetryCount 有合理默认值 |
| `TransferProgress_NegativeBytesTransferred_Throws` | bytesTransferred \< 0 抛 ArgumentOutOfRangeException |
| `TransferProgress_NegativeTotalBytes_Throws` | totalBytes \< 0 抛 ArgumentOutOfRangeException |
| `TransferProgress_BytesExceedsTotal_Throws` | bytesTransferred > totalBytes 抛 ArgumentOutOfRangeException |
| `TransferProgress_NegativeSpeed_Throws` | speedBytesPerSecond \< 0 抛 ArgumentOutOfRangeException |
| `TransferProgress_Percent_ComputesCorrectly` | (25, 100) → 25%；TotalBytes=null → null |
| `TransferProgress_Percent_ClampsTo100` | (150, 100) → 100 |
| `TransferStatus_Values_AreStable` | 枚举值顺序不变（后端持久化依赖数值）：Pending=0, Running=1, Paused=2, Interrupted=3, Succeeded=4, Failed=5, Canceled=6 |

### 5.2 状态机规则

| Case | 期望 |
| --- | --- |
| `Pending_CanCancel_CanRetry_State` | CanCancel=true，CanRetry=false |
| `Pending_To_Running` | WithStatus(Running) 成功 |
| `Pending_To_Canceled` | WithStatus(Canceled) 成功（等效用户取消待执行任务） |
| `Running_To_Succeeded` | 成功流转，UpdatedAt 更新 |
| `Running_To_Failed` | StatusReason / ErrorCategory / IsRetryable 正确填充 |
| `Running_To_Canceled` | 运行中取消成功 |
| `Running_To_Interrupted` | 应用关闭中断，IsRetryable=true |
| `Failed_CanRetry_CanCancel_State` | CanRetry=true，CanCancel=false |
| `Failed_To_Pending` | Retry 重置，清除 StatusReason / ErrorCategory / IsRetryable |
| `Interrupted_CanRetry_CanCancel_State` | CanRetry=true，CanCancel=false |
| `Interrupted_To_Pending` | 手动恢复成功 |
| `Paused_To_Pending` | 恢复执行 |
| `Succeeded_Retry_Rejected` | CanRetry=false，WithStatus(Pending) 或 Retry 拒绝 |
| `Succeeded_Cancel_Rejected` | CanCancel=false，WithStatus(Canceled) 拒绝 |
| `Canceled_Retry_Rejected` | CanRetry=false |
| `Canceled_Cancel_Rejected` | CanCancel=false |
| `WithStatus_OlderUpdatedAt_Throws` | 传入更早 UpdatedAt 抛 ArgumentException |
| `WithStatus_SameUpdatedAt_Allowed` | 相同时间戳允许（幂等更新场景） |
| `CanRetry_Requires_IsRetryable_True` | IsRetryable=false 时即使 CanRetry()=true 也不应重试 |
| `Direction_Upload_LocalIsSource_RemoteIsTarget` | 上传时 LocalPath 是来源、RemotePath 是目标 |
| `Direction_Download_RemoteIsSource_LocalIsTarget` | 下载时 RemotePath 是来源、LocalPath 是目标 |

## 6. TransferQueue 测试矩阵

| Case | 期望 |
| --- | --- |
| `SelectPending_ReturnsOnlyPendingTasks` | Running / Succeeded / Failed / Canceled / Interrupted / Paused 排除 |
| `SelectPending_OrdersByCreatedAt_Ascending` | 先创建的先返回 |
| `SelectPending_EmptyList_ReturnsEmptyArray` | 无 Pending 时返回空数组 |
| `SelectPending_NullInput_Throws` | 抛 ArgumentNullException |
| `SelectPending_MixedStatuses_OnlyPendingReturned` | 5 个任务中 2 个 Pending，只返回那 2 个 |

## 7. TransferWorker 测试矩阵

### 7.1 Upload 路径

| Case | 期望 |
| --- | --- |
| `Upload_SingleFile_Success` | Status=Succeeded，Progress=100%，队列查询可看到进度 |
| `Upload_ProviderCreationFailed` | Status=Failed，ErrorCategory=ProviderUnavailable |
| `Upload_ProviderAuthenticationFailed` | Status=Failed，ErrorCategory=Authentication |
| `Upload_LocalFileNotFound` | Status=Failed，ErrorCategory=NotFound（本地文件不存在） |
| `Upload_RemoteConflict` | Status=Failed，ErrorCategory=Conflict |
| `Upload_NetworkTimeout` | Status=Failed，ErrorCategory=Network，IsRetryable=true |
| `Upload_CancelledDuringExecution` | Status=Interrupted，IsRetryable=true |
| `Upload_ProgressCallback_ReceivesUpdates` | IProgress\<TransferProgress\>.Report 至少被调用一次 |
| `Upload_AccountLookupFailed` | Status=Failed，ErrorCategory=Unknown 或 ProviderUnavailable |

### 7.2 Download 路径

| Case | 期望 |
| --- | --- |
| `Download_SingleFile_Success` | Status=Succeeded，Progress=100% |
| `Download_ProviderCreationFailed` | Status=Failed，ErrorCategory=ProviderUnavailable |
| `Download_RemoteFileNotFound` | Status=Failed，ErrorCategory=NotFound |
| `Download_LocalWriteFailed` | Status=Failed，ErrorCategory=Unknown 或 StorageError |
| `Download_CancelledDuringExecution` | Status=Interrupted，IsRetryable=true |
| `Download_ProgressCallback_ReceivesUpdates` | IProgress\<TransferProgress\>.Report 至少被调用一次 |

### 7.3 生命周期与错误

| Case | 期望 |
| --- | --- |
| `Worker_ExecutesOnlyOnce` | 同一 worker 第二次调用 ExecuteAsync 应拒绝或抛出 |
| `Worker_Disposed_RejectsOperations` | dispose 后调用 ExecuteAsync 应抛出 ObjectDisposedException |
| `Worker_ReportsFinalProgress_OnCompletion` | Succeeded 时 Progress=100%（或最终值） |
| `Worker_DoesNotLeakProvider_AfterExecution` | provider 在 worker 执行后被 dispose（可用 FakeProvider 的 disposed flag 验证） |
| `Worker_DoesNotLeakCredentialLease` | credential lease 在 worker 执行后被释放 |

## 8. TransferTaskScheduler 测试矩阵

### 8.1 Submit

| Case | 期望 |
| --- | --- |
| `SubmitAsync_SavesPendingTask` | 任务落 store，status=Pending |
| `SubmitAsync_DoesNotExecuteTask` | worker 未被调用 |
| `SubmitAsync_NullTask_Throws` | 抛 ArgumentNullException |
| `SubmitAsync_ThenWakeAsync_Executes` | submit 后 wake 能执行该任务 |

### 8.2 Cancel

| Case | 期望 |
| --- | --- |
| `CancelAsync_PendingTask_StatusCanceled` | Pending → Canceled |
| `CancelAsync_RunningTask_StatusCanceled` | Running → Canceled |
| `CancelAsync_SucceededTask_ReturnsConflict` | 返回 Conflict 错误 |
| `CancelAsync_FailedTask_ReturnsConflict` | 返回 Conflict 错误 |
| `CancelAsync_InterruptedTask_ReturnsConflict` | 返回 Conflict 错误 |
| `CancelAsync_CanceledTask_ReturnsConflict` | 返回 Conflict 错误 |
| `CancelAsync_NonExistentTask_ReturnsNotFound` | NotFound 错误 |
| `CancelAsync_NullTaskId_Throws` | 抛 ArgumentNullException |
| `CancelAsync_CanceledTask_StatusReasonSet` | Canceled 任务保存 StatusReason |

### 8.3 Retry

| Case | 期望 |
| --- | --- |
| `RetryAsync_FailedTask_BackToPending` | Failed → Pending，清除旧错误信息 |
| `RetryAsync_InterruptedTask_BackToPending` | Interrupted → Pending |
| `RetryAsync_SucceededTask_ReturnsConflict` | 返回 Conflict |
| `RetryAsync_PendingTask_ReturnsConflict` | 返回 Conflict |
| `RetryAsync_RunningTask_ReturnsConflict` | 返回 Conflict |
| `RetryAsync_CanceledTask_ReturnsConflict` | 返回 Conflict |
| `RetryAsync_NonExistentTask_ReturnsNotFound` | NotFound 错误 |
| `RetryAsync_NullTaskId_Throws` | 抛 ArgumentNullException |
| `RetryAsync_ResetsRetryableFlag` | 重试后 IsRetryable 重置为未决定状态 |

### 8.4 Wake

| Case | 期望 |
| --- | --- |
| `WakeAsync_ProcessesAllPendingTasks` | n 个 Pending 创建 n 个 worker |
| `WakeAsync_NoPendingTasks_NoOp` | 空操作，返回成功 |
| `WakeAsync_MixedStatuses_OnlyPending` | Pending 被执行，Running/终态跳过 |
| `WakeAsync_PartialWorkerFailure_DoesNotAffectOthers` | 3 个 Pending 中第 2 个 worker 失败，第 1 和第 3 仍可完成 |
| `WakeAsync_StoreUnavailable_ReturnsError` | 存储不可用时返回失败 |
| `WakeAsync_CreatesWorkerPerPendingTask` | 每个 Pending 任务使用独立 worker 实例 |

## 9. TransferRuntimeInitializer 测试矩阵

| Case | 期望 |
| --- | --- |
| `InitializeAsync_NoRunningTasks_Success` | 状态无变化 |
| `InitializeAsync_RunningTasks_MarkedInterrupted` | 所有 Running → Interrupted，IsRetryable=true，StatusReason 有值 |
| `InitializeAsync_MixedRunningAndPending` | Running → Interrupted，Pending 不变 |
| `InitializeAsync_StoreUnavailable_ReturnsError` | 返回失败结果 |
| `InitializeAsync_Idempotent_SecondCallNoOp` | 第二次调用时已无 Running，空操作 |
| `InitializeAsync_InterruptedTasks_HaveRetryableError` | Interrupted 任务的 ErrorCategory=Unknown，IsRetryable=true |

## 10. 队列和历史快照测试矩阵

### 10.1 队列快照（ITransferStateStore.ListQueueAsync）

| Case | 期望 |
| --- | --- |
| `ListQueue_ReturnsPendingAndRunningTasks` | 包含 Pending + Running 状态的任务+进度 |
| `ListQueue_ExcludesTerminalStates` | Succeeded / Failed / Canceled / Interrupted 不出现 |
| `ListQueue_Empty_ReturnsEmptyList` | 无任务时返回空 |
| `ListQueue_TaskWithProgress_ProgressPopulated` | Progress 字段正确填充 |
| `ListQueue_SnapshotCanRetry_MatchesTask` | Task.IsRetryable + Task.CanRetry() 一致 |
| `ListQueue_ProgressIsImmutable_AfterSnapshot` | 快照取出后修改原始 Progress 不影响快照 |

### 10.2 历史快照（ITransferStateStore.ListHistoryAsync）

| Case | 期望 |
| --- | --- |
| `ListHistory_IncludesAllTerminalStates` | Succeeded / Failed / Canceled / Interrupted 出现在历史 |
| `ListHistory_Empty_ReturnsEmptyList` | 无终端任务时返回空 |
| `ListHistory_Pagination_SkipZeroTakeDefault` | skip=0, take=50 返回前 50 条 |
| `ListHistory_Pagination_SkipPage` | skip=50, take=50 返回 51-100 条 |
| `ListHistory_Pagination_ExceedsTotal` | skip=100（总共 80 条）返回空 |
| `ListHistory_ExcludesNonTerminalStates` | Pending / Running / Paused 不出现在历史 |
| `ListHistory_TaskWithProgress_ProgressPopulated` | 已完成任务的进度快照正确 |

## 11. 进度与速度测试矩阵

### 11.1 TransferProgress 回调

| Case | 期望 |
| --- | --- |
| `Worker_ReportsProgressDuringUpload` | FakeProvider 的 WriteAsync 调用 IProgress.Report |
| `Worker_ReportsProgressDuringDownload` | FakeProvider 的 OpenReadAsync 调用 IProgress.Report |
| `Worker_FinalProgressAfterCompletion` | 完成后进度为 100% 或最终值 |
| `Worker_ProgressWithUnknownTotalBytes` | TotalBytes=null 时 Percent=null |
| `CancelledTask_DoesNotReportFalseProgress` | 取消后不再有虚假的 100% 汇报 |

### 11.2 TransferSpeedMeter

（当前为占位符，测试覆盖待实现后补充）

| Case | 期望 |
| --- | --- |
| `SpeedMeter_ComputesBytesPerSecond` | (bytes delta) / (time delta) 计算正确 |
| `SpeedMeter_InitialState_ReturnsNullOrZero` | 尚未接收到数据时无有效速度 |
| `SpeedMeter_MultipleSamples_Averages` | 多次采样后速度平滑或滚动窗口合理 |
| `SpeedMeter_Disposed_StopsAcceptingData` | Dispose 后 Report 被忽略或抛出 |

## 12. DI 注册校验测试矩阵

| Case | 期望 |
| --- | --- |
| `AddAtomBoxTransfer_Registers_TransferQueue` | ServiceCollection 含 TransferQueue |
| `AddAtomBoxTransfer_Registers_TransferWorker` | ServiceCollection 含 TransferWorker（Transient） |
| `AddAtomBoxTransfer_Registers_FuncTransferWorkerFactory` | Func\<TransferWorker\> 已注册 |
| `AddAtomBoxTransfer_Registers_ITransferTaskScheduler` | ITransferTaskScheduler 已注册 |
| `AddAtomBoxTransfer_Registers_TransferRuntimeInitializer` | TransferRuntimeInitializer 已注册 |
| `FuncTransferWorker_CreatesNewInstanceEachTime` | 两次调用工厂返回不同实例 |
| `ServiceProvider_Resolves_ITransferTaskScheduler` | 构建后可成功解析 |
| `ServiceProvider_ValidateOnBuild_Passes` | 在完整 DI 图中 ValidateOnBuild 通过 |
| `ServiceProvider_MissingDependency_Fails` | 缺少 IStorageAccountRepository 时 ValidateOnBuild 报错 |

## 13. Round 2：真实 Provider 集成验证矩阵

Round 2 验证 Transfer 端到端集成。测试默认不执行，需要 opt-in 环境变量。

### 13.1 Transfer + FakeObjectStorageProvider

FakeObjectStorageProvider 是 Phase 11 Provider 契约测试中已建的正式 fake provider，走完整 `IStorageProvider` 契约路径。

| Case | 期望 |
| --- | --- |
| `Transfer_WithObjectStorageProvider_UploadDownloadRoundtrip` | 创建上传任务 → TransferWorker 执行 → 下载校验 → 状态 Succeeded |
| `Transfer_WithObjectStorageProvider_DeleteAfterUpload` | 上传后删除远端对象 → 任务完成 |
| `Transfer_WithObjectStorageProvider_ListAfterUpload_ShowsObject` | 上传后 provider 列表中可看到该对象 |
| `Transfer_WithObjectStorageProvider_ProviderAuthFailure` | Provider 返回 Authentication 错误 → 任务 Failed，ErrorCategory=Authentication |
| `Transfer_WithObjectStorageProvider_ProviderNotFound` | Provider 返回 NotFound → 任务 Failed，ErrorCategory=NotFound |
| `Transfer_WithObjectStorageProvider_ProgressReportedCorrectly` | 进度回调累计字节数与实际文件大小一致 |

### 13.2 Transfer + Aliyun OSS（opt-in）

需要同 Phase 11 Provider 测试矩阵 §9 的环境变量：`ATOMBOX_TEST_ALIYUN_OSS=1`

| Case | 期望 |
| --- | --- |
| `Transfer_AliyunOss_UploadSmallFile` | 上传小文件到 OSS 测试 prefix，状态 Succeeded |
| `Transfer_AliyunOss_DownloadUploadedFile` | 下载已上传的文件，内容一致 |
| `Transfer_AliyunOss_DeleteTestObject` | 删除测试对象，状态 Succeeded |
| `Transfer_AliyunOss_QueueShowsProgress` | 执行期间队列快照可查到进度 |
| `Transfer_AliyunOss_HistoryShowsCompletion` | 完成后历史快照可查到任务记录 |

### 13.3 Transfer + SFTP（opt-in）

需要同 Phase 11 Provider 测试矩阵 §10.5 的环境变量：`ATOMBOX_TEST_SFTP=1`

| Case | 期望 |
| --- | --- |
| `Transfer_Sftp_UploadSmallFile` | 上传小文件到 SFTP 测试 prefix，状态 Succeeded |
| `Transfer_Sftp_DownloadUploadedFile` | 下载已上传的文件，内容一致 |
| `Transfer_Sftp_DeleteTestFile` | 删除测试文件，状态 Succeeded |

### 13.4 Transfer + FTP（opt-in）

需要同 Phase 11 Provider 测试矩阵 §10.5 的环境变量：`ATOMBOX_TEST_FTP=1`

| Case | 期望 |
| --- | --- |
| `Transfer_Ftp_UploadSmallFile` | 上传小文件到 FTP 测试 prefix，状态 Succeeded |
| `Transfer_Ftp_DownloadUploadedFile` | 下载已上传的文件，内容一致 |
| `Transfer_Ftp_DeleteTestFile` | 删除测试文件，状态 Succeeded |

## 14. 完成条件

### 14.1 Round 1 完成条件（本轮通过即可标记 Phase 11 Transfer 验证完成）

- TransferTask 模型测试全部通过，覆盖 §5 全部 case
- TransferQueue 测试全部通过，覆盖 §6 全部 case
- TransferWorker 测试覆盖 upload + download + 常见失败路径 + 取消 + 中断，覆盖 §7 全部 case
- TransferTaskScheduler 测试覆盖 submit / cancel / retry / wake 完整编排，覆盖 §8 全部 case
- TransferRuntimeInitializer 测试覆盖 Running→Interrupted 路径，覆盖 §9 全部 case
- 队列和历史快照测试覆盖 snapshot 一致性 + 分页边界，覆盖 §10 全部 case
- 进度汇报测试覆盖 Progress 回调捕获，覆盖 §11 全部 case
- DI 注册校验测试通过，覆盖 §12 全部 case
- `dotnet test AtomBox.Transfer.Tests` 全部通过
- 测试不依赖外部网络、真实 provider 或真实文件系统
- 所有错误详情和日志不包含 secret material

### 14.2 Round 2 完成条件（可选，不作为 Phase 11 当前完成门槛）

- Transfer + FakeObjectStorageProvider 集成测试覆盖 §13.1 全部 case
- 设置 OSS 环境变量后 Transfer + Aliyun OSS 可通过 §13.2 的 upload / download / delete 测试
- 设置 SFTP / FTP 环境变量后对应集成测试可通过 §13.3 或 §13.4 测试
- Round 2 测试默认不跑，不纳入 `dotnet test AtomBox.Transfer.Tests` 默认结果
- Round 2 测试 `ATOMBOX_TEST_*` 环境变量模板记录于 `.local/` 目录或本文档附录
