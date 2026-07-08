# 本地文件指纹索引

> 状态：Done
>
> 目标版本：v0.1.4
>
> 创建时间：2026-07-07
>
> 影响模块：Core / Application / Infrastructure / Desktop

## 背景

远程存储通常以各自的远端路径或对象身份作为文件定位方式，不提供通用的“传入本地文件指纹，快速判断该文件是否曾经上传到当前账号”的接口。

对象存储通常以 `bucket + object key` 作为对象身份；FTP、SFTP、WebDAV 等协议通常以远端路径作为文件身份。不同 Provider 的远端身份语义不同，因此 AtomBox 不能依赖某一种远端协议能力完成统一判断。

ETag 也不能作为通用文件 MD5 使用。分片上传、服务端加密或不同厂商实现都可能让 ETag 不等于整个文件内容的 MD5。

AtomBox 如果要支持上传前快速判断“当前存储账号是否曾经上传过相同文件”，需要维护自己的本地文件指纹索引。

## 目标

- 在本机维护文件指纹索引。
- 适用于所有支持上传的 Provider，不限于 OSS。
- 上传前可按文件指纹和当前存储账号查询历史上传记录。
- 查询命中时返回历史上传位置，供 Desktop 提示用户是否再次上传。
- 上传成功后由 Infrastructure 的传输状态存储装饰器写入或更新索引。
- 设置页提供统一开关管理是否启用上传指纹索引。
- 远程文件浏览器底部展示上传前指纹计算状态。
- 索引文件放在 AtomBox 应用数据目录。
- 第一版使用普通 JSON 文件持久化，不引入 SQLite。
- Core 定义模型和端口。
- Infrastructure 负责 JSON 文件读写。
- Application 负责上传前指纹计算、查询、确认，并创建带指纹元数据的上传任务。

## 非目标

- 不使用 SQLite。
- 不使用 JSONL。
- 不做跨设备同步。
- 不做远端索引文件。
- 不做 AtomBox 自建云同步服务。
- 不做跨 Provider、跨账号的纯内容级命中提示。
- 不承诺对象存储服务端秒传。
- 不依赖 OSS ETag 作为文件 MD5。
- 不修改 Provider 契约。
- 不让 Desktop 直接读写索引文件。
- 不修改传输队列 UI。
- 不新增 `TransferStatus`。
- 不把上传前指纹计算放入 Transfer worker。
- 不让 Transfer worker 直接依赖指纹索引端口或 JSON 实现。
- 不在第一版提供指纹计算进度条或取消按钮。
- 不允许多个上传准备会话并发运行。

## 索引文件位置

索引文件必须放在 AtomBox 应用数据目录下，由 Infrastructure 的 `AtomBoxStoragePaths` 管理。

推荐路径：

```text
{AtomBoxStoragePaths.StateDirectory}/fingerprints/file-fingerprint-index.json
```

Windows 默认路径形态：

```text
%APPDATA%/AtomBox/state/fingerprints/file-fingerprint-index.json
```

索引文件不得放在：

- 仓库目录。
- 程序安装目录。
- 用户下载目录。
- 远端 provider 目录。

## 索引格式

第一版使用普通 JSON 文件，不使用 SQLite，也不使用 JSONL。

推荐顶层结构：

```json
{
  "schemaVersion": 1,
  "records": []
}
```

推荐记录字段：

| 字段 | 说明 |
|---|---|
| `hashAlgorithm` | 主指纹算法，第一版推荐 `sha256`。 |
| `hashValue` | 主指纹值。 |
| `fileSize` | 本地文件大小。 |
| `storageAccountId` | 上传目标账号。 |
| `providerId` | 上传目标 Provider。 |
| `remotePath` | 上传成功后的远端路径。 |
| `etag` | provider 返回的可选版本标识，只作参考，不作为文件 MD5。 |
| `uploadedAt` | 上传成功时间。 |
| `lastSeenAt` | 最近一次查询或确认时间。 |

第一版主匹配条件是账号范围命中：

```text
sha256 + fileSize + storageAccountId
```

`remotePath` 不参与命中条件，而是作为查询结果返回给 Desktop。这样用户在同一个存储账号内上传相同文件时，AtomBox 可以提示该文件上一次或多次上传到具体哪个远端位置。

MD5 可以作为未来兼容字段或辅助字段，但不能作为唯一可信指纹。

## 上传任务指纹元数据

上传前计算出的指纹需要随正式上传任务一起持久化，否则上传完成后无法可靠写入索引。

`TransferTask` 第一版增加可选指纹元数据字段：

| 字段 | 说明 |
|---|---|
| `fingerprintHashAlgorithm` | 上传前计算出的主指纹算法，第一版为 `sha256`。 |
| `fingerprintHashValue` | 上传前计算出的主指纹值。 |
| `fingerprintFileSize` | 计算指纹时读取到的本地文件大小。 |
| `fingerprintCalculatedAt` | 指纹计算完成时间，可选。 |

