# Phase 11 Provider 测试矩阵（OSS + SFTP/FTP/WebDAV）

> 文档状态：第一版发布基线冻结
>
> 创建时间：2026-06-16
>
> 冻结时间：2026-06-22
>
> 范围决策：Phase 11 当前 Provider 范围先打通对象存储 Provider，并补齐 SFTP / FTP / WebDAV 文件传输 Provider 的核心认证和基础操作能力。阿里云盘、百度网盘 provider 代码和注册已经存在，但不进入本轮真实验收范围。

> 发布基线说明：本文是 Provider headless 验证矩阵，不等价于第一版用户界面承诺。第一版发布主路径为对象存储、SFTP、FTP、WebDAV；FTPS、网盘 OAuth/token refresh 和网盘产品级体验后置。

## 1. 阶段目标

Phase 11 Provider 专项的目标是先建立一套稳定的 Provider 契约测试方法，然后用 OSS 完成本轮真实对象存储验证，并用 SFTP/FTP/WebDAV 补齐基础文件传输 provider 的无 UI 验证。

本阶段不追求 provider 种类覆盖，而追求：

- Provider 抽象稳定。
- OSS 路径语义稳定。
- OSS 上传、下载、列表、删除可被无 UI 验证。
- S3 兼容对象存储 provider 支持目录标记创建、对象移动、对象重命名的无 UI 验证。
- OSS 错误映射可诊断且不泄漏 secret。
- fake provider、真实 OSS provider、SFTP/FTP/WebDAV 文件传输 provider 可以复用同一套核心契约测试方法。

## 2. 范围边界

### 2.1 本轮必须覆盖

- Provider registry。
- Provider factory。
- Fake object storage provider。
- Aliyun OSS provider。
- OSS root / bucket / object path 语义。
- OSS 基础对象操作：
  - 连接测试。
  - 列 bucket。
  - 列 bucket root。
  - 上传小文件。
  - 下载小文件。
  - 删除测试对象。
  - 对 S3 兼容 provider：创建伪目录、对象重命名、对象移动。
- OSS 常见错误映射。
- OSS opt-in integration tests。

### 2.2 本轮明确后置

- 阿里云盘 / 百度网盘真实账号验收、OAuth/token refresh 和产品级体验。
- OAuth / token refresh。
- provider session pool。
- 分片上传和断点续传。
- 跨 provider 复制。
- 文件夹递归上传 / 下载。

这些能力后续可以使用同一套 Provider 契约测试方法扩展，但不作为 Phase 11 当前完成条件。

### 2.3 文件传输 Provider 补充范围

SFTP / FTP / WebDAV 在 Provider 模块中作为独立文件传输能力推进，优先级为 SFTP、FTP、WebDAV。

| Provider | 本轮认证范围 | 本轮基础操作 |
| --- | --- | --- |
| SFTP | host/IP、port、Linux username、密码认证、SSH 私钥认证、可选私钥 passphrase、host key policy/fingerprint。 | 列表、上传、下载、删除文件、删除空目录、上传前创建父目录、rootPath 映射。 |
| FTP | host/IP、port、匿名认证、用户名+密码认证、passive/active、timeoutSeconds。 | 列表、上传、下载、删除文件、删除空目录、上传前创建父目录、rootPath 映射。 |
| WebDAV | http/https endpoint URL、rootPath、匿名认证、用户名+密码 Basic 认证、timeoutSeconds。 | PROPFIND 列表、PUT 上传、GET 下载、DELETE 删除、MKCOL 创建目录、MOVE 移动/重命名、上传前创建父目录、rootPath 映射。 |

规则：

- `authMode` 是账号配置字段，不是 secret。
- password、privateKey、privateKeyPassphrase 等属于 secret material，不进入 Registry、TransferTask、日志或 UI 长期状态。
- FTP 匿名认证不得读取 credential store。
- SFTP `hostKeyPolicy=fingerprint` 必须提供 `hostKeyFingerprint`；默认 `acceptAny` 用于兼容本地测试和临时服务器。
- FTP 默认 `transferMode=passive`；active 作为显式配置；FTPS 暂不进入本轮范围。
- WebDAV endpoint 是完整 base URL，例如 `https://example.com/remote.php/dav/files/user/`；`rootPath` 是 base URL 下的相对根。
- SFTP/FTP/WebDAV provider 仍然是短生命周期运行对象，不在应用启动阶段连接服务器。

## 3. 测试分层

Provider 测试分为四层：

