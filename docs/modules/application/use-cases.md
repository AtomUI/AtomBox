# AtomBox.Application 用例设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-15
>
> 冻结范围：当前文档定义的 Application 第一版用户用例清单、服务边界和 Phase 9 收口用例约束。
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的用例清单或服务边界，必须先说明原因，再同步更新相关 Application、Core、Presentation 文档。

本文档定义 `AtomBox.Application` 第一版用户用例。Application 只编排用户流程，不实现 UI、不实现 Provider、不实现 Transfer worker、不实现 Infrastructure 持久化。

## 1. 设计目标

Application 的第一版目标是把 Desktop 的用户动作收敛为稳定用例服务：

- 账号管理。
- 远程浏览。
- 传输任务创建和传输状态查询。
- 应用设置读取和保存。

Application 不能退化成 Provider、Transfer、Infrastructure 的机械转发层。它必须表达用户流程、输入校验、能力判断和结果组织。

## 2. 服务划分

第一版 Application 服务如下：

| 服务 | 职责 |
| --- | --- |
| `AccountAppService` | 账号新增、编辑、删除、列表、连接测试。 |
| `RemoteBrowserAppService` | 远程存储入口解析、目录列表、远程资源删除、远程路径刷新。 |
| `TransferAppService` | 创建上传/下载任务、取消/重试任务、查询传输队列和历史。 |
| `SettingsAppService` | 读取应用设置、保存应用设置、恢复默认设置。 |

这些服务是应用级无状态服务，可以长期存在，但不能持有 provider、SDK client、secret material、ViewModel 或 UI 状态。

## 3. 账号用例

`AccountAppService` 第一版用例：

| 用例 | 输入 | 输出 | 说明 |
| --- | --- | --- | --- |
| 添加账号 | `AddStorageAccountRequest` | `OperationResult<StorageAccountSummary>` | 保存账号配置和凭据引用；不保存 secret 明文。 |
| 更新账号 | `UpdateStorageAccountRequest` | `OperationResult<StorageAccountSummary>` | 修改账号配置；不影响已运行传输任务。 |
| 删除账号 | `DeleteStorageAccountRequest` | `OperationResult` | 删除前必须检查未完成传输任务引用。 |
| 列出账号 | `ListStorageAccountsRequest` | `OperationResult<IReadOnlyList<StorageAccountSummary>>` | 给左侧菜单、账号管理页和远程入口使用。 |
| 测试连接 | `TestConnectionRequest` | `OperationResult<TestConnectionResult>` | 可以创建短生命周期 provider；不得长期持有。 |

账号删除必须遵守生命周期文档：只要仍有未完成任务引用该账号，就禁止删除。

## 4. 远程浏览用例

`RemoteBrowserAppService` 第一版用例：

| 用例 | 输入 | 输出 | 说明 |
| --- | --- | --- | --- |
| 解析资源入口 | `ResolveRemoteEntryRequest` | `OperationResult<RemoteEntryResult>` | 处理资源类型节点进入右侧页面时的账号选择、空状态或上次账号。 |
| 列出远程资源 | `ListRemoteItemsRequest` | `OperationResult<ListRemoteItemsResult>` | 返回 `RemoteItem` 快照列表。 |
| 预检远程文件预览 | `PreviewRemoteFileRequest` | `OperationResult<PreviewRemoteFileResult>` | 图片返回元数据，小文本完成读取和解码；不创建下载任务。 |
| 打开远程图片预览流 | `PreviewRemoteFileRequest` | `OperationResult<RemotePreviewStreamResult>` | ImagePreviewer 按需调用，返回受大小限制的图片 Stream。 |
| 删除远程资源 | `DeleteRemoteItemRequest` | `OperationResult` | 删除文件或对象；文件夹第一版只打开不删除。 |
| 获取路径上下文 | `GetRemotePathContextRequest` | `OperationResult<RemotePathContextResult>` | 组织当前路径、是否可上传、是否 bucket 列表等页面所需状态。 |

