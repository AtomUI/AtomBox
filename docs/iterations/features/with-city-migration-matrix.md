# AtomBox × AtomUI.City 迁移主矩阵

> 状态：Active / Governing / Decisions Closed
>
> 实施分支：`feature/with-city`
>
> 创建时间：2026-10-13
>
> 全矩阵决策收口时间：2026-10-16
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

截至 2026-10-16，AtomBox 全部现有模块与 City 全部现有能力的矩阵决策已经收口。`AtomBox.Application` 直接采用 City.State；`AtomBox.Transfer` 直接采用 City.Core.Module 与 City.State；`AtomBox.Desktop` 负责 Host、UI 能力和上游 State 的只读消费。其他业务模块经审计后明确不采用 City。这是当前产品和架构事实，不是永久禁止未来新需求重新开启对应单元。

| AtomBox 模块 | 当前直接采用的 City 能力 | 当前说明 |
| --- | --- | --- |
| Core | 无 | 保持领域内核纯净；全部 City 能力均已审计并明确不采用。 |
| Application | State | 拥有账号目录和应用设置的运行时投影；Repository 仍为持久化事实源。 |
| Transfer | Core、State | 通过 City Module 接入 Host 生命周期，并拥有已持久传输队列与 Runtime 健康的应用级投影；Store 仍是事实源。 |
| Providers | 无 | 全部 City 能力均已审计并明确不采用；继续依赖 AtomBox.Core 和具体 SDK/协议库。 |
| Infrastructure | 无 | 全部 City 能力均已审计并明确不采用；继续实现 AtomBox.Core 技术端口。 |
| Desktop | Core、Mvvm、Routing、Presentation、State、Localization、Build、Generators | Host、生命周期、路由、视图组织、本地化、MVVM 以及 Application State 的 UI 消费已完成自动门禁；真实 UI 验证仍由人工完成。 |
| Engineering | Core、Mvvm、Routing、Presentation、State、Localization（测试引用）、Build、Generators | Application/Transfer State 与 Desktop 集成测试直接验证 City 合同；Build 和 Generators 已进入 Desktop 编译链。City.Testing、CLI 和 Templates 经审计后不采用。 |

## 主矩阵：运行时基础与交互能力

| AtomBox \ City | Core | Mvvm | Routing | Presentation | State | EventBus | Localization |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Core | 否 | 否 | 否 | 否 | 否 | 否 | 否 |
| Application | 否 | 否 | 否 | 否 | 已 | 否 | 否 |
| Transfer | 已 | 否 | 否 | 否 | 已 | 否 | 否 |
| Providers | 否 | 否 | 否 | 否 | 否 | 否 | 否 |
| Infrastructure | 否 | 否 | 否 | 否 | 否 | 否 | 否 |
| Desktop | 已 | 已 | 已 | 已 | 已 | 否 | 已 |
| Engineering | 已 | 已 | 已 | 已 | 已 | 否 | 已 |

## 主矩阵：数据、安全与扩展能力

| AtomBox \ City | Data | Security | PluginSystem |
| --- | --- | --- | --- |
| Core | 否 | 否 | 否 |
| Application | 否 | 否 | 否 |
| Transfer | 否 | 否 | 否 |
| Providers | 否 | 否 | 否 |
| Infrastructure | 否 | 否 | 否 |
| Desktop | 否 | 否 | 否 |
| Engineering | 否 | 否 | 否 |

## 主矩阵：工程能力

| AtomBox \ City | Build | Generators | Testing | CLI | Templates |
| --- | --- | --- | --- | --- | --- |
| Core | 否 | 否 | 否 | 否 | 否 |
| Application | 否 | 否 | 否 | 否 | 否 |
| Transfer | 否 | 否 | 否 | 否 | 否 |
| Providers | 否 | 否 | 否 | 否 | 否 |
| Infrastructure | 否 | 否 | 否 | 否 | 否 |
| Desktop | 已 | 已 | 否 | 否 | 否 |
| Engineering | 已 | 已 | 否 | 否 | 否 |

## 审计顺序（已完成）

