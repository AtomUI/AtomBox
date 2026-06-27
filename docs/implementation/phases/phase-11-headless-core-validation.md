# Phase 11：无 UI 核心能力验证 / Headless Core Validation

> 文档状态：第一版发布基线冻结
>
> 创建时间：2026-06-16
>
> 冻结时间：2026-06-22
>
> 阶段定位：暂停 Presentation 新功能堆叠，先用无 UI 的单元测试、集成测试和 headless sample 夯实非 UI 模块的功能可用性、健壮性和扩展兼容性。

> 发布基线说明：Phase 11 是历史阶段文档。当前代码已经在 Phase 11/12 后继续完成 Provider、Transfer 和 Desktop Presentation 收口；本文保留无 UI 验证方法和测试范围作为后续回归依据，不再作为当前推荐开发起点。

## 1. 阶段背景

当前 AtomBox 已经进入真实 GUI 验收阶段，但多个问题表现为 UI bug，实际牵扯到底层模块能力是否闭环：

- 添加账号后用户不知道从哪里进入远程浏览。
- 账号管理页数量和列表展示不一致。
- 左侧菜单没有连接实例或明确浏览入口。
- Dialog、下拉框、列表控件等 UI 交互问题干扰了底层能力验证。

GUI 会把底层模块问题和 Presentation 问题混在一起。Phase 11 的目标是先把非 UI 模块独立验证清楚，再回到 Presentation 做稳定承载。

Phase 11 不追求继续打磨桌面布局，也不继续扩大 UI 交互范围。它要求通过无 UI 的方式证明：账号、凭据、Provider、远程浏览、传输任务、错误映射和本地持久化这些核心链路在模块层面是可用、可测、可扩展的。

## 2. 涉及模块总览

Phase 11 涉及以下模块：

| 模块 | 是否重点 | Phase 11 职责 |
| --- | --- | --- |
| Core | 是 | 稳定值对象、模型、端口、错误和能力语义，补齐边界测试。 |
| Application | 是 | 用例级 headless scenario 主战场，验证账号、远程浏览、传输创建、错误结果。 |
| Providers | 是 | 本轮主线仍以 OSS 为真实对象存储闭环，同时补齐 SFTP/FTP/WebDAV 基础文件传输 provider 的无 UI 契约、配置校验和 opt-in 真实验证准备；FTPS 后置。 |
| Infrastructure | 是 | 验证本地配置、账号、凭据、状态存储的可靠性和坏数据恢复边界。 |
| Transfer | 是 | 验证无 UI 传输任务生命周期、状态落盘、失败/中断语义和调度边界。 |
| Tests | 是 | 建立跨模块 scenario tests、contract tests、integration tests 分层。 |
| Samples / Headless Runner | 是 | 提供 console/sample 方式执行真实闭环，不依赖 Desktop UI。 |
| Presentation / Desktop | 限制 | 暂停新增 UI 能力，只允许修阻塞性 bug；不得作为核心链路验证入口。 |

## 3. 总目标

Phase 11 完成后，AtomBox 应能在不启动 GUI 的情况下验证以下链路：

1. 创建、编辑、列出、测试账号。
2. 凭据写入、读取、轮换、缺失和损坏处理。
3. Provider 注册、选择、创建、释放；本轮真实对象存储 provider 只要求 OSS，文件传输 provider 覆盖 SFTP/FTP/WebDAV 基础能力。
4. 远程入口解析：无账号、单账号、多账号、指定账号。
5. OSS bucket 列表、bucket root、对象列表、上传、下载、删除。
6. SFTP/FTP/WebDAV rootPath 下的目录列表、文件上传、文件下载、文件删除、空目录删除、创建目录、移动/重命名和基础认证配置。
7. 传输任务创建、执行、失败、中断、历史查询。
8. 错误映射、脱敏详情和可诊断上下文。
9. 本地 accounts/settings/credentials/state 文件的健壮读写。

## 4. Core 模块任务