| 层级 | 默认运行 | 目标 |
| --- | --- | --- |
| Registry tests | 是 | 验证 provider 元数据注册正确。 |
| Factory tests | 是 | 验证账号、凭据、provider id 到 provider 实例的创建链路。 |
| Contract tests with fake provider | 是 | 验证统一 Provider 契约本身合理且可重复。 |
| File transfer provider tests | 是 | 验证 SFTP/FTP/WebDAV 认证、配置校验、路径映射和基础文件/目录操作。 |
| Aliyun OSS opt-in integration tests | 否 | 验证真实 OSS provider 遵守契约。 |
| S3 compatible object storage opt-in integration tests | 否 | 验证华为 OBS、百度 BOS、京东云 OSS、青云 QingStor、火山 TOS 等 S3 兼容 provider 的列表、上传、下载、目录标记、移动、重命名。 |

普通 `dotnet test AtomBox.slnx` 只跑前三层。真实 OSS 测试必须显式 opt-in。

## 4. 公共 Provider 契约测试

公共契约测试面向 `IStorageProvider`，不关心具体 provider 类型。

### 4.1 连接测试

| Case | 期望 |
| --- | --- |
| `TestConnection_Should_ReturnSuccess_ForValidProvider` | 有效 provider 返回成功结果和能力摘要。 |
| `TestConnection_Should_ReturnFailure_ForInvalidCredential` | 无效凭据返回鉴权错误，不抛原始异常。 |
| `TestConnection_Should_NotLeakSecret` | 错误 message、details、provider code 中不得包含 secret。 |

### 4.2 列表

| Case | 期望 |
| --- | --- |
| `ListRoot_Should_ReturnEntries` | root 路径可以返回条目。OSS 下 root 表示 bucket 列表。 |
| `ListBucketRoot_Should_ReturnEntries` | bucket root 可以返回对象或空列表。 |
| `ListBucketRoot_Should_ReturnCommonPrefixesAsFolders` | OSS bucket root 或任意 prefix 列表必须把 `CommonPrefixes` 映射为 Core `Folder`，用于 UI 展示伪目录。 |
| `ListFolderPrefix_Should_ReturnDirectChildrenOnly` | OSS prefix 列表必须使用 delimiter 语义，只返回当前 prefix 的直接子对象和直接子 prefix，不把深层对象平铺到当前层。 |
| `ListFolderPrefix_Should_SearchObjectsByPrefix` | OSS `RemotePath(bucket/prefix, Folder)` 必须转换为 OSS `Prefix=prefix/` 查询，用于按前缀搜索/浏览对象。 |
| `ListMissingPath_Should_ReturnNotFound` | 不存在路径返回统一 NotFound 错误。 |
| `List_Should_ReturnPaginationContext_WhenSupported` | 支持分页时返回 next cursor；不支持时明确为空。 |

### 4.3 上传

| Case | 期望 |
| --- | --- |
| `WriteSmallFile_Should_Succeed` | 上传小文件成功。 |
| `WriteSmallFile_ThenList_Should_ShowObject` | 上传后列表中可以看到对象。 |
| `WriteToInvalidPath_Should_ReturnFailure` | 非法路径返回配置或路径错误。 |
| `Write_Should_NotLeakLocalPathSecretLikeSegments` | 错误详情不得泄漏敏感路径片段或凭据。 |

### 4.4 下载

| Case | 期望 |
| --- | --- |
| `OpenReadExistingFile_Should_ReturnContent` | 下载已有文件，内容与上传一致。 |
| `OpenReadMissingFile_Should_ReturnNotFound` | 下载不存在对象返回 NotFound。 |
| `OpenReadFolderOrBucket_Should_ReturnFailure` | 对 bucket 或目录读取文件流应返回类型错误或不支持。 |

### 4.5 删除

| Case | 期望 |
| --- | --- |
| `DeleteExistingFile_Should_Succeed` | 删除已有文件成功。 |
| `DeleteExistingFile_ThenList_Should_NotShowObject` | 删除后列表不再出现该对象。 |
| `DeleteMissingFile_Should_ReturnNotFoundOrIdempotentSuccess` | 行为必须明确；本轮建议返回 NotFound。 |
| `DeleteBucket_Should_ReturnUnsupported` | 本轮不支持删除 OSS bucket。 |

### 4.6 生命周期

| Case | 期望 |
| --- | --- |
| `DisposedProvider_Should_RejectOperations` | provider dispose 后不再允许远程操作。 |
| `Provider_Should_NotBeSharedAcrossTests` | 每个测试使用独立 provider 实例和独立测试对象前缀。 |