这些字段只用于上传成功后写入本地索引。下载任务、设置关闭时创建的上传任务、用户取消历史命中提示时，都可以不携带这些字段。

## 模块边界

Core：

- 定义文件指纹记录模型。
- 定义查询条件和值对象。
- 定义本地指纹索引端口，例如 `IFileFingerprintIndexStore`。
- `IFileFingerprintIndexStore` 至少支持查询、写入或更新、统计和清空全部索引。
- `TransferTask` 定义可选指纹元数据字段，用于上传成功后的索引写入。
- 不访问文件系统，不引用 JSON 序列化实现。

Infrastructure：

- 实现 JSON 文件持久化。
- 通过 `AtomBoxStoragePaths` 获取索引文件路径。
- 负责目录创建、文件读写、格式版本读取和损坏文件处理。
- 实现 `JsonFileFingerprintIndexStore : IFileFingerprintIndexStore`，负责读写 `file-fingerprint-index.json`。
- 新增 `FingerprintAwareTransferStateStoreDecorator : ITransferStateStore`。
- 装饰器内部包装真实 `TransferStateStore`，先保存传输状态，再在符合条件时写入指纹索引。
- 装饰器只在 `TransferDirection.Upload`、`TransferStatus.Succeeded`、任务带完整指纹元数据且设置开启时写入索引。
- 装饰器通过 `IStorageAccountRepository` 按 `task.StorageAccountId` 查询账号，取得 `providerId` 等索引记录所需的 Provider 信息。
- 如果账号查询失败或账号已不存在，装饰器不写入索引，并按非阻塞错误处理。
- 索引写入失败不得把已经成功的上传任务改成失败；应作为非阻塞错误处理。
- 不接触 Desktop UI。

Application：

- 在上传前计算或接收本地文件指纹，并结合当前 `storageAccountId` 查询索引。
- 查询命中时返回历史上传记录给 Desktop，至少包含 `providerId`、`storageAccountId`、`remotePath`、`uploadedAt`。
- 用户确认上传后，创建带指纹元数据的正式上传任务。
- 不直接等待 Transfer worker 完成，也不直接在上传成功后写入索引。
- 上传失败、取消或中断时不得写入成功索引。
- 组织查询结果给 UI 或后续上传策略使用。

Desktop：

- 第一版不直接读写索引文件。
- 设置页新增“指纹索引”开关，使用 `atom:ToggleSwitch`，默认关闭。
- 第一版当前 UI 只展示开关，不展示索引文件路径、记录数量、最近更新时间和清空全部索引入口。
- 索引统计和清空能力保留在 Application / Core 端口中，后续再决定是否暴露到 UI。
- 远程文件浏览器复用底部 `StatusMessage` 区域展示上传前指纹计算状态。
- 单文件计算时显示 `正在计算文件指纹：demo.txt`。
- 多文件计算时显示 `正在计算文件指纹 1/3：demo.txt`。
- 指纹计算期间禁用上传按钮。
- 同一远程文件浏览器同一时刻只允许一个上传准备会话；已有会话时，新的上传请求应被阻止并提示用户稍后再试。
- 用户在某个 Provider 账号内上传文件时，如 Application 返回账号内历史命中记录，应弹窗提示用户以前上传过该文件。
- 弹窗需要展示上一次或多次上传到的具体 `remotePath`。
- 弹窗提供“取消”和“再次上传”两个动作；取消时不进入上传流程，再次上传时继续正常上传。

Providers / Transfer：

- 第一版不修改 Provider 契约。
- 第一版不让 Transfer worker 直接读写索引文件。
- Transfer worker 仍只依赖 `ITransferStateStore` 更新任务状态。
- Transfer worker 不直接依赖 `IFileFingerprintIndexStore`。
- 第一版不修改传输队列表格和交互。
- 第一版不新增传输任务准备态；正式上传任务仍只在用户确认后按现有流程创建。

## 数据流

上传前：

```text
Desktop
  -> 用户在远程文件浏览器选择上传文件
    -> 如上传指纹索引关闭，按现有流程创建上传任务
    -> 如上传指纹索引开启，远程文件浏览器底部显示正在计算文件指纹
    -> 指纹计算期间禁用上传按钮，并阻止第二个上传准备会话
  -> Application 上传前指纹查询编排
    -> 计算或读取本地文件 sha256 + fileSize
    -> 结合当前 storageAccountId 查询账号内历史记录
    -> Core 指纹索引端口
      -> Infrastructure JSON 索引实现
    -> 未命中时按现有流程创建上传任务
    -> 命中时返回历史 remotePath 给 Desktop
    -> 用户选择“再次上传”后按现有流程创建上传任务
    -> 用户选择“取消”时停止上传，不创建上传任务
```

上传成功后：