以下是本轮已经执行完成的评估顺序，不表示所有能力都必须迁移。现有矩阵已经没有开放的 `审-*` 或 `阻` 单元：

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
| Desktop：Mvvm / Presentation 生命周期扩展 | [AtomBox × AtomUI.City MVVM 迁移](with-city-mvvm.md) | 21 项已实施；已对齐 City `34ce19f` Effective Cancellation Token、UI 通知调度和 `IUiCommandFactory` 合同，人工 UI 验证待完成。 |
| Desktop：EventBus | [AtomBox × AtomUI.City 迁移审计](with-city.md) | 决策完成：不采用；保留直接 UI 通知，业务事实继续由上游 State 提供。 |
| Desktop：Security | [AtomBox × AtomUI.City 迁移审计](with-city.md) | 决策完成：不采用；外部存储连接凭据不等于 City 当前应用主体。 |
| Desktop：PluginSystem | [AtomBox × AtomUI.City 迁移审计](with-city.md) | 决策完成：不采用；当前没有插件产品合同，未来 Provider 插件运行时也不归 Desktop 所有。 |
| Desktop：Data | [AtomBox × AtomUI.City 迁移审计](with-city.md) | 决策完成：不采用；Desktop 不拥有网络传输，远程 I/O 继续由 Application/Transfer 编排并由 Providers 执行。 |
| Desktop：Testing | [AtomBox × AtomUI.City 迁移审计](with-city.md) | 决策完成：不采用；保留真实 AtomBox City Host 集成测试，当前 City.Testing 不能提供等价证据。 |
| Desktop：CLI | [AtomBox × AtomUI.City 迁移审计](with-city.md) | 决策完成：不采用；CLI 属于 Engineering，且当前命令不能覆盖 AtomBox 的真实检查与跨平台发布合同。 |
| Desktop：Templates | [AtomBox × AtomUI.City 迁移审计](with-city.md) | 决策完成：不采用；通用模板不能直接维护 AtomBox 的项目专属页面、导航、本地化和测试约定。 |
| Application：State / EventBus | [AtomBox.Application × AtomUI.City.State 迁移](with-city-application.md) | State 自动化实施完成，真实 UI 验证待完成；EventBus 本轮不采用。 |
| Application：Security | [AtomBox.Application × AtomUI.City 迁移与审计](with-city-application.md) | 决策完成：不采用；外部存储连接账号及其多凭据 lease 不等于 City Host 的单一活动应用主体。 |
| Application：Routing | [AtomBox.Application × AtomUI.City 迁移与审计](with-city-application.md) | 决策完成：不采用；Desktop 解码导航参数并转换为纯 Application 用例请求，Application 不感知 route、outlet 或 journal。 |
| Application：Core | [AtomBox.Application × AtomUI.City 迁移与审计](with-city-application.md) | 决策完成：不直接采用；Application 没有独立 Host、Module Scope、长期运行资源或 shutdown 事务，Desktop 组合根只负责注册与生命周期接线。 |
| Application：Localization | [AtomBox.Application × AtomUI.City 迁移与审计](with-city-application.md) | 决策完成：不采用；Application 返回稳定错误语义与诊断信息，Desktop 在展示时使用 City.Localization 生成可即时切换语言的用户文案。 |
| Application：Data | [AtomBox.Application × AtomUI.City 迁移与审计](with-city-application.md) | 决策完成：不采用；Application 只通过 Core Provider 端口编排用例，网络 transport、认证与协议错误仍归 Providers 所有。 |
| Application：PluginSystem | [AtomBox.Application × AtomUI.City 迁移与审计](with-city-application.md) | 决策完成：不采用；当前没有插件产品合同，未来 Provider 插件运行时也应先由 Providers 或专用集成模块拥有。 |
| Application：Testing | [AtomBox.Application × AtomUI.City 迁移与审计](with-city-application.md) | 决策完成：不采用；保留 xUnit、真实 City.State 合同和 AtomBox 专属替身。 |
| Application：Mvvm | [AtomBox.Application × AtomUI.City 迁移与审计](with-city-application.md) | 决策完成：不采用；City.Mvvm 由 Desktop 使用，Application 保留普通异步用例、权威校验和跨调用方并发保护。 |
| Application：Presentation | [AtomBox.Application × AtomUI.City 迁移与审计](with-city-application.md) | 决策完成：不采用；Application 返回前端无关用例快照，View、Window、Outlet、Interaction 和 ViewModel composition 继续由 Desktop 持有。 |
| Application：Build | [AtomBox.Application × AtomUI.City 迁移与审计](with-city-application.md) | 决策完成：不采用；City.Build 由 Desktop/Engineering 编译链持有，Application 没有 RESX、manifest、插件打包或 AOT 发布职责。 |
| Application：Generators | [AtomBox.Application × AtomUI.City 迁移与审计](with-city-application.md) | 决策完成：不采用；显式 DI 注册规模小且已有合同测试，不为生成少量注册代码人为创建无生命周期责任的 City Module。 |
| Application：CLI | [AtomBox.Application × AtomUI.City 迁移与审计](with-city-application.md) | 决策完成：不采用；City.CLI 是框架工程工具进程，未来 AtomBox 业务 CLI 也应作为独立前端调用 Application。 |
| Application：Templates | [AtomBox.Application × AtomUI.City 迁移与审计](with-city-application.md) | 决策完成：不采用；通用模板不匹配现有 Application 的业务设置、测试和无 Module 边界，也不进入生产依赖。 |
| Core：全部 City 能力与领域纯净边界 | [AtomBox.Core × AtomUI.City 迁移审计](with-city-core.md) | Core 不采用 City；不可变集合缺陷已修复并完成自动化验证。 |
| Transfer：State | [AtomBox.Transfer × AtomUI.City.State 迁移](with-city-transfer.md) | 自动化实施完成，人工 UI 验证待完成；State 阶段延后的工程问题已由 Runtime 专项接续完成。 |
| Transfer：Core / Module 后台 Runtime | [AtomBox.Transfer 后台 Runtime 与 City.Core.Module 重构决策记录](with-city-transfer-runtime.md) | 代码重构与默认自动化门禁完成：City Module 承载应用级启停，Transfer 自有 Runtime 负责持久化队列、CAS 领取、执行租约、进度、取消、恢复与有限关闭；真实 Provider/UI 人工验收待执行。 |
| Transfer：其余 City 能力 | 本文“Transfer 审计结论” | 决策完成：均不采用；UI 能力不属于 Transfer，协议与认证属于 Providers，工程能力没有独立采用场景。 |
| Providers：全部 City 能力 | [AtomBox.Providers × AtomUI.City 迁移审计](with-city-providers.md) | 决策完成：本轮全部不采用；SDK、自研 HTTP、S3Compatible、FTP/SFTP 均保持现状。 |
| Infrastructure：全部 City 能力 | [AtomBox.Infrastructure × AtomUI.City 迁移审计](with-city-infrastructure.md) | 决策完成：全部不采用；自身工程债独立登记，不以 City 迁移名义处理。 |
| Engineering：全部 City 能力 | [AtomBox Engineering × AtomUI.City 工程能力审计](with-city-engineering.md) | Build/Generators 已完成收口并通过自动门禁；现有运行时合同测试保留，Testing/CLI/Templates 不采用。 |
| 全矩阵收口 | 本文三张主矩阵及各专项记录 | 决策完成；当前无开放的 `审-*` 或 `阻` 单元，真实 Provider/UI 人工验收单独保持 Manual Pending。 |