远程浏览属于短用例。Application 可以创建短生命周期 provider 调用目录列表、删除、能力探测，但不能缓存 provider。

远程预览用例规则：

- 只允许 `RemoteItemKind.File` 进入预览。
- 只支持 Core 预览模型定义的图片和小文本格式。
- 通过文件名扩展名判断预览类型和 content type。
- 根据请求中的 `Size` 做读取前大小预检；超限时不得创建 provider。
- 图片预检只读取账号和请求元数据，不创建 provider、不提前下载图片。
- `OpenRemoteImagePreviewStreamAsync` 由 ImagePreviewer 的 Stream 工厂按需调用。它创建短生命周期 provider，复用 `IStorageProvider.DownloadAsync(RemotePath, Stream, ...)` 写入受限 `MemoryStream`。
- 图片流读取完成后再次校验实际字节数并把 Position 重置为 `0`；返回 Stream 由调用方负责释放。
- 文本预览继续创建短生命周期 provider，将内容写入受限 `MemoryStream` 后完成编码识别。
- 图片和文本都必须在读取完成后校验实际字节数，避免远程 size 缺失或不准确导致大文件进入内存。
- 文本预览只支持 UTF-8、UTF-8 BOM、UTF-16 LE、UTF-16 BE；无法识别编码或疑似二进制内容时返回失败。
- 预览不是 Transfer 任务，不进入传输队列，不写传输历史。
- Application 不返回 Avalonia、AtomUI、Bitmap 或任何 UI 类型。

## 5. 传输用例

`TransferAppService` 第一版用例：

| 用例 | 输入 | 输出 | 说明 |
| --- | --- | --- | --- |
| 上传前准备 | `PrepareBatchUploadTasksRequest` | `OperationResult<PrepareBatchUploadTasksResult>` | 可按设置计算本地文件指纹，并查询当前账号历史上传记录；不创建传输任务。 |
| 创建上传任务 | `CreateUploadTasksRequest` | `OperationResult<CreateTransferTasksResult>` | 只创建任务描述，不创建 provider。 |
| 创建下载任务 | `CreateDownloadTasksRequest` | `OperationResult<CreateTransferTasksResult>` | 只创建任务描述，不创建 provider。 |
| 查询传输队列 | `GetTransferQueueRequest` | `OperationResult<TransferQueueSnapshot>` | 返回 UI 可展示的不可变队列快照。 |
| 查询传输历史 | `GetTransferHistoryRequest` | `OperationResult<TransferHistoryPage>` | 支持上一页/下一页。 |
| 取消任务 | `CancelTransferTaskRequest` | `OperationResult` | 调用 Core 传输调度端口或状态端口。 |
| 重试任务 | `RetryTransferTaskRequest` | `OperationResult` | 只触发任务状态流转，不直接执行 worker 细节。 |

传输用例必须严格遵守：Application 只创建和操作传输任务，不直接执行传输，不创建传输 provider，不接触 Transfer worker。

上传前准备属于正式传输任务创建前的短用例。第一版规则：

- 仅在应用设置开启上传指纹索引时计算 `sha256`。
- 查询范围限定到当前 `storageAccountId`。
- 命中结果只返回历史上传记录，由 Presentation 决定是否弹窗确认。
- 用户取消命中提示时，不调用创建上传任务用例。
- 用户选择再次上传时，创建带指纹元数据的上传任务。
- 上传前准备不进入 Transfer worker，不新增传输状态，不写传输历史。

Application 查询传输队列或历史时，只能通过 Core 传输状态存储/查询端口获取不可变快照；不能拿到 `TransferQueue`、`TransferManager`、worker 或任何可变运行时对象。

## 6. 设置用例

`SettingsAppService` 第一版用例：

