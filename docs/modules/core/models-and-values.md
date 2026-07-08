# AtomBox.Core 模型和值对象设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的 Core 第一版模型、值对象、字段边界和纯领域规则
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的模型或值对象边界，必须先说明原因，再同步更新相关 Core、Application、Transfer、Providers、Infrastructure 文档。

本文档定义 `AtomBox.Core` 第一版模型和值对象。Core 模型是 AtomBox 的产品语言，不是 UI DTO、SDK DTO、数据库实体或配置文件结构。

## 1. 基本原则

- Core 模型必须表达 AtomBox 内部稳定业务事实或业务快照。
- Core 值对象必须不可变，并在创建时完成必要的基本校验。
- Core 模型字段只能使用 Core 类型或 .NET BCL 类型。
- Core 模型可以包含纯领域规则，但不能访问外部资源。
- Core 模型不能因为某个 UI 控件、某个 SDK 字段或某张数据库表方便，就被迫长出无关字段。

禁止把 Core 写成“大家都能用的公共包”。这是最容易腐烂的架构错误。

## 2. 账号模型

`StorageAccountId` 是账号稳定标识。

- 类型语义：不可变值对象。
- 生命周期：账号创建后长期稳定。
- 禁止行为：账号重命名、endpoint 修改、region 修改、凭据轮换时改变 ID。
- 推荐底层形态：`Guid`、ULID 字符串或等价稳定 ID；不要使用账号名称作为 ID。

`StorageAccount` 描述一个远程存储账号的配置事实。

第一版建议字段：

| 字段 | 含义 |
| --- | --- |
| `Id` | `StorageAccountId`，稳定账号标识。 |
| `ProviderCategory` | `StorageProviderCategory`，provider 大类。 |
| `ProviderId` | `StorageProviderId`，具体 provider 标识。 |
| `DisplayName` | 用户可见名称，只是展示名，不参与身份判断。 |
| `Endpoint` | 可选 endpoint；不是所有 provider 都需要。 |
| `Region` | 可选 region；不是所有 provider 都需要。 |
| `CredentialRef` | 凭据引用，不是凭据明文。 |
| `CreatedAt` | 创建时间。 |
| `UpdatedAt` | 最近更新时间。 |

第一版不设计 `Enabled` / `Disabled` 状态。账号存在即表示可被用户选择；连接是否可用由连接测试或具体操作结果表达。

`StorageProviderCategory` 表示 provider 大类，不表示具体 SDK 类型。

第一版建议类型值：

```text
ObjectStorage
FileTransfer
NetDisk
```

具体厂商、协议或网盘类型不要硬塞进这个 enum。阿里云 OSS、腾讯 COS、SFTP、阿里云盘等具体 provider 必须用 `StorageProviderId` 表达。

`StorageProviderId` 是具体 provider 标识。

示例：

```text
aliyun-oss
tencent-cos
qiniu-kodo
minio
ftp
sftp
aliyun-drive
baidu-netdisk
```

`StorageProviderId` 不是 SDK 类型名，也不是 UI 展示名。它用于让 `IStorageProviderRegistry` 查找 provider 描述，让 `IStorageProviderFactory` 创建正确的 provider 实例。

## 3. 凭据值对象

`CredentialRef` 是凭据引用，不是凭据明文。

- 可以进入 `StorageAccount`、`TransferTask`、运行上下文和脱敏诊断信息。
- 不能被当成 secret material。
- 不能包含 AccessKey、password、private key、refresh token、access token 等真实认证材料。

`CredentialLease` 是运行时占用声明，不是持久化业务模型。

- Core 只能定义 lease 句柄或端口契约。
- lease 计数、pending-delete、物理清理、并发锁由 Infrastructure 实现。
- `CredentialLease` 不能进入 `TransferTask` 持久化字段。

## 4. 远程路径

`RemotePath` 是远程路径值对象，不能长期用裸 `string` 替代。

第一版建议表达：

| 字段 | 含义 |
| --- | --- |
| `Value` | 规范化后的路径文本。 |
| `Kind` | 路径语义，例如 bucket 根、目录、对象路径等。 |
| `Separator` | provider 语义下的路径分隔符，默认可为 `/`。 |

`RemotePath` 至少应支持这些纯规则：

- 判断是否根路径。
- 组合子路径。
- 取父路径。
- 取名称部分。
- 规范化重复分隔符。

`RemotePath` 不能做：

- 访问本地文件系统。
- 调用 provider 查询路径是否存在。
- 读取配置。
- 依赖某个 SDK 的路径类型。

`LocalPath` 是本地路径值对象。