## Desktop 尚未采用能力审计结论

- `AB-DESKTOP × CITY-EVENTBUS` 标记为 `否`。账号、设置、传输队列和进度属于上游业务事实，已经分别由 Application/Transfer 的 City.State 提供；Desktop 只消费状态并形成 UI 投影，不建立第二条事件通道。
- Desktop 剩余的 `CurrentMenuKeyChanged`、`RemoteAccountMenuRefreshed`、`LanguageChanged`、`PropertyChanged` 以及 Avalonia 控件事件，都是发布者和消费者明确的窗口、ViewModel、导航或 Binding 协作通知，不是跨模块的一对多业务事件。
- 用 EventBus 替换上述直接通知会增加事件合同、Host 模块、Scope owner、异步派发、失败处理和顺序测试成本，并隐藏原本清晰的一对一关系；当前没有对应收益。
- 本单元不要求修改 City，也不修改 AtomBox 运行时代码。未来只有出现由 Desktop 真正拥有、需要多个未知消费者处理的一次性 UI 事实时，才重新开启 EventBus 审计。
- `AB-DESKTOP × CITY-SECURITY` 标记为 `否`。AtomBox 没有当前登录用户、角色、权限点、登录/登出、路由授权或命令授权；Desktop 采集的是多个可并发使用的外部存储连接凭据，不是 City Host 的全局活动认证主体。
- City.Security 的 `ClaimsPrincipal`、Permission、Route Guard、Command Authorization 和单一全局活动账号不能替代 AtomBox 的 `CredentialRef`、`CredentialMaterialLease` 及 Provider 认证链路。远端服务返回的认证/授权结果也不等于 AtomBox 本地用户权限。
- `CredentialSecretMaterial` 使用不可清零的字符串保存敏感值，以及账号弹窗测试连接使用 `CancellationToken.None`，已作为独立安全工程问题登记；它们必须按实际所有权单独治理，不能作为 Desktop 引入 City.Security 的理由。
- `AB-DESKTOP × CITY-PLUGINSYSTEM` 标记为 `否`。当前 15 个 Provider 全部在编译期静态注册，AtomBox 没有插件目录、manifest、安装、启停、动态加载、动态卸载、更新或管理 UI 的产品合同。
- 即使未来正式立项 Provider 插件化，稳定插件合同、动态 descriptor/creator registry、ProviderId 冲突处理、运行中实例排空和 contribution lease 都属于 Providers 或专用集成项目。Desktop 最多消费应用用例并提供管理 UI，不能拥有插件运行时。
- City.PluginSystem 的通用发现、隔离、贡献和卸载能力不会自动定义 AtomBox Provider 插件语义；本单元不修改 City，也不修改 AtomBox 运行时代码。未来需求立项时必须重新开启 `AB-PROVIDERS × CITY-PLUGINSYSTEM` 专项审计。
- `AB-DESKTOP × CITY-DATA` 标记为 `否`。Desktop 没有直接使用 HTTP、gRPC、SignalR、FTP、SFTP 或厂商 SDK，也没有引用 City.Data；它只调用 Application 服务和 Transfer 服务。
- 账号连通性测试、远程浏览、预览和传输虽然由 UI 发起，但网络访问分别由 Application/Transfer 编排，并最终通过 `IStorageProviderFactory` 进入 Providers。按照能力归属铁律，不能把这些职责重新包装到 Desktop。
- Providers 已完成 City.Data 专项审计并决定不采用。Desktop 若自行接入 City.Data，会绕过 Provider SDK、认证、错误映射、幂等和流生命周期合同，形成第二条网络访问通道；本单元不修改 City，也不修改 AtomBox 运行时代码。
- `AB-DESKTOP × CITY-TESTING` 标记为 `否`。Desktop 测试继续启动真实 `Program.CreateHost()`，验证实际模块、DI、路由、View 注册、本地化和生命周期；City.Testing 的轻量 TestHost 与手写 RoutingTestHost 不能替代这些证据。
- City.Testing 当前只有 `FakeUiDispatcher` 能直接替换 Desktop 的一个自建替身，但该包会同时引入几乎全部 City 运行时模块和 Roslyn；确定性调度器也不能接管 AtomBox 现有的 Task、Channel 与 PeriodicTimer，收益不足。
- `MvvmMigrationTests` 中一处 `Task.Delay(10)` 轮询作为独立测试工程债登记，后续应改用明确完成信号或可等待状态；不为解决这一点引入 City.Testing。本单元不修改 City，也不修改 AtomBox 运行时代码。
- `AB-DESKTOP × CITY-CLI` 标记为 `否`。CLI 是 Engineering 工具，不是 Desktop 运行时能力；City.Build 与 Generators 已由 MSBuild 直接接入，不需要 CLI 才能工作。
- City CLI 的 build/test/publish 只能转发有限的 dotnet 参数，不能表达 AtomBox 的多 RID、自包含单文件、签名、DMG、AppImage 和发布资源合同；workspace inspect 又硬编码 City 自身的 solution/output 布局，不能检查 AtomBox 的真实路由、View 或本地化产物。
- City CLI 的部分专题文档领先于实际实现，作为 City 自身文档一致性问题保留；AtomBox 继续使用 `build.ps1`、发布脚本与 GitHub Actions，不增加一套能力不完整的 CLI 入口。本单元不修改 City，也不修改 AtomBox 运行时代码。
- `AB-DESKTOP × CITY-TEMPLATES` 标记为 `否`。City 的通用 application/page/localization/test 模板适合新建标准 City 工程，但不能直接维护 AtomBox 已稳定的集中路由、导航参数、DI、目录、本地化投影和真实 Host 测试约定。
- 增量 Page 模板不会更新 `AtomBoxRouteCatalog`、`NavigationTarget`、菜单、参数编解码或 Desktop DI；Localization 模板使用 Module scope，与 AtomBox Host scope 多 Catalog 方案不同。当前也没有 AtomBox 专属模板包或项目级覆盖入口。
- 审计发现 City `AUC-TEMPLATES-007` 生成的 View 未实现 `IViewDataContextAware`，会被 City `ViewBinder` 在真实绑定时拒绝；这是独立 City 高优先级缺陷，不改变 AtomBox 的“不采用”结论，也不作为 AtomBox 迁移阻塞项。本单元不修改 AtomBox 运行时代码。

