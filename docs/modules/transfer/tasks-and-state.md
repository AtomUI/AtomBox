# AtomBox.Transfer 任务与状态设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的 Transfer 第一版任务语义、状态流转、路径模型和状态快照边界
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的任务或状态边界，必须先说明原因，再同步更新相关 Transfer、Core、Application、Presentation 文档。

本文档定义 Transfer 如何理解 Core 中的 `TransferTask`、状态和进度。

## 1. TransferTask 语义

`TransferTask` 是 Core 定义的持久化业务对象。

第一版任务字段语义：

| 字段 | 语义 |
| --- | --- |
| `StorageAccountId` | 远程账号引用。 |
| `LocalPath` | 本地路径值对象。 |
| `RemotePath` | 远程路径值对象。 |
| `Direction` | 上传或下载。 |
| `Options` | 覆盖策略、重试偏好等传输选项。 |
| `Status` | 任务状态。 |

不要使用 `SourcePath` / `TargetPath` 作为核心模型字段。上传时 `LocalPath` 是来源、`RemotePath` 是目标；下载时 `RemotePath` 是来源、`LocalPath` 是目标。字段语义固定，方向只决定数据流向。

## 2. 状态模型

第一版 `TransferStatus`：

```text
Pending
Running
Paused
Interrupted
Succeeded
Failed
Canceled
```

状态语义：

| 状态 | 含义 |
| --- | --- |
| `Pending` | 已创建，等待执行。 |
| `Running` | 正在执行。 |
| `Paused` | 保留的可恢复状态；第一版主要用于关闭保存或后续恢复设计，不承诺用户主动暂停能力。 |
| `Interrupted` | 非正常中断，例如退出超时、进程关闭、网络中断导致执行上下文丢失。 |
| `Succeeded` | 成功完成。 |
| `Failed` | 执行失败，可根据错误和策略决定是否可重试。 |
| `Canceled` | 用户取消。 |

第一版不自动恢复 `Paused` / `Interrupted` 任务。它们可以展示给用户，由用户手动恢复或重试。

## 3. 状态流转

第一版推荐流转：

```text
Pending -> Running -> Succeeded
Pending -> Running -> Failed
Pending -> Running -> Canceled
Pending -> Running -> Paused
Pending -> Running -> Interrupted
Failed -> Pending   // Retry
Paused -> Pending   // Manual restart / future resume
Interrupted -> Pending // Recover / manual restart
```

取消不是删除。失败不是对象消亡。历史记录和队列展示应以任务状态为依据。

## 4. 进度模型

`TransferProgress` 是进度快照，不是 worker。

第一版进度数据：

- 已传输字节数。
- 总字节数，未知时允许为空。
- 百分比，可计算或为空。
- 速度快照，可为空。

进度更新可以很频繁，但不能直接推给 ViewModel。Transfer 应先更新内部状态或状态存储，再由 Application 查询用例组织为 UI 需要的快照。

## 5. 队列快照

`TransferQueue` 是 Transfer 内部可变运行时对象。

UI 需要的是 Application 结果，例如：

```text
TransferQueueSnapshot
TransferHistoryPage
```

这些结果由 Application 通过 Core 查询端口组织。Presentation 只能消费这些结果，不能消费 Transfer 内部队列。

## 6. Store 分工

`ITransferTaskStore` 负责任务事实持久化：

- 保存任务。
- 更新任务状态。
- 读取待执行任务。
- 读取历史任务。

`ITransferStateStore` 负责状态快照查询：

- 查询队列快照所需状态。
- 查询历史分页所需状态。
- 查询运行中任务状态。

具体存储实现属于 Infrastructure。Transfer 可以调用 Core 端口，但不能引用具体数据库、文件或缓存实现。

## 7. 错误与重试

Provider SDK/API/协议异常必须先由 Providers 转换为 Core 统一错误模型。

Transfer 可以根据 Core 错误模型和 `TransferRetryPolicy` 判断是否重试，但不能判断具体 SDK 异常类型。

重试必须改变任务状态或创建明确的重试调度行为，不能在 worker 内无限循环。无限重试是后台任务系统里最蠢的灾难之一。

## 8. 删除与历史

第一版任务可以失败、取消、重试、展示历史。

删除历史记录是否物理删除任务，后续由 Application 用例决定。Transfer 不提供 UI 级“清空历史”语义。

## 9. 禁止事项

- 不在任务中保存 provider 实例。
- 不在任务中保存 SDK client。
- 不在任务中保存 secret material。
- 不在任务中保存完整账号配置快照。
- 不用 `SourcePath` / `TargetPath` 作为持久化核心字段。
- 不让 ViewModel 直接订阅进度事件。
- 不把 `TransferQueue` 暴露为 Application 或 Presentation 可消费对象。