Core 允许定义 `LocalPath`，但不能使用 `FileInfo`、`DirectoryInfo`、文件流或任何本地文件系统访问 API 作为 Core 模型字段。`LocalPath` 只表达路径文本语义，是否存在、是否可读、大小多少，必须由外层模块在实际操作时判断。

## 5. 远程资源快照

`RemoteItem` 是远程资源在某一时刻的不可变快照。

第一版建议字段：

| 字段 | 含义 |
| --- | --- |
| `Name` | 资源名称。 |
| `Path` | `RemotePath`。 |
| `Kind` | `RemoteItemKind`。 |
| `Size` | 字节大小；文件夹或未知大小允许为空。 |
| `UpdatedAt` | 远程更新时间；未知时允许为空。 |
| `ETag` | 可选远程版本标识。 |
| `ContentType` | 可选内容类型。 |

`RemoteItemKind` 第一版建议：

```text
File
Folder
Bucket
Unknown
```

不要为了某个 provider 的特殊对象类型过早扩 enum。特殊差异优先留在 provider 内部，除非已经成为跨 OSS、FTP/SFTP、网盘都稳定需要表达的产品语言。

`RemoteItem` 不是活动代理对象。删除、重命名、覆盖后，旧快照不会自动变化，必须重新列表或重新查询。

## 5.1 远程预览模型

远程预览模型用于表达“小文件直接查看”的跨层语义。它不是传输任务，也不是 UI 控件模型。

第一版预览只支持图片和小文本：

```text
Text
Image
```

`RemotePreviewKind` 表示预览类型。

`RemotePreviewOptions` 表示预览大小限制：

| 字段 | 含义 |
| --- | --- |
| `MaxTextBytes` | 文本预览最大读取字节数。 |
| `MaxImageBytes` | 图片预览最大读取字节数。 |

默认限制由 Core 提供稳定常量：文本 `1 MB`，图片 `10 MB`。

`PreviewRemoteFileRequest` 表示一次预览请求：

| 字段 | 含义 |
| --- | --- |
| `StorageAccountId` | 远程账号。 |
| `Path` | 远程文件路径。 |
| `FileName` | 文件名，用于扩展名判断、标题展示和 content type 推断。 |
| `Size` | 列表项中的文件大小，允许为空，用于读取前预检。 |
| `Kind` | 远程资源类型，只允许 `File` 进入预览。 |

`PreviewRemoteFileResult` 表示一次成功预览结果：

| 字段 | 含义 |
| --- | --- |
| `Kind` | 文本或图片。 |
| `FileName` | 文件名。 |
| `ContentType` | 根据扩展名推断的 MIME 类型。 |
| `Size` | 实际读取到的字节数。 |
| `Content` | 原始字节。 |
| `Text` | 文本预览解码结果；图片预览为空。 |
| `EncodingName` | 文本编码名称；图片预览为空。 |

不支持预览、文件超限、编码不支持、疑似二进制内容等情况通过 `OperationResult<PreviewRemoteFileResult>.Failure(...)` 返回。失败状态不写入 `PreviewRemoteFileResult`。

Core 不能引用 Avalonia、AtomUI、Bitmap、Image 或任何 UI 类型。

## 5.2 文件指纹索引模型

文件指纹索引用于表达“本机曾经向某个存储账号上传过某个文件”的业务事实。它不是远端 provider 能力，也不是对象存储协议的一部分。

第一版主指纹算法为 `sha256`，主匹配条件为：

```text
hashAlgorithm + hashValue + fileSize + storageAccountId
```

`FileFingerprintQuery` 表示一次账号范围内的历史上传查询：

| 字段 | 含义 |
| --- | --- |
| `HashAlgorithm` | 指纹算法，第一版为 `sha256`。 |
| `HashValue` | 指纹值。 |
| `FileSize` | 计算指纹时读取到的本地文件大小。 |
| `StorageAccountId` | 查询范围限定到当前存储账号。 |

`FileFingerprintRecord` 表示一条历史上传记录：

| 字段 | 含义 |
| --- | --- |
| `HashAlgorithm` | 指纹算法。 |
| `HashValue` | 指纹值。 |
| `FileSize` | 本地文件大小。 |
| `StorageAccountId` | 上传目标账号。 |
| `ProviderId` | 上传目标 Provider。 |
| `RemotePath` | 上传成功后的远端路径。 |
| `ETag` | provider 返回的可选版本标识，只作参考。 |
| `UploadedAt` | 上传成功时间。 |
| `LastSeenAt` | 最近一次查询或更新命中时间。 |

