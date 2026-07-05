# AtomBox 实现路线图

> 文档状态：第一版发布基线冻结
>
> 冻结时间：2026-06-22
>
> 冻结范围：当前文档定义的 Phase 0-12 工程实现顺序、阶段边界、禁止跳步规则、阶段完成条件和 v0.1 发布基线状态
>
> 变更规则：实现阶段不得随意调整本文档定义的实现顺序；如需调整，必须先说明原因，再同步更新相关 architecture、modules、ui 文档。

本文档定义 AtomBox 从设计文档进入代码实现时的落地顺序。

它不是功能愿望清单，也不是版本发布计划。它的作用是防止实现阶段乱跳层、乱引依赖、过早接入具体 SDK、过早写复杂 UI。

## 1. 总原则

实现顺序必须遵守以下原则：

- 先工程骨架，后业务功能。
- 先 Core 契约，后 Application 编排。
- 先组合根和 DI 边界，后具体服务实现。
- 先 Desktop 空 Shell，后业务页面。
- 先最小闭环，后真实 provider 扩展。
- 先可编译、可启动、依赖方向正确，再追求功能完整。

每次开始实现代码前，必须先读取 `docs/architecture/overview.md` 和 `docs/architecture/lifecycle.md`。这两个文件是统一纲领，不能靠记忆替代。

每次开始实现某个模块或页面前，必须按模块名和功能名读取相关设计文档：

- 实现 Core / Application / Transfer / Providers / Infrastructure / Presentation 时，读取 `docs/modules/` 下对应模块目录中的文档。
- 实现 Desktop 技术骨架、导航、ViewFactory、DI、AtomUI 集成时，读取 `docs/modules/presentation/` 下对应文档。
- 实现具体 UI 页面或交互时，读取 `docs/ui/` 下对应页面文档。
- 如果某个实现同时跨越多个模块，必须读取所有相关模块文档，不允许只读当前正在编辑的项目文档。

这是实现阶段的上下文加载规则。不要假装记得所有设计细节；上下文窗口有限，必须用文档重新建立局部事实。

禁止为了快速看到效果而直接写阿里云 OSS、SFTP、网盘 API 或复杂传输逻辑。那会把工程结构写歪。

## 2. Phase 索引

阶段细节已经拆分到 `docs/implementation/phases/`。`roadmap.md` 只保留总原则、索引、跳步规则和当前发布基线状态。

| Phase | 主题 | 详细文档 |
|---|---|---|
| Phase 0 | 工程骨架 | `phases/phase-00-engineering-skeleton.md` |
| Phase 1 | Core 最小契约 | `phases/phase-01-core-minimal-contract.md` |
| Phase 2 | DI 与组合根边界 | `phases/phase-02-di-composition-root.md` |
| Phase 3 | Desktop 空 Shell | `phases/phase-03-desktop-empty-shell.md` |
| Phase 4 | Infrastructure 最小本地实现 | `phases/phase-04-infrastructure-minimal-local.md` |
| Phase 5 | Application 用例骨架 | `phases/phase-05-application-usecase-skeleton.md` |
| Phase 6 | Providers 第一条链路 | `phases/phase-06-providers-first-path.md` |
| Phase 7 | Transfer 最小闭环 | `phases/phase-07-transfer-minimal-loop.md` |
| Phase 8 | 真实 Provider 扩展 | `phases/phase-08-real-provider-expansion.md` |
| Phase 9 | 产品可试用性收口 / Release Readiness | `phases/phase-09-release-readiness.md` |
| Phase 10 | 真实可用性打磨 / Usability Hardening | `phases/phase-10-usability-hardening.md` |
| Phase 11 | 无 UI 核心能力验证 / Headless Core Validation | `phases/phase-11-headless-core-validation.md` |

## 3. 测试矩阵索引

长期可复用的测试规范和测试矩阵统一放在 `docs/implementation/testing/`。

| 范围 | 文档 |
|---|---|
| Provider 契约、OSS、SFTP、FTP、WebDAV 测试矩阵 | `testing/phase-11-provider-test-matrix.md` |
| Transfer 调度、状态、队列、历史和集成测试矩阵 | `testing/phase-12-transfer-testing.md` |

## 4. 跳步规则

以下跳步禁止发生：

- 未完成 Phase 0 就写业务功能。
- 未完成 Core 契约就写真实 provider。
- 未完成组合根就让模块自己解析 DI。
- 未完成 Desktop 空 Shell 就写复杂业务页面。
- 未完成 fake / test provider 链路就接多个真实 SDK。
- 未完成最小 Transfer 闭环就写复杂并发、断点续传、自动恢复。
- 未完成 Phase 9 可试用性收口就进入高级传输能力、完整 OAuth、自动恢复或复杂 provider session 复用。
- 未完成 Phase 10 真实可用性打磨就继续扩展新 provider、高级传输能力、完整 OAuth 或自动恢复。
- 未完成 Phase 11 无 UI 核心能力验证就继续大规模打磨 Presentation UI 或新增复杂交互。

如果必须跳步，必须先更新本文档，并说明跳步原因、风险和回滚方式。

## 5. v0.1 发布基线状态

截至 2026-06-22，当前代码已经从 Phase 11/12 的 headless 验证继续推进到 Desktop Presentation 收口，并形成可发布的第一版基线。本文档不再把“切换到 Phase 11”作为当前推荐起点。

当前 v0.1 发布基线能力：

- Desktop Shell、首页、左侧菜单、远程存储汇总页、账号管理、应用设置、传输队列、传输历史已经形成可用闭环。
- Provider 主路径包含对象存储、SFTP、FTP、WebDAV；对象存储已接入阿里云 OSS、腾讯 COS、七牛 Kodo、又拍云 USS、华为云 OBS、百度 BOS、京东云 OSS、青云 QingStor、火山引擎 TOS 等 provider。
- Transfer 支持上传、下载、多任务并发、运行中取消、失败/中断重试、速度统计、队列和历史查询。
- 账号、凭据、本地设置、日志、状态和传输记录通过 Application / Infrastructure 路径进入 UI，不允许 UI 绕过 Application 直接访问 Provider 或存储实现。
- Windows self-contained 单文件发布脚本已经作为第一版发包路径；trim / AOT 仍属于后续体积优化探索。

当前 v0.1 不承诺能力：

- FTPS。
- 完整 OAuth 授权体验、后台 token refresh 和网盘产品级体验。
- 断点续传、分片续传恢复、自动恢复历史任务、全局限速、跨 provider 同步。
- 文件夹递归上传/下载、多选批量远程文件操作、缩略图和预览。

下一阶段建议：

```text
v0.1 发布前：
  -> 固定 build / publish 命令
  -> 人工桌面 smoke test
  -> 真实账号最小路径验收
  -> secret 泄漏检查
  -> 打包产物体积记录

v0.1 发布后：
  -> 根据真实使用反馈修 bug
  -> 补齐 provider opt-in 集成测试报告
  -> 参考 professionalization-roadmap.md 规划专业化能力
  -> 再讨论 FTPS、网盘 OAuth、断点续传、体积优化和更复杂 UI 能力
```