## 5. Fake Object Storage Provider 测试

Fake provider 用来验证契约本身，不依赖网络、不依赖真实账号。

需要支持：

- 内存 bucket 集合。
- 内存 object 集合。
- root 列 bucket。
- bucket root 列 object。
- 上传 byte content。
- 下载 byte content。
- 删除 object。
- 注入错误：
  - authentication failed。
  - permission denied。
  - network timeout。
  - not found。
  - conflict。
  - throttled。

Fake provider 必须通过公共 Provider 契约测试。

Fake provider 不是生产 provider。它只用于测试和 headless scenario，不进入真实用户配置列表。

## 6. Provider Registry 测试

| Case | 期望 |
| --- | --- |
| `ProviderIds_Should_BeUnique` | 所有 provider id 唯一。 |
| `AliyunOss_Should_BeRegistered` | `aliyun-oss` 已注册。 |
| `AliyunOss_Should_BelongToObjectStorage` | category 为 ObjectStorage。 |
| `AliyunOss_Should_DeclareCapabilities` | 声明 list/upload/download/delete 等能力。 |
| `AliyunOss_Should_DeclareRequiredConfigFields` | endpoint、region 等配置字段完整。 |
| `AliyunOss_Should_NotDeclareSecretAsConfigField` | access key secret 不得作为普通 config field。 |
| `DeferredProviders_Should_NotBlockOss` | WebDAV/网盘后置不影响 OSS registry 测试。 |

## 7. Provider Factory 测试

| Case | 期望 |
| --- | --- |
| `CreateAliyunOssProvider_Should_Succeed_WithValidAccountAndCredential` | 有效账号和凭据可以创建 provider。 |
| `CreateProvider_Should_Fail_WhenAccountProviderIdUnknown` | provider id 不存在返回 ProviderUnavailable 或等价错误。 |
| `CreateProvider_Should_Fail_WhenCredentialMissing` | credential ref 不存在返回 Credential 错误。 |
| `CreateProvider_Should_Fail_WhenCredentialInvalid` | credential 内容缺少必填 secret 返回 Credential 或 Configuration 错误。 |
| `CreateProvider_Should_NotCacheSecret` | factory 不缓存 secret material。 |
| `CreateProvider_Should_ReturnNewInstanceEachTime` | 每次创建短生命周期 provider 实例。 |

## 8. Aliyun OSS 专属测试矩阵

### 8.1 配置字段

| 字段 | 要求 |
| --- | --- |
| endpoint | 必填，例如 `oss-cn-hangzhou.aliyuncs.com`。 |
| region | 可选或必填取决于 SDK 需要，但测试矩阵中必须明确。 |
| bucket | 可选；为空时 root 进入 bucket 列表。 |
| accessKeyId | 必填 secret credential field。 |
| accessKeySecret | 必填 secret credential field。 |

### 8.2 OSS 路径语义

| 路径 | 语义 |
| --- | --- |
| `RemotePath.Root` | OSS bucket 列表。 |
| `RemotePath(bucket, BucketRoot)` | 指定 bucket 根。 |
| `RemotePath(bucket/prefix, Folder)` | bucket 内 prefix / pseudo folder。 |
| `RemotePath(bucket/key, File)` | bucket 内 object key。 |

测试 case：

| Case | 期望 |
| --- | --- |
| `RootPath_Should_ListBuckets_WhenBucketNotConfigured` | bucket 未配置时 root 列 bucket。 |
| `RootPath_Should_ListConfiguredBucket_WhenBucketConfigured` | bucket 已配置时默认进入该 bucket。 |
| `BucketRoot_Should_ListObjects` | bucket root 列 object/prefix。 |
| `ObjectKey_Should_PreserveSlashes` | object key 中的 `/` 作为 OSS key 语义保留。 |
| `FolderPrefix_Should_EndWithSlashWhenNeeded` | prefix/folder 语义规范化。 |
| `CommonPrefixes_Should_BeShownAsFolders` | OSS `CommonPrefixes` 必须展示为 `RemoteItemKind.Folder`，路径保留完整 bucket/prefix。 |
| `PrefixListing_Should_UseOssPrefixAndDelimiter` | prefix 浏览必须使用 OSS `Prefix` 和 `Delimiter="/"`，实现“当前目录”视图。 |
| `PrefixListing_Should_NotFlattenNestedObjects` | `a/b/c.txt` 在列 `a/` 时应显示 `b` 文件夹；在列 `a/b/` 时才显示 `c.txt`。 |

### 8.2.1 OSS 文件夹展示与前缀搜索专项

