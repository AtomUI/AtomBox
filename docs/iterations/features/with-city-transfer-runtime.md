# AtomBox.Transfer 后台 Runtime 与 City.Core.Module 重构决策记录

> Matrix ID：`AB-TRANSFER × CITY-CORE`
>
> 状态：1–7 阶段代码重构与默认自动化门禁已完成；真实 Provider 与真实 UI 人工验收待执行
>
> 决策开始时间：2026-10-16

## 1. 目标与框架边界

`AtomBox.Transfer` 将建立由应用生命周期拥有的后台 `TransferRuntime`。Runtime 由 AtomBox 自行实现，City 只通过 `AtomBoxTransferModule` 提供服务注册、应用启动、应用关闭和 `ApplicationScope` 生命周期边界。传输队列、worker、并发、取消、恢复、进度与状态持久化仍由 Transfer 拥有，不进入 City。

本次不要求 City 增强。City 现有 Module、ApplicationScope 和 Host shutdown 能力足以承载该设计。

## 2. 已确认决策

### 2.1 Runtime 生命周期

- Runtime 是应用级单例，在 City Host 完成应用初始化时启动，在应用关闭阶段停止。
- 生命周期状态采用 `Created -> Starting -> Running -> Stopping -> Stopped`，启动失败进入 `Faulted`。
- `StartAsync`、`StopAsync` 必须幂等；并发调用共享同一次启停过程，不能创建多个调度循环。
- 只有 `Running` 状态接受新任务；进入 `Stopping` 后原子关闭准入。
- `Stopped` 是同一个 Host 内的终态，不支持重新启动；重新运行必须创建新的 Host。
- Runtime 预先启动的是异步调度能力，不是固定占用线程的空转循环。没有任务时必须通过异步信号等待。

### 2.2 提交与执行分离

- Application 提交任务成功表示任务已经作为 `Pending` 事实可靠持久化并交给 Runtime，不表示上传或下载已经成功。
- 提交方法在受理完成后立即返回，不等待 Provider 执行完成；UI 文案应表达“已添加到传输队列”。
- 页面或 ViewModel 令牌只控制任务受理阶段。任务持久化成功后，所有权转移给应用级 Runtime，页面停用不得取消实际传输。
- 用户取消必须通过显式的任务取消用例按任务 ID 发起。
- 持久化 Store 是事实源，内存调度信号只是提示。持久化成功后即使应用在发出信号前崩溃，任务也不能丢失。
- 现有公开 `WakeAsync` 不再保留“执行并等待全部 Pending 任务完成”的语义；调度唤醒将收进 Runtime 内部。

### 2.3 Runtime 与 Provider SDK 的协调边界

- Runtime 为每个逻辑传输创建并跟踪一个受管 `Task`，负责全局并发、活跃任务表、取消请求、异常观察、有限等待和最终状态；Provider 负责一次实际协议或厂商 SDK 操作。
- Runtime 继续只依赖 Core 中的 `IStorageProviderFactory` / `IStorageProvider`，不得识别阿里云、SFTP、FTP 等具体实现，也不得把 Provider 实例持久化进任务。
- `CancellationToken` 是协作式取消，不是强杀机制。Runtime 能完全管理自己的逻辑任务，但不能承诺强制终止任意第三方 SDK。
- 当前部分 Provider 只在操作开始前检查令牌，随后调用同步 `UploadFile`、`DownloadFile`、`PutObject` 等 API；其返回 `Task` 不代表真正的异步 I/O。该差异必须由 Providers 按实际 SDK/协议能力治理，不能在 Transfer 中编写厂商特例。
- 真正异步且支持取消的实现直接 `await` 并贯穿令牌；同步 SDK 必须与 Runtime 调度循环隔离到受控后台任务；支持 abort/disconnect 的 adapter 在取消时主动中止；不支持中止的实现采用“请求取消、有限等待、超时记为 `Interrupted`”策略。
- `Task.Run` 只能隔离阻塞调用，不能终止已经运行的同步 SDK 方法。禁止使用强杀线程作为取消方案。
- Provider 需要向统一能力合同如实表达阻塞 I/O、协作取消、进度和主动中止能力。能力合同定义应位于 Core，具体值和中止实现位于 Providers，Transfer 只据此选择通用执行与关闭策略。
- Provider 内部分片并发与 Runtime 的文件级并发是两个独立层次，必须分别限制，避免“文件并发数 × SDK 分片并发数”形成失控请求量。

