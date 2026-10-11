# AtomBox × AtomUI.City 迁移审计

## 当前切片

第一阶段只迁移 Desktop 组合根和应用生命周期：

- `AtomUI.City` 以 Git 子模块固定版本；
- `ApplicationHost` 成为唯一 Root Provider 所有者；
- `PresentationModule` 和 `AtomBoxDesktopModule` 是显式启动根；
- Infrastructure 与 Transfer 初始化进入 City Module 生命周期；
- Avalonia 继续拥有 UI 线程和 desktop lifetime，City Presentation 管理主窗口 session；
- `ValidateOnBuild` 与 `ValidateScopes` 继续在所有环境启用。

## 依赖边界

```text
AtomBox.Desktop
  -> AtomUI.City.Core / Presentation / Generators
  -> AtomBox.Application / Transfer / Providers / Infrastructure / Core

AtomBox.Application / Transfer / Providers / Infrastructure / Core
  -X-> AtomUI.City
```

业务程序集保持框架无关。City 模块只在 Desktop 适配现有 `AddAtomBox*` 注册入口，不把 City 类型下沉到业务 API。

## 本阶段保留的实现

以下能力继续使用 AtomBox 现有实现，避免在一次改动中同时迁移生命周期和交互语义：

- `INavigationService`、`IPageViewModelFactory`；
- `IViewFactory` 和 View 显式映射；
- `ILocalizationService`、RESX 资源和语言切换；
- AtomBox ViewModel 基类、命令和 Dialog 工作流。

## 后续评估门

下一阶段应分别评估，而不是批量替换：

1. City Routing 是否能完整表达 AtomBox 的动态账号菜单、导航参数和页面释放语义；
2. City Presentation ViewRegistry 是否能替代当前 ViewFactory，且不破坏 AtomUI 控件接入；
3. City Localization 是否支持现有 RESX、即时切换和缺失键策略；
4. City Mvvm 的 activation、command、validation 是否值得替换现有轻量实现；
5. tray hide、显式退出、系统关闭和 startup failure 的窗口 session 回归门禁。

每个后续切片必须保持可运行应用，并保留业务程序集不依赖 City 的约束，除非先完成新的架构评审。
