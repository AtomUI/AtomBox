# AtomBox × AtomUI.City 迁移审计

> 统领文档：[AtomBox × AtomUI.City 迁移主矩阵](with-city-migration-matrix.md)
>
> 本文是 Desktop 已实施能力和已决审计单元的专项记录；后续范围、顺序和状态以迁移主矩阵为首要依据。

## 迁移结论

AtomBox Desktop 的框架职责已迁移到 AtomUI.City：City Host 是唯一组合根，City Module 管理应用初始化和关闭，City Routing 保存导航真相，City Presentation 管理 WindowSession、View 注册、ViewModel lease、绑定、Outlet 提交和旧页面释放。

City 能力遵循业务所有权落位：Application 和 Transfer 分别直接采用 City.State；Core、Providers 和 Infrastructure 保持零 City 引用。Desktop 只消费上游状态并形成 UI 投影，不接管上游业务事实。

## 当前架构

```text
Avalonia desktop lifetime
  -> City ApplicationHost / module lifecycle
      -> RoutingModule
      -> PresentationModule
      -> AtomBoxDesktopModule
          -> City language-package registration
          -> AtomBox localized-text projection
          -> existing AtomBox application services

City Router
  -> AtomBox route/parameter adapter
  -> City ViewModel lease
  -> generated ViewRegistry
  -> ViewFactory + ViewBinder
  -> primary RouteOutlet
```

依赖方向：

```text
AtomBox.Desktop
  -> AtomUI.City.Core / Mvvm / Routing / Presentation / Localization / Generators
  -> AtomUI.City.State（只读消费 Application / Transfer 状态）
  -> AtomUI.City.Build (RESX -> locpack at build time)
  -> AtomBox.Application / Transfer / Providers / Infrastructure / Core

AtomBox.Application / Transfer
  -> AtomUI.City.State

AtomBox.Providers / Infrastructure / Core
  -X-> AtomUI.City
```

## 已完成的框架迁移

- `AtomUI.City` 以 Git 子模块固定版本；
- `ApplicationHost` 是唯一 Root Provider 所有者；
- `ValidateOnBuild` 与 `ValidateScopes` 在所有环境启用；
- Infrastructure 与 Transfer 初始化进入 `AtomBoxDesktopModule` 生命周期；
- Avalonia UI 线程和 desktop lifetime 由 City Presentation 接入；
- 主窗口在首屏导航前注册 WindowSession 和 `primary` RouteOutlet；
- 六个页面由 `[ViewFor]` 和生成的 `GeneratedPresentationViewRegistrar` 注册；
- 原有手写 `IViewFactory`、View 映射和 DataTemplate 已删除；
- 六条页面路由进入 City route graph；
- 账号、远端路径和历史页码参数先编码进 City 路由快照，再重建为 AtomBox 参数；
- 原有 `IPageViewModelFactory` 和 `CurrentViewModel` 双重页面状态已删除；
- 页面实例、DI scope、View 解绑和旧页面释放由 City Outlet entry 所有；
- Desktop ViewModel 统一继承 City `ViewModelBase`，页面初始化、订阅、循环和取消进入 ActivationScope；
- Desktop Command 统一使用 City CommandFactory 和 CommunityToolkit 公共接口，AtomBox 自建 Command 已删除；
- MainWindow 是 WindowSession 的逻辑 Root，StatusBar 是显式 owned child；路由页面仍由 RouteOutlet 独立拥有；
- 定时刷新和账号变化刷新分别使用 `StartUniqueAsync`、`PeriodicTimer` 和容量 1 的 Channel，停用时取消并 drain；
- 菜单选中键仍由 AtomBox 维护，因为它是 NavMenu 的应用展示状态，不是第二套路由状态。

## Desktop EventBus 审计

`AB-DESKTOP × CITY-EVENTBUS` 的决策是**不采用**。

账号、设置、传输队列和进度都是上游业务事实，已经由 Application/Transfer 的 City.State 提供。Desktop 订阅这些状态并投影到 UI；如果再次发布同义事件，会形成状态与事件双通道及额外的一致性责任。

Desktop 当前剩余事件均为局部 UI 协作：