目标：证明 Core 的模型和值对象足够稳定，不依赖 UI 就能表达完整业务语义。

需要做：

- 补齐 `RemotePath` 测试：
  - root、bucket root、folder、file/object path。
  - combine、normalize、trim、非法路径。
  - OSS bucket 列表路径与 bucket 内对象路径的区分。
- 补齐 `StorageAccount` 测试：
  - provider category / provider id 必填。
  - display name 规范化。
  - endpoint、region、provider config 规范化。
  - credential ref 不允许空。
- 补齐 `StorageCapabilitySet` 测试：
  - 上传、下载、删除、列目录、分页能力组合。
  - bucket root 与普通目录的能力差异表达。
- 补齐 `StorageError` 测试：
  - category、code、retryable、provider error code。
  - 错误构造不得携带 secret。
- 补齐端口契约测试准备：
  - `IStorageProvider` 行为矩阵。
  - `IStorageAccountRepository` CRUD 行为矩阵。
  - `ICredentialStore` secret 生命周期行为矩阵。

完成标准：

- Core 测试覆盖核心值对象和错误语义。
- Core 不出现任何 Avalonia、AtomUI、SDK、Infrastructure 引用。
- 所有 Core 模型都能被 Application / Provider / Transfer 共同使用，不需要 Presentation 补语义。

## 5. Application 模块任务

目标：用 Application scenario tests 验证用户用例，不启动 UI。

需要做：

- 账号用例测试：
  - 添加账号成功。
  - 添加账号时 provider 不存在。
  - 添加账号时 credential 保存失败。
  - 编辑账号保留旧凭据。
  - 编辑账号更新凭据。
  - 列出全部账号。
  - 按 provider category 列出账号。
  - 删除账号前置检查预留。
- 连接测试用例：
  - 成功返回 provider、endpoint、region、target summary、capabilities。
  - 认证失败映射为统一错误。
  - 网络失败映射为 retryable 错误。
  - provider 不可用返回结构化错误。
- 远程入口解析测试：
  - OSS 无账号 -> 无账号状态。
  - OSS 单账号 -> 自动选中账号。
  - OSS 多账号 -> 返回账号选择状态。
  - 指定账号 -> 跳过账号选择并加载路径。
- 远程浏览测试：
  - root 列 bucket。
  - bucket root 列对象。
  - 文件夹路径列对象。
  - 分页 cursor 传递。
  - 删除对象成功后返回成功结果。
  - 删除失败返回脱敏错误详情。
- 传输任务创建测试：
  - 上传任务创建。
  - 下载任务创建。
  - 空文件列表、非法路径、账号不存在、能力不支持。
  - 创建成功后任务可通过队列查询。

完成标准：

- Application scenario tests 可以用 fake repository、fake credential store、fake provider factory 运行。
- Application 不引用 Desktop、Avalonia、AtomUI、具体 SDK。
- 所有 UI 需要展示的状态都可以从 Application 结果对象中拿到。

## 6. Providers 模块任务

目标：把 Provider 验证分为 fake object storage provider contract tests、OSS opt-in integration tests，以及 SFTP/FTP/WebDAV 文件传输 provider 的基础契约测试和真实连接 opt-in 准备。本轮不做 FTPS、网盘 API 的真实闭环。

Provider 专项测试矩阵见 `docs/implementation/testing/phase-11-provider-test-matrix.md`。

需要做：

- Fake object storage provider contract tests：
  - `TestConnectionAsync` 成功/失败。
  - `ListAsync` root 返回 bucket。
  - `ListAsync` bucket root 返回对象。
  - `OpenReadAsync` 下载文件。
  - `WriteAsync` 上传文件。
  - `DeleteAsync` 删除文件。
  - Provider dispose 后不能继续使用。
  - 所有异常必须映射为 `StorageError`。
- Provider registry tests：
  - 所有 provider descriptor 唯一。
  - category 正确。
  - config fields 完整。
  - 必填字段可被 Application 校验。