### 2.4 持久化队列与调度信号

- `ITransferTaskStore` 是任务事实源；内存 Channel 只表示“持久化队列可能发生变化，请重新检查”，不得把未持久化的 `TransferTask` 仅保存在内存队列中。
- 调度信号采用容量为 1、单读取者、多写入者的 bounded Channel。重复信号允许合并，不能因大量文件提交而积累等量内存通知。
- Runtime 只拥有一个异步调度循环。没有信号时通过 `WaitToReadAsync` 挂起，不允许空转轮询，也不需要为等待专门占用线程。
- 发送信号的固定时机包括 Runtime 启动、新任务持久化成功、重试变回 `Pending`、Worker 完成释放槽位、并发设置变化和调度查询临时失败后的延迟重试。
- 正确顺序必须是“先持久化 `Pending`，再通知 Runtime”。应用在两步之间崩溃时，Runtime 下次启动必须从 Store 重新发现任务。
- Store 查询临时失败时，Runtime 必须记录异常、退避并自行重新请求调度，不能消费信号后永久等待；不可恢复的调度循环故障使 Runtime 进入 `Faulted` 并关闭准入。
- 停止时先关闭提交准入并等待已经进入受理阶段的提交结束，然后才能停止调度循环，避免任务刚落盘而 Runtime 已经退出。

### 2.5 任务状态持久化时序

- 创建任务时先持久化为 `Pending`，提交成功即表示 AtomBox 已经可靠接受该任务。
- Runtime 执行 Provider 前，必须以“预期状态为 `Pending`”的原子条件更新把任务领取为 `Running`；领取失败的调度方不得执行该任务。
- `Running` 期间按节流规则持久化进度，最终完成、失败、取消或中断前强制刷新最后进度。
- Provider 成功返回后持久化为 `Succeeded`；业务或网络失败为 `Failed`；用户取消且 Worker 已经退出为 `Canceled`；应用关闭、超时或最终结果无法确认为 `Interrupted`。
- 终态任务不删除，只从活动队列视图进入历史视图。
- 如果远端操作已经成功但本地 `Succeeded` 落盘失败，本地不得虚构完成事实。重启后遗留的 `Running` 只能按结果未知处理为 `Interrupted`，后续根据幂等、覆盖策略或远端校验决定是否可重试。

### 2.6 原子领取、活跃任务所有权与并发

- `TransferTask` 增加持久化、单调递增的 `Revision`，不再使用 `UpdatedAt` 判断并发写入先后。时间戳只用于展示和审计，不是并发令牌。
- Core 的 `ITransferTaskStore` 增加条件更新合同；写入必须同时校验任务 ID、预期 `Revision` 和预期状态。Infrastructure 在同一原子临界区内完成读取、比较、替换和持久化。
- `Pending -> Running` 是领取动作。只有条件更新成功的调度方才能调用 Provider；领取失败必须归还并发名额并跳过执行。
- Runtime 持有按 `TransferTaskId` 建立的活跃任务表。每个条目至少持有执行 `Task`、专属取消源和任务 ID，禁止创建不被 Runtime 观察的 fire-and-forget Worker。
- Worker 无论成功、失败、取消还是抛出异常，都必须在统一 `finally` 收尾中完成异常观察、活跃表移除、并发名额释放和下一次调度通知。
- 文件级最大并发由 Runtime 的活跃任务数/并发名额统一约束：先获得名额，再原子领取；领取失败立即归还；Worker 完成后释放并请求继续调度。
- 用户取消不能先无条件写 `Canceled` 再让旧 Worker 继续写状态；最终状态更新同样使用 Revision/预期状态条件，防止取消、进度、重试和关闭结果互相覆盖。
- 当前 JSON Store 的 `SemaphoreSlim` 只能保证单进程内原子性。AtomBox Desktop 明确采用单实例运行；如果未来允许多个进程共享任务库，必须改用跨进程锁或支持事务条件更新的数据库，不能继续依赖进程内锁。
- 以上修改分别落在真实所有者：Core 定义条件更新合同，Infrastructure 实现原子持久化，Transfer 决定状态转换并持有 Worker，Desktop 承担单实例入口；City 不参与任务并发控制。