- `NavigationService.CurrentMenuKeyChanged` 在导航服务和主窗口 ViewModel 之间同步菜单选中状态；
- `MainWindowViewModel.CurrentMenuKeyChanged` 和 `RemoteAccountMenuRefreshed` 只通知其对应窗口更新控件；
- `LanguageChanged` 与 `PropertyChanged` 分别承担本地化投影和标准 MVVM Binding 通知；
- Avalonia 控件事件属于窗口、菜单和托盘 adapter 的直接交互合同。

这些通知的发布者、消费者和生命周期都明确，不是跨模块的一对多业务事件。改用 City.EventBus 会额外引入事件合同、EventBus Host 模块、Scope owner、异步派发、失败策略和顺序测试，同时隐藏原本清晰的一对一关系。因此本单元不修改 City，也不修改 AtomBox 运行时代码。未来只有出现由 Desktop 真正拥有、需要多个未知消费者处理的一次性 UI 事实时，才重新审计 EventBus。

## Desktop Security 审计

`AB-DESKTOP × CITY-SECURITY` 的决策是**不采用**。

AtomBox 当前没有应用用户登录、`ClaimsPrincipal`、角色、权限点、登录/登出、路由授权或命令授权。Desktop 账号弹窗采集的密码、AccessKey、Token 和 SFTP 私钥属于多个可同时存在、可并发使用的外部存储连接；Desktop 只把短暂输入交给 Application 用例，持久化和 lease 由 Core/Infrastructure 合同管理，实际认证由 Provider 执行。

City.Security 管理的是 City Host 当前应用主体、认证状态、Permission、Route Guard、Command Authorization 和 Access Token。它虽然支持持久化多个身份账号，但一个 Host 同一时刻只发布一个全局活动主体，并且明确不保存用户密码。该模型不能替代 AtomBox 的 `CredentialRef`、`CredentialMaterialLease` 和 Provider 认证链路，也不能把远端服务返回的认证/授权结果解释成 AtomBox 本地用户权限。

强行接入会把多个并发存储连接错误映射成单一活动用户，引入两套同名但语义不同的 `ICredentialStore`，并把凭据责任错误上移到 Desktop。因此本单元不修改 City，也不修改 AtomBox 运行时代码。

本次审计同时登记两项独立安全工程问题，但不在 City.Security 迁移中顺手施工：

- `CredentialSecretMaterial` 使用不可清零的 `string` 和冻结字典保存敏感值；
- 账号弹窗测试连接使用 `CancellationToken.None`，关闭弹窗不能取消已经开始的远端认证测试。

它们应按实际所有权单独设计和治理，不能作为引入 City.Security 的理由。

## Desktop PluginSystem 审计

`AB-DESKTOP × CITY-PLUGINSYSTEM` 的决策是**不采用**。

AtomBox 当前 15 个 Provider 全部由 `AddAtomBoxProviders()` 在编译期静态注册；`StorageProviderRegistry` 和 `StorageProviderFactory` 在应用启动时建立固定 descriptor/creator 映射。当前没有插件目录、manifest、安装、启停、动态加载、动态卸载、更新或插件管理 UI，正式 Provider 设计也明确把独立插件发布排除在第一版范围之外。

City.PluginSystem 提供发现、安装、manifest、依赖校验、独立 ServiceProvider/加载上下文、Contribution lease 和卸载诊断等通用运行时能力，但不会自动定义 AtomBox 的 Provider 插件合同。即使未来正式立项 Provider 插件化，稳定共享合同、动态 descriptor/creator registry、ProviderId 冲突处理、停用后的新实例拒绝、现有 Provider/凭据 lease 排空和 contribution 撤销，都应由 Providers 或专用集成项目拥有。Desktop 最多通过 Application 用例提供插件管理 UI，不能持有插件加载器或 Provider contribution registry。

因此本单元不修改 City，也不修改 AtomBox 运行时代码。未来需求立项时必须重新开启 `AB-PROVIDERS × CITY-PLUGINSYSTEM` 专项审计，不能直接在 Desktop 中接入插件运行时。

## Desktop Data 审计

`AB-DESKTOP × CITY-DATA` 的决策是**不采用**。

Desktop 生产代码没有直接使用 `HttpClient`、HTTP request/response、gRPC、SignalR、WebSocket、FTP/SFTP client 或厂商 SDK，也没有引用 `AtomUI.City.Data`。页面和对话框只调用 `AccountAppService`、`RemoteBrowserAppService`、`TransferAppService`；`IStorageProviderRegistry` 在账号弹窗中只提供静态 Provider 描述信息，不承担远程访问。

