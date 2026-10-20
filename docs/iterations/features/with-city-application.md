# AtomBox.Application × AtomUI.City 迁移与审计

> 状态：Automated / Manual UI Pending
>
> Matrix ID：`AB-APPLICATION × CITY-STATE`、`AB-APPLICATION × CITY-EVENTBUS`、`AB-APPLICATION × CITY-SECURITY`、`AB-APPLICATION × CITY-ROUTING`、`AB-APPLICATION × CITY-CORE`、`AB-APPLICATION × CITY-LOCALIZATION`、`AB-APPLICATION × CITY-DATA`、`AB-APPLICATION × CITY-PLUGINSYSTEM`、`AB-APPLICATION × CITY-TESTING`、`AB-APPLICATION × CITY-MVVM`、`AB-APPLICATION × CITY-PRESENTATION`、`AB-APPLICATION × CITY-BUILD`、`AB-APPLICATION × CITY-GENERATORS`、`AB-APPLICATION × CITY-CLI`、`AB-APPLICATION × CITY-TEMPLATES`
>
> 实施分支：`feature/with-city`
>
> 创建时间：2026-10-13

## 结论

`AtomBox.Application` 直接引用 `AtomUI.City.State`，负责把账号目录和应用设置投影为应用级、只读、可观察的运行时状态。Repository 继续是持久化事实源；City.State 不替代持久化，也不保存凭据明文。

`AtomBox.Application` 当前没有独立的业务事件流需求。账号和设置的“当前事实 + 后续变化”统一由 State 表达，不引入 EventBus，不为同一事实建立双通道。

`AtomBox.Application` 当前不采用 City.Security。Application 的账号是多个可被并发浏览和传输任务使用的外部存储连接，不是 City Host 的当前登录主体；AtomBox 的任意 Provider 凭据、lease 和延迟删除合同也不能由 City 的应用主体 token 模型替代。

`AtomBox.Application` 当前不采用 City.Localization。Application 输出稳定错误语义和诊断信息，Desktop 在展示边界按当前语言本地化；错误原因码粒度不足作为 AtomBox 自身合同技术债处理。

`AtomBox.Application` 当前不采用 City.Data。Application 只通过 Core Provider 端口编排远端用例，网络 transport、认证、协议错误和连接治理继续归 Providers 所有。

`AtomBox.Application` 当前不采用 City.PluginSystem。当前没有插件产品合同；未来 Provider 插件运行时也应先由 Providers 或专用集成模块拥有，Application 继续消费稳定业务端口。

`AtomBox.Application.Tests` 当前不采用 City.Testing。测试继续直接验证真实 Application 与 City.State 合同，并使用 AtomBox 业务端口的专属替身。

`AtomBox.Application` 当前不采用 City.Mvvm。MVVM 生命周期、Command、Interaction 和展示验证由 Desktop 持有；Application 保留前端无关的异步用例、权威业务校验、标准取消/进度合同与跨调用方并发保护。

`AtomBox.Application` 当前不采用 City.Presentation。Application 输出普通用例快照，View、Window、Outlet、Interaction handler 和 ViewModel composition 继续由 Desktop 持有。

`AtomBox.Application` 当前不采用 City.Build。City.Build 已由 Desktop/Engineering 编译链持有；Application 是没有 RESX、manifest、插件打包或 AOT 发布职责的业务类库。

`AtomBox.Application` 当前不采用 City.Generators。现有显式 DI 注册规模小且已有合同测试；不为生成少量注册代码而人为创建一个没有生命周期责任的 City Module。

`AtomBox.Application` 当前不采用 City.CLI。City.CLI 是 `atomui city` 工程工具进程，不是业务用例依赖；未来业务命令行前端也应作为独立适配器调用 Application。

`AtomBox.Application` 当前不采用 City.Templates。通用 City 脚手架不匹配现有 Application 的业务设置、测试和无 Module 边界，也不应成为生产类库依赖。

## Security 专项审计

### AtomBox 当前模型