## Core 审计结论

- `AB-CORE × CITY-*`：AtomBox.Core 是产品领域模型与稳定端口程序集，全部 City 单元均判定为 `否`。运行时、UI、数据、安全、插件和工程能力必须由真正拥有相关责任的上层模块采用，Core 保持零 City 依赖。
- 审计发现的四处 Core 自有集合伪只读问题已经修复。该缺陷会破坏快照语义并间接影响 Application 的 City.State 合同，但根因归属 Core；修复仅使用标准 .NET 不可变或只读集合，没有把 City 引入 Core。
- 凭据明文合同与文档矛盾、JSON 注解耦合、依赖边界门禁缺失已记录为独立技术债，不随本次不可变集合修复扩大施工范围。

## Application 审计结论

- `AB-APPLICATION × CITY-STATE`：账号目录与应用设置是跨调用方共享的运行时事实投影，业务所有者是 Application。`AtomBox.Application` 直接引用 City.State，Repository 仍为持久化事实源，Desktop 只读消费。City `AUC-STATE-009` 原子 Observe 和 DI Writer factory 已由 City 提交 `c2843b9`、`ac70dad` 完成；AtomBox 自动化迁移已完成。
- `AB-APPLICATION × CITY-EVENTBUS`：当前账号/设置需求都是“当前事实 + 后续变化”，State 已完整覆盖；为同一事实再发业务事件会形成双通道一致性问题，因此标记为 `否`。
- `AB-APPLICATION × CITY-SECURITY`：标记为 `否`。Application 管理的是可被浏览和传输任务并发使用的外部存储连接账号，没有 AtomBox 当前登录用户、`ClaimsPrincipal`、角色、权限、登录/登出或用例授权需求。City.Security 的单一全局活动主体以及 access/refresh token 模型，不能替代 AtomBox 的任意 Provider secret、`CredentialRef`、`CredentialMaterialLease` 和 pending-delete 链路。
- 两边虽然都有名为 `ICredentialStore` 的合同，但 City 合同按 `SecurityAccountKey + resource` 保存应用主体 token；AtomBox 合同按 `CredentialRef` 保存密码、私钥、AccessKey、网盘 token 等任意材料并提供 lease。强行适配会混淆安全边界并破坏多账号并发语义。本单元不修改 City，也不修改 AtomBox 运行时代码；未来只有出现真正的应用用户认证与授权产品需求时才重新开启审计，且仍不得取代外部存储凭据系统。
- `AB-APPLICATION × CITY-ROUTING`：标记为 `否`。City route values 只在 Desktop.Navigation 中编解码并在 ViewModel 激活前转换为 `StorageAccountId`、`RemotePath`、`PageIndex` 等纯业务输入；Application 的请求合同不包含 route、outlet、journal、redirect、`NavigationSnapshot` 或 ViewModel 类型。
- `TransferHistoryNavigationParameter.PageIndex` 与 `GetTransferHistoryRequest.PageIndex` 数值相同但所有权不同：前者属于页面进入状态，后者属于历史分页查询。未来即使用 City `IRouteResolver` 预加载业务数据，resolver adapter 也应位于 Desktop/Presentation 集成边界并调用 Application，不能让 Application 服务实现 Routing 合同。
- `AB-APPLICATION × CITY-CORE`：标记为 `否`。Application 只提供普通 .NET DI 扩展、无状态用例服务和一次性 State 初始化器，不拥有独立 Host、Module Scope、后台资源或 shutdown 事务；无需为了程序集分层而机械创建 City Module。
- `AtomBoxDesktopModule` 调用 Application 自有注册入口与初始化器属于组合根生命周期接线，不转移账号、设置或 State 业务所有权。强行拆出 Application Module 还会引入 Infrastructure 初始化、Application State 读取和 Transfer Runtime 恢复之间的跨模块阶段排序。本单元不修改 City 或 AtomBox 运行时代码；只有 Application 将来拥有长期运行资源、独立启停事务或需要被多个 City Host 作为完整模块复用时才重新审计。
- `AB-APPLICATION × CITY-LOCALIZATION`：标记为 `否`。Application 继续返回与前端、语言无关的结构化错误语义和原始诊断信息；Desktop 在实际展示时用 City.Localization 把语义映射为当前语言，因此即时切换语言后可以重新投影，而不会被 Application 调用发生时的文化环境冻结。
- `StorageErrorCode` 当前过于粗粒度，多个不同失败原因会落入同一个 `ValidationFailed` 或 `OperationNotSupported`。后续应由 AtomBox Core/Application 合同补充稳定的 `ReasonCode` / `DetailCode` 与安全格式化参数，Desktop 按代码本地化；禁止依赖 `StorageError.Message` 文本匹配。这是独立 AtomBox 技术债，不是 City.Localization 能力缺陷，本单元不修改 City 或运行时代码。
- `AB-APPLICATION × CITY-DATA`：标记为 `否`。Application 的连接测试、远程浏览、预览和删除只通过 Core `IStorageProviderFactory` / `IStorageProvider` 编排；项目没有 HTTP、gRPC、SignalR、FTP、SFTP、厂商 SDK 或 City.Data 依赖。具体 transport、认证、错误映射、retry、幂等和 stream replay 归 Providers 所有。
- 在 Application 接入 City.Data 会绕过或重复包装 Provider，形成 `DataResult/DataError` 与 `OperationResult/StorageError` 双重合同和第二条网络通道，违反能力归属铁律。Providers 专项已经决定不采用 City.Data；未来只有出现不属于存储 Provider 的独立业务后端 API 时才重新审计。本单元不修改 City 或 AtomBox 运行时代码。
- `RemoteBrowserAppService` 当前通过匹配 `InvalidOperationException.Message` 中的 `preview content exceeded` 判断受限预览流超限。后续应改为专用异常或结构化结果；这是独立 Application 技术债，与 City.Data 无关。
- `AB-APPLICATION × CITY-PLUGINSYSTEM`：标记为 `否`。Application 当前只有固定注册的账号、浏览、传输和设置用例，不拥有插件目录、manifest、动态加载、启停、贡献或卸载流程；将来即使 Provider 动态插件化，Application 仍应通过稳定的 `IStorageProviderFactory` 使用能力而不感知来源。
- City.PluginSystem 只提供通用插件运行时，不定义 AtomBox 的 Provider 或业务插件合同。当前 Provider 插件化没有产品目标；未来立项时必须先由 Providers 或专用集成模块设计共享 contribution contract、动态 registry、冲突处理、实例与凭据 lease 排空和撤销流程，再按实际插件管理用例重新审计 Application。本单元不修改 City 或 AtomBox 运行时代码。
- `AB-APPLICATION × CITY-TESTING`：标记为 `否`。Application 测试直接验证用例、真实 `ApplicationStateRegistry`、State 投影、DI 注册及 Infrastructure 数据实现，并使用 `TaskCompletionSource` 等明确完成信号；City.Testing 当前没有已实现的 State test kit，其他 Module/Plugin/Routing/Data/UI 测试宿主不匹配 Application 边界。
- City.Testing 的通用 `TestHost` 不能装配 `AddAtomBoxApplication()` 所需服务，`DeterministicScheduler` 也不能接管现有 Repository、`SemaphoreSlim`、`TaskCompletionSource` 或 State 通知。只为 `TestDirectory` 等小工具引入全部 City 运行时模块和 Roslyn，依赖成本高于收益。当前全解门禁中的 Application 测试为 161/161 通过，无跳过。
- `AtomBox.Application.Tests` 当前直接引用但未在源码使用 `AtomUI.City.Core`；该引用很可能可由 City.State 的传递依赖覆盖，应作为独立测试依赖清理候选，在删除后以构建和当前 Application 全量回归验证。City State Testing 文档领先于实现的问题在 City 仓库另行登记。
- `AB-APPLICATION × CITY-MVVM`：标记为 `否`。Application 没有 ViewModel、属性通知、Command、Interaction 或 MVVM Validation；City Command/Activation/Operation scope 由 Desktop ViewModel 所有，只把标准 `CancellationToken` 传入 Application 用例。
- Application 的参数校验是所有前端都必须经过的权威业务边界，不能由 Desktop `ValidationScope` 替代；账号、设置和 State 投影的 `SemaphoreSlim` 保护跨多个调用者的一致性，也不能由单个 ViewModel Command 的并发策略替代。`IProgress<T>` 继续作为标准 .NET 业务进度端口。本单元不修改 City 或 AtomBox 运行时代码。
- `AB-APPLICATION × CITY-PRESENTATION`：标记为 `否`。Application 没有 Avalonia、View、Window、Dialog、Outlet、ViewLocator、ViewBinder、Interaction handler 或 ViewModel composition；它返回账号摘要、远端项、分页、路径能力和错误等前端无关用例快照，Desktop 再投影为 ViewModel 与真实 UI。
- `RemotePathContextResult.CanDeleteSelectedFile` 实际没有接收或检查选中文件，目前只由 `!path.IsRoot` 计算，Desktop 还会结合真实选中项和写入能力完成最终判断。该字段应后续改为表达路径级能力的准确名称或重新设计合同；这是独立 Application DTO 技术债，不是引入 City.Presentation 的理由。本单元不修改 City 或 AtomBox 运行时代码。
- `AB-APPLICATION × CITY-BUILD`：标记为 `否`。Application 没有导入 City.Build，也没有 RESX、City manifest、插件打包、应用发布或 Strict AOT 责任；City.Build 已在 Desktop/Engineering 编译链中服务真正的应用宿主需求。
- AtomBox 根 `Directory.Build.props` 的目标框架、警告策略和 `.artifacts` 输出目录属于仓库级工程规范，不等于 Application 采用 City.Build。City.State 运行时引用也不会传递 MSBuild Build 资产；本单元不修改 City、运行时代码或项目文件。
- `AB-APPLICATION × CITY-GENERATORS`：标记为 `否`。Application 没有 Route、View、Localization、Plugin、Data Client 或 EventBus 生成元数据；当前 `AddAtomBoxApplication()` 的少量显式注册已有合同测试覆盖。
- City DI Generator 要求生成服务由唯一的 `ServiceRegistrationOwner` City Module 持有。Application 当前没有独立 Module 生命周期，不能为了替代几行注册代码人为创建 Module；`AddState()` 等显式组合仍无法消失。本单元不修改 City、运行时代码或项目文件。
- `AB-APPLICATION × CITY-CLI`：标记为 `否`。City.CLI 是入口固定为 `atomui city` 的工程工具进程，负责模板生成、dotnet 命令转发、City workspace 和插件诊断，不承载 AtomBox 账号、浏览、设置或传输业务。
- 未来业务 CLI 的正确依赖方向是独立 `AtomBox.Cli` 前端调用 Application，而不是 Application 反向依赖 City.CLI。届时应在矩阵新增模块行单独审计；本单元不修改 City 或 AtomBox 代码。
- `AB-APPLICATION × CITY-TEMPLATES`：标记为 `否`。Application Template 面向新工作区，Module Template 与已确认的无 Application Module 边界冲突，Page/Localization 属于 Desktop，Plugin 没有产品合同。
- Configuration Template 的 Host Options 不等于 Repository 持久化并投影到 City.State 的用户业务设置；Test Template 的占位测试也不能替代现有业务合同测试。Application 不引用 Templates，本单元不修改 City 或 AtomBox 运行代码。
- Desktop State 候选已完成归属回退：账号和设置不在 Desktop 自建 State；Desktop 只负责 Dispatcher、ActivationScope 和 UI 投影。