`ETag` 不参与主匹配判断。分片上传、服务端加密或不同厂商实现都可能让 ETag 不等于文件内容 MD5。

## 6. 传输模型

`TransferTaskId` 是传输任务稳定标识。

`TransferTask` 是持久化业务对象。

第一版建议字段：

| 字段 | 含义 |
| --- | --- |
| `Id` | `TransferTaskId`。 |
| `StorageAccountId` | 账号引用。 |
| `Direction` | `TransferDirection`。 |
| `LocalPath` | 本地路径，使用 `LocalPath`。 |
| `RemotePath` | 远程路径，使用 `RemotePath`。 |
| `Status` | `TransferStatus`。 |
| `Options` | `TransferOptions`。 |
| `CreatedAt` | 创建时间。 |
| `UpdatedAt` | 最近更新时间。 |
| `FingerprintHashAlgorithm` | 可选，上传前计算出的指纹算法。 |
| `FingerprintHashValue` | 可选，上传前计算出的指纹值。 |
| `FingerprintFileSize` | 可选，计算指纹时读取到的本地文件大小。 |
| `FingerprintCalculatedAt` | 可选，指纹计算完成时间。 |

`TransferTask` 禁止保存：

- provider 实例。
- SDK client。
- secret material。
- 完整账号配置快照。
- UI 选中状态。
- ViewModel 或命令对象。

`TransferDirection` 第一版建议：

```text
Upload
Download
```

`TransferDirection` 只表示传输方向，不用来反向解释 `SourcePath` / `TargetPath` 这种含糊字段。上传时 `LocalPath` 是来源、`RemotePath` 是目标；下载时 `RemotePath` 是来源、`LocalPath` 是目标。字段语义必须固定，方向只决定数据流向。

`TransferTask` 中的指纹字段是可选上传元数据，只用于上传成功后写入本地文件指纹索引。下载任务、设置关闭时创建的上传任务、用户取消历史命中提示时，都可以不携带这些字段。

`TransferStatus` 第一版建议：

```text
Pending
Running
Paused
Interrupted
Succeeded
Failed
Canceled
```

`TransferProgress` 是进度快照，不是 worker。

第一版建议字段：

| 字段 | 含义 |
| --- | --- |
| `BytesTransferred` | 已传输字节数。 |
| `TotalBytes` | 总字节数，未知时允许为空。 |
| `Percent` | 可选百分比，可由前两个字段计算。 |
| `SpeedBytesPerSecond` | 可选速度快照。 |

## 7. 设置模型

`ApplicationSettings` 是应用设置快照。

第一版只放真正跨 Application / Infrastructure / Presentation 都需要理解的设置。不要把 UI 控件状态、窗口坐标、临时筛选条件塞进 Core 设置模型。

可以进入 Core 的设置示例：

- 默认并发数。
- 默认覆盖策略。
- 传输完成后的基础行为偏好。
- 是否启用上传指纹索引。

不应进入 Core 的设置示例：

- 某个 Avalonia 控件的展开状态。
- 某个页面的临时排序列。
- AtomUI 主题控件的内部状态。
- provider SDK 的原始配置对象。

## 8. 纯领域规则

Core 可以定义纯领域规则。

允许示例：

- `RemotePath.Combine(...)`
- `RemotePath.GetParent()`
- `LocalPath.GetFileName()`
- `TransferTask.CanRetry()`
- `TransferTask.CanCancel()`
- `StorageAccount.RequiresEndpoint()`
- `StorageCapabilitySet.Supports(...)`

禁止示例：

- `LoadSettingsFromJson(...)`
- `SaveTaskToSQLite(...)`
- `CreateAliyunOssClient(...)`
- `ShowErrorDialog(...)`
- `WriteLog(...)`
- `ResolveViewModel(...)`

判断标准很粗暴：这段代码如果需要文件、网络、数据库、UI、日志框架、DI 容器或具体 SDK，它就不该在 Core。

## 9. 命名约束

- 值对象使用明确业务名，例如 `RemotePath`、`StorageAccountId`、`CredentialRef`。
- 持久化业务对象使用业务实体名，例如 `StorageAccount`、`TransferTask`。
- 快照对象使用语义名，例如 `RemoteItem`、`TransferProgress`。
- 不使用 `BaseModel`、`CommonModel`、`DataObject`、`DtoBase` 这类空泛名字。
- 不在 Core 中创建 `Utils`、`Helpers`、`Extensions` 大杂烩目录。

Core 里的名字必须能让人一眼看出它属于 AtomBox 的产品语言。看不出来，就大概率不该放进 Core。