- Application 没有当前登录用户、`ClaimsPrincipal`、角色、权限点、登录/登出、路由授权或用例授权合同。
- 每个远程浏览、连接测试和传输用例都显式选择 `StorageAccountId`；多个 OSS、FTP、SFTP 或网盘账号可以被不同操作同时使用，不存在必须随 Host 全局切换的活动账号。
- 账号凭据通过 Core `ICredentialStore` 保存为 `CredentialRef`。实际材料可以是 AccessKey、临时安全令牌、用户名/密码、SFTP 私钥与口令或网盘 token；Provider 在操作窗口内通过 `CredentialMaterialLease` 使用，旧凭据可进入 pending-delete 等待 lease 排空。
- Application 只编排保存、替换和清理，不实现加密，不把明文放入 State、结果、缓存、日志或长期字段。

### City.Security 当前模型

- City.Security 表达 City Host 的当前应用主体、认证状态、`ClaimsPrincipal`、Permission、Authorization、Route Guard、Command Authorization 和 Access Token。
- 它可以持久化多个身份账号，但一个 City Host 同一时刻只发布一个全局活动主体；账号身份固定由 authentication scheme、authority、tenant 和 subject 构成。
- 其 `AccountCredentialSnapshot` 保存按 resource 区分的 access token、refresh token、scheme 和 expiry，并明确不保存用户密码。
- City 的 `ICredentialStore` 不提供 AtomBox 所需的任意 secret 字典、`CredentialRef`、使用 lease 或 pending-delete 合同。

### 决策与边界

`AB-APPLICATION × CITY-SECURITY` 标记为 `否`。两边虽有“账号”和 `ICredentialStore` 等同名概念，安全边界与生命周期完全不同。若强行适配，多个并发存储连接会被错误映射为单一活动用户；切换某个浏览或传输账号还会污染整个 Host 的 principal、token 和权限状态。

本单元不修改 City，也不修改 AtomBox 运行时代码。未来只有 AtomBox 正式增加应用用户登录、企业身份、角色权限或“哪个用户可以执行哪个用例”等产品需求时，才重新审计 City.Security；即使届时采用，它也只负责应用主体认证授权，仍不得替代外部存储凭据系统。

## Routing 专项审计

### 当前参数流

City route values 只存在于 Desktop 导航层。`NavigationService` 负责把 `RemoteBrowserNavigationParameter`、`TransferHistoryNavigationParameter` 和 `AccountManagementNavigationParameter` 编码为 City 参数，在导航成功后解码，并在 ViewModel 首次激活前应用最终参数。

ViewModel 再把页面进入状态转换成纯 Application 用例请求：

- `RemoteBrowserNavigationParameter` 的账号和路径转换为 `ListRemoteItemsRequest(StorageAccountId, RemotePath, ...)`；
- `TransferHistoryNavigationParameter.PageIndex` 转换为 `GetTransferHistoryRequest.PageIndex`；
- `AccountManagementNavigationParameter.SelectedAccountId` 只用于 Desktop 列表选中状态，Application 不感知该导航参数。

Application 的公开请求只包含 Core ID、值对象、查询条件和普通 BCL 类型，不包含 route、outlet、journal、redirect、`NavigationSnapshot`、ViewModel 或 Presentation 类型。

### 决策与边界

`AB-APPLICATION × CITY-ROUTING` 标记为 `否`。页面参数与用例参数可以承载相同的值，但前者决定页面如何进入和展示，后者表达要执行的业务查询或操作；二者不应因为数值相同而合并为同一个 Routing 合同。

City `IRouteResolver` 可以由应用适配器调用 Application 服务预加载数据，但该适配器拥有导航事务和 redirect/result 映射责任，应位于 Desktop 或专用 Presentation 集成边界。Application 服务本身不得实现 `IRouteResolver`，也不得根据当前页面或导航历史改变业务结果。

本单元不修改 City，也不修改 AtomBox 运行时代码。Desktop 未来从手写参数编解码演进为 City 强类型生成路由，仍属于 `Desktop × Routing` 的实现调整，不改变 Application 的无 Routing 依赖边界。