### 2.7 当前版本不提供断点续传

- 当前版本明确不提供上传或下载断点续传，不持久化可恢复分片会话、upload ID、已完成 part 清单、下载 range 偏移或协议私有恢复令牌。
- `Interrupted`、`Failed` 或用户手动重试一律从任务起点重新执行；已有进度只用于展示和诊断，不作为恢复执行依据。
- Provider/厂商 SDK 即使自身支持 multipart resume，也不能在统一产品合同之外私自宣称任务可续传。未来如立项，必须逐 Provider 声明恢复能力、会话持久化、安全过期、远端清理和兼容策略。
- 重试前仍必须遵守覆盖策略、幂等能力和必要的远端校验，避免“远端实际成功但本地结果未知”时盲目重复写入。

### 2.8 应用启动恢复

- `Pending` 表示已经可靠受理且尚未被 Runtime 领取。启动完成后自动调度所有持久化 `Pending`；这属于可靠队列执行，不属于断点续传。
- 遗留 `Running` 表示上次运行已经领取但最终结果未知。启动时通过 Revision/预期状态条件原子转换为 `Interrupted`，保留最后进度，不自动重试。
- `Paused`、`Interrupted`、`Failed`、`Canceled`、`Succeeded` 启动时保持原状态，不自动执行；允许重试的任务必须由用户显式发起，并从任务起点重新执行。
- 启动顺序固定为：进入 `Starting`、确认单实例、读取任务、恢复遗留 `Running`、重建并发布 City.State、启动唯一调度循环、进入 `Running` 并开放准入、发送首次调度信号。
- 恢复或 State 初始化失败时 Runtime 进入 `Faulted`，不得开放任务准入或启动 Worker；原始异常向 City Module 传播并发布脱敏的 Faulted 投影。
- 本地 Store 与远端 Provider 不存在分布式事务，当前版本不承诺 exactly-once。远端可能已成功而本地未记录终态的任务必须保守地保持结果未知，不能自动重复执行。

### 2.9 应用关闭事务与不合作 SDK

- City 停止 HostScope/ApplicationScope 时先广播取消；Runtime 必须立即、原子地进入 `Stopping`、关闭提交准入并停止领取新的 `Pending`，CancellationToken 回调中不得执行磁盘 I/O 或同步等待。
- 关闭准入后等待已经进入受理阶段的提交完成；成功落盘的任务保持 `Pending` 并在下次启动自动执行，未成功落盘的提交不得报告已受理。
- 对所有活跃 Worker 请求取消，并在 Provider 明确支持且保证安全时调用 abort/disconnect。Worker 等待使用小于 City Host 总关闭期限的独立宽限期，同时预留最终状态与进度落盘预算。
- “等待有界”只保证 `TransferRuntime.StopAsync` 不无限等待，不代表 Runtime 能强制终止另一个 `Task`。超时只停止等待，底层同步 SDK 调用可能仍在进程内运行。
- 超时执行体在 Runtime 内标记为 `Abandoned`，通过 `ExecutionLease` 禁止继续发布进度或提交最终状态；Runtime 使用 Revision/预期状态 CAS 将其持久化为 `Interrupted`。迟到 Worker 的旧 Revision 不能覆盖该结果。
- Worker 自己拥有正在使用的 Provider、Stream 和协议资源。Runtime 不得从其他线程强制 Dispose，除非对应 Provider 明确声明并实现并发安全的中止合同；Worker 真正返回后只负责释放自己的资源。
- Worker 的 Provider 执行结果与 Runtime 的状态提交必须解耦：Worker/Provider executor 返回结果并释放局部资源，Runtime 持有 ExecutionLease、Revision 和最终提交权，避免被放弃的执行体在应用服务释放后继续访问 Store 或 City.State。
- 正式 Host 停止是终态且不恢复同一 Host。Runtime 完成必要落盘后不再等待不合作 SDK，Host 继续结束，最终由操作系统随 AtomBox 进程退出统一回收线程、连接、句柄和内存。
- 上述兜底只适用于应用真正退出。最小化到托盘不得停止 Host；用户取消单个任务时应用仍在运行，不合作 Worker 继续留在活跃表并占用并发槽位，不能伪装成已经停止。
- 如果未来要求无条件强制终止 SDK，必须把 Provider 执行迁移到可被终止的独立子进程；当前版本不采用进程隔离，也不使用 `Thread.Abort` 等不安全方案。
- 最终持久化使用只受关闭剩余期限约束的 flush token，不能使用已经取消的 Runtime/ApplicationScope token。持久化失败必须保留并向 City Module/Host 传播；City.State 发布失败不能推翻已经成功落盘的事实。

