# AtomBox Desktop DI 设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的 Desktop 第一版依赖注入使用规则、组合根边界和 IServiceProvider 使用约束
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的 DI 边界，必须先说明原因，再同步更新相关 Presentation 文档和生命周期文档。

本文档定义 `AtomBox.Desktop` 如何使用 `Microsoft.Extensions.DependencyInjection`。本文档只讨论 Desktop 组合根和 Presentation 内部依赖装配，不重新定义全局生命周期规则。

全局生命周期规则以 `docs/architecture/lifecycle.md` 为准。

## 1. 基本原则

Desktop 使用 `Microsoft.Extensions.DependencyInjection` 作为第一版 DI 容器。

DI 是实现手段，不是设计起点。对象是否注册为 Singleton、Transient 或由工厂手动创建，必须服从它的生命周期语义、资源占用和状态归属，不能因为容器写起来方便就乱塞。

## 2. 组合根

`AtomBox.Desktop` 是唯一组合根。

组合根负责：

- 创建 `ServiceCollection`。
- 注册 Application 服务。
- 注册 Transfer 运行时。
- 注册 Providers。
- 注册 Infrastructure。
- 注册 Desktop 内部服务。
- 创建 `ServiceProvider`。
- 创建主窗口。

推荐文件：

```text
Composition/
  DesktopCompositionRoot.cs
  ServiceCollectionExtensions.cs
```

`App.axaml.cs` 只允许调用组合根，不允许散落模块注册细节。

## 3. ServiceProvider 边界

`IServiceProvider` 只能存在于以下位置：

- `DesktopCompositionRoot`。
- 测试中用于装配对象的测试组合根。

测试组合根是组合根的测试形态，不是普通业务对象例外。

`IServiceProvider` 禁止进入：

- Desktop 内部普通工厂对象。
- 普通 ViewModel。
- Core。
- Application。
- Transfer。
- Providers。
- Infrastructure 的业务实现对象。
- Row ViewModel。
- Navigation parameter。
- Dialog result。

把 `IServiceProvider` 塞进普通对象，本质上就是 Service Locator。那不是灵活，那是把依赖关系藏起来。

Desktop 内部工厂如果需要创建对象，必须优先接收显式工厂委托或具体工厂依赖，不直接持有 `IServiceProvider`。

## 4. 生命周期映射

Desktop 内部对象推荐生命周期如下：

| 对象 | 推荐方式 | 说明 |
| --- | --- | --- |
| `MainWindow` | DI 创建 | 主窗口由组合根创建。 |
| `MainWindowViewModel` | Singleton | 跟随主窗口和应用进程存在，承载 Shell 状态。 |
| `StatusBarViewModel` | Singleton | 跟随主窗口和应用进程存在，展示应用摘要状态。 |
| `INavigationService` | Singleton | 管理当前页面 ViewModel。 |
| `IViewFactory` | Singleton | 只保存显式 View 映射，不保存业务状态。 |
| `IDialogService` | Singleton | 统一弹窗入口，可持有主窗口访问器。 |
| `IMessageService` | Singleton | 统一消息提示入口。 |
| `IUiDispatcher` | Singleton | 封装 Avalonia UI 线程调度。 |
| `IPageViewModelFactory` | Singleton | 通过显式工厂委托创建导航目标对应的页面 ViewModel。 |
| 页面 ViewModel | Transient 或显式工厂创建 | 由 Navigation 创建或复用，不能全局乱注册为 Singleton。 |
| Dialog ViewModel | Transient 或显式工厂创建 | 跟随弹窗生命周期。 |
| Row ViewModel | 手动创建 | 行对象是展示快照，不进 DI。 |
| Navigation parameter | 手动创建 | 参数对象不进 DI。 |
| Dialog result | 手动创建 | 结果对象不进 DI。 |
| Avalonia View | ViewFactory 创建 | 默认不注册进 DI。 |

`Scoped` 不是 Desktop 第一版默认生命周期。桌面应用没有天然 HTTP request scope。除非未来设计出明确的“工作区 scope”或“传输批次 scope”，否则不要引入 Scoped。