## Core 专项审计

### 当前生命周期事实

- `AddAtomBoxApplication()` 只注册 Application 自己的无状态用例服务、State、Projector 和一次性初始化器，不创建 ServiceProvider，也不启动后台任务。
- `AccountAppService`、`RemoteBrowserAppService`、`TransferAppService` 和 `SettingsAppService` 不持有 provider、连接、worker、ViewModel 或 secret material；没有独立 stop/dispose 事务。
- `AtomBoxApplicationStateInitializer` 只从 Repository 加载账号和设置投影。读取失败由 State 发布 `Faulted`，不存在需要 Module 持有到 shutdown 的运行资源。
- `AtomBoxDesktopModule` 负责组合 Infrastructure、Providers、Transfer、Application 与 Desktop，并按 Infrastructure 初始化、Application State 初始化、Transfer Runtime 恢复的顺序完成 Host 启动接线。

### 决策与边界

`AB-APPLICATION × CITY-CORE` 标记为 `否`。City Module 是能力组织和生命周期单元，不是每个解决方案程序集都必须建立的一一映射。Application 当前没有独立 Host、Module Scope、长期运行资源或 shutdown 钩子，新增 `AtomBoxApplicationModule` 只会把既有 `AddAtomBoxApplication()` 再包装一层。

Desktop 组合根调用 Application 自有注册入口和初始化器只属于生命周期接线，没有把账号、设置或 State 业务逻辑搬入 Desktop，符合能力归属铁律。若把 State 初始化迁入独立 Application Module，还必须重新安排 Infrastructure 初始化发生在其之前、Transfer 恢复发生在其之后；为一个没有独立资源的模块引入这条阶段依赖链，成本高于收益。

本单元不修改 City，也不修改 AtomBox 运行时代码。未来只有 Application 自己拥有长期后台资源、独立启停事务、Module Scope，或需要被多个 City Host 作为完整模块复用时，才重新开启 City.Core 审计。

## Localization 专项审计

### 当前错误与展示链路

- Application 使用 `StorageError` 返回可判定的错误类别和原始诊断消息；其项目不引用 City.Localization。
- Desktop ViewModel 使用 `IAtomBoxLocalizedText`（由 City.Localization 支撑）生成状态、标题和摘要等最终用户文案。
- `ErrorDialogRequest` 保留 `StorageError`，`DialogService` 只在错误详情中展示其原始 `Message`；用户首先看到的标题与摘要由 Desktop 按当前语言生成。

### 决策与边界

`AB-APPLICATION × CITY-LOCALIZATION` 标记为 `否`。Application 是前端无关的用例层；若在这里注入 `ILocalizationService`，返回文本会被固定在用例调用时的文化环境，语言即时切换后无法重新投影，同时 CLI、测试和日志聚合也会获得文化相关结果。

正确边界是 Application 返回稳定、结构化、与语言无关的错误语义及安全参数，Desktop 在实际展示时将其映射为 City.Localization key；原始 `Message` 仅保留为诊断信息。不得通过匹配英文 `Message` 来识别错误原因。

当前 `StorageErrorCode.ValidationFailed` 和 `OperationNotSupported` 等类别过粗，无法稳定区分多个具体业务原因。后续应由 AtomBox Core/Application 合同增加稳定的 `ReasonCode` / `DetailCode` 与安全格式化参数，或细化现有错误码；该工作是独立 AtomBox 技术债，不是 City.Localization 缺陷。本单元不修改 City，也不修改 AtomBox 运行时代码。

## Data 专项审计

### 当前远端访问链路

- Application 项目只引用 Core、City.State 和 DI abstractions；没有 `HttpClient`、HTTP/gRPC/SignalR 类型、FTP/SFTP 库、厂商 SDK 或 City.Data 引用。
- `AccountAppService` 和 `RemoteBrowserAppService` 通过 Core `IStorageProviderFactory` 创建短生命周期 `IStorageProvider`，调用领域化的列表、下载和删除操作，并使用 `await using` 释放。
- `IStorageProviderFactory` 的具体实现及 SDK、HTTP、FTP、SFTP transport 位于 Providers；Application 只组织连接测试、远程浏览、预览和删除等用例。