OSS 没有真实目录，但 Provider 必须为上层 UI 和 Application 提供稳定的“文件夹视图”语义：

| Case | 期望 |
| --- | --- |
| `AliyunOss_ListBucketRoot_Should_ShowPseudoFolders` | 当 bucket 中存在 `docs/readme.txt`、`images/logo.png` 这类对象时，列 bucket root 应显示 `docs`、`images` 两个 `Folder`。 |
| `AliyunOss_ListFolderPrefix_Should_ShowChildObjects` | 当列 `bucket/docs` 时，应使用 OSS `Prefix=docs/` 搜索，并显示 `docs/` 下的直接子对象。 |
| `AliyunOss_ListFolderPrefix_Should_ShowChildFolders` | 当存在 `docs/api/index.md` 时，列 `bucket/docs` 应显示 `api` 子文件夹，而不是把 `index.md` 平铺到 `docs` 层。 |
| `AliyunOss_ListNestedFolderPrefix_Should_ShowNestedFiles` | 当列 `bucket/docs/api` 时，应显示 `index.md`，验证进入子 prefix 后可以看到对应文件。 |
| `AliyunOss_PrefixSearch_Should_NotReturnSiblingPrefixes` | 列 `bucket/docs` 时不得返回 `downloads/`、`docsets/` 等 sibling prefix 下的对象或目录。 |
| `AliyunOss_PrefixSearch_Should_NormalizeTrailingSlash` | `bucket/docs` 和 `bucket/docs/` 的查询语义一致，最终都应使用 `Prefix=docs/`。 |

### 8.3 基础操作

| Case | 期望 |
| --- | --- |
| `AliyunOss_TestConnection_Should_Succeed` | 有效配置测试连接成功。 |
| `AliyunOss_ListRoot_Should_ReturnBucketsOrConfiguredBucket` | 根据 bucket 配置返回 bucket 列表或指定 bucket root。 |
| `AliyunOss_UploadSmallFile_Should_Succeed` | 上传小文件成功。 |
| `AliyunOss_DownloadUploadedFile_Should_MatchContent` | 下载内容与上传一致。 |
| `AliyunOss_DeleteUploadedFile_Should_RemoveObject` | 删除后对象不存在。 |
| `AliyunOss_OverwriteExistingObject_Should_FollowPolicy` | 覆盖策略行为明确；本轮可先允许覆盖或返回冲突，但必须固定。 |

### 8.3.1 S3 兼容对象存储扩展操作

S3 兼容 provider 当前包括：华为云 OBS、百度智能云 BOS、京东云 OSS、青云 QingStor、火山引擎 TOS。它们复用 `S3CompatibleProvider` 抽象，并在 registry 中声明 `CreateFolder`、`Rename`、`Move` 能力。

| Case | 期望 |
| --- | --- |
| `S3Compatible_CreateFolder_Should_CreateFolderMarkerObject` | `CreateFolder(bucket/a/b)` 应写入空对象 `a/b/`，列表时展示为 `Folder`。 |
| `S3Compatible_RenameObject_Should_CopyThenDeleteSource` | 重命名对象应在同父 prefix 内执行 copy + delete，目标对象可下载，源对象不再作为当前层文件展示。 |
| `S3Compatible_MoveObject_Should_CopyThenDeleteSource` | 移动对象应支持跨 prefix copy + delete，目标 prefix 列表可见并可下载。 |
| `ObjectStorage_MoveFolder_Should_MovePrefixRecursively` | 伪目录移动必须递归列 prefix，逐对象 copy + delete，并 best-effort 删除源目录 marker。 |
| `S3Compatible_ListFolderMarker_Should_NotShowMarkerAsFile` | `a/b/` 目录标记对象不能在 `a/b` 列表中显示为一个空文件。 |
| `S3Compatible_UploadDownloadLargePayload_Should_ReportProgress` | 大 payload 上传 / 下载必须保持内容一致，并报告最终进度。 |
| `ObjectStorage_ListPage_Should_UseProviderNativeCursor_WhenAvailable` | 阿里 OSS、腾讯 COS、七牛 Kodo、S3 兼容 provider 使用 provider 原生 cursor；不支持原生 cursor 的 provider 使用 Core 默认 offset pagination。 |
| `S3Compatible_MoveFailure_Should_NotDeleteSourceOrLeakSecret` | copy 阶段失败时不得继续删除源对象，错误信息不得泄漏底层异常中的 secret。 |
| `ObjectStorage_LargeUpload_Should_UseMultipartCapability_WhenDeclared` | 声明 `MultipartUpload` 的 provider 在大文件上传时应走 SDK/协议分片或断点上传路径。 |