账号连通性测试、远程目录浏览、文件预览、上传和下载虽然由 UI 操作触发，但真正的业务链路是：

```text
Desktop
  -> Application / Transfer
    -> IStorageProviderFactory
      -> Providers
        -> 厂商 SDK / HTTP / FTP / SFTP
```

City.Data 是 HTTP、gRPC、SignalR 等外部数据源的 transport 与 request pipeline，负责连接生命周期、认证、缓存、弹性策略、错误映射和诊断；其正式合同也明确要求 ViewModel 不直接使用裸网络客户端。当前 AtomBox 分层已经满足这一约束，不需要为了“使用 City.Data”把网络责任上移到 Desktop。

Providers 已在 `AB-PROVIDERS × CITY-DATA` 专项中决定本轮不采用 City.Data。Desktop 若绕过该结论自行接入，会重复或越过 Provider SDK、外部账号认证、Provider 错误映射、mutation 幂等、stream replay 和连接生命周期合同，形成第二条远程访问通道，并违反能力归属铁律。因此本单元不修改 City，也不修改 AtomBox 运行时代码。

## Desktop Testing 审计

`AB-DESKTOP × CITY-TESTING` 的决策是**不采用**。

`AtomBox.Desktop.Tests` 当前通过 `Program.CreateHost()` 启动真实 AtomBox City Host，使用真实的 `AtomBoxDesktopModule`、DI 注册、Application/Transfer State 初始化、生成的本地化资源与 View 注册，并验证六条生产路由和六组生产 View 映射。该测试不会启动真实 Avalonia 窗口，但它证明的是 AtomBox 实际组合根，而不是重新搭建的一套测试模块图。

City.Testing 当前成熟度仍为 `Partially Implemented`。它的 `TestHost` 不暴露可替代生产 `IApplicationHost` 的 ServiceProvider、模块组合和 ApplicationScope；`ModuleTestHost` 缺少为 AtomBox 注入 `AtomBoxStoragePaths`、桌面偏好等组合根服务并检查最终服务容器的入口；`RoutingTestHost` 需要测试重新声明 route name、pattern 和 ViewModel type，不能验证 AtomBox 生成并注册的真实 RouteGraph。Testing 文档规划的 `PresentationTestRuntime`、fake ViewLocator、fake Outlet 等 Presentation 测试运行时在当前源码中也尚未实现。

当前可直接复用的 `FakeUiDispatcher` 可以替换 Desktop 测试中的 `ImmediateUiDispatcher`，但本地化、导航与账号对话框替身仍是 AtomBox 业务合同，只能由 AtomBox 自己维护。`DeterministicScheduler` 只控制显式提交给它的 work item，不能接管生产代码现有的 Task、Channel、State notification 或 `PeriodicTimer`。为了这一个 dispatcher 替身引用 City.Testing，会额外带入 Core、Data、EventBus、Localization、Mvvm、PluginSystem、Presentation、Routing、Security、State 和 Roslyn，依赖成本明显高于收益。

因此保留现有真实 Host 集成测试和小型应用专属替身，不新增 City.Testing 项目引用，也不修改 City。审计期间 Release Desktop 测试 32/32 通过。

本次同时登记一项独立测试工程债：`MvvmMigrationTests.WaitUntilAsync` 使用 `Task.Delay(10)` 轮询异步状态。它应在后续测试治理中改为明确的完成信号、可等待版本或其他确定性同步合同，但不能作为引入整个 City.Testing 包的理由。

## Desktop CLI 审计

`AB-DESKTOP × CITY-CLI` 的决策是**不采用**。

City CLI 是命令进程边界中的工程工具，不是 Desktop 运行时能力。AtomBox 已经通过 MSBuild 直接接入 City.Build 和 City.Generators；运行时 Host、路由、View 注册和本地化都不依赖 CLI。若要采用 CLI，其所有权也应属于仓库级 Engineering，而不是 `AtomBox.Desktop` 生产项目。