## 5. ViewModel 创建

页面 ViewModel 可以通过 DI 创建，但创建入口必须收敛。

推荐方式：

```text
NavigationService
  -> IPageViewModelFactory
    -> 显式 Func<TViewModel> / 具体页面工厂
      -> 页面 ViewModel
```

禁止方式：

```text
ViewModel
  -> IServiceProvider.GetRequiredService(...)
```

`IPageViewModelFactory` 不直接持有 `IServiceProvider`。组合根负责把显式工厂委托传入 `IPageViewModelFactory`。

页面 ViewModel 的依赖必须通过构造函数显式声明，例如：

```text
RemoteBrowserViewModel
  -> IRemoteBrowserAppService
  -> IMessageService
  -> IDialogService
```

ViewModel 不能依赖 Provider、SDK client、Transfer worker、Infrastructure repository 具体实现。

## 6. ViewFactory 与 DI

ViewFactory 只负责 ViewModel 到 View 的映射。

ViewFactory 可以由 DI 创建为 Singleton，但 ViewFactory 不负责从 DI 中解析页面 ViewModel。

允许：

```text
ViewFactory.CreateView(existingViewModel)
  -> new RemoteBrowserView { DataContext = existingViewModel }
```

禁止：

```text
ViewFactory.CreateView(NavigationTarget.RemoteBrowser)
  -> serviceProvider.GetRequiredService<RemoteBrowserViewModel>()
  -> new RemoteBrowserView(...)
```

原因很简单：ViewFactory 如果开始解析 ViewModel，它就吞掉了 Navigation 的职责。边界一旦糊掉，后续所有页面切换问题都会变成“到底谁创建了谁”的烂账。

## 7. DialogService 与 DI

DialogService 可以通过显式工厂委托创建 Dialog ViewModel。

推荐流程：

```text
ViewModel
  -> IDialogService.ShowAccountDialogAsync(request)
    -> 显式 Func<AccountDialogViewModel> / 具体弹窗工厂
    -> AccountDialogViewModel
    -> AccountDialogView
```

Dialog ViewModel 是短生命周期对象，弹窗关闭后释放。

DialogService 不执行业务保存。它只负责收集用户输入并返回结果。真正的新增、编辑、删除等业务操作必须回到 Application 用例服务。

## 8. 模块注册

各模块可以提供扩展方法供 Desktop 组合根调用：

```text
services.AddAtomBoxApplication()
services.AddAtomBoxTransfer()
services.AddAtomBoxProviders()
services.AddAtomBoxInfrastructure()
services.AddAtomBoxDesktop()
```

这些扩展方法只负责注册本模块服务，不能创建主窗口，不能启动 UI，不能连接远程 provider。

Desktop 组合根负责调用这些注册方法并最终创建 `ServiceProvider`。

Core 第一版不提供 `AddAtomBoxCore()`。Core 暂时只定义类型、端口、模型、值对象和领域规则，不与 DI 发生直接关系。如果未来 Core 出现确实需要容器管理的纯 Core 无状态服务，再补充 Core 注册入口。

## 9. 校验规则

实现阶段应开启 DI 构建校验：

```text
ValidateOnBuild = true
ValidateScopes = true
```

即使第一版不使用 Scoped，也应该保留校验，防止未来错误生命周期依赖悄悄进入。

启动失败时，Desktop 可以展示最小启动错误界面，但不能创建正常 Main Shell。

## 10. 禁止事项

- 不把所有对象都注册进 DI。
- 不把 Row ViewModel、参数对象、结果对象注册进 DI。
- 不把 `IServiceProvider` 注入普通对象、普通工厂或 ViewModel。
- 不在 ViewFactory 中解析页面 ViewModel。
- 不在 DialogService 中执行业务保存。
- 不把 Provider、SDK client、secret material、Transfer worker 注册为全局单例。
- 不把 Scoped 当成默认桌面生命周期。
- 不让 Core、Application、Transfer、Providers、Infrastructure 依赖 Desktop DI 细节。