真实集成测试必须覆盖：

- bucket root list。
- 当前 prefix 下 root 文件与 child prefix 展示。
- child prefix 下文件展示。
- 上传后下载内容一致。
- 创建目录标记后列表展示为文件夹。
- 重命名和移动后的目标对象可下载。
- 测试对象保留在 `*_TEST_PREFIX` 或 `*_MANUAL_PREFIX` 下，便于人工验收。

### 8.4 错误映射

| 场景 | 期望错误 |
| --- | --- |
| access key id 错误 | Authentication / Unauthorized。 |
| access key secret 错误 | Authentication / Unauthorized。 |
| bucket 不存在 | NotFound。 |
| endpoint 错误 | Network / Configuration，按实际 SDK 可诊断结果固定。 |
| region 与 bucket 不匹配 | Configuration 或 Provider。 |
| 无 list bucket 权限 | PermissionDenied。 |
| 无 put object 权限 | PermissionDenied。 |
| 无 get object 权限 | PermissionDenied。 |
| 无 delete object 权限 | PermissionDenied。 |
| 网络超时 | Network / Timeout，retryable=true。 |
| 服务端限流 | Throttled 或 ProviderUnavailable，retryable=true。 |
| HTTP 409 / 412 | Conflict。 |
| HTTP 400 | Validation。 |

所有错误测试都必须确认：

- 不抛 SDK 原始异常到外层。
- 不泄漏 access key、secret、authorization header。
- 可保留脱敏 request id、HTTP status、provider error code。

## 9. Aliyun OSS Opt-in Integration Tests

真实 OSS integration tests 默认跳过。只有满足环境变量时才执行。

推荐环境变量：

```text
ATOMBOX_TEST_ALIYUN_OSS=1
ATOMBOX_OSS_ENDPOINT=oss-cn-hangzhou.aliyuncs.com
ATOMBOX_OSS_REGION=cn-hangzhou
ATOMBOX_OSS_BUCKET=your-test-bucket
ATOMBOX_OSS_ACCESS_KEY_ID=...
ATOMBOX_OSS_ACCESS_KEY_SECRET=...
ATOMBOX_OSS_TEST_PREFIX=atombox-tests/
```

规则：

- 所有测试对象必须写入 `ATOMBOX_OSS_TEST_PREFIX` 下。
- 测试必须清理自己创建的对象。
- 删除操作只能删除 test prefix 下的对象。
- 失败日志必须脱敏。
- 如果环境变量缺失，测试显示 skipped，不失败。
- 普通 `dotnet test AtomBox.slnx` 不要求真实 OSS 账号。

## 10. SFTP / FTP / WebDAV 文件传输测试矩阵

### 10.1 SFTP 配置与认证

| Case | 期望 |
| --- | --- |
| `Sftp_CreateProvider_Should_Fail_WhenHostMissing` | host 缺失返回 Validation。 |
| `Sftp_CreateProvider_Should_Fail_WhenPortInvalid` | port 不在 1-65535 返回 Validation。 |
| `Sftp_CreateProvider_Should_Fail_WhenPasswordAuthMissingPassword` | password 模式缺少 password 返回 Validation，并释放 credential lease。 |
| `Sftp_CreateProvider_Should_Fail_WhenPrivateKeyAuthMissingPrivateKey` | privateKey 模式缺少 privateKey 返回 Validation，并释放 credential lease。 |
| `Sftp_CreateProvider_Should_AcceptPrivateKeyPassphrase` | privateKey 模式可带 `privateKeyPassphrase`。 |
| `Sftp_CreateProvider_Should_Fail_WhenFingerprintPolicyMissingFingerprint` | `hostKeyPolicy=fingerprint` 缺少 `hostKeyFingerprint` 返回 Validation。 |
| `Sftp_CreateProvider_Should_Fail_WhenHostKeyPolicyInvalid` | 非 `acceptAny/fingerprint` 策略返回 Validation。 |
| `Sftp_CreateProvider_Should_Fail_WhenTimeoutOutOfRange` | `timeoutSeconds` 不在 1-600 返回 Validation。 |

### 10.2 SFTP 基础文件操作