- Provider factory tests：
  - 账号不存在。
  - credential 不存在。
  - credential 解密/读取失败。
  - provider id 不存在。
  - 每次创建短生命周期 provider。
- SFTP provider headless tests：
  - 配置校验：host 必填、port 为 1-65535、rootPath 规范化。
  - 认证校验：`authMode=password` 需要 `username/password`。
  - 认证校验：`authMode=privateKey` 需要 `username/privateKey`，可选 `privateKeyPassphrase`。
  - Host key 校验：默认 `hostKeyPolicy=acceptAny`；`fingerprint` 模式必须提供 `hostKeyFingerprint`；不支持的策略返回 Validation。
  - 列表：rootPath 下目录和文件映射为 `RemoteItemKind.Folder/File`。
  - 创建目录：`CreateFolderAsync` 可创建目标目录和缺失父目录；root 返回 Validation。
  - 重命名/移动：`RenameAsync` 在同父目录内改名；`MoveAsync` 可移动文件或目录并创建目标父目录；目标已存在返回 Conflict。
  - 上传：上传文件前自动创建缺失父目录，进度回调可报告字节数。
  - 下载：下载文件成功；下载目录返回 Validation；下载不存在文件返回 NotFound。
  - 删除：删除文件走 file delete；删除空目录走 directory delete；禁止删除 root。
  - 生命周期：provider dispose 必须释放 SSH client 和 credential lease。
- FTP provider headless tests：
  - 配置校验：host 必填、port 为 1-65535、rootPath 规范化。
  - 认证校验：`authMode=password` 需要 `username/password`。
  - 认证校验：`authMode=anonymous` 不读取 credential store。
  - 网络配置：`transferMode=passive` 默认，支持 `active`；`timeoutSeconds` 范围为 1-600。
  - 列表：rootPath 下目录和文件映射为 `RemoteItemKind.Folder/File`。
  - 创建目录：`CreateFolderAsync` 可创建目标目录和缺失父目录；root 返回 Validation。
  - 重命名/移动：`RenameAsync` 在同父目录内改名；`MoveAsync` 按文件/目录分别走协议 move 能力，并创建目标父目录；目标已存在返回 Conflict。
  - 上传：上传文件前自动创建缺失父目录，进度回调可报告字节数。
  - 下载：下载文件成功；下载目录返回 Validation；下载不存在文件返回 NotFound。
  - 删除：删除文件走 file delete；删除空目录走 directory delete；禁止删除 root。
  - 生命周期：provider dispose 必须释放 FTP client 和 credential lease。
- WebDAV provider headless tests：
  - 配置校验：endpoint 必须为绝对 http/https URL，rootPath 规范化。
  - 认证校验：`authMode=password` 需要 `username/password`。
  - 认证校验：`authMode=anonymous` 不读取 credential store。
  - 网络配置：`timeoutSeconds` 范围为 1-600。
  - 列表：PROPFIND Depth 1 的目录和文件映射为 `RemoteItemKind.Folder/File`。
  - 创建目录：`CreateFolderAsync` 使用 MKCOL，可创建目标目录和缺失父目录；root 返回 Validation。
  - 重命名/移动：`RenameAsync` 和 `MoveAsync` 使用 MOVE，目标已存在返回 Conflict。
  - 上传：PUT 文件前自动创建缺失父目录，进度回调可报告字节数。
  - 下载：GET 文件成功；下载目录返回 Validation；下载不存在文件返回 NotFound。
  - 删除：DELETE 文件或目录；禁止删除 root。
  - 生命周期：provider dispose 必须释放 HTTP client 和 credential lease。
- 阿里云 OSS opt-in integration tests：
  - 通过环境变量启用，默认不跑。
  - 必需环境变量示例：
    - `ATOMBOX_OSS_ENDPOINT`
    - `ATOMBOX_OSS_REGION`
    - `ATOMBOX_OSS_BUCKET`
    - `ATOMBOX_OSS_ACCESS_KEY_ID`
    - `ATOMBOX_OSS_ACCESS_KEY_SECRET`
  - 测试连接。
  - 列 bucket 或指定 bucket。
  - 上传小文件。
  - 下载小文件。
  - 删除测试对象。
  - 失败时输出脱敏诊断。
