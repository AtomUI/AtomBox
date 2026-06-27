# AtomBox 模块文档索引

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-13
>
> 冻结范围：当前文档定义的架构、模块边界、依赖关系、目录规划或实现约束
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的设计边界，必须先说明原因，再同步更新相关设计文档。

本文件是 AtomBox 模块文档的导航页，也是模块边界速查表。

AI 或开发者在修改代码前，必须先阅读本文件，判断改动属于哪个模块，再阅读对应模块文档。不要凭文件名乱放代码，那是架构腐烂的开始。

## 文档冻结状态

当前架构与模块设计文档处于阶段性冻结状态。

AI 或开发者在实现代码时必须遵守这些文档。除非用户明确要求重新设计架构，否则不得主动修改模块边界、依赖方向、跨模块端口归属或目录规划。

如果实现过程中发现文档与代码目标冲突，必须先暂停实现，说明冲突点，并先更新设计文档，再继续编码。

## 1. 模块总览

| 模块 | 文档 | 物理项目 | 核心职责 |
| --- | --- | --- | --- |
| Core | [core.md](core.md) | `AtomBox.Core` | 产品内核，定义核心模型、值对象、抽象接口、能力、错误和领域规则。 |
| Application | [application.md](application.md) | `AtomBox.Application` | 用户用例编排，把 UI 动作转换为应用流程。 |
| Presentation | [presentation.md](presentation.md) | `AtomBox.Desktop` | Avalonia / AtomUI UI、ViewModel、桌面交互和组合根。 |
| Transfer | [transfer.md](transfer.md) | `AtomBox.Transfer` | 传输任务调度，管理队列、并发、暂停、取消、重试、进度。 |
| Providers | [providers.md](providers.md) | `AtomBox.Providers` | 具体存储后端适配，把 SDK/API/协议转换为 Core 统一模型。 |
| Infrastructure | [infrastructure.md](infrastructure.md) | `AtomBox.Infrastructure` | 配置、凭据、日志、本地持久化、缓存等技术实现。 |

## 2. 推荐阅读顺序

AI 读取项目时，按以下顺序阅读文档：

```text
1. Docs/architecture/overview.md
2. Docs/modules/overview.md
3. Docs/modules/core.md
4. Docs/modules/application.md
5. Docs/modules/transfer.md
6. Docs/modules/providers.md
7. Docs/modules/infrastructure.md
8. Docs/modules/presentation.md
```

不要先读 UI 文档就开始写 ViewModel。Presentation 最容易把代码诱导成“ViewModel 直接调 SDK”的烂结构，必须先理解 Core、Application、Transfer、Providers、Infrastructure 的边界。

## 3. 模块依赖摘要

代码依赖必须向 Core 收敛：

```text
AtomBox.Desktop
  -> AtomBox.Application
  -> AtomBox.Transfer
  -> AtomBox.Providers
  -> AtomBox.Infrastructure
  -> AtomBox.Core

AtomBox.Application
  -> AtomBox.Core

AtomBox.Transfer
  -> AtomBox.Core

AtomBox.Providers
  -> AtomBox.Core

AtomBox.Infrastructure
  -> AtomBox.Core
```

依赖规则摘要：

- `Core` 不依赖任何外层模块。
- `Application` 只编排用户用例，不依赖 UI、具体 SDK 或具体 provider 实现。
- `Transfer` 只做任务调度，不依赖具体 provider 实现。
- `Providers` 负责后端适配，可以依赖外部 SDK，但不能依赖 UI。
- `Infrastructure` 负责技术实现，不承载用户用例。
- `Desktop / Presentation` 是 UI 和组合根，可以引用实现模块，但不能让 UI 类型向内层泄漏。
- 跨模块稳定端口统一定义在 `Core`，实现由 `Providers`、`Infrastructure`、`Transfer` 提供，并由 `Desktop` 组合根注入。

## 4. AI 修改代码强约束

AI 在修改代码前必须遵守以下规则：

- 先判断改动属于哪个模块，再阅读对应模块文档。
- 不得在 Core 中加入 Avalonia、AtomUI、日志、配置读写、数据库、云 SDK、FTP/SFTP 库、HTTP API 实现。
- 不得在 ViewModel 中直接调用 Provider、Infrastructure、Transfer 或具体 SDK。
- 不得把 SDK DTO、API response、协议库对象泄漏出 Providers。
- 不得让 SDK/API/协议异常穿透 Providers；Core 只定义错误模型，Application 只消费 Core 错误结果。
- 不得在 Application 或 Transfer 中定义跨模块稳定端口；这类端口必须优先归入 Core。
- 不得在 Transfer 中引用具体 Provider 实现。
- 不得让 Transfer 依赖 Application。
- 不得在 Infrastructure 中编写用户用例流程。
- 不得用裸 `string` 在系统中长期表达远程路径，应使用 Core 中的 `RemotePath` 或等价值对象。
- 不得把敏感凭据明文保存到普通配置文件。
- 不得为了省事把所有服务注册成 Singleton；生命周期必须基于对象职责单独判断。
- 不得绕过 Application 直接从 UI 进入底层实现。

## 5. 常见任务路由

| 任务 | 应修改模块 |
| --- | --- |
| 新增远程资源模型、路径模型、能力模型、错误模型 | Core |
| 新增账号、浏览、设置、传输创建等用户流程 | Application |
| 新增上传/下载队列、重试、限速、进度聚合 | Transfer |
| 接入阿里云 OSS、腾讯 COS、七牛 Kodo、又拍云 USS、华为云 OBS、百度智能云 BOS、京东云 OSS、青云 QingStor、火山引擎 TOS、SFTP、FTP、WebDAV、阿里云盘、百度网盘等后端 | Providers |
| 新增 SDK/API/协议异常到统一错误模型的映射 | Providers；如需新增稳定错误码或错误分类，先改 Core |
| 保存账号配置、凭据加密、本地缓存、日志初始化 | Infrastructure |
| 新增窗口、页面、ViewModel、图标、样式、交互状态 | Presentation |
| 调整模块引用关系、项目拆分、目录布局 | Architecture 文档和对应模块文档 |
| 新增跨模块稳定接口，例如仓储、凭据、provider factory、transfer store | Core |

## 6. 文档维护规则

- 新增模块时，必须新增对应模块文档，并更新本文件。
- 调整模块职责时，必须同步更新本文件和对应模块文档。
- 调整依赖关系时，必须同步更新 `Docs/architecture/overview.md` 和本文件。
- 新增重要业务对象时，先判断是否属于 Core；如果属于 Core，先更新 `core.md`。
- 新增跨模块稳定端口时，先更新 `core.md`，不要直接放进 Application 或 Transfer。
- 新增 provider 时，先更新 `providers.md`，再进入实现。
- 新增 UI 工作区或页面时，先更新 `presentation.md`，再进入实现。
- 文档不是摆设。文档和代码不一致时，先修正设计，再改代码。

## 7. 生命周期阅读规则

模块职责文档只说明“这个模块该做什么、不该做什么”。对象和服务“应该活多久”统一阅读 `Docs/architecture/lifecycle.md`。

AI 在实现模块前，必须同时读取对应模块文档和生命周期文档。尤其是以下场景，不能只凭模块职责推断：

- 新增业务对象。
- 新增运行时服务。
- 新增后台任务。
- 新增 DI 注册。
- 新增 provider 创建逻辑。
- 新增传输任务状态处理。
- 新增凭据读取、轮换或清理逻辑。

如果模块文档和生命周期文档出现冲突，以更严格的边界为准，并先更新文档再实现代码。
