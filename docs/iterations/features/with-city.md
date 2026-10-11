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
  -> AtomUI.City.Core / Routing / Presentation / Generators
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

## Window 生命周期修复

AtomBox 支持“关闭时最小化到托盘”。应用的 Avalonia `Closing` 处理器会先取消用户关闭并隐藏窗口。City 原实现仍会接管已取消的关闭，导致 WindowSession 继续结束。

City `353d3b9` 修复了该契约：非 OS 关闭若已被更早的应用处理器取消，WindowSession 保持 `Ready`；OS shutdown 仍不可拒绝。修复包含真实 Avalonia Headless Window 回归和原有 Outlet 压力场景。

## 保留的应用能力

以下实现保留在 AtomBox Desktop，并不构成第二套 Host、Router 或 Presentation 框架：

- RESX、同步 XAML `Loc` 扩展和即时语言切换；City Localization 当前采用 locpack、异步 lookup 和 scope package，直接替换会改变资源格式及 UI 绑定合同；
- AtomBox 的轻量 ViewModel 基类和命令；City Command 会把异常、取消和并发映射为 `OperationResult`，替换属于业务行为变更；
- Dialog、Message、文件选择器和托盘菜单；这些是应用 UI adapter，由 Desktop 组合层拥有。

City Presentation 已经为现有 ViewModel 提供 DI scope lease、Outlet 生命周期和确定性释放，因此保留 ViewModel 基类不会绕开 City 页面生命周期。

## 自动验证

- AtomBox Release 全解构建：0 warning、0 error；
- AtomBox 全量测试：671/671；
- win-x64、linux-x64、osx-arm64 自包含单文件发布全部成功；
- Desktop 测试：30/30，覆盖 Host 启停、六条 route、六个 View 注册以及导航参数往返；
- City Presentation Headless 进程测试：4/4，覆盖真实 Avalonia Window/VisualTree、预取消 Closing 和 10,000 次 Outlet 提交压力；
- `src` 中不存在 `BuildServiceProvider`；
- 非 Desktop 业务程序集不存在 `AtomUI.City` 引用；
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