- SFTP opt-in integration tests：
  - 通过 `.local/sftp.env.ps1` 或同名环境变量启用，默认不跑。
  - 必需环境变量：
    - `ATOMBOX_TEST_SFTP`
    - `ATOMBOX_SFTP_HOST`
    - `ATOMBOX_SFTP_PORT`
    - `ATOMBOX_SFTP_ROOT_PATH`
    - `ATOMBOX_SFTP_AUTH_MODE`
    - `ATOMBOX_SFTP_USERNAME`
    - `ATOMBOX_SFTP_PASSWORD` 或 `ATOMBOX_SFTP_PRIVATE_KEY`
  - 可选环境变量：
    - `ATOMBOX_SFTP_PRIVATE_KEY_PASSPHRASE`
    - `ATOMBOX_SFTP_HOST_KEY_POLICY`
    - `ATOMBOX_SFTP_HOST_KEY_FINGERPRINT`
    - `ATOMBOX_SFTP_TEST_PREFIX`
  - 测试连接、列目录、上传小文件、下载校验、删除测试文件、创建并删除测试空目录。
- FTP opt-in integration tests：
  - 通过 `.local/ftp.env.ps1` 或同名环境变量启用，默认不跑。
  - 必需环境变量：
    - `ATOMBOX_TEST_FTP`
    - `ATOMBOX_FTP_HOST`
    - `ATOMBOX_FTP_PORT`
    - `ATOMBOX_FTP_ROOT_PATH`
    - `ATOMBOX_FTP_AUTH_MODE`
  - 密码模式需要：
    - `ATOMBOX_FTP_USERNAME`
    - `ATOMBOX_FTP_PASSWORD`
  - 可选环境变量：
    - `ATOMBOX_FTP_TRANSFER_MODE`
    - `ATOMBOX_FTP_TIMEOUT_SECONDS`
    - `ATOMBOX_FTP_TEST_PREFIX`
  - 测试连接、列目录、上传小文件、下载校验、删除测试文件、创建并删除测试空目录。
- WebDAV opt-in integration tests：
  - 通过 `.local/webdav.env.ps1` 或同名环境变量启用，默认不跑。
  - 必需环境变量：
    - `ATOMBOX_TEST_WEBDAV`
    - `ATOMBOX_WEBDAV_ENDPOINT`
    - `ATOMBOX_WEBDAV_ROOT_PATH`
    - `ATOMBOX_WEBDAV_AUTH_MODE`
  - 密码模式需要：
    - `ATOMBOX_WEBDAV_USERNAME`
    - `ATOMBOX_WEBDAV_PASSWORD`
  - 可选环境变量：
    - `ATOMBOX_WEBDAV_TIMEOUT_SECONDS`
    - `ATOMBOX_WEBDAV_TEST_PREFIX`
  - 测试连接、列目录、创建目录、上传小文件、下载校验、移动/重命名、删除测试文件和测试目录。

本轮不做：

- FTPS provider。
- WebDAV provider 契约测试。
- 阿里云盘 / 百度网盘 API 测试。
- 腾讯 COS / 七牛 Kodo 等其他对象存储真实 provider 测试作为补充验证，不进入 Phase 11 完成条件。

完成标准：

- 普通测试不依赖外网和真实账号。
- 真实 provider 测试必须显式 opt-in。
- SDK DTO 和 SDK exception 不穿透 Core / Application。
- OSS Provider 错误映射覆盖认证、授权、网络、路径不存在、限流、服务端错误。
- SFTP/FTP Provider 错误映射覆盖认证失败、网络失败、路径不存在、权限不足和目录/文件类型不匹配。

## 7. Infrastructure 模块任务

目标：验证本地文件存储、凭据存储和状态存储可以承受真实使用中的坏数据和边界情况。

需要做：

