# Phase 10 真实可用性打磨规划

> 文档状态：阶段性冻结
>
> 创建时间：2026-06-15
>
> 冻结时间：2026-06-15
>
> 范围：Phase 10 真实账号、真实路径、真实文件、真实错误下的可用性打磨、验收矩阵和结束判定
>
> 变更规则：Phase 10 实现阶段不得随意扩展到高级传输、完整 OAuth、自动恢复或新增 provider；如需调整，必须先同步更新 `roadmap.md` 和相关 architecture、modules、ui 文档。

本文档承接 `roadmap.md` 的 Phase 10。`roadmap.md` 定义阶段边界，本文档定义 Phase 10 要解决的具体问题和验收方式。

## 1. Phase 10 定位

Phase 9 已经让 AtomBox 达到可试用 MVP：主链路能闭环，失败可诊断，危险操作有确认，启动和中断有解释。

Phase 10 解决的问题是：

> 用户拿真实账号运行 AtomBox 时，能否顺利完成第一轮真实使用，并且遇到问题时知道怎么改。

Phase 10 的关键词：

- 真实账号。
- 真实错误。
- 真实路径。
- 真实文件。
- 真实反馈。
- 真实发布前检查。

## 2. 不解决的问题

Phase 10 明确不做：

- 不新增 provider。
- 不做完整 OAuth 授权流程。
- 不做后台 token refresh。
- 不做断点续传。
- 不做分片上传。
- 不做复杂并发队列。
- 不做自动恢复历史任务。
- 不做跨 provider 同步。
- 不做文件夹递归上传 / 下载。
- 不做高级搜索、缩略图、预览。
- 不做完整安装包 / 自动更新系统。
- 不做账号云同步或多设备同步。

## 3. 总目标

Phase 10 完成后，AtomBox 应具备：

- 用户可以配置一个真实阿里云 OSS 账号并完成基本文件操作。
- 用户可以看懂账号配置哪里错了。
- 用户可以看懂 provider 返回的常见错误。
- 用户可以安全地上传、下载、删除单文件。
- 用户可以从队列 / 历史判断任务是否成功、失败、取消、中断。
- 用户可以根据错误详情采取下一步动作。
- 开发者有固定手工验收路径判断当前版本是否可以给自己试用。

## 4. 执行切片

### 4.1 阿里云 OSS 真实可用性优先

需要解决：

- OSS endpoint 输入是否清晰。
- region 和 endpoint 的关系是否清晰。
- bucket 字段为空时，远程浏览 root 的行为是否符合预期。
- bucket 字段非空时，是否直接进入 bucket root。
- root / bucket root / folder / file 的路径展示是否稳定。
- 上传时 remote path 是否正确拼接。
- 下载时 local path 是否合理。
- 删除时是否只允许删除文件，避免误删目录。
- 测试连接到底测的是 root 还是 bucket。
- 连接测试成功详情是否说明 provider、endpoint、region、bucket/root、capability。
- 连接测试失败详情是否说明认证失败、权限不足、bucket 不存在、endpoint 错误、网络不可达、服务端限流或异常。
- OSS SDK exception 是否全部映射到 `StorageError`。
- OSS 错误详情中是否避免泄漏 AccessKeySecret。

手工验收路径：

1. 输入错误 AccessKey。
2. 输入错误 Secret。
3. 输入错误 endpoint。
4. 输入不存在 bucket。
5. 输入无权限 bucket。
6. 输入正确账号和 bucket。
7. 列表 bucket。
8. 上传小文件。
9. 下载该文件。
10. 删除该文件。
11. 查看传输历史。
12. 查看失败详情。

### 4.2 账号配置体验打磨

需要解决：

- provider 选择后字段动态变化。
- 必填字段明确。
- secret 字段不回显。
- 编辑账号时 secret 留空表示不修改。
- endpoint 输入前后空格清理。
- endpoint 是否允许带协议头有统一规则。
- port 字段校验为数字。
- bucket / rootPath / driveId 等字段有基础校验。
- 显示名为空时有合理默认名。
- 测试连接按钮在表单明显无效时禁用。
- 测试连接过程中防止重复点击。
- 测试连接成功后展示足够信息。
- 测试连接失败后展示下一步建议。
- 保存不强制测试连接，但未测试状态应有可读提示。
- 删除账号前明确说明不删除云端文件，但会影响关联任务展示。
- 账号列表展示 provider、display name、endpoint、region、bucket/root 摘要、最近更新时间。

### 4.3 远程浏览体验打磨

需要解决：

- 当前路径展示人能看懂。
- root 和 bucket root 区分清楚。
- 返回上级按钮状态正确。
- 刷新按钮总是可用。
- 加载中状态防止重复操作。
- 空目录有明确状态。
- 列表失败后保留原路径和重试请求。
- 分页按钮和实际 cursor 一致。
- 文件大小格式友好。
- 修改时间本地化显示。
- folder / file 类型展示清晰。
- 文件行双击规则稳定：folder 进入目录，file 不直接下载。
- 上传按钮在 root 禁用。
- 下载按钮在 folder 禁用。
- 删除按钮在 folder 禁用。
- 删除成功后刷新当前页。
- 删除失败后可以查看详情。
- 上传 / 下载创建任务后可以快速跳到队列。
- 没有账号时远程浏览页显示明确空状态。
- 账号不可用时提示去账号页修复。