```text
TransferWorker
  -> ITransferStateStore.UpdateStatusAsync(Succeeded)
    -> FingerprintAwareTransferStateStoreDecorator
      -> TransferStateStore 保存传输状态
      -> 判断 Upload + Succeeded + 完整指纹元数据 + 设置开启
      -> IStorageAccountRepository 查询账号并取得 providerId
      -> IFileFingerprintIndexStore
        -> JsonFileFingerprintIndexStore
          -> file-fingerprint-index.json
```

如果 `TransferStateStore` 保存传输状态失败，装饰器不得写入索引。如果索引写入失败，上传成功状态仍然保持成功，索引错误按非阻塞错误处理。

## 测试与验收

Core 测试：

- 指纹记录字段基本校验。
- `sha256 + fileSize + storageAccountId` 账号范围匹配规则。
- 相同 `sha256 + fileSize` 但不同 `storageAccountId` 不应互相命中。
- 同一账号下相同文件可返回多个历史 `remotePath`。
- ETag 不参与主匹配判断。

Infrastructure 测试：

- 首次写入时自动创建目录和 JSON 文件。
- 已有索引文件可读取。
- 同一指纹可追加多个远端目标记录。
- 已有相同账号和远端路径记录时可更新 `lastSeenAt` 或相关时间字段。
- 损坏 JSON 文件返回结构化错误，不静默吞掉。
- 装饰器收到 `Upload + Succeeded + 完整指纹元数据` 时写入索引。
- 装饰器收到下载、失败、取消或中断任务时不写入索引。
- 设置关闭时装饰器不写入索引。
- 真实 `TransferStateStore` 保存失败时装饰器不写入索引。
- 装饰器无法按 `storageAccountId` 查询到账号时不写入索引。
- 指纹索引写入失败不改变传输成功状态。

Application 测试：

- 设置关闭时不计算指纹、不查询索引，并按现有流程创建上传任务。
- 上传前查询同一账号命中时能返回历史记录。
- 上传前查询未命中时继续正常上传流程。
- 用户取消历史命中提示时不进入上传流程。
- 用户选择“再次上传”时继续正常上传流程，并创建带指纹元数据的上传任务。
- 上传失败、取消或中断时不写入成功索引。

Desktop 测试：

- 设置页开关可保存和恢复。
- 设置页只展示“指纹索引”开关，开关可保存和恢复。
- 远程文件浏览器底部能显示单文件和多文件指纹计算文案。
- 指纹计算期间上传按钮禁用。
- 已有上传准备会话时，第二次上传请求被阻止并提示稍后再试。
- 命中历史记录时弹窗展示文件曾经上传过的信息和历史远端路径。
- 弹窗提供“取消”和“再次上传”动作。
- 传输队列 UI 和 `TransferStatus` 保持不变。

手工验收：

- 索引文件出现在 AtomBox 应用数据目录下。
- 索引文件是普通 JSON。
- 文件内容不包含 secret material。
- 设置页可以开启或关闭上传指纹索引。
- 远程文件浏览器上传前会在底部显示指纹计算状态。
- 指纹计算期间不能启动第二批上传准备。
- 在同一存储账号内再次上传同一文件时，本机能查询到历史上传记录。
- 查询命中提示中能看到上一次上传到的具体远端路径。

## 实际实现记录

- Core 已新增文件指纹记录、查询对象、索引统计对象和 `IFileFingerprintIndexStore` 端口。
- `TransferTask` 已新增可选指纹元数据字段，上传成功后可用于索引写入。
- Infrastructure 已新增 `JsonFileFingerprintIndexStore`，索引路径由 `AtomBoxStoragePaths.StateDirectory` 下的 `fingerprints/file-fingerprint-index.json` 管理。
- Infrastructure 已通过 `FingerprintAwareTransferStateStoreDecorator` 在真实传输状态保存成功后维护索引。
- Application 已新增上传前准备流程，可按设置计算 `sha256`、查询账号范围历史记录，并创建带指纹元数据的上传任务。
- Desktop 已在 Settings 页面接入“指纹索引”开关；Remote Browser 已接入底部计算文案、上传按钮禁用和历史命中确认弹窗。

## 实际验证记录

- `dotnet build AtomBox.slnx --no-restore` 通过，0 警告，0 错误。
- `dotnet test AtomBox.slnx --no-restore --no-build --logger "console;verbosity=minimal"` 通过，612 个测试，0 失败。

## 文档同步

实现完成前需要同步更新：

- `docs/modules/core/models-and-values.md`
- `docs/modules/core/ports.md`
- `docs/modules/application/use-cases.md`
- `docs/modules/transfer/tasks-and-state.md`
- `docs/modules/infrastructure/local-storage-and-configuration.md`
- `docs/modules/infrastructure/dependency-injection.md`

如上传流程或测试矩阵新增长期规则，需要同步：

- `docs/implementation/testing/phase-12-transfer-testing.md`