AtomBox 当前由 `build.ps1` 编排 restore、build、test 和可选 Desktop 启动，由 `publish-self-contained-singlefile.ps1` 编排三个 RID 的自包含单文件发布，并由 GitHub Actions 处理 Windows ZIP、macOS 签名 DMG、Linux AppImage、平台资源、PDB 清理和 Release。City CLI 的 build/test/pack/publish 当前只向 dotnet 转发 `--project`、`--configuration` 和 `--framework`，不能表达 AtomBox 的 runtime、自包含、单文件、压缩、原生库提取、签名与平台打包合同，不能替代现有入口。

City CLI 的通用检查也不能为 Desktop 提供新增证据：`inspect workspace` 当前硬编码查找 `AtomUICity.slnx` 和 `output/`，与 AtomBox 的 `AtomBox.slnx` 和 `.artifacts/` 不一致；非 workspace 的 project/module/route/manifest inspect 尚未读取实际工程事实，只返回请求的 target；docs/tests check 目前只检查目录是否存在。将这些命令加入 AtomBox 会形成一套结果不完整甚至可能误导的额外入口。

因此 AtomBox 不安装或包装 City CLI，不修改 City，不修改 Desktop 运行时代码，也不改变现有脚本和 CI。City CLI 的 generation/new/plugin 命令分别属于 Templates、新项目创建或未来插件专项，不能算作 Desktop 采用 CLI 的理由。

本次同时记录一项 City 自身文档一致性发现：CLI 的 build/inspect 专题文档描述了更多参数和 project/module/route/manifest 检查能力，但当前 Feature 表与代码尚未完整实现这些内容。该问题不阻塞 AtomBox，因为本单元已经决定不采用；若 City 后续继续演进 CLI，应在 City 仓库单独校正文档或补齐实现。

## Desktop Templates 审计

`AB-DESKTOP × CITY-TEMPLATES` 的决策是**不采用**。

City.Templates 已提供具备 create-only、冲突检测、取消和失败回滚合同的 application、module、page、localization、configuration 与 test 模板，适合创建遵循通用 City 约定的新工作区。Templates 是工程期代码生成能力，不应进入 `AtomBox.Desktop` 生产引用；是否用它维护已有项目，还取决于生成物能否直接符合应用自身约定。

AtomBox Desktop 已稳定采用集中式 `AtomBoxRouteCatalog`、`NavigationTarget`、菜单键和参数编解码，View 与 ViewModel 分目录组织，ViewModel 显式注册进 DI，所有 View 实现 `IViewDataContextAware`，本地化采用 Host scope 多 Catalog RESX/locpack 和同步 `IAtomBoxLocalizedText` 投影，并通过真实 `Program.CreateHost()` 检查最终路由与 View 注册。City 增量 Page 模板则按 `Routes/<Page>` 生成独立 `[RouteMap]`、最小 ViewModel/View 和反射测试，不会修改 AtomBox 的路由目录、导航枚举、菜单、参数 codec 或 DI；Localization 模板生成 Module scope 包、独立 key 类型和测试，也不会接入 AtomBox 的 Catalog 与投影。

City.Templates 当前没有应用级模板覆盖、AtomBox 专属模板包或增量合并钩子。使用通用模板后仍需手工拆分目录、改写 route id/outlet、补 DI、接导航和菜单、调整本地化作用域并重写测试；这会产生一套貌似标准但未真正接入应用的中间代码，收益低于按 AtomBox 既有约定直接新增页面。因此不为 AtomBox 安装、包装或引用 City.Templates，也不修改 AtomBox 运行时代码。未来若页面数量和团队规模使脚手架收益成立，应建立由 Engineering 所有的 AtomBox 专属模板并重新审计，而不是直接套用通用模板。

本次审计还发现 City `AUC-TEMPLATES-007` 的独立高优先级缺陷：Page 模板生成的 Avalonia View 带有 `[ViewFor]`，但没有实现 `IViewDataContextAware`；City `ViewBinder.Bind` 对缺少该接口的 View 必然抛出 `PresentationError.BindingFailed`。现有生成测试只验证文件、编译、attribute 和 ViewModel activation，没有执行真实 `ViewBinder.Bind`，因此不能证明页面链路可运行。该缺陷应在 City 仓库修复模板输出并补充真实绑定回归，但它不改变 AtomBox 本单元的“不采用”决策，也不构成 AtomBox 迁移阻塞。

## 本地化迁移