### 2.10 取消来源与最终状态

- `TransferStatus` 在现有数值末尾追加 `Canceling`，不改变已持久化枚举值。它表示用户已请求取消，但 Worker/Provider 尚未确认退出，仍属于活动任务并占用并发槽位。
- 用户取消尚未执行的 `Pending` 时，CAS 直接转换为 `Canceled`；用户取消 `Running` 时，CAS 转为 `Canceling` 后再通知执行体，不得提前伪装成已经取消。
- Provider 响应用户取消并退出后，`Canceling -> Canceled`；如果 Provider 在取消生效前已经明确成功，则提交 `Succeeded`；如果仍未退出而 Host 最终关闭或 Runtime 放弃等待，则提交 `Interrupted`。
- Host/ApplicationScope 关闭导致的停止统一为 `Interrupted`，不是用户 `Canceled`。Provider 已经抢先可靠提交的明确成功或失败结果不回滚。
- Provider 返回 `OperationCanceled` 时必须结合 Runtime 记录的取消来源判断：用户取消为 `Canceled`，Host 关闭为 `Interrupted`，AtomBox 未请求取消则是意外 Provider 失败并记为 `Failed`。
- Provider 已结束并明确返回 timeout/普通错误时记为 `Failed`；Runtime 等待超时而 SDK 仍未结束、远端结果无法确认时记为 `Interrupted`。
- 未捕获执行异常必须被 Runtime 观察并映射为 `Failed`；ExecutionLease 已放弃后的迟到异常只进入诊断，不能改变 `Interrupted`。
- 取消接口幂等：`Running -> Canceling` 只发生一次，重复取消 `Canceling` 直接成功，已经 `Canceled` 也返回已完成语义；终态是否允许新建重试由明确用例决定。
- 当前版本中 `Canceled` 只表示用户发起取消且执行体已经退出，不承诺回滚远端已经产生的部分数据；`Interrupted` 明确表示最终远端结果未知。

### 2.11 Interrupted 的用户决策与放弃语义

