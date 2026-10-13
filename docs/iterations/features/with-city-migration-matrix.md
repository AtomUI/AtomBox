# AtomBox × AtomUI.City 迁移主矩阵

> 状态：Active / Governing
>
> 实施分支：`feature/with-city`
>
> 创建时间：2026-10-13
>
> 覆盖范围：AtomBox 全部模块 × AtomUI.City 全部能力模块

## 文档权威性

本文是 AtomBox 后续 City 化工作的总纲和首要执行依据。任何 City 相关审计、方案讨论、City 能力补齐、AtomBox 迁移、测试和验收，都必须先在本文中找到或新增对应矩阵单元，再进入专项文档和代码施工。

本文统领迁移范围、审计顺序、决策状态和完成证据，但不取代两个项目各自的正式设计文档：

1. 迁移范围、优先级、状态和下一项工作，以本文为准。
2. AtomBox 模块职责和依赖边界，以 `docs/architecture/`、`docs/modules/` 为准。
3. City 能力合同和实现约束，以 City 仓库的 ADR、Feature、API、Lifecycle、Testing 和 Compatibility 文档为准。
4. 单项冲突、方案和实施细节，记录在本文链接的专项 feature 文档中。
5. 如果四者冲突，先停止代码施工，修正文档并在本文记录决策，不得用既成代码反推设计。

“审计某个 AtomBox 模块”不等于“给该模块直接添加 City 引用”。审计必须同时判断直接引用、通过 Desktop 组合、通过应用 adapter、只复用标准 .NET 合同以及明确不采用五种结果，优先保持合理的依赖方向。

## 能力归属铁律

City 能力的实施位置必须跟随**业务能力的所有权**，不得跟随最先发现问题、发起调用或展示结果的上层模块。这是本矩阵高于审计顺序、迁移便利性和局部改造成本的纲领性约束。

具体规则如下：

1. 如果 `AtomBox.Desktop` 中的一段业务是通过 `AtomBox.Application`、`AtomBox.Transfer`、`AtomBox.Providers`、`AtomBox.Infrastructure` 或其他上游基础模块提供的能力实现，那么适用的 City 能力原则上必须在拥有该业务语义、状态、生命周期、并发和恢复责任的上游模块中接入，不能在 Desktop 中重新包装一套业务实现。
2. Desktop 负责窗口、视图、ViewModel、交互状态、UI 线程调度和展示投影；它可以消费上游模块通过 City 或稳定合同提供的状态与事件，但不得为了避免迁移上游模块，而在 Desktop 中接管上游业务的事实源、任务调度、状态机、事件时序或错误恢复。
3. 例如传输队列、进度、取消、重试和传输状态变化属于 `AtomBox.Transfer`。即使这些能力当前由 Desktop 页面调用和展示，若要采用 City.State、City.EventBus 或其他 City 能力，也必须先审计并改造 `Transfer × City` 对应矩阵单元；不能仅在 Desktop 中增加轮询协调器或业务事件来宣称传输能力已经完成 City 化。
4. 同理，账户用例和账户目录事实首先属于 `AtomBox.Application` 及其下游端口。Desktop 可以持有账户列表的纯展示投影，但账户变更事实、共享业务状态和跨调用方一致性不能因为当前入口是桌面对话框，就被定义成 Desktop 业务。
5. 如果为了保持核心模块对 City 的依赖隔离而采用标准 .NET 端口加 City adapter，端口和业务触发点仍必须定义在业务所有者模块；adapter 应放在该模块的明确集成边界或组合根中，并且只能做协议转换、注册和生命周期接线，不能承载查询、聚合、重试、并发控制或状态恢复等业务逻辑。必要时可新增诸如 `AtomBox.Transfer.City` 的专用集成项目，但不得把它伪装成 Desktop 私有实现。
6. Desktop 中发现的候选能力一旦确认属于上游模块，必须把主审计单元迁移到相应的 `上游模块 × City 能力`，Desktop 单元只记录消费方式和 UI 适配责任。不得以“调用点位于 Desktop”为理由，把矩阵状态错误地记在 `Desktop × City`。