| Case | 期望 |
| --- | --- |
| `Sftp_ListRoot_Should_MapFilesAndFolders` | rootPath 下目录和文件映射为 Core folder/file。 |
| `Sftp_CreateFolder_Should_CreateFolderAndParents` | 创建目录时自动创建缺失父目录；root 返回 Validation。 |
| `Sftp_RenameFile_Should_MoveWithinSameParent` | 文件重命名转换为同父目录 move。 |
| `Sftp_MoveFolder_Should_CreateDestinationParentAndMove` | 目录移动前创建目标父目录，并走协议 rename/move 能力。 |
| `Sftp_MoveToExistingDestination_Should_ReturnConflict` | 目标路径已存在返回 Conflict。 |
| `Sftp_Upload_Should_CreateMissingParentDirectories` | 上传 `a/b/file.txt` 前自动创建缺失的 `a`、`a/b`。 |
| `Sftp_DownloadFile_Should_WriteContentAndReportProgress` | 下载文件写入目标 stream，并报告进度。 |
| `Sftp_DownloadFolder_Should_ReturnValidation` | 下载目录返回 Validation，不调用 download file。 |
| `Sftp_DownloadMissingFile_Should_ReturnNotFound` | 缺失文件返回 NotFound。 |
| `Sftp_DeleteFile_Should_CallDeleteFile` | 文件路径删除走协议 file delete。 |
| `Sftp_DeleteFolder_Should_CallDeleteDirectory` | `RemotePathKind.Folder` 删除走协议 directory delete。 |
| `Sftp_DeleteRoot_Should_ReturnValidation` | root 不允许删除。 |
| `Sftp_PathMapping_Should_SupportCurrentWorkingDirectoryRoot` | `rootPath=.` 时使用登录后的当前工作目录，不强制拼成绝对路径。 |
| `Sftp_PathMapping_Should_SupportSlashRootWithoutDuplicateSeparators` | `rootPath=/` 时远端路径形如 `/folder/file.txt`，不能出现重复斜杠。 |
| `Sftp_PathMapping_Should_PreserveSpecialCharacterSegments` | 中文、空格、`#`、`+` 等路径段应原样传递给 SFTP adapter。 |

### 10.3 FTP 配置与认证

| Case | 期望 |
| --- | --- |
| `Ftp_CreateProvider_Should_Fail_WhenHostMissing` | host 缺失返回 Validation。 |
| `Ftp_CreateProvider_Should_Fail_WhenPortInvalid` | port 不在 1-65535 返回 Validation。 |
| `Ftp_CreateProvider_Should_Fail_WhenPasswordAuthMissingPassword` | 用户名密码模式缺少 password 返回 Validation，并释放 credential lease。 |
| `Ftp_CreateProvider_Should_CreateAnonymousProviderWithoutCredentialStore` | anonymous 模式不得读取 credential store。 |
| `Ftp_CreateProvider_Should_Fail_WhenTransferModeInvalid` | `transferMode` 非 `passive/active` 返回 Validation。 |
| `Ftp_CreateProvider_Should_Fail_WhenTimeoutOutOfRange` | `timeoutSeconds` 不在 1-600 返回 Validation。 |

### 10.4 FTP 基础文件操作

| Case | 期望 |
| --- | --- |
| `Ftp_ListRoot_Should_MapFilesAndFolders` | rootPath 下目录和文件映射为 Core folder/file。 |
| `Ftp_CreateFolder_Should_CreateFolderAndParents` | 创建目录时自动创建缺失父目录；root 返回 Validation。 |
| `Ftp_RenameFile_Should_MoveWithinSameParent` | 文件重命名转换为同父目录 move。 |
| `Ftp_MoveFolder_Should_CreateDestinationParentAndMove` | 目录移动前创建目标父目录，并走协议 directory move 能力。 |
| `Ftp_MoveToExistingDestination_Should_ReturnConflict` | 目标路径已存在返回 Conflict。 |
| `Ftp_Upload_Should_CreateMissingParentDirectories` | 上传 `a/b/file.txt` 前自动创建缺失的 `a`、`a/b`。 |
| `Ftp_DownloadFile_Should_WriteContentAndReportProgress` | 下载文件写入目标 stream，并报告进度。 |
| `Ftp_DownloadFolder_Should_ReturnValidation` | 下载目录返回 Validation，不调用 download file。 |
| `Ftp_DownloadMissingFile_Should_ReturnNotFound` | 缺失文件返回 NotFound。 |
| `Ftp_DeleteFile_Should_CallDeleteFile` | 文件路径删除走协议 file delete。 |
| `Ftp_DeleteFolder_Should_CallDeleteDirectory` | `RemotePathKind.Folder` 删除走协议 directory delete。 |
| `Ftp_DeleteRoot_Should_ReturnValidation` | root 不允许删除。 |

### 10.5 SFTP / FTP Opt-in Integration Tests