- `Interrupted` 表示本地执行已经结束，但远端最终结果未知。它不是普通失败，也不是用户已经取消完成的任务；Runtime 不得自动重试、自动删除或把它伪装成 `Canceled`。
- `Interrupted` 必须保留在活动队列中等待用户显式决策。队列 Action 列同时提供“重试”和“放弃”两个入口：重试执行 `Interrupted -> Pending`，从任务起点重新执行；放弃执行 `Interrupted -> Abandoned`，终止本地任务生命周期。
- `Abandoned` 是用户明确表示“不再重试”的持久化终态。它从活动队列移入历史，用于保留可审计事实；不得复用 `Canceled`、`Failed` 或直接物理删除任务记录来表达放弃。为保持既有持久化枚举兼容性，新状态只能追加到 `TransferStatus` 数值末尾。
- 放弃只改变 AtomBox 本地任务状态，不调用 Provider，不删除、回滚或覆盖远端对象，也不承诺远端没有产生完整或部分数据。Desktop 必须在执行前显示确认提示，明确说明“远端最终结果仍然未知”和“本操作不会处理远端数据”。
- `RetryAsync` 与 `AbandonAsync` 必须使用任务 ID、预期 Revision 和预期 `Interrupted` 状态执行条件更新。两者并发时最多一个成功，失败方返回冲突，不得覆盖已经完成的另一项决策。
- 状态查询必须保持单一归属：活动队列包含 `Pending`、`Running`、`Canceling`、`Paused`、`Interrupted`；历史包含 `Succeeded`、`Failed`、`Canceled`、`Abandoned`。同一 `Interrupted` 任务不得同时出现在队列和历史。
- Application 暴露独立的放弃用例，Transfer Runtime 拥有状态转换和 City.State 发布，Infrastructure 只实现条件持久化，Desktop 只负责双操作展示、确认和反馈；任何 UI 都不得直接调用 Store 删除任务。
- 自动化测试至少覆盖放弃成功、非 `Interrupted` 状态拒绝、重试与放弃并发互斥、State 从队列移除、历史可见以及放弃过程不调用 Provider。

### 2.12 进度异步汇聚、持久化与 State 发布

- Provider 继续只接收标准 `IProgress<TransferProgress>`。`Report` 路径必须是线程安全、O(1)、不等待磁盘的内存更新；禁止在 SDK 回调中使用 `GetAwaiter().GetResult()` 同步等待异步 Store。
- Transfer 为每个活跃执行维护最新进度、执行 Revision/序号和 ExecutionLease，并由统一异步 flush 循环合并高频回调。旧中间值允许被覆盖，最新值不能丢失；迟到执行或已经 Abandoned 的回调不得继续落盘。
- 同一次执行的字节进度保持单调，分片并发回调必须经汇聚器串行化速度计算和最新快照选择。
- flush 信号只负责提示“存在脏进度”，不保存全部事件。Runtime 按统一节流窗口收集所有已变化任务，并通过批量端口一次性持久化，避免每个 Worker 分别重写完整 JSON。
- 普通进度持久化成功后才原子更新 City.State；City.State 继续是持久化事实的瞬时投影，不提前发布未经落盘的数据。
- 执行结束时关闭普通进度入口并强制 flush 最新快照；固定提交顺序是“最终进度持久化成功，再使用 Revision/预期状态 CAS 提交终态，最后发布 City.State”，避免终态成功而历史进度仍旧。
- 当前混合状态与进度的 `UpdateStatusAsync(task, progress)` 必须拆分为批量进度更新、条件状态转换和最终执行提交等明确端口。中间进度不得反复保存整个 `TransferTask` 或无意义地推进任务 Revision。
- 中间进度写入失败时保留内存最新值、记录诊断并退避重试，不立即中断网络传输，也不发布 State；连续失败使 Runtime 进入降级并停止新任务准入。最终进度或终态提交失败不得宣称成功，原始错误必须保留，遗留 `Running` 下次启动按 `Interrupted` 恢复。
- Core 负责定义无 City 依赖的进度批量、条件转换和最终提交端口/DTO；Transfer 负责汇聚、节流、ExecutionLease、flush 时序及 State 投影；Infrastructure 负责完整 JSON 读改写锁、批量 upsert、原子文件替换和真实持久化错误。Providers 与 City 均不因本项修改。

### 2.13 City Module 接入与组合边界

