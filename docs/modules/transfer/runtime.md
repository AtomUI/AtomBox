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
- 维护内部队列。
- 创建执行批次。
- 创建和释放 worker。
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

第一版运行时对象：

| 对象 | 生命周期 | 职责 |
| --- | --- | --- |
| `TransferManager` | 应用级运行时服务 | 管理调度器启动停止、提交任务、停止 worker、协调状态刷新。 |
| `TransferQueue` | Transfer 内部状态 | 保存待执行任务和运行中任务的内部队列视图。 |
| `TransferScheduler` | 应用级或 manager 内部服务 | 按并发、优先级、状态选择任务执行。 |
| `TransferWorker` | 执行批次生命周期 | 执行一个可合并任务批次。 |
| `TransferProgressReporter` | 执行期对象 | 汇报单任务或批次进度快照。 |
| `TransferSpeedMeter` | 执行期对象 | 计算速度快照。 |

这些对象都不能被 ViewModel 直接持有。`TransferQueue`、`TransferManager`、`TransferWorker` 都不能进入 Application 结果对象。

## 3. 调度入口

Core 端口 `ITransferTaskScheduler` 是 Transfer 对外暴露的调度入口。

第一版语义：

- 提交任务。
- 取消任务。
- 重试任务。
- 唤醒调度器处理待执行任务。

`ITransferTaskScheduler` 不能返回 `TransferQueue`、`TransferManager`、worker 或可变运行时对象。

Application 可以调用 `ITransferTaskScheduler`，但只能表达用户用例，例如创建任务后提交、取消、重试。Application 不能指挥 worker 如何执行，也不能决定 provider 生命周期。

## 4. 执行批次

`TransferWorker` 执行的是批次，不等于用户点击一次，也不等于单个文件。

一个执行批次可以包含：

- 一个任务。
- 同账号、同方向、同目标、同策略的一组可合并任务。

批次是 provider 生命周期的主要边界。批次开始时，Transfer 通过 Core 端口读取账号并创建 provider；批次结束时释放 provider 或 provider session。

第一版不跨用户提交批次共享 provider 实例。未来如果要跨批次复用，必须先设计明确的 `ProviderSessionPool` 或 `ProviderClientPool`。

## 5. Provider 创建边界

Transfer 执行任务时使用：

```text
IStorageAccountRepository
IStorageProviderFactory
IStorageProvider
```

正确流程：

```text
TransferWorker
  -> IStorageAccountRepository.Get(StorageAccountId)
  -> IStorageProviderFactory.Create(StorageAccount)
  -> IStorageProvider 执行上传或下载
```

禁止流程：

```text
TransferWorker
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

第一版不把 `ITransferStatePublisher` 作为 Core 公共端口暴露。

Transfer 内部可以有事件、回调或队列机制用于 worker 向 manager 汇报状态，但这只是 Transfer 内部实现细节，不能泄漏给 Application、Presentation 或 ViewModel。

正式状态路径：

```text
Transfer Runtime
  -> 更新 ITransferTaskStore / ITransferStateStore 对应实现
  -> Application 查询用例
  -> TransferQueueSnapshot / TransferHistoryPage
  -> Presentation 展示
```

## 7. 启动与关闭

启动时：

- 初始化 Transfer Runtime。
- 读取未完成任务或队列状态，供 Application 后续查询。
- 第一版不自动恢复执行历史未完成任务。

关闭时：

- 停止接受新任务。
- 请求 worker 停止。
- 有限超时等待 worker。
- 未完成任务保存为 `Paused` 或 `Interrupted`。
- flush 关键传输状态。

Transfer 停止不能无限等待。无法及时停止的 worker，应把任务标记为 `Interrupted`，然后允许应用继续退出。

## 8. Phase 9 收口规则

Phase 9 不改变 Transfer 的职责边界，但要求传输结果具备可解释性。

状态规则：

- 失败任务必须保存失败原因摘要和错误类别。
- 用户取消任务必须与网络失败、权限失败、provider 错误区分。
- 应用关闭或 worker 超时导致的未确认结果必须保存为 `Interrupted` 或等价状态。
- `Interrupted` 不能自动恢复执行；后续只能由用户通过 Application 重试用例显式恢复。

重试规则：

- Transfer 只接收重试调度请求，不由 UI 直接操作内部队列。
- 可重试条件由任务状态和错误类别共同决定，再通过 Application 快照暴露给 UI。
- 第一版重试不承诺断点续传；重试可以从任务起点重新执行。

展示规则：

- Transfer 不生成 UI 文案。
- Transfer 可以保存结构化错误类别、脱敏技术码、最后更新时间和可重试标志所需状态。
- Presentation 通过 Application 查询结果展示失败详情、重试按钮和中断原因。

## 9. 禁止事项

- 不引用 Application。
- 不引用 Desktop、Avalonia、AtomUI、ViewModel。
- 不引用具体 provider 实现。
- 不引用具体云 SDK、FTP/SFTP client、网盘 API client。
- 不读取配置文件。
- 不访问数据库具体实现。
- 不保存 secret material。
- 不暴露 `TransferQueue`、`TransferManager`、worker 给 Application 或 Presentation。
- 不把内部事件发布机制做成 UI 可直接订阅的公共端口。