### 决策与边界

`AB-APPLICATION × CITY-DATA` 标记为 `否`。City.Data 解决 HTTP、gRPC、SignalR 的请求管线、认证、缓存、resilience、并发、诊断和传输问题；Application 拥有的是用户用例编排，而不是 transport 实现。

若在 Application 中接入 City.Data，会绕过或重复包装 `IStorageProvider`，同时形成 City `DataResult/DataError` 与 Core `OperationResult/StorageError` 两套结果模型，并把 endpoint、认证、协议错误、retry、幂等和 stream replay 决策错误地提升到用例层。按照能力归属铁律，任何 Provider transport 改造都必须发生在 Providers；该模块的专项审计已经决定本轮不采用 City.Data。

未来只有 Application 新增一个不属于存储 Provider 的独立业务后端 API 时，才为该场景重新开启 City.Data 审计。本单元不修改 City，也不修改 AtomBox 运行时代码。

审计同时发现 `RemoteBrowserAppService` 通过匹配 `InvalidOperationException.Message` 中的 `preview content exceeded` 判断受限预览流超限。后续应使用专用异常类型或结构化结果替代文本匹配；这是独立 Application 技术债，与 City.Data 无关。

## PluginSystem 专项审计

### 当前扩展模型

- Application 的账号、浏览、传输和设置服务在 Host 启动阶段固定注册；项目没有插件目录、manifest、安装、动态程序集加载、启停、contribution lease 或卸载流程。
- 15 个 Provider 由 Providers 在编译期静态注册，Application 只通过 Core `IStorageProviderFactory` 创建 Provider，不感知具体实现或来源。
- 当前产品设计明确不把 Provider 插件化独立发布列入第一版目标，也不存在其他已定义的 AtomBox 业务插件合同。

### 决策与边界

`AB-APPLICATION × CITY-PLUGINSYSTEM` 标记为 `否`。City.PluginSystem 提供发现、安装、manifest、依赖校验、独立加载上下文、贡献 lease 和卸载诊断等通用运行时，但不会自动定义 AtomBox 插件可以贡献什么以及如何与现有业务端口协调。

若未来正式立项第三方 Provider 插件，稳定共享 contribution contract、动态 descriptor/creator registry、Provider ID 冲突处理、停用后拒绝新实例、既有 Provider 与凭据 lease 排空，以及 contribution 撤销，首先属于 Providers 或专用插件集成模块。Application 理论上仍可继续消费稳定的 `IStorageProviderFactory`，无需感知 Provider 是内置还是插件来源。

只有未来出现明确的插件安装、启用、停用或更新用户用例时，才重新审计 Application 是否负责其业务编排；即使如此，Application 也不应持有 `AssemblyLoadContext`、`PluginRuntime` 或 Provider contribution registry。本单元不修改 City，也不修改 AtomBox 运行时代码。

## Testing 专项审计

### 当前测试结构

- `AtomBox.Application.Tests` 使用 xUnit，直接覆盖账号、浏览、传输和设置用例、DI 注册、真实 `ApplicationStateRegistry` 与 Application State 投影，以及基于 Infrastructure 数据实现的集成路径。
- State 测试直接构造真实 registry、writer 和 projector，验证首值观察、版本、失败投影、不可变快照、并发刷新顺序和凭据脱敏，不需要重新模拟 City.State。
- 并发测试使用 `TaskCompletionSource` 和明确完成信号，没有固定 `Task.Delay`；本轮 Release 回归为 163/163 通过、0 跳过。

### City.Testing 当前适配性

City.Testing 当前实现通用 TestHost、Module/Plugin/Routing TestHost、Fake UI Dispatcher、Deterministic Scheduler、Data 替身、Generator/AOT 工具和测试分层标记，但没有源码或 public API 可用的 `StateTestStore`、状态记录器或 `UseStateTesting`。