归属判断至少回答以下问题：

- 去掉 Avalonia、窗口和 ViewModel 后，这项能力是否仍然成立？如果成立，通常不属于 Desktop。
- 谁拥有持久化事实源、业务状态机和一致性规则？City 能力原则上落在该所有者模块。
- CLI、后台服务或未来其他前端是否也应获得同样行为？如果是，行为不得只实现在 Desktop。
- Desktop 当前做的是纯展示投影，还是正在补偿上游模块缺少的业务能力？后者必须回到上游模块治理。

若业务所有权与现有项目依赖方向发生冲突，应先调整模块边界或建立专用集成层，再实施 City 迁移；不得用 Desktop 层的快捷实现绕过架构决策。

## 两个审计维度

AtomBox 维度：

- `Core`：领域模型、值对象、稳定端口、能力和错误模型。
- `Application`：账号、浏览、设置和传输等用户用例编排。
- `Transfer`：传输队列、调度、worker、进度、重试和取消。
- `Providers`：对象存储、文件协议和网盘后端适配。
- `Infrastructure`：配置、凭据、本地持久化、缓存和日志。
- `Desktop`：Avalonia/AtomUI UI、ViewModel、桌面适配器和组合根。
- `Engineering`：测试项目、构建、发布、模板和开发工具；它不是业务程序集，但必须纳入 City 工程能力审计。

City 维度：

- 运行时基础：`Core`、`Mvvm`、`Routing`、`Presentation`。
- 应用能力：`State`、`EventBus`、`Data`、`Security`、`Localization`、`PluginSystem`。
- 工程能力：`Build`、`Generators`、`Testing`、`Cli`、`Templates`。

## 状态图例

| 标记 | 含义 |
| --- | --- |
| `已` | 已采用，已有实现和自动化证据。 |
| `审-H` | 尚未形成最终结论，高优先级审计。 |
| `审-M` | 尚未形成最终结论，中优先级审计。 |
| `审-L` | 尚未形成最终结论，低优先级或当前业务触发面较小。 |
| `适` | 审计后决定通过 adapter、组合根或标准 .NET 合同间接使用。 |
| `否` | 审计后明确不采用，并已记录原因。 |
| `阻` | 已决定采用，但 City 能力缺失或合同冲突，必须先修复 City。 |

`审-*` 只表示审计优先级，不代表最终一定迁移，也不授权增加项目引用。矩阵状态只能在专项审计形成结论并补齐证据后变更。

## 当前事实基线

截至本文建立时，生产程序集里只有 `AtomBox.Desktop` 直接使用 City；测试程序集里 `AtomBox.Desktop.Tests` 直接引用 City.Localization。其余业务模块没有 City 项目引用；这是当前事实，不是永久禁止未来合理使用 City 的结论。

| AtomBox 模块 | 当前直接采用的 City 能力 | 当前说明 |
| --- | --- | --- |
| Core | 无 | 保持领域内核纯净；是否存在可复用能力仍须正式审计。 |
| Application | 无 | 当前只依赖 AtomBox.Core。 |
| Transfer | 无 | 当前只依赖 AtomBox.Core，拥有应用级持久传输调度。 |
| Providers | 无 | 当前只依赖 AtomBox.Core 和具体 SDK/协议库。 |
| Infrastructure | 无 | 当前只依赖 AtomBox.Core 和技术实现库。 |
| Desktop | Core、Mvvm、Routing、Presentation、Localization、Build、Generators | Host、生命周期、路由、视图组织、本地化和 MVVM 迁移已完成自动门禁；真实 UI 验证仍由人工完成。 |
| Engineering | Localization（仅 Desktop.Tests） | Desktop 本地化集成测试直接引用 City.Localization；尚未正式评估 City.Testing、CLI 和 Templates。 |

## 主矩阵：运行时基础与交互能力