### 4.4 上传 / 下载单文件闭环打磨

需要解决：

- 上传前选择本地文件体验稳定。
- 上传到当前远程路径时 remote path 正确。
- 同名文件冲突有最小策略：默认不覆盖，已存在时失败并说明冲突。
- 下载前选择本地目标目录明确。
- 下载目标文件名保留远端文件名。
- 下载到已存在本地文件时有最小冲突策略。
- 上传 / 下载任务创建失败时有可读错误。
- 任务创建成功时提示并可跳转队列。
- 传输失败时展示 provider 错误或本地文件错误。
- 本地文件不存在、无权限、路径不可写等错误映射清楚。
- 进度显示不误导：百分比未知时不要显示 0% 卡死，完成时显示 100%。
- 速度 / 剩余时间 Phase 10 暂不做。

### 4.5 传输队列和历史体验打磨

需要解决：

- 队列页区分 Pending、Running、Succeeded、Failed、Canceled、Interrupted。
- 历史页包含 Succeeded、Failed、Canceled、Interrupted。
- Running 任务刷新频率合适。
- 取消任务立即更新 UI。
- 取消原因不显示为失败原因。
- 失败任务只有 `CanRetry` 时才显示重试。
- 中断任务说明最终状态未知。
- 重试后旧失败原因清除。
- 任务详情展示本地路径、远端路径、方向、provider/account、创建时间、更新时间、状态原因、错误类别。
- 历史清理有确认。
- 清理历史不删除正在进行的队列任务。
- 历史为空时显示明确空状态。
- 队列为空时显示明确空状态。

### 4.6 错误详情和用户下一步建议

需要解决：

- `Authentication`：建议检查 AccessKey、Secret 或 token。
- `Authorization`：建议检查 bucket 权限、RAM policy 或路径权限。
- `Network`：建议检查 endpoint、网络、代理、防火墙。
- `NotFound`：建议检查 bucket/path 是否存在。
- `Conflict`：建议检查同名文件、任务状态或重复操作。
- `Provider`：建议查看 provider 返回码和服务状态。
- `Infrastructure`：建议检查本地配置目录、状态目录和磁盘权限。
- `Validation`：建议检查表单字段格式。

错误详情可以展示：

- category。
- code。
- retryable。
- provider id。
- provider error code。
- sanitized provider message。
- endpoint / bucket / path 摘要。

错误详情不得展示：

- AccessKeySecret。
- token。
- Authorization header。
- credential raw payload。

### 4.7 设置页真实可用性

需要解决：

- 当前配置摘要够用。
- 默认下载目录可查看 / 修改。
- 提供打开配置目录、状态目录、日志目录入口。
- 重置设置有确认。
- 设置保存失败有详情。
- 设置重置后 UI 刷新。
- 不做复杂偏好设置。

### 4.8 启动诊断和本地数据恢复

需要解决：

- 启动失败时展示失败摘要、exception 类型、配置目录、状态目录、凭据目录、日志目录。
- 提供打开配置目录、打开日志目录、退出应用按钮。
- Phase 10 暂不直接提供破坏性重置本地配置按钮。
- Infrastructure 初始化失败包含足够上下文。
- 配置 JSON 损坏时明确提示是哪类文件。
- 状态 JSON 损坏时不进入正常 Shell。
- 凭据存储不可用时不进入正常 Shell。

### 4.9 发布前验收与手工测试矩阵

固定命令：

```powershell
dotnet restore AtomBox.slnx
dotnet build AtomBox.slnx --no-restore
dotnet test AtomBox.slnx --no-restore --no-build
```

桌面 smoke：

- 启动。
- 新增账号。
- 测试连接。
- 列表。
- 上传。
- 下载。
- 删除。
- 看队列。
- 看历史。

手工测试要求：

- 不提交真实凭据。
- 手工测试结果可记录在本地临时文件，不进 repo。

## 5. 推荐执行顺序

1. 文档拆分和 Phase 10 规划落盘。
2. OSS 真实可用性打磨。
3. 账号配置体验打磨。
4. 远程浏览体验打磨。
5. 上传 / 下载单文件冲突和反馈打磨。
6. 传输队列 / 历史细节打磨。
7. 错误详情下一步建议。
8. 设置页和启动诊断小收口。
9. 最终验收矩阵执行。

## 6. Phase 10 第一刀

第一刀从 OSS + 账号测试连接 + 远程路径上下文开始。

原因：

- 它最容易验证真实账号能不能用。
- 它会自然暴露 endpoint、bucket、权限、路径、错误映射问题。
- 相关改动能沉淀到 provider、account dialog、remote browser 和 error details 的共性能力中。

## 7. Phase 10 结束判定

Phase 10 可以结束的条件：

- `roadmap.md` Phase 10 完成条件全部满足。
- 本文档固定检查均有结果。
- 使用真实 OSS 账号的核心路径可在桌面应用中完成手工验收。
- 已知剩余问题不破坏真实可用性闭环，并被记录为 Phase 11 或后续增强。
- 相关文档从执行草案重新冻结。