真实 SFTP/FTP integration tests 默认跳过。只有满足环境变量时才执行。

SFTP 推荐环境变量：

```text
ATOMBOX_TEST_SFTP=1
ATOMBOX_SFTP_HOST=example.com
ATOMBOX_SFTP_PORT=22
ATOMBOX_SFTP_ROOT_PATH=/
ATOMBOX_SFTP_AUTH_MODE=password
ATOMBOX_SFTP_USERNAME=...
ATOMBOX_SFTP_PASSWORD=...
ATOMBOX_SFTP_PRIVATE_KEY=...
ATOMBOX_SFTP_PRIVATE_KEY_PASSPHRASE=...
ATOMBOX_SFTP_HOST_KEY_POLICY=acceptAny
ATOMBOX_SFTP_HOST_KEY_FINGERPRINT=...
ATOMBOX_SFTP_TIMEOUT_SECONDS=30
ATOMBOX_SFTP_TEST_PREFIX=atombox-tests/
```

FTP 推荐环境变量：

```text
ATOMBOX_TEST_FTP=1
ATOMBOX_FTP_HOST=example.com
ATOMBOX_FTP_PORT=21
ATOMBOX_FTP_ROOT_PATH=/
ATOMBOX_FTP_AUTH_MODE=password
ATOMBOX_FTP_USERNAME=...
ATOMBOX_FTP_PASSWORD=...
ATOMBOX_FTP_TRANSFER_MODE=passive
ATOMBOX_FTP_TIMEOUT_SECONDS=30
ATOMBOX_FTP_TEST_PREFIX=atombox-tests/
```

真实测试规则：

- 所有测试文件和目录必须写入 `*_TEST_PREFIX` 下。
- 默认可以清理自己创建的测试对象；如果用于人工验收，可单独运行保留文件的 smoke。
- 失败日志必须脱敏，不输出密码、私钥或完整 credential material。
- 普通 `dotnet test AtomBox.slnx` 不要求真实 SFTP/FTP 服务器。

### 10.6 WebDAV 配置与认证

| Case | 期望 |
| --- | --- |
| `WebDav_CreateProvider_Should_Fail_WhenEndpointMissingOrInvalid` | endpoint 缺失、非绝对 URL 或非 http/https 返回 Validation。 |
| `WebDav_CreateProvider_Should_Fail_WhenPasswordAuthMissingPassword` | 用户名密码模式缺少 password 返回 Validation，并释放 credential lease。 |
| `WebDav_CreateProvider_Should_CreateAnonymousProviderWithoutCredentialStore` | anonymous 模式不得读取 credential store。 |
| `WebDav_CreateProvider_Should_Fail_WhenTimeoutOutOfRange` | `timeoutSeconds` 不在 1-600 返回 Validation。 |

### 10.7 WebDAV 基础文件操作

| Case | 期望 |
| --- | --- |
| `WebDav_ListRoot_Should_MapFilesAndFolders` | PROPFIND Depth 1 返回目录和文件，映射为 Core folder/file。 |
| `WebDav_CreateFolder_Should_CreateFolderAndParents` | MKCOL 创建目录，自动创建缺失父目录；root 返回 Validation。 |
| `WebDav_Upload_Should_CreateMissingParentDirectories` | PUT 上传 `a/b/file.txt` 前自动创建缺失的 `a`、`a/b`。 |
| `WebDav_DownloadFile_Should_WriteContentAndReportProgress` | GET 下载文件写入目标 stream，并报告进度。 |
| `WebDav_DownloadFolder_Should_ReturnValidation` | 下载目录返回 Validation，不调用 GET。 |
| `WebDav_DownloadMissingFile_Should_ReturnNotFound` | 缺失文件返回 NotFound。 |
| `WebDav_DeleteFileOrFolder_Should_CallDelete` | 文件或目录删除走 DELETE。 |
| `WebDav_RenameFile_Should_MoveWithinSameParent` | 文件重命名转换为 MOVE。 |
| `WebDav_MoveFolder_Should_CreateDestinationParentAndMove` | 目录移动前创建目标父目录，并走 MOVE。 |
| `WebDav_MoveToExistingDestination_Should_ReturnConflict` | 目标路径已存在返回 Conflict。 |
| `WebDav_DeleteRoot_Should_ReturnValidation` | root 不允许删除。 |

### 10.8 WebDAV Opt-in Integration Tests

真实 WebDAV integration tests 默认跳过。只有满足环境变量时才执行。

WebDAV 推荐环境变量：