- Application 没有 City Module、Routing、PluginSystem、City.Data 或 UI Dispatcher，对应测试宿主没有适用对象。
- 通用 `TestHost` 不提供装配 `AddAtomBoxApplication()` 与 AtomBox 仓储替身所需的 `IServiceCollection/ServiceProvider` 扩展点。
- `DeterministicScheduler` 只控制显式提交给它的 work item，不能接管生产代码中的 Repository Task、`SemaphoreSlim`、`TaskCompletionSource` 或 State 通知。
- `TestDirectory` 可以替代四处临时目录样板，但不足以抵消 City.Testing 对 Core、Data、EventBus、Localization、Mvvm、PluginSystem、Presentation、Routing、Security、State 和 Roslyn 的整体依赖。

### 决策与独立债务

`AB-APPLICATION × CITY-TESTING` 标记为 `否`。保留当前 xUnit、真实 City.State 合同和 AtomBox 专属业务替身；不修改 Application 生产代码，也不为复用少量工具引入 City.Testing。

City `state-testing.md` 与 `detailed-design.md` 把 `StateTestStore`、状态记录器和 `UseStateTesting` 描述为可用能力，但这些类型未进入 Feature 表、源码或 public API。这是 City 文档领先于实现的问题，已在 City 仓库登记，不能作为 AtomBox 当前采用依据。

`AtomBox.Application.Tests.csproj` 还直接引用了测试源码没有使用的 `AtomUI.City.Core`；该程序集已由 City.State 传递引用。这是独立测试依赖清理候选，后续删除后必须重新通过构建和 163 项回归，本单元暂不顺手修改。

## Mvvm 专项审计

### 当前调用边界

- Application 没有 ViewModel、属性变化通知、Command、Interaction、`ValidationScope` 或 CommunityToolkit.Mvvm 类型；项目引用仅为 Core、City.State 和 DI abstractions。
- Desktop ViewModel 使用 City Presentation `IUiCommandFactory`（经 AtomBox 薄适配）、`OperationScope` 和 Activation owner 管理用户动作，再把标准 `CancellationToken` 传给 Application 服务并消费 `OperationResult`。
- Application 请求和结果是普通不可变业务合同；`IProgress<UploadPreparationProgress>` 是标准 .NET 进度端口，不依赖 UI 或 ViewModel。

### 相似概念但不同责任

Application 的参数校验是信任边界和业务不变量，必须对 Desktop、未来 CLI、测试和后台调用全部生效。City `ValidationScope` 则负责 ViewModel 输入状态、错误提示和 Command 可执行性；Desktop 可以提前反馈，但不能替代 Application 的权威校验。

Application 中账号、设置和 State 投影的 `SemaphoreSlim` 保护多个调用方之间的提交和发布顺序。City Command 的 Reject、Allow 或 RejectByKey 只约束某个 ViewModel 命令入口，无法保护其他 ViewModel、CLI 或后台调用，因此不能替代服务级并发门。

City `OperationScope` 与 `ActivationScope` 由 ViewModel 生命周期拥有。Application 只观察调用方传入的 `CancellationToken`，不得反向持有 UI scope，也不得让用例结果依赖 ViewModel 是否仍处于 Active。

### 决策

`AB-APPLICATION × CITY-MVVM` 标记为 `否`。City.Mvvm 继续只由 Desktop 使用；Application 保留普通异步方法、标准取消和进度合同、权威业务校验及自身一致性保护。本单元不修改 City，也不修改 AtomBox 运行时代码。

## Presentation 专项审计

### 当前展示边界

- Application 没有 Avalonia、View、Window、Dialog、Outlet、ViewLocator、ViewBinder、Interaction handler 或 ViewModel composition 类型，也不引用 City.Presentation。
- 账号摘要、远端项、分页游标、路径上下文、传输快照和错误结果属于一次用户用例的前端无关输出；Desktop 把它们转换为行、选中状态、Command 状态、本地化文案和 Dialog 请求。
- Application 不返回“打开哪个 View”“导航到哪个 Outlet”或“弹出哪个 Dialog”等展示指令。确认、消息、错误对话框以及逻辑 Root/Child ViewModel 所有权都由 Desktop 决定。