## Transfer 审计结论

- `AB-TRANSFER × CITY-STATE`：传输队列和进度是 Transfer 拥有的应用级运行时事实投影。`AtomBox.Transfer` 直接采用 City.State，持久化 Store 继续是事实源，Desktop 只读观察。历史分页不进入 State。
- State 迁移阶段曾延后的 Runtime 关停、worker 所有权、取消竞态和进度持久化问题，已经在 `AB-TRANSFER × CITY-CORE` 专项中完成治理；早期 State 专项记录保留为历史阶段证据。
- 自动化证据：2026-10-16 最终门禁 Core 61/61、Infrastructure 164/164、Providers 171/171、Application 161/161、Transfer 71/71、EndToEnd 4/4、Desktop 34/34，全解回归 666/666；Release 全解构建 0 warning / 0 error。真实 Provider 与真实 UI 仍待人工验收。
- `AB-TRANSFER × CITY-CORE`：默认自动化实施完成。City Module 只承载应用级启停边界，后台 Runtime、持久化队列扫描、执行租约和恢复事务由 Transfer 自行实现；公开同步 `WakeAsync`、旧 `TransferTaskScheduler`、内存 `TransferQueue` 和独立 `TransferRuntimeInitializer` 已退出生产路径，详见专项决策记录。
- `AB-TRANSFER × CITY-MVVM / ROUTING / PRESENTATION / LOCALIZATION`：标记为 `否`。Transfer 不拥有 ViewModel、Command、页面路由、View 组织或用户文案；这些能力由 Desktop 使用，Transfer 只暴露前端无关合同和只读状态。
- `AB-TRANSFER × CITY-EVENTBUS`：标记为 `否`。队列、进度与 Runtime 健康需要“当前事实 + 后续变化”，City.State 已完整覆盖；再发布同义事件会形成双通道一致性责任。
- `AB-TRANSFER × CITY-DATA / SECURITY`：标记为 `否`。Transfer 只通过 Core Provider 端口调度操作，不拥有 HTTP/协议实现、厂商认证或应用主体；相关边界属于 Providers，且 Providers 专项已决定不采用对应 City 能力。
- `AB-TRANSFER × CITY-PLUGINSYSTEM`：标记为 `否`。当前没有动态 Provider 插件产品合同；未来如立项，插件运行时应先由 Providers 或专用集成模块拥有。
- `AB-TRANSFER × CITY-BUILD / GENERATORS / TESTING / CLI / TEMPLATES`：标记为 `否`。Build 与 Generators 由 Desktop/Engineering 编译链使用；Transfer 继续以真实 Runtime、Store、Provider 替身和 City Host 集成测试验证合同，不为复用有限工具引入其余工程模块。
- Transfer 全部 City 单元至此完成决策收口。采用项是 Core 与 State；其余单元均为经过审计后的主动不采用，而不是遗漏。

