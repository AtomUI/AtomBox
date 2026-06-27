# AtomBox Desktop 技术骨架

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的 AtomBox.Desktop 第一版技术骨架、目录结构、启动流程和基础服务边界
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的技术骨架，必须先说明原因，再同步更新本文档及相关 Presentation 文档。

本文档定义 `AtomBox.Desktop` 第一版代码骨架。它关注 Avalonia / AtomUI 应用如何启动、如何组合依赖、如何承载主窗口、如何切换页面、如何统一弹窗和消息。

本文档不定义具体页面线框图。具体页面结构继续以 `docs/ui/` 下文档为准。

## 1. 设计目标

Desktop 技术骨架必须满足以下目标：

- 保持轻量，不引入 ReactiveUI、复杂路由框架或自造大型 MVVM 框架。
- 明确 `Shell`、`Navigation`、`ViewFactory`、`DialogService`、`MessageService` 的边界。
- 保证 ViewModel 只调用 Application 用例服务，不直接接触 Provider、Transfer worker、SDK client、Infrastructure 实现。
- 使用显式注册，避免运行时反射扫描，服务 AOT / trimming 目标。
- AtomUI 作为 Desktop UI 基础设施使用，但不得把 AtomUI 类型泄漏到 Core、Application、Transfer、Providers、Infrastructure。

## 2. 推荐目录

`AtomBox.Desktop` 第一版推荐目录如下：

```text
src/AtomBox.Desktop/
  AtomBox.Desktop.csproj
  Program.cs
  App.axaml
  App.axaml.cs

  Assets/

  Composition/
    DesktopCompositionRoot.cs
    ServiceCollectionExtensions.cs

  Shell/
    MainWindow.axaml
    MainWindow.axaml.cs
    MainWindowViewModel.cs
    StatusBarViewModel.cs

  Navigation/
    INavigationService.cs
    NavigationService.cs
    NavigationTarget.cs
    NavigationParameter.cs
    NavigationItemViewModel.cs
    IPageViewModelFactory.cs
    PageViewModelFactory.cs

  ViewFactory/
    IViewFactory.cs
    ViewFactory.cs
    ViewRegistration.cs

  Dialogs/
    IDialogService.cs
    DialogService.cs
    AccountDialog/
      AccountDialogView.axaml
      AccountDialogViewModel.cs
      AccountDialogResult.cs

  Services/
    IMessageService.cs
    MessageService.cs
    IUiDispatcher.cs
    AvaloniaUiDispatcher.cs

  Views/
    RemoteBrowser/
    Transfers/
    Settings/
    Accounts/

  ViewModels/
    ViewModelBase.cs
    RelayCommand.cs
    AsyncRelayCommand.cs

  Resources/
    Styles.axaml
    Theme.axaml
```

## 3. 目录职责

| 目录 | 职责 |
| --- | --- |
| `Composition/` | Desktop 唯一组合根，负责 DI 容器创建、模块注册、主窗口创建。 |
| `Shell/` | 主窗口、顶部品牌区、左侧菜单、右侧内容承载区、底部状态栏。 |
| `Navigation/` | 管理应用内当前位置、导航目标、导航参数、菜单项 ViewModel 和页面 ViewModel 显式工厂。 |
| `ViewFactory/` | 根据 ViewModel 显式创建对应 View。 |
| `Dialogs/` | 统一弹窗入口和弹窗 ViewModel。 |
| `Services/` | UI 专用服务，例如消息提示、UI 线程调度。 |
| `Views/` | 具体页面 View。 |
| `ViewModels/` | 页面 ViewModel、行 ViewModel、命令基础类型。 |
| `Resources/` | 样式、主题、资源字典、字体资源引用。 |
| `Assets/` | 图标、封面、图片等 Desktop 静态资源。 |

## 4. 启动流程

Desktop 启动流程如下：

```text
Program.Main
  -> AppBuilder.Configure<App>()
  -> UsePlatformDetect()
  -> WithAtomUIDefaultOptions()
  -> StartWithClassicDesktopLifetime(args)

App.Initialize
  -> UseAtomUI(...)
  -> Load XAML

App.OnFrameworkInitializationCompleted
  -> DesktopCompositionRoot.Build()
  -> Resolve MainWindow
  -> Show MainWindow
```

启动阶段只做本地初始化和依赖装配，不连接远程 provider，不刷新远程目录，不自动恢复执行传输任务。

## 5. Shell 边界

`MainWindow` 是 Desktop 唯一主 Shell。

Shell 允许包含：

- 顶部品牌区。
- 左侧 AtomUI `NavMenu`。
- 右侧 `ContentControl`。
- 底部状态栏。

Shell 禁止包含：

- 远程 provider 调用。
- SDK client。
- Transfer worker。
- 凭据明文。
- 账号新增或编辑表单的内联实现。

Shell 负责承载页面，不负责实现页面业务。

## 6. Navigation 与 ViewFactory 边界

`NavigationService` 负责决定当前业务位置，并输出当前 ViewModel。

`ViewFactory` 负责把当前 ViewModel 转成 Avalonia `Control`。

两者关系如下：

```text
左侧菜单点击 / 页面命令
  -> NavigationService.NavigateAsync(target, parameter)
    -> IPageViewModelFactory.Create(target, parameter)
      -> CurrentViewModel
        -> ViewFactory.CreateView(CurrentViewModel)
          -> ContentControl 展示 View
```

Navigation 不创建 View。ViewFactory 不决定导航目标。

`NavigationService` 不直接持有 `IServiceProvider`，也不直接 `new` 页面 ViewModel。页面 ViewModel 由 `IPageViewModelFactory` 通过组合根传入的显式工厂委托或具体页面工厂创建。

## 7. Dialog 与 Message 边界

弹窗和消息必须通过 Desktop 服务封装：

- `IDialogService` 负责账号新增、账号编辑、确认框、错误详情等阻塞式交互。
- `IMessageService` 负责轻量成功、失败、警告提示。

页面和 ViewModel 不得散落直接调用 AtomUI 静态弹窗或消息 API。否则后续全局样式、错误处理、日志关联、自动化测试会变成灾难。

## 8. ViewModel 规则

ViewModel 可以包含：

- UI 展示状态。
- 选中项。
- 加载状态。
- 错误展示状态。
- 命令。
- 调用 Application 用例服务后的结果快照。

ViewModel 禁止包含：

- Provider 实例。
- SDK client。
- Transfer worker。
- Infrastructure repository 具体实现。
- Secret material。
- Avalonia 之外模块可见的 UI 类型。

ViewModel 生命周期跟随页面、窗口或工作区，不作为 Core 业务对象。

## 9. 禁止事项

- 不使用 ReactiveUI。
- 不把 `IObservable` 作为状态、命令、路由和事件系统主公共 API。
- 不做运行时程序集扫描来发现 View 或服务。
- 不引入 Router，除非未来出现外部深链、可序列化导航地址或历史栈需求。
- 不在第一版业务列表使用 AtomUI `DataGrid` 或 Avalonia 原生 `DataGrid`。
- 不把 Navigation、ViewFactory、DialogService 混成一个万能 UI 服务。