| AtomBox \ City | Core | Mvvm | Routing | Presentation | State | EventBus | Localization |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Core | 审-M | 审-L | 审-L | 审-L | 审-L | 审-M | 审-L |
| Application | 审-M | 审-L | 审-M | 审-L | 阻 | 否 | 审-M |
| Transfer | 审-H | 审-L | 审-L | 审-L | 审-H | 审-H | 审-L |
| Providers | 审-M | 审-L | 审-L | 审-L | 审-M | 审-M | 审-L |
| Infrastructure | 审-H | 审-L | 审-L | 审-L | 审-H | 审-M | 审-L |
| Desktop | 已 | 已 | 已 | 已 | 审-H | 审-H | 已 |
| Engineering | 审-M | 审-M | 审-M | 审-M | 审-M | 审-M | 已 |

## 主矩阵：数据、安全与扩展能力

| AtomBox \ City | Data | Security | PluginSystem |
| --- | --- | --- | --- |
| Core | 审-L | 审-M | 审-M |
| Application | 审-M | 审-H | 审-M |
| Transfer | 审-M | 审-L | 审-M |
| Providers | 审-H | 审-H | 审-H |
| Infrastructure | 审-M | 审-H | 审-M |
| Desktop | 审-L | 审-M | 审-M |
| Engineering | 审-M | 审-M | 审-M |

## 主矩阵：工程能力

| AtomBox \ City | Build | Generators | Testing | CLI | Templates |
| --- | --- | --- | --- | --- | --- |
| Core | 审-L | 审-L | 审-M | 审-L | 审-L |
| Application | 审-L | 审-L | 审-M | 审-L | 审-L |
| Transfer | 审-L | 审-L | 审-H | 审-L | 审-L |
| Providers | 审-L | 审-L | 审-H | 审-L | 审-L |
| Infrastructure | 审-L | 审-L | 审-H | 审-L | 审-L |
| Desktop | 已 | 已 | 审-H | 审-M | 审-M |
| Engineering | 审-H | 审-H | 审-H | 审-M | 审-M |

## 审计顺序

顺序表示评估先后，不预设一定要迁移：

1. `Desktop` 已采用能力收口：以现有 Desktop 总结和 MVVM 21 项记录作为已完成基线，人工 UI 烟测仍是最终验收项。
2. `Core`：先确认领域纯净边界和 City 类型可见性，为其余模块建立共同约束。
3. `Application`：重点审计 State、EventBus、Security 以及 Routing 参数与用例边界。
4. `Transfer`：重点审计长期任务所有权、State、EventBus、生命周期、测试时钟和并发测试能力。
5. `Providers`：重点审计 City.Data、Security、PluginSystem、连接生命周期和统一诊断管线。
6. `Infrastructure`：重点审计配置/生命周期、Security、State 持久化、诊断和缓存边界。
7. `Desktop` 尚未采用的能力：只审计真正由 Desktop 所有的 State、EventBus、Security、PluginSystem 使用场景；发现账户、传输、Provider、持久化等上游业务候选时，必须依据“能力归属铁律”退回对应上游矩阵单元审计，Desktop 只记录消费和 UI 适配方式。
8. `Engineering`：统一审计 City.Testing、Build、Generators、CLI 和 Templates，形成长期门禁和新模块脚手架规则。

如前序模块发现跨模块稳定端口或依赖方向必须调整，应先更新 AtomBox 架构文档，再继续后序审计。

## 单个矩阵单元的必答问题

每次只处理一个明确的“AtomBox 模块 × City 能力”单元，至少回答：