## Providers 审计结论

- `AB-PROVIDERS × CITY-*` 全部标记为 `否`。厂商 SDK 始终优先保留，自研 HTTP、S3Compatible、FTP 和 SFTP 同样不做 City 迁移。
- City.Data 的错误响应、多账号认证和连接治理收益已完成评估，但不足以抵消领域语义、幂等、stream replay 和依赖成本；本轮不补 City 能力，也不建立 pilot。
- City.Security 与外部存储账号凭据不是同一安全边界；Provider 插件化没有第一版产品目标，因此 Security 与 PluginSystem 同样不采用。
- 审计同时发现 Providers 自有的同步阻塞 IO、取消不可贯穿、短生命周期 `HttpClient`、部分全量内存下载和生产 dispose 合同测试缺口。它们必须按业务所有权在 Providers 治理，不能归因给 Desktop 或用 City 名义掩盖。
- 详细的候选收益、冲突和否决理由见 [Providers 专项审计](with-city-providers.md)。

## Infrastructure 审计结论

- `AB-INFRASTRUCTURE × CITY-*` 全部标记为 `否`。Infrastructure 保持零 City 引用，继续实现 Core 技术端口。
- Desktop 在 City Host 初始化阶段调用 `InfrastructureInitializer` 是组合根接线，不构成 Infrastructure 迁移，也不要求 Infrastructure 依赖 City.Core。
- City.State 不能取代磁盘事实，City.Security 不能取代外部账号 secret/lease，City.Data 与本地存储无关；其他 City 模块也没有成立的所有权场景。
- 并发读改写、pending-delete 清理、密钥保护、schema migration、日志和缓存空壳已作为 Infrastructure 自有工程债记录，详见 [Infrastructure 专项审计](with-city-infrastructure.md)。