| 用例 | 输入 | 输出 | 说明 |
| --- | --- | --- | --- |
| 读取设置 | `GetApplicationSettingsRequest` | `OperationResult<ApplicationSettingsResult>` | 返回设置快照。 |
| 保存设置 | `UpdateApplicationSettingsRequest` | `OperationResult<ApplicationSettingsResult>` | 显式保存用户设置。 |
| 恢复默认设置 | `ResetApplicationSettingsRequest` | `OperationResult<ApplicationSettingsResult>` | 需要确认弹窗由 Presentation 处理。 |
| 读取上传指纹索引统计 | `GetUploadFingerprintIndexStatisticsRequest` | `OperationResult<UploadFingerprintIndexStatisticsResult>` | 返回索引文件路径、记录数量和最近更新时间。 |
| 清空上传指纹索引 | `ClearUploadFingerprintIndexRequest` | `OperationResult` | 清空全部索引；当前 Desktop 暂不暴露入口，后续如暴露则确认弹窗由 Presentation 处理。 |

设置保存必须通过 Application 用例显式触发，不能由 ViewModel 或 Infrastructure 私自决定业务流程。

上传指纹索引设置规则：

- `ApplicationSettings` 保存是否启用上传指纹索引。
- Settings 用例只读取或保存设置，不直接参与上传流程。
- 索引统计和清空通过 Core 端口完成，Application 不关心 JSON 文件格式。
- 第一版只支持清空全部索引，不提供按账号清空。
- 当前 Desktop 只暴露“指纹索引”开关，暂不展示索引统计和清空入口。

## 7. Phase 9 收口用例约束

Phase 9 允许 Application 结果对象补充 UI 可稳定展示的只读信息，但不改变 Application 的层职责。

账号和连接测试收口：

- `TestConnectionResult` 应能表达 provider、目标 endpoint 或 root 摘要、成功状态、失败错误类别、用户可读原因和可选诊断标识。
- 账号配置校验失败应通过 `OperationResult` 返回结构化错误，不由账号弹窗自行拼接业务规则。
- 删除账号失败时，Application 必须说明是否因为未完成传输任务引用导致阻断。

远程浏览收口：

- `RemotePathContextResult` 应表达当前路径、是否可上传、是否可删除当前选中项、是否处于 bucket/root 视图、是否存在下一页或上一页等 UI 能力标志。
- `ListRemoteItemsResult` 应携带分页状态、当前路径摘要和可用于重试的请求上下文快照。
- 删除远程资源的 Application 用例只执行删除，不负责弹确认；确认属于 Presentation。

传输收口：

- `TransferQueueSnapshot` 和 `TransferHistoryPage` 应能展示失败原因、取消原因或中断原因。
- 可重试状态必须由 Application 结果明确表达，不能由 UI 根据字符串猜测。
- 查询队列和历史仍然只能返回不可变快照，不能泄漏 Transfer Runtime 内部对象。

错误展示收口：

- Application 可以把 Core 错误组织成 Presentation 可消费的错误摘要和错误详情快照。
- 错误详情快照不得包含 secret material、SDK DTO、SDK exception、provider session 或本地绝对凭据路径。
- Application 不返回“应该弹窗”的指令；ViewModel 根据用例结果决定调用 `IDialogService` 或 `IMessageService`。

## 8. 禁止事项

- Application 不引用 Avalonia、AtomUI、ViewModel、View。
- Application 不引用具体 SDK、FTP/SFTP 客户端、网盘 HTTP API 客户端。
- Application 不直接读取配置文件、不访问数据库、不加密凭据。
- Application 不持有 provider、SDK client、secret material。
- Application 不创建 Transfer worker，不实现队列调度。
- Application 不定义跨模块稳定端口；跨模块端口统一在 Core。
- Application 不接触具体 SDK/API/协议异常。
- Application 第一版不定义 `ApplicationResult<T>`；所有用例直接返回 Core 的 `OperationResult<T>`。