- `AtomBox.Transfer` 新增直接的 `AtomUI.City.Core` 项目引用及 `AtomBoxTransferModule`；已有 City.State 引用继续保留。Runtime、队列和 Worker 仍由 AtomBox 实现，Module 只负责生命周期接线。
- `AtomBoxTransferModule.ConfigureServices` 调用现有 `AddAtomBoxTransfer()`。该标准 DI 扩展继续保留供测试组合使用；Desktop 删除重复的 `AddAtomBoxTransfer()` 调用。
- Module 在 `OnPostApplicationInitializationAsync` 启动 Runtime。此时所有 Module 的普通初始化已经结束，当前 Desktop 组合根负责的 Infrastructure 初始化和 Application State 初始化均已完成。
- Module 把 `ApplicationScope.CancellationToken` 作为 Runtime 运行期 owner 令牌，把 City 传给 Module 回调的唯一 Effective Token 原样作为本次 `StartAsync` 的取消令牌；Runtime 内核只接收标准 CancellationToken，不持有 City `LifecycleScope` 对象。这里单独传 owner 令牌属于 City 取消合同明确保留的长生命周期高级适配器场景：初始化回调结束后 City 会释放内部 linked CTS，因此不得把回调 Effective Token 保存为 Runtime 的永久生命令牌。
- Module 在 `OnApplicationShutdownAsync` 调用 Runtime `StopAsync`，并把 City shutdown token 作为收尾期限。ApplicationScope token 已先广播取消，shutdown token 只约束有限等待和最终 flush。
- `AtomBoxDesktopModule` 增加对 `AtomBoxTransferModule` 的显式 `DependsOn`，组合根同时显式 `UseModule<AtomBoxTransferModule>()`。City 当前不会自动注册缺失的依赖模块，只有显式注册后模块图才可验证。
- Desktop 删除 `TransferRuntimeInitializer` 的手工调用，不再拥有 Transfer 的注册、启动、停止或恢复；Desktop 继续作为组合根提供 Infrastructure、Providers 等 Core 端口的具体实现。
- Providers 和 Infrastructure 继续不创建 City Module。Transfer Module 依赖 `ITransferTaskStore`、`ITransferStateStore`、`IStorageAccountRepository`、`IStorageProviderFactory` 等 Core 端口，由最终 Host 组合完整实现。
- Runtime 启动失败必须使 City Host 启动失败并触发回滚；Runtime 关闭失败在完成其他尽力清理后向 City 传播，由 Host 聚合。禁止把结构化错误压扁成只含 message、无 inner cause 的普通异常。
- `TransferRuntimeInitializer` 的恢复职责并入 `TransferRuntimeController.StartAsync` 后删除；同一 Host 内 Runtime 只启动一次，停止后不可重启。

### 2.14 任务故障、Runtime 健康状态与异常传播

- 必须严格区分单个传输任务故障与整个 Runtime 故障。Provider、网络、文件或单任务业务失败只影响对应任务，任务保存为 `Failed` 后 Runtime 继续调度其他任务；不能因为一个任务失败就停止整个传输系统。
- Runtime 健康采用 `Healthy / Degraded / Faulted` 三态，并与 `Created / Starting / Running / Stopping / Stopped / Faulted` 生命周期状态分开表达。生命周期回答“Runtime 正处于哪个阶段”，健康状态回答“当前阶段是否仍能可靠工作”。
- Store、进度写入或队列读取发生可恢复的暂时故障时进入 `Degraded`：停止新任务准入、保留最后可信的队列投影、对后台操作退避重试；恢复成功后回到 `Healthy` 并重新开放准入。不得把一次暂时读取失败投影成空队列。
- 唯一调度循环意外退出、重试策略耗尽、活跃执行所有权/Revision 不变量被破坏或其他不可恢复的基础设施故障，使 Runtime 进入 `Faulted`：永久关闭本 Host 的准入，停止继续领取任务，对活跃 Worker 请求取消并执行有限收尾。
- Runtime 必须观察自己创建的每一个后台 `Task`。禁止未受管的 fire-and-forget；后台循环异常必须由统一顶层边界捕获、记录并转换为健康状态，不能静默死亡或成为未观察异常。
- Provider 原始异常不能直接写入任务 JSON 或公开 City.State。任务持久化和 UI 只使用 Core `StorageError` 及脱敏字段；完整异常链交给开发者诊断链路。
- Runtime 启动失败使用保留结构化错误和原始 `InnerException` 的专用异常向 `AtomBoxTransferModule` 传播，由 City 终止并回滚 Host 启动。关闭阶段在完成其他尽力清理后聚合异常向 City 传播，不能因第一个异常跳过其余清理。
- Transfer 定义薄的 Runtime 诊断端口，Module 将其适配到 City `IHostDiagnostics`。诊断端口保留完整技术信息，公开健康 State 只包含脱敏摘要。City 仍不负责磁盘日志；是否接入文件、遥测或其他持久化 Sink 由最终 AtomBox 应用配置决定。
- 队列事实投影与 Runtime 健康投影必须分离。`IAtomBoxTransferState` 分别公开只读 Queue 与 Runtime 快照；健康快照至少包含生命周期、健康等级、是否接受提交、活跃/等待数量、最后一次脱敏故障和更新时间。
- 已经获得过成功队列快照后再发生刷新故障时，必须保留最后可信任务列表并标记 `IsStale`、失败摘要和最后成功时间；只有首次初始化从未成功时才允许呈现“当前无可用快照”，不能把故障错误表达为“队列为空”。