- `JsonFileStore` 测试：
  - 文件不存在时返回默认值。
  - 目录不存在时自动创建。
  - JSON 损坏时返回结构化错误。
  - 写入失败时返回结构化错误。
  - 保存后可重新读取。
- 账号仓储测试：
  - add / update / delete / list / get。
  - 重复 id 行为。
  - 不存在 id 行为。
  - category filter 行为。
- 凭据存储测试：
  - 保存 secret。
  - 读取 secret。
  - 覆盖 secret。
  - 删除 secret。
  - credential index 损坏。
  - key 文件缺失或损坏。
  - secret 不出现在 accounts.json。
- 设置和状态存储测试：
  - settings 默认值。
  - transfer task state roundtrip。
  - app data root 可替换为测试临时目录。
- 启动诊断基础：
  - 配置目录不可写。
  - accounts.json 损坏。
  - credential store 不可用。

完成标准：

- Infrastructure 测试全部使用临时目录，不污染真实 `%APPDATA%\AtomBox`。
- secret 不出现在普通配置、日志、错误详情中。
- Infrastructure 不引用 Desktop / Application / Providers / Transfer。

## 8. Transfer 模块任务

目标：验证传输任务不依赖 UI 也能创建、调度、执行和持久化状态。

需要做：

- 传输任务模型测试：
  - upload/download 任务创建。
  - local path / remote path 必填。
  - account id 必填。
  - overwrite policy。
- 调度器测试：
  - 单任务执行成功。
  - provider 创建失败。
  - provider 执行失败。
  - 本地文件不存在。
  - 远端冲突。
  - cancellation token 取消。
- 状态测试：
  - Pending -> Running -> Succeeded。
  - Pending -> Running -> Failed。
  - Pending -> Running -> Cancelled。
  - 关闭或中断 -> Interrupted。
  - 失败原因持久化。
- 队列和历史查询测试：
  - running queue snapshot。
  - completed history snapshot。
  - failed/interrupted 可解释。
  - 清理历史前置规则预留。

完成标准：

- Transfer 不依赖 Application、Desktop、Provider 具体实现。
- Transfer 通过 Core 端口解析账号和 provider。
- 普通测试可以用 fake provider 运行。
- 状态落盘可重复读取。

## 9. Tests 分层任务

目标：建立清晰的测试分层，避免所有问题都靠 GUI 手工验收。

需要做：

- Unit tests：
  - Core 值对象。
  - Core 模型。
  - Application 纯业务分支。
  - Transfer 状态机。
- Contract tests：
  - `IStorageProvider` contract。
  - `IStorageAccountRepository` contract。
  - `ICredentialStore` contract。
- Scenario tests：
  - 创建账号 -> 测试连接 -> 远程入口解析 -> 列 bucket。
  - 列 bucket -> 进入 bucket -> 上传 -> 下载 -> 删除。
  - 创建下载任务 -> 执行 -> 队列/历史查询。
- Integration tests：
  - Infrastructure 临时目录。
  - Provider fake/in-memory。
  - Aliyun OSS opt-in。
  - SFTP/FTP opt-in。
- Regression tests：
  - 已发现 bug 的底层复现测试，避免用 UI 重复踩坑。

完成标准：

- `dotnet test AtomBox.slnx` 默认只跑无外部依赖测试。
- 真实 provider tests 默认跳过。
- 每个跨模块 scenario 都有明确 Arrange / Act / Assert。

## 10. Headless Sample / Runner 任务

目标：提供一个不启动 Desktop 的可执行样例，用来跑真实闭环和手工诊断。

推荐新增项目：

```text
samples/AtomBox.Headless/
  AtomBox.Headless.csproj
  Program.cs
```

或者如果不想新增 samples 目录，也可以先放在：

```text
tests/AtomBox.Headless.Tests/
```

推荐命令：

```text
dotnet run --project samples/AtomBox.Headless -- scenario oss-smoke
dotnet run --project samples/AtomBox.Headless -- scenario account-list
dotnet run --project samples/AtomBox.Headless -- scenario transfer-smoke
```