Application 返回页面需要的数据，不等于 Application 拥有 Presentation。零/一个/多个账号、是否存在下一页、路径能否执行某类业务操作，以及可重试的请求快照，都可以被 Desktop、CLI 或其他前端消费；它们不应实现或引用 City Presentation 合同。

### 决策与独立债务

`AB-APPLICATION × CITY-PRESENTATION` 标记为 `否`。City.Presentation 继续由 Desktop 使用；Application 保持无 Avalonia 依赖的用例结果合同。本单元不修改 City，也不修改 AtomBox 运行时代码。

审计发现 `RemotePathContextResult.CanDeleteSelectedFile` 的名称与实际语义不一致：Application 没有接收或检查选中文件，该值当前仅由 `!path.IsRoot` 计算，Desktop 随后才结合真实选中项、文件类型和写入能力完成最终判断。后续应将其重命名为准确的路径级能力或重新设计结果合同；这是独立 AtomBox DTO 技术债，不是 City.Presentation 能力缺陷。

## Build 专项审计

### 当前构建边界

- `AtomBox.Application.csproj` 只引用 AtomBox.Core、City.State 和 DI abstractions；没有导入 City.Build props/targets，也没有引用 Build.Tasks 或 Generators。
- Application 没有 RESX、City manifest、插件清单、应用打包、Strict AOT 发布或 City 构建诊断输出需求。
- AtomBox 根 `Directory.Build.props` 统一设置目标框架、可空性、警告策略和 `.artifacts` 输出目录。这是 AtomBox 仓库级工程规范，不代表每个项目采用 City.Build。
- City.Build 的 props、完整 targets、Build.Tasks 和 Generators 当前只接入 Desktop；其中实际价值是把 Desktop RESX 编译为 locpack，并在应用宿主编译链完成 manifest、诊断和可选发布检查。

### 决策

`AB-APPLICATION × CITY-BUILD` 标记为 `否`。Application 使用 City.State 不会传递性引入 City.Build；运行时模块依赖与 MSBuild 工程能力是两条独立边界。

若在 Application 中额外导入 City.Build，将引入任务程序集存在性校验、manifest 中间目录、构建诊断和默认 RESX 扫描等编译期耦合，却没有相应产物可生成，也会模糊业务类库与桌面应用宿主的边界。City.Build 继续由 Desktop/Engineering 所有，本单元不修改 City，也不修改 AtomBox 运行时代码或项目文件。

未来只有 Application 自身开始发布独立 City manifest、拥有 City 编译期声明或形成可独立打包的应用/插件边界时，才重新审计；普通仓库级编译规范仍应留在 AtomBox 的 `Directory.Build.props`。

## Generators 专项审计

### 当前可生成输入

- Application 没有 Route、View、Localization、Plugin、Data Client 或 EventBus 生成元数据；这些 Generator family 没有输入对象。
- `AddAtomBoxApplication()` 显式调用 `AddState()`，并注册状态实现、只读状态接口、投影器、初始化器和四个应用服务。代码规模小，Application 合同测试会检查服务类型与 Singleton 生命周期。
- Desktop 已把 City.Generators 作为 Analyzer 接入自己的编译单元，但 Roslyn Generator 不会扫描并重写项目引用中的 Application 源码。Application 若要生成自身声明，仍必须单独接入 Generator。

### DI Generator 冲突

City DI Generator 可以读取 Service Attribute、lifetime marker 和 ExposeServices 元数据，但包含生成服务的程序集必须声明且只能声明一个带 `ServiceRegistrationOwner` 的 `IModule`。Application 当前没有独立 Module、生命周期资源或启停事务，前述 Core 审计已经决定不为程序集分层机械创建 City Module。

