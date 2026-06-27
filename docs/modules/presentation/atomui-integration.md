# AtomBox Desktop AtomUI 集成设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的 Desktop 第一版 AtomUI 集成方式、依赖边界和禁止事项
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的集成边界，必须先说明原因，再同步更新相关 Presentation 文档和 UI 文档。

本文档定义 `AtomBox.Desktop` 如何接入 AtomUI。组件选型以 `docs/ui/atomui-components.md` 为准，本文档只描述工程集成方式。

## 1. 定位

AtomUI 是 Desktop UI 基础设施，不是跨模块公共依赖。

允许依赖 AtomUI 的位置：

- `AtomBox.Desktop`
- Desktop 的 View
- Desktop 的样式资源
- Desktop 的弹窗、消息、主题封装服务

禁止依赖 AtomUI 的位置：

- `AtomBox.Core`
- `AtomBox.Application`
- `AtomBox.Transfer`
- `AtomBox.Providers`
- `AtomBox.Infrastructure`

## 2. 启动接入

`Program.cs` 使用 AtomUI 默认选项：

```csharp
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .WithAtomUIDefaultOptions()
    .LogToTrace();
```

`App.Initialize` 中接入 AtomUI：

```csharp
this.UseAtomUI(builder =>
{
    builder.WithDefaultCultureInfo(CultureInfo.CurrentUICulture);
    builder.WithDefaultTheme(IThemeManager.DEFAULT_THEME_ID);
    builder.UseAlibabaSansFont();
    builder.UseDesktopControls();
});
```

第一版不启用 `UseDesktopDataGrid()`。业务列表明确禁止使用 DataGrid，启动阶段也不应该主动注册 DataGrid 能力制造误用机会。

## 3. 主窗口

主窗口使用 AtomUI `Window`：

```text
MainWindow.axaml
  -> atom:Window
```

主窗口不使用 Avalonia 原生 `Window` 替代。

## 4. 左侧菜单

左侧功能菜单使用 AtomUI：

```text
NavMenu
  NavMenuNode
```

左侧菜单是功能导航，不是远程目录树。因此不使用 `TreeView` 承载左侧功能菜单。

## 5. 列表实现

以下业务列表第一版禁止使用 AtomUI `DataGrid` 和 Avalonia 原生 `DataGrid`：

- 远程文件列表。
- 传输队列。
- 传输历史。
- 账号管理。

实现方式：

- 优先使用轻量行模板。
- 可使用 Avalonia 原生 `ItemsControl`、`ListBox`、`ListView`。
- 如 AtomUI `ListView` / `ListBox` 足够轻，可以采用 AtomUI 版本。
- 表头、行 hover、选中态、右键菜单通过轻量模板和样式实现。

禁止为了列对齐、排序或选中方便引入 DataGrid。

## 6. 弹窗和消息

AtomUI 弹窗和消息只能通过 Presentation 服务使用：

```text
ViewModel
  -> IDialogService / IMessageService
    -> AtomUI Dialog / MessageBox / Message / Notifications
```

ViewModel 不直接依赖具体 AtomUI 弹窗 API。这样后续替换弹窗实现、统一样式、统一错误记录才有入口。

## 7. 样式资源

AtomUI 样式和 AtomBox 自定义样式放在 `Resources/` 下：

```text
Resources/
  Theme.axaml
  Styles.axaml
```

页面内允许少量局部布局样式，但禁止在页面里硬编码大批颜色、间距、字体。跨页面一致的样式必须收敛到资源文件。

## 8. 禁止事项

- 不在 Core、Application、Transfer、Providers、Infrastructure 引用 AtomUI。
- 不启用 `UseDesktopDataGrid()` 作为第一版默认能力。
- 不用 AtomUI `DataGrid` 或 Avalonia 原生 `DataGrid` 做第一版业务列表。
- 不用 `TreeView` 做左侧功能菜单。
- 不为每个 AtomUI 基础控件再包一层 AtomBox 控件。
- 不把 AtomUI 静态弹窗或消息 API 散落在页面和 ViewModel 中。