需要支持的 scenario：

- `account-list`
  - 打印当前账号列表。
- `oss-test-connection`
  - 从环境变量创建临时 OSS 账号并测试连接。
- `oss-list-root`
  - 列 bucket 或指定 bucket root。
- `oss-upload-download-delete`
  - 上传小文件、下载校验、删除测试对象。
- `transfer-smoke`
  - 创建一个 fake provider 下载或上传任务并执行。

完成标准：

- Runner 不依赖 Avalonia / AtomUI。
- Runner 可以复用生产 DI 注册，但不能启动 Desktop Shell。
- Runner 输出脱敏结果。
- Runner 不把 secret 写进日志。

## 11. Presentation 限制规则

Phase 11 期间，Presentation 进入冻结/隔离状态。

允许做：

- 修复阻塞 headless 验证的组合根 bug。
- 修复严重 crash。
- 添加极少量诊断入口，帮助验证非 UI 模块。

禁止做：

- 新增复杂 UI 页面。
- 继续打磨 Dialog 布局。
- 继续调整左侧菜单动态数据节点，除非 headless 链路已证明底层可用。
- 把核心验证依赖到手工点击 GUI。
- 在 ViewModel 中绕过 Application 直接访问 provider、repository、credential store 或 transfer runtime。

完成标准：

- Phase 11 的验收不依赖 GUI。
- GUI 只作为最后 smoke，不作为核心功能正确性的唯一证据。

## 12. 推荐执行顺序

Phase 11 建议按以下顺序执行：

1. 梳理现有测试覆盖，列出缺口。
2. Core 值对象和错误语义补测。
3. Infrastructure 临时目录和凭据存储补测。
4. Providers contract tests：先 fake object storage provider，再补 SFTP/FTP 文件传输 provider 基础契约，后 Aliyun OSS opt-in。
5. Application 账号和远程入口 scenario tests。
6. Application 远程浏览和传输创建 scenario tests。
7. Transfer 状态机和调度 scenario tests。
8. 建立 headless sample / runner。
9. 增加 Aliyun OSS opt-in integration tests；补 SFTP/FTP opt-in integration test 准备；FTPS/WebDAV/网盘 API 后置。
10. 跑完整 `restore / build / test / headless smoke` 验收。

## 13. 完成条件

Phase 11 完成必须满足：

- `dotnet build AtomBox.slnx` 通过。
- `dotnet test AtomBox.slnx` 通过，且默认不需要真实云账号。
- Core / Application / Providers / Infrastructure / Transfer 都有针对 Phase 11 关键链路的测试。
- 至少一个无 UI scenario 能跑通：
  - 添加账号。
  - 测试连接。
  - 解析远程入口。
  - 列远程对象。
- 至少一个 Transfer 无 UI scenario 能跑通：
  - 创建任务。
  - 执行任务。
  - 查询队列或历史。
- Aliyun OSS 真实账号测试可以通过环境变量 opt-in 执行。
- SFTP/FTP 基础文件传输 provider 有无 UI 契约测试和 `.local` opt-in 配置模板；真实服务器测试默认不跑。
- FTPS/网盘 API 不进入 Phase 11 当前完成条件。
- 所有错误详情和日志不包含 secret material。
- Presentation 没有新增业务绕行路径。

## 14. Phase 11 结束后再回到 Presentation 的条件

只有满足以下条件，才建议重新进入 Presentation/UI 阶段：

- 非 UI 模块已能证明账号、Provider、远程浏览和传输闭环可用。
- 真实 OSS provider 的核心能力至少有 opt-in 验证路径。
- 后续 provider 的测试方法已通过 OSS-only 矩阵沉淀，但不要求本阶段实现。
- Application 结果对象已经足够支撑 UI 展示，不需要 ViewModel 自己推断业务状态。
- 已知 GUI bug 能被明确归类为 Presentation 问题，而不是底层链路不确定。