### 2.15 自动化测试与真实 UI 验收门禁

- 旧 `Phase 12 Transfer 测试矩阵` 中基于 7 状态、同步式 `WakeAsync` 和 `TransferRuntimeInitializer` 的合同被本次 Runtime 设计取代。新的权威测试基线见 `docs/implementation/testing/phase-12-transfer-runtime-testing.md`。
- Runtime 单元测试必须确定性覆盖并发启停、幂等、同 Host 不可重启、信号合并且不丢持久化任务、并发上限、CAS 唯一领取、任务故障隔离、健康降级与恢复、致命故障、取消来源、迟到结果围栏、非阻塞进度和有限关闭。
- 并发测试使用 `TaskCompletionSource`、可控 Provider、可控 Store 与可控时间源建立明确同步点；禁止依靠长时间 `Task.Delay` 或时序碰运气形成易抖动测试。
- Core/Infrastructure 合同测试必须覆盖旧枚举值稳定、`Canceling=7`、Revision 单调性、预期 Revision/状态 CAS、并发唯一领取、批量进度无覆盖、JSON 原子替换和重启恢复。
- City Host 集成测试必须覆盖 Module 显式注册与顺序、Post 初始化启动、健康状态、启动失败回滚、ApplicationScope 取消后的有限关闭、关闭异常聚合，以及 Desktop 不再拥有旧初始化器。
- 无 UI 端到端测试使用临时真实 JSON Store 和可控 Fake Provider 走通“Application 提交 -> Pending 持久化 -> Runtime 领取 -> Provider -> 进度 -> 终态 -> City.State”全链路，并覆盖退出、重启、失败、取消和迟到返回。
- 真实 Provider 测试继续采用 opt-in 凭据。日常 CI 不要求外部密钥；发布或重大迁移验收至少选择一个真实 Provider 完成上传、下载、取消和任务故障隔离。
- 真实 AtomBox UI 烟测由人工在编译/发布产物上执行，至少覆盖页面切换后继续传输、托盘存续、运行中取消、单任务失败不拖垮 Runtime、退出重启后 `Interrupted`、手动从头重试和正常有限退出。
- 自动化门禁全部通过后只能声明“代码重构完成、具备人工验收条件”；只有真实 UI 烟测完成并记录日期、提交号、构建产物和 Provider 后，才能声明“Transfer Runtime 迁移正式验收完成”。

## 3. 尚待逐项确认

无。已讨论的 Runtime 设计项全部确认，生产代码与默认自动化门禁已于 2026-10-16 完成。

尚未完成的只有需要外部环境和人工交互的验收：至少一个批准范围内的真实 Provider 验证，以及真实 AtomBox 编译/发布产物 UI 烟测。在这两项留下报告前，不宣称 Transfer Runtime 正式验收完成。