## 8. 本轮实现记录

Phase 10 已完成的工程收口：

- 阿里云 OSS 连接测试详情展示 endpoint、region、bucket/root、目标路径和能力摘要。
- 阿里云 OSS 常见网络、限流和临时服务错误映射为统一 `StorageError`，并保留 provider error code。
- 统一错误详情按错误类别追加下一步建议，不展示 secret material。
- 账号弹窗字段提示补齐，port 增加 1-65535 基础校验。
- 账号弹窗字段变更后，连接测试状态回到需要重新测试。
- 账号管理列表展示账号范围摘要，例如 bucket 列表或具体 bucket。
- 远程浏览路径展示区分 bucket root、普通目录和 root；空状态明确展示。
- OSS 上传默认不覆盖远端已有对象，目标 key 已存在时返回 `Conflict`。
- 下载默认不覆盖本地已有文件，目标文件存在时返回 `Conflict`。
- 传输队列和历史页补齐空状态，详情中展示原因字段。
- 设置页提供打开配置目录、状态目录、日志目录入口。
- 启动失败界面展示异常类型和关键本地诊断路径。

已执行自动化验收：

- `dotnet build AtomBox.slnx --no-restore`
- `dotnet test AtomBox.slnx --no-restore --no-build`
- 桌面启动 smoke
- 边界扫描
- secret 泄漏扫描

仍需用户本地执行的手工验收：

- 使用真实阿里云 OSS 账号执行本文档 4.1 的 12 步手工路径。
- 手工验收不得把真实 AccessKey、AccessKeySecret、token 或私有 endpoint 提交到仓库。
## 附录：roadmap 原始阶段摘要

以下内容来自重组前的 docs/implementation/roadmap.md，用于保留原路线图中的阶段定位和完成条件。

## 12. Phase 10：真实可用性打磨 / Usability Hardening

目标：在 Phase 9 可试用 MVP 基础上，用真实账号、真实路径、真实文件和真实错误打磨第一轮使用体验。Phase 10 不新增大能力，重点是让用户配置真实阿里云 OSS 账号后，能稳定完成连接测试、列表、上传、下载、删除、队列/历史查看，并能根据错误详情知道下一步怎么处理。

阶段定位：

- Phase 10 是可试用 MVP 的真实使用打磨阶段，不是高级能力扩展阶段。
- Phase 10 优先把阿里云 OSS 打磨成真实 provider 样板，再把共性改动沉淀到账号、远程浏览、传输、错误详情和启动诊断。
- Phase 10 的详细执行清单、验收矩阵和结束判定见 `docs/implementation/phases/phase-10-usability-hardening.md`。

推荐顺序：

1. 阿里云 OSS 真实可用性优先：endpoint、region、bucket/root、连接测试详情、常见 OSS 错误映射和脱敏。
2. 账号配置体验打磨：动态字段、必填校验、secret 编辑语义、测试连接状态、账号摘要。
3. 远程浏览体验打磨：路径展示、空状态、加载失败重试、上传下载删除按钮能力规则、操作后反馈。
4. 上传 / 下载单文件闭环打磨：本地文件错误、远端冲突、任务创建反馈、跳转队列。
5. 传输队列和历史体验打磨：状态可读、详情充分、重试规则、空状态、历史清理安全。
6. 错误详情和下一步建议：按错误类别提供可执行建议，详情继续保持脱敏。
7. 设置页和启动诊断小收口：本地目录入口、配置/日志路径、启动失败恢复路径。
8. 发布前验收矩阵：固定命令、桌面 smoke、OSS 手工验收路径和边界扫描。

允许做：

- 调整 Provider 错误映射、连接测试路径、provider 配置字段提示和脱敏详情。
- 调整 Account / RemoteBrowser / Transfer / Settings 的 Application 结果对象和 ViewModel 展示状态。
- 增加最小单文件冲突反馈、空状态、下一步建议和跳转入口。
- 增加真实 OSS 手工验收文档和发布前可重复检查清单。

禁止做：

- 新增 provider。
- 做完整 OAuth 授权流程、后台 token refresh、provider session pool。
- 做断点续传、分片上传、复杂并发队列、全局限速或自动恢复历史任务。
- 做文件夹递归上传/下载、跨 provider 同步、搜索、缩略图或预览。
- 让 UI 直接调用 provider、Transfer 内部队列或 Infrastructure 存储实现。
- 让 SDK DTO、SDK exception、secret material 或 provider session 穿透到 Core / Application / Presentation。

完成条件：

- 使用真实阿里云 OSS 账号可以完成连接测试、列表、上传小文件、下载小文件、删除测试文件、查看队列和历史。
- OSS 常见认证、授权、网络、bucket/object 不存在、限流或服务端错误能映射为统一错误类别，并提供脱敏详情和下一步建议。
- 账号配置、远程浏览、传输队列/历史的主要空状态、失败状态和操作反馈可读。
- 发布验收清单中的 restore / build / test / desktop smoke / boundary scan / secret scan 均可执行并记录结果。