```text
ATOMBOX_TEST_WEBDAV=1
ATOMBOX_WEBDAV_ENDPOINT=https://example.com/dav/
ATOMBOX_WEBDAV_ROOT_PATH=/
ATOMBOX_WEBDAV_AUTH_MODE=password
ATOMBOX_WEBDAV_USERNAME=...
ATOMBOX_WEBDAV_PASSWORD=...
ATOMBOX_WEBDAV_TIMEOUT_SECONDS=30
ATOMBOX_WEBDAV_TEST_PREFIX=atombox-tests/
```

真实测试规则：

- 所有测试文件和目录必须写入 `ATOMBOX_WEBDAV_TEST_PREFIX` 下。
- 默认可以清理自己创建的测试对象；如果用于人工验收，可单独运行保留文件的 smoke。
- 失败日志必须脱敏，不输出密码或完整 credential material。
- 普通 `dotnet test AtomBox.slnx` 不要求真实 WebDAV 服务器。

## 11. 后置 Provider 记录

以下 provider 本轮不测试、不验收：

| Provider | 后置原因 |
| --- | --- |
| FTPS | TLS 模式、证书校验、显式/隐式 FTPS 需要单独设计和测试，本轮先不做。 |
| 阿里云盘 | 涉及 OAuth/token/API 限制，本轮不做。 |
| 百度网盘 | 涉及 OAuth/token/API 限制，本轮不做。 |
| 腾讯 COS / 七牛 Kodo / 又拍云 USS | 已补齐目录标记、对象移动、对象重命名和伪目录递归移动；腾讯 COS 已具备大文件分片路径；七牛 Kodo 当前基础对象能力已接入，但真实环境下暂不声明 `MultipartUpload`；又拍云当前不声明 `MultipartUpload`。 |
| 百度智能云 BOS / 京东云 OSS / 青云 QingStor | 已注册为 S3 兼容 provider，已具备 headless 单测和 opt-in 真实集成测试入口；真实账号验收等待 `.local` 环境变量填充后执行。 |

后续迭代可复用本文档的公共 Provider 契约测试，再新增 provider-specific matrix。

## 12. Phase 11 Provider 完成条件

本专项完成必须满足：

- Provider registry tests 通过。
- Provider factory tests 通过。
- Fake object storage provider 通过公共契约测试。
- Aliyun OSS provider 有 opt-in integration tests。
- S3 兼容 provider 有共享 headless 单测覆盖目录标记、移动、重命名，并为华为 OBS、百度 BOS、京东云 OSS、青云 QingStor、火山 TOS 提供 opt-in integration tests。
- 对象存储 provider 文件管理能力覆盖阿里 OSS、腾讯 COS、七牛 Kodo、又拍云 USS、华为 OBS、百度 BOS、京东云 OSS、青云 QingStor、火山 TOS：
  - 创建伪目录 marker。
  - 对象 copy + delete 移动。
  - 对象重命名。
  - 伪目录递归移动。
- 分页能力：
  - 阿里 OSS 使用 marker。
  - 腾讯 COS 使用 marker。
  - 七牛 Kodo 使用 marker。
  - S3 兼容 provider 使用 continuation-token / marker。
  - 又拍云当前使用 Core 默认 offset pagination。
- 大文件上传：
  - 阿里 OSS 使用 SDK `ResumableUploadObject`。
  - 腾讯 COS 使用 SDK multipart upload。
  - 七牛 Kodo 使用 SDK `ResumableUploader`。
  - 百度 BOS、京东云 OSS、青云 QingStor 通过 S3 兼容 multipart upload。
  - 七牛 Kodo、又拍云、华为 OBS、火山 TOS 当前不声明 `MultipartUpload`，仍走普通上传路径。
- 设置 OSS 环境变量后，Aliyun OSS 可以通过：
  - 连接测试。
  - 列 bucket / bucket root。
  - 上传小文件。
  - 下载小文件。
  - 删除测试对象。
- 设置对应环境变量后，S3 兼容对象存储 provider 可以通过：
  - 列 bucket root。
  - 上传 root object 和 child prefix object。
  - 列 prefix 并展示伪目录。
  - 创建目录标记。
  - 重命名对象。
  - 移动对象。
  - 下载并校验内容。
- 所有 OSS 错误映射返回 Core `StorageError`。
- 测试输出、日志、错误详情不包含 secret material。
- SFTP/FTP/WebDAV 进入文件传输 Provider headless 验证范围，并提供 `.local` opt-in 环境变量模板。
- FTPS/网盘 API 不进入本轮完成条件。