把当前注册迁移为生成代码，需要给业务服务增加 City DI 元数据、创建一个仅承接注册的 Application Module 并接入 Analyzer；`AddState()` 等显式组合逻辑仍然需要保留。这样只是用额外模块概念和编译期管线替代少量清晰、已测试的 `AddSingleton`，没有净收益。

### 决策

`AB-APPLICATION × CITY-GENERATORS` 标记为 `否`。保留显式 `AddAtomBoxApplication()`；City.Generators 本身不存在能力缺陷，本单元不修改 City、Application 源码或项目文件。

未来只有 Application 真正成为独立 City Module，或出现大量模块化服务、事件、路由等编译期声明时，才连同 Module 所有权重新审计 Generator，而不能单独为了减少几行 DI 注册启用它。

## CLI 专项审计

### 当前进程边界

- AtomBox 当前只有 Desktop 应用入口，没有 `AtomBox.Cli` 业务前端；Application 自身是普通类库，不解析命令行、不读写 Console，也不决定进程 exit code。
- City.CLI 是入口固定为 `atomui city` 的独立工程工具进程，当前负责创建或生成 City 工程产物、转发 `dotnet build/test/pack/publish`、检查 City workspace 和插件包，并输出 CI/Agent envelope。
- City.CLI 明确不承载业务领域能力，也没有列出 AtomBox 账号、浏览远端文件、修改设置或创建传输任务等业务命令。

### 依赖方向

未来若 AtomBox 正式增加命令行产品入口，正确结构应为独立的 `AtomBox.Cli` 适配器调用 AtomBox.Application，再由 Application 编排 Core、Transfer 和 Providers。Application 不得反向引用任何 CLI 前端或框架工程工具。

City.CLI 的 public `CliApplication` 仍然实现固定的 City 工具命令集，不是可嵌入业务应用的通用 Command Host。是否为未来 `AtomBox.Cli` 选择其他命令解析库，应由该新前端项目单独审计，不改变 Application 的依赖边界。

### 决策

`AB-APPLICATION × CITY-CLI` 标记为 `否`。Application 不引用 City.CLI；本轮不创建 AtomBox 业务 CLI 项目，也不修改 City 或 AtomBox 运行时代码。

未来业务 CLI 立项时，应在迁移矩阵中新增独立 AtomBox 模块行，逐项审计 Host 组合、业务命令、非交互模式和输出合同，不能把该需求记作 Application 采用 City.CLI。

## Templates 专项审计

### 现有结构与模板产物

- Application 已是结构稳定的既有业务类库，不需要 City Application Template 创建新工作区。
- Module Template 固定生成 `ModuleBase`、Module Options 和 Contribution identity；Application 当前没有独立 City Module 生命周期，使用该模板会推翻已经确认的 Core 边界。
- Page 与 Localization Template 属于 Desktop 展示边界；Plugin Template 没有当前产品合同。
- Configuration Template 生成 Microsoft Options、`IValidateOptions` 和启动配置骨架。AtomBox `ApplicationSettings` 是用户运行时可编辑、经 Repository 持久化并投影到 City.State 的业务数据，不是 Host 启动 Options。
- Test Template 当前生成占位的 `Assert.True(true)` 和通用测试矩阵；Application 已有针对真实用例、Repository、并发和 City.State 合同的完整业务测试，通用骨架不能替代它们。

### 决策

`AB-APPLICATION × CITY-TEMPLATES` 标记为 `否`。开发者临时参考模板不等于模块采用 Templates；Application 生产项目和测试项目均不引用模板程序集。本单元不修改 City 或 AtomBox 运行代码。

未来若 AtomBox 要为自身业务模块建立脚手架，应由 Engineering 设计符合 AtomBox 分层、命名、DI、测试和文档规则的项目专属模板，再建立独立矩阵单元；不能让通用 City 模板反向定义 Application 架构。

## City 前置能力（已完成）

原 City 的 `Value/Version` 读取和 `OnChange` 注册是两个独立动作，调用间存在漏失更新窗口；`ApplicationStateRegistry.CreateWriter(...)` 也没有抽象工厂进入 DI。因此本迁移先在 City 正式仓库完成：