本地化运行时已迁移到 City。Desktop 继续把 RESX 作为便于 C# 生态维护的源格式；City Build 在编译期将六个 Catalog、两种语言转换为嵌入式 locpack，并生成资源声明。应用启动阶段把生成的语言包注册到 `LanguagePackageRegistry`，之后语言解析、回退、版本切换和 Presentation culture bridge 均由 City 负责。

AtomBox 保留一个同步只读投影 `IAtomBoxLocalizedText`，用于兼容 Avalonia XAML Binding 和现有同步 ViewModel 调用。投影在初始化时异步创建 City localized-text handles；切换语言时按同一 City revision 收齐所有资源后一次性发布不可变快照，因此 UI 不会观察到中英混杂的中间状态。语言偏好和切换命令由 `IDesktopLanguageService` 适配到 City，AtomBox 不再持有第二套 ResourceManager 或语言解析实现。

## Window 生命周期修复

AtomBox 支持“关闭时最小化到托盘”。应用的 Avalonia `Closing` 处理器会先取消用户关闭并隐藏窗口。City 原实现仍会接管已取消的关闭，导致 WindowSession 继续结束。

City `353d3b9` 修复了该契约：非 OS 关闭若已被更早的应用处理器取消，WindowSession 保持 `Ready`；OS shutdown 仍不可拒绝。修复包含真实 Avalonia Headless Window 回归和原有 Outlet 压力场景。

## 保留的应用能力

以下实现保留在 AtomBox Desktop，并不构成第二套 Host、Router、Localization 或 Presentation 框架：

- 同步 XAML `Loc` 扩展和 localized-text 快照投影；它们是 City 异步本地化运行时到 Avalonia 同步 Binding 合同的 UI adapter；
- Dialog、Message、文件选择器和托盘菜单；这些是应用 UI adapter，由 Desktop 组合层拥有。

第二阶段的 21 项专项审计和实施证据见 [AtomBox × AtomUI.City MVVM 迁移](with-city-mvvm.md)。

## 自动验证

- AtomBox Release 全解构建：0 warning、0 error；
- AtomBox 全量测试：693/693；
- win-x64、linux-x64、osx-arm64 自包含单文件发布全部成功；
- Desktop 测试：32/32，覆盖 Host 启停、六条 route、六个 View 注册、导航参数往返、City 本地化、MVVM 生命周期、Shell composition、唯一 worker 和依赖边界；
- City MVVM 测试 89/89、Public API governance 4/4，Mvvm net8.0/net10.0 构建通过；
- 本地化集成测试覆盖 City embedded package 读取、英文到简体中文切换，以及同一 revision 的原子快照发布；
- City Generator 测试 206/206、Localization 测试 96/96；静态 embedded package 不依赖 `Assembly.Location`，单文件发布可直接加载 source assembly；
- City Presentation Headless 进程测试：4/4，覆盖真实 Avalonia Window/VisualTree、预取消 Closing 和 10,000 次 Outlet 提交压力；
- `src` 中不存在 `BuildServiceProvider`；
- Application 和 Transfer 只引用其业务所有权需要的 `AtomUI.City.State`；Core、Providers 和 Infrastructure 不引用 City；
- Desktop 运行时代码不存在旧 `ResourceManager` 或自建 `ILocalizationService`；
- 已删除的自建 ViewFactory、PageViewModelFactory 和 `CurrentViewModel` 在源码与测试中均无残留引用。
- 已删除的自建 `ViewModelBase`、`LocalizedViewModelBase`、`RelayCommand` 和 `AsyncRelayCommand` 在源码与测试中均无残留定义。

## 人工 UI 烟测

自动验证不等价于真实桌面交互。最终由人工执行：

1. 启动编译后的 AtomBox，确认默认启动页正确；
2. 依次打开首页、远端浏览、传输队列、传输历史、设置、账号管理；
3. 从动态账号菜单进入远端浏览，确认账号和初始路径参数生效；
4. 从远端页进入队列/历史，确认路由参数和菜单选中状态一致；
5. 切换中英文，确认当前页、导航、状态栏和托盘菜单即时刷新；
6. 将关闭行为设为最小化，点击关闭后确认窗口隐藏、托盘可恢复且页面未被释放；
7. 从托盘显式退出，确认进程结束；
8. 重启后验证启动页偏好、账号数据和传输状态正常。
