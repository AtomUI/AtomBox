# 本地文件指纹索引

> 状态：Planned
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
- 上传成功后写入或更新索引。
- 索引文件放在 AtomBox 应用数据目录。
- 第一版使用普通 JSON 文件持久化，不引入 SQLite。
- Core 定义模型和端口。
- Infrastructure 负责 JSON 文件读写。
- Application 在上传前后编排查询和写入。

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

## 模块边界

Core：

- 定义文件指纹记录模型。
- 定义查询条件和值对象。
- 定义本地指纹索引端口，例如 `IFileFingerprintIndexStore`。
- 不访问文件系统，不引用 JSON 序列化实现。

Infrastructure：

- 实现 JSON 文件持久化。
- 通过 `AtomBoxStoragePaths` 获取索引文件路径。
- 负责目录创建、文件读写、格式版本读取和损坏文件处理。
- 不接触 Desktop UI。

Application：

- 在上传前计算或接收本地文件指纹，并结合当前 `storageAccountId` 查询索引。
- 查询命中时返回历史上传记录给 Desktop，至少包含 `providerId`、`storageAccountId`、`remotePath`、`uploadedAt`。
- 在上传成功后写入索引。
- 上传失败、取消或中断时不得写入成功索引。
- 组织查询结果给 UI 或后续上传策略使用。

Desktop：

- 第一版不直接读写索引文件。
- 用户在某个 Provider 账号内上传文件时，如 Application 返回账号内历史命中记录，应弹窗提示用户以前上传过该文件。
- 弹窗需要展示上一次或多次上传到的具体 `remotePath`。
- 弹窗提供“取消”和“再次上传”两个动作；取消时不进入上传流程，再次上传时继续正常上传。

Providers / Transfer：

- 第一版不修改 Provider 契约。
- 第一版不让 Transfer worker 直接读写索引文件。

## 数据流

上传前：

```text
Desktop
  -> Application 上传用例
    -> 计算或读取本地文件 sha256 + fileSize
    -> 结合当前 storageAccountId 查询账号内历史记录
    -> Core 指纹索引端口
      -> Infrastructure JSON 索引实现
    -> 命中时返回历史 remotePath 给 Desktop
    -> 用户确认后继续上传，用户取消时停止上传
```

上传成功后：

```text
Transfer 完成结果 / Application 上传完成编排
  -> Core 指纹索引端口
    -> Infrastructure JSON 索引实现
      -> file-fingerprint-index.json
```

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

Application 测试：

- 上传前查询同一账号命中时能返回历史记录。
- 上传前查询未命中时继续正常上传流程。
- 用户取消历史命中提示时不进入上传流程。
- 用户选择“再次上传”时继续正常上传流程。
- 上传成功后写入索引。
- 上传失败、取消或中断时不写入成功索引。

Desktop 测试：

- 命中历史记录时弹窗展示文件曾经上传过的信息和历史远端路径。
- 弹窗提供“取消”和“再次上传”动作。

手工验收：

- 索引文件出现在 AtomBox 应用数据目录下。
- 索引文件是普通 JSON。
- 文件内容不包含 secret material。
- 在同一存储账号内再次上传同一文件时，本机能查询到历史上传记录。
- 查询命中提示中能看到上一次上传到的具体远端路径。

## 文档同步

实现完成前需要同步更新：

- `docs/modules/core/models-and-values.md`
- `docs/modules/core/ports.md`
- `docs/modules/application/use-cases.md`
- `docs/modules/infrastructure/local-storage-and-configuration.md`

如上传流程或测试矩阵新增长期规则，需要同步：

- `docs/implementation/testing/phase-12-transfer-testing.md`