## Engineering 审计结论

- Build 和 Generators 已进入 Desktop 编译链。本轮把局部 Localization targets 接入收口为完整 City.Build targets，并保持 City application packaging、Strict AOT 和插件打包关闭。
- Application/Transfer/Desktop 测试继续直接验证其实际采用的 Core、Mvvm、Routing、Presentation、State 和 Localization 合同；不为此引入 City.Testing。
- City.Testing、CLI 和 Templates 的收益不足以覆盖依赖与维护成本，标记为 `否`。EventBus、Data、Security、PluginSystem 仍由业务模块决定，Engineering 不接管。
- Release 的 Windows、macOS、Linux 主仓 checkout 必须递归检出 City submodule；详细设计与验收见 [Engineering 专项审计](with-city-engineering.md)。
- 当前自动化证据统一采用 2026-10-16 最终门禁：全解回归 666/666；Release 全解构建 0 warning / 0 error。

## 状态维护规则

- 每次开始讨论新单元前，先引用 Matrix ID、当前标记和审计重点。
- 每次形成方案后，先更新本文和对应专项文档，再修改代码。
- City 有通用能力缺口时，矩阵单元标记为 `阻`；City 修复并同步远端后，AtomBox 才继续实施。
- AtomBox 自动门禁通过后只能标记为 `Automated`；涉及真实桌面交互时，必须完成人工 UI 验证才能标记 `Done`。
- 每次提交应能回溯到矩阵单元；不得用“顺手重构”扩大迁移范围。
- 新增 AtomBox 模块或 City 能力模块时，必须先扩展本文矩阵。
- 一项迁移完成后，同步更新本文状态、专项文档、AtomBox 正式模块文档以及必要的 City 正式文档。