- `AUC-STATE-009` 原子 `Observe(...)`：同一同步边界捕获当前快照并注册订阅，锁外先投递初始值，再按版本顺序投递更新。
- `IApplicationStateWriterFactory`：模块通过 `StateWriteAuthority.Module("AtomBox.Application")` 获取受 OwnerWrite 约束的 writer。

City 文档提交 `c2843b9`、实现提交 `ac70dad` 已同步 `develop`；AtomBox 的 `external/AtomUI.City` 已更新至 `ac70dad`。

## 状态模型与所有权

- `IAtomBoxApplicationState` 对外只暴露 `IReadOnlyState<AccountCatalogState>` 和 `IReadOnlyState<ApplicationSettingsState>`。
- 两个 State 均为 Application lifetime、OwnerWrite、Transient snapshot；Owner 固定为 `AtomBox.Application`。
- 状态显式区分 `Uninitialized`、`Ready`、`Faulted`。读取失败不得发布伪造的空账号或默认设置。
- `AccountCatalogState` 只包含 `StorageAccountSummary` 不可变快照和安全错误摘要。
- `ApplicationSettingsState` 只包含 Core `ApplicationSettings` 快照和安全错误摘要。
- Desktop、未来 CLI 或其他前端都是只读消费者；写入只发生在 Application 初始化器和用例提交成功之后。

## 数据流

```text
Application initializer
  -> repositories
  -> publish Ready / Faulted state

AccountAppService / SettingsAppService successful mutation
  -> repository remains source of truth
  -> reload committed snapshot
  -> owner writer publishes state

Desktop activation
  -> State.Observe(... Dispatcher ...)
  -> initial snapshot
  -> ordered subsequent updates
```

账号添加、更新、删除和草稿连接测试中的凭据保存/清理由 `AccountAppService` 编排。Desktop 对话框只采集 `CredentialSecretMaterial` 并作为一次请求的瞬时输入传入 Application。明文不得进入 State、结果对象、缓存、日志或长期字段。

## Desktop 消费调整

- 删除 `IAccountDialogWorkflow.AccountsChanged` 及其本地 Channel 合并刷新实现。
- 账号管理、Shell、Home 和远程浏览通过账号 State 建立展示投影。
- Settings 页面通过设置 State 获得初始值和后续保存/重置结果。
- Dialog、确认框、文件选择器和本地化继续属于 Desktop；它们不下沉到 Application。

## 非目标

- 不把远端目录、传输队列或 Provider session 放入本轮 State。
- 不引入 City.EventBus。
- 不让 State 成为第二套持久化或恢复机制。
- 不在本轮处理 Transfer 内部批量提交、worker 或历史分页状态。

## 验收

- City 原子观察、集合观察、并发顺序、Dispose 竞态和 Writer authority 测试通过。
- Application 初始化成功/失败、账号增删改、设置保存/重置、凭据补偿和 State 更新测试通过。
- Desktop 不再声明或订阅 `AccountsChanged`，账号/设置投影使用 `Observe(...)`。
- State、返回 DTO 和诊断文本中均无凭据明文。
- AtomBox 全量测试和 Release 构建通过；真实 UI 烟测由用户最终执行。

## 自动化实施证据

- City.State：154/154 测试通过；Public API governance：4/4；Release 全解构建 0 warning / 0 error。
- AtomBox.Application：162/162 测试通过，覆盖初始化成功/失败、原子首值观察、并发投影顺序、账号与设置状态发布以及凭据写入/替换/清理。
- AtomBox.Desktop：31/31 测试通过，覆盖 Shell 对 Application State 的激活期观察和依赖边界。
- 生产代码中已删除 `IAccountDialogWorkflow.AccountsChanged` 和 Desktop 对 `ICredentialStore` 的直接依赖。
- AtomBox 全仓 Release：677/677 测试通过；全解构建 0 warning / 0 error。
- 真实 UI 验收按用户决定由人工执行。
