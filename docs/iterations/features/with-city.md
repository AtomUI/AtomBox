# AtomBox × AtomUI.City 迁移审计

## 迁移结论

AtomBox Desktop 的框架职责已迁移到 AtomUI.City：City Host 是唯一组合根，City Module 管理应用初始化和关闭，City Routing 保存导航真相，City Presentation 管理 WindowSession、View 注册、ViewModel lease、绑定、Outlet 提交和旧页面释放。

业务程序集仍保持框架无关；迁移没有把 City 类型下沉到 Core、Application、Transfer、Providers 或 Infrastructure。

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
  -> AtomUI.City.Core / Routing / Presentation / Localization / Generators
  -> AtomUI.City.Build (RESX -> locpack at build time)
  -> AtomBox.Application / Transfer / Providers / Infrastructure / Core

AtomBox.Application / Transfer / Providers / Infrastructure / Core
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
- 菜单选中键仍由 AtomBox 维护，因为它是 NavMenu 的应用展示状态，不是第二套路由状态。

## 本地化迁移

本地化运行时已迁移到 City。Desktop 继续把 RESX 作为便于 C# 生态维护的源格式；City Build 在编译期将六个 Catalog、两种语言转换为嵌入式 locpack，并生成资源声明。应用启动阶段把生成的语言包注册到 `LanguagePackageRegistry`，之后语言解析、回退、版本切换和 Presentation culture bridge 均由 City 负责。

AtomBox 保留一个同步只读投影 `IAtomBoxLocalizedText`，用于兼容 Avalonia XAML Binding 和现有同步 ViewModel 调用。投影在初始化时异步创建 City localized-text handles；切换语言时按同一 City revision 收齐所有资源后一次性发布不可变快照，因此 UI 不会观察到中英混杂的中间状态。语言偏好和切换命令由 `IDesktopLanguageService` 适配到 City，AtomBox 不再持有第二套 ResourceManager 或语言解析实现。

## Window 生命周期修复

AtomBox 支持“关闭时最小化到托盘”。应用的 Avalonia `Closing` 处理器会先取消用户关闭并隐藏窗口。City 原实现仍会接管已取消的关闭，导致 WindowSession 继续结束。

City `353d3b9` 修复了该契约：非 OS 关闭若已被更早的应用处理器取消，WindowSession 保持 `Ready`；OS shutdown 仍不可拒绝。修复包含真实 Avalonia Headless Window 回归和原有 Outlet 压力场景。

## 保留的应用能力

以下实现保留在 AtomBox Desktop，并不构成第二套 Host、Router、Localization 或 Presentation 框架：

- 同步 XAML `Loc` 扩展和 localized-text 快照投影；它们是 City 异步本地化运行时到 Avalonia 同步 Binding 合同的 UI adapter；
- AtomBox 的轻量 ViewModel 基类和命令在第一阶段迁移中暂时保留；第二阶段已完成 21 项专项审计，将按 [AtomBox × AtomUI.City MVVM 迁移](with-city-mvvm.md) 迁入 City 的生命周期和 Command 合同；
- Dialog、Message、文件选择器和托盘菜单；这些是应用 UI adapter，由 Desktop 组合层拥有。

第一阶段中，City Presentation 已经为路由页面提供 DI scope lease、Outlet 生命周期和确定性释放；第二阶段将进一步统一 ViewModel 激活资源、异步任务、Command，以及非路由 Shell Root/Child 的所有权。第一阶段的“保留”不再表示永久架构结论。

## 自动验证

- AtomBox Release 全解构建：0 warning、0 error；
- AtomBox 全量测试：666/666；
- win-x64、linux-x64、osx-arm64 自包含单文件发布全部成功；
- Desktop 测试：25/25，覆盖 Host 启停、六条 route、六个 View 注册、导航参数往返以及 City 本地化集成；
- 本地化集成测试覆盖 City embedded package 读取、英文到简体中文切换，以及同一 revision 的原子快照发布；
- City Generator 测试 206/206、Localization 测试 96/96；静态 embedded package 不依赖 `Assembly.Location`，单文件发布可直接加载 source assembly；
- City Presentation Headless 进程测试：4/4，覆盖真实 Avalonia Window/VisualTree、预取消 Closing 和 10,000 次 Outlet 提交压力；
- `src` 中不存在 `BuildServiceProvider`；
- 非 Desktop 业务程序集不存在 `AtomUI.City` 引用；
- Desktop 运行时代码不存在旧 `ResourceManager` 或自建 `ILocalizationService`；
- 已删除的自建 ViewFactory、PageViewModelFactory 和 `CurrentViewModel` 在源码与测试中均无残留引用。

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