1. 该能力真正由哪个 AtomBox 模块拥有？去掉 Desktop 后是否仍然成立？当前审计单元是否选对？
2. AtomBox 当前实现是什么，真正业务诉求是什么？
3. City 当前能力和正式合同是什么？
4. 两者是能力重叠、语义冲突、能力缺口，还是根本不适用？
5. 采用 City 后，直接引用应落在哪个物理项目？是否破坏依赖方向？
6. 应选择直接采用、专用集成项目、adapter、组合根接入、标准 .NET 合同传递，还是明确不采用？
7. City 是否需要先补能力？若需要，先在 City 文档记录并在 City 仓库实现、测试、提交、同步。
8. AtomBox 要删除哪些旧实现，保留哪些应用特有逻辑？
9. 自动测试、构建、发布和人工 UI 验收证据分别是什么？
10. 哪些正式架构、模块、API 或兼容性文档需要同步？

没有回答完这些问题，不得把单元从 `审-*` 改成已决状态，也不得开始大规模代码替换。

## 单元决策记录格式

每个已审计单元必须在本文追加或链接一份记录，至少包含：

| 字段 | 内容 |
| --- | --- |
| Matrix ID | 例如 `AB-TRANSFER × CITY-EVENTBUS`。 |
| AtomBox 现状 | 关键类型、所有权、生命周期、线程和异常合同。 |
| City 现状 | 对应 Feature/API/ADR 及版本或提交。 |
| 冲突与收益 | 能解决什么问题，会引入什么代价。 |
| 决策 | 直接采用 / adapter / 组合根 / 标准 .NET 合同 / 不采用 / City 先补齐。 |
| 依赖方式 | 具体项目引用和依赖方向。 |
| 实施范围 | City 与 AtomBox 各自修改项。 |
| 验收证据 | 测试、构建、发布、人工验证。 |
| 文档 | 两个仓库中需要同步的文档链接。 |
| 状态 | Planned / City Blocked / Implementing / Automated / Manual Pending / Done。 |

## 当前专项记录

| 范围 | 文档 | 状态 |
| --- | --- | --- |
| Desktop：Core / Routing / Presentation / Localization / Build / Generators | [AtomBox × AtomUI.City 迁移审计](with-city.md) | 自动化完成，人工 UI 验证待完成。 |
| Desktop：Mvvm / Presentation 生命周期扩展 | [AtomBox × AtomUI.City MVVM 迁移](with-city-mvvm.md) | 21 项已实施，人工 UI 验证待完成。 |
| Application：State / EventBus | [AtomBox.Application × AtomUI.City.State 迁移](with-city-application.md) | State 已决定直接采用，等待 City AUC-STATE-009；EventBus 本轮不采用。 |
| 其余矩阵单元 | 后续按审计顺序建立专项记录 | 待审计。 |

## Application 审计结论

- `AB-APPLICATION × CITY-STATE`：账号目录与应用设置是跨调用方共享的运行时事实投影，业务所有者是 Application。决定由 `AtomBox.Application` 直接引用 City.State，Repository 仍为持久化事实源，Desktop 只读消费。当前标记为 `阻`，前置条件是 City `AUC-STATE-009` 原子 Observe 和 DI Writer factory。
- `AB-APPLICATION × CITY-EVENTBUS`：当前账号/设置需求都是“当前事实 + 后续变化”，State 已完整覆盖；为同一事实再发业务事件会形成双通道一致性问题，因此标记为 `否`。
- Desktop State 候选已完成归属回退：账号和设置不在 Desktop 自建 State；Desktop 只负责 Dispatcher、ActivationScope 和 UI 投影。

## 状态维护规则

- 每次开始讨论新单元前，先引用 Matrix ID、当前标记和审计重点。
- 每次形成方案后，先更新本文和对应专项文档，再修改代码。
- City 有通用能力缺口时，矩阵单元标记为 `阻`；City 修复并同步远端后，AtomBox 才继续实施。
- AtomBox 自动门禁通过后只能标记为 `Automated`；涉及真实桌面交互时，必须完成人工 UI 验证才能标记 `Done`。
- 每次提交应能回溯到矩阵单元；不得用“顺手重构”扩大迁移范围。
- 新增 AtomBox 模块或 City 能力模块时，必须先扩展本文矩阵。
- 一项迁移完成后，同步更新本文状态、专项文档、AtomBox 正式模块文档以及必要的 City 正式文档。