回到 Presentation 后，优先做：

- 左侧菜单动态账号节点。
- 远程浏览页账号选择列表可视化。
- bucket / 文件列表交互。
- 账号管理页列表和编辑体验。
- Dialog UI 最终整理。
## 附录：roadmap 原始阶段摘要

以下内容来自重组前的 docs/implementation/roadmap.md，用于保留原路线图中的阶段定位和完成条件。

## 13. Phase 11：无 UI 核心能力验证 / Headless Core Validation

目标：暂停继续堆叠 Presentation UI 布局和交互，把 Core、Application、Providers、Infrastructure、Transfer 等非 UI 模块先用单元测试、契约测试、集成测试和 headless sample 验证到可用、健壮、可扩展。Phase 11 的详细执行清单见 `docs/implementation/phases/phase-11-headless-core-validation.md`。

阶段定位：

- Phase 11 是一次工程节奏校正：先证明非 UI 核心链路稳定，再回到 Desktop Presentation 做最终承载。
- Phase 11 不追求新增 GUI 能力，不继续打磨 Dialog、菜单、列表和页面布局。
- Phase 11 的验收不依赖手工点击桌面 UI，而依赖自动化测试和无 UI sample。
- Presentation 在本阶段只允许修阻塞性 crash 或组合根问题，不作为业务功能验证入口。

涉及模块：

1. Core：值对象、模型、端口、错误、能力语义补测。
2. Application：账号、连接测试、远程入口解析、远程浏览、传输创建等用例 scenario tests。
3. Providers：执行 fake object storage provider contract tests、registry/factory tests、阿里云 OSS opt-in integration tests，并补齐 SFTP/FTP/WebDAV 文件传输 provider 的 headless 契约验证；网盘 API 后置。
4. Infrastructure：本地配置、账号仓储、凭据存储、状态存储、坏数据恢复测试。
5. Transfer：任务模型、调度、状态机、队列和历史查询无 UI 测试。Transfer 专项测试矩阵见 `docs/implementation/testing/phase-12-transfer-testing.md`。
6. Tests：建立 unit / contract / scenario / integration / regression 分层。
7. Samples / Headless Runner：提供 console/headless 方式跑真实闭环。
8. Presentation：冻结新 UI 能力，只做必要隔离和阻塞修复。

允许做：

- 增加 Core / Application / Providers / Infrastructure / Transfer 的测试覆盖。
- 增加 fake object storage provider、in-memory repository、临时目录 fixture、headless runner。
- 增加真实 OSS opt-in integration tests，默认不在普通 `dotnet test` 中执行。
- 调整 Application 结果对象，使 UI 后续能稳定展示状态和错误。
- 修复非 UI 模块中暴露出的真实 bug。

禁止做：

- 继续新增复杂 UI 页面、Dialog 布局或左侧菜单动态交互。
- 把核心功能正确性依赖到 GUI 手工点击。
- 在 ViewModel 中绕过 Application 直接调用 provider、repository、credential store 或 Transfer runtime。
- 让真实 provider integration tests 默认依赖外网或真实账号。
- 让 secret material 进入日志、错误详情、测试输出或普通配置文件。

完成条件：

- `dotnet build AtomBox.slnx` 通过。
- `dotnet test AtomBox.slnx` 通过，且默认不需要真实云账号。
- 至少一个无 UI 账号/远程浏览 scenario 跑通。
- 至少一个无 UI Transfer scenario 跑通。
- Aliyun OSS 真实账号测试可通过环境变量 opt-in 执行。
- SFTP/FTP/WebDAV 文件传输 provider 进入 Phase 11 headless 验证范围；网盘 API 不进入当前完成条件。
- 非 UI 模块错误详情和日志都不泄漏 secret。
- Presentation 没有新增业务绕行路径。

阶段完成后，才建议重新回到 Presentation/UI 阶段，集中做左侧菜单动态账号节点、远程浏览账号选择、bucket / 文件列表交互、账号管理页和 Dialog 最终整理。
