# AtomBox Desktop ViewFactory 设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的 Desktop 第一版 ViewFactory、ViewModel 到 View 显式映射和 View 创建规则
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的 View 创建边界，必须先说明原因，再同步更新相关 Presentation 文档。

本文档定义 Desktop 如何根据 ViewModel 创建 Avalonia View。第一版使用显式 ViewFactory，不使用运行时程序集扫描。

## 1. 定位

ViewFactory 是 ViewModel 到 View 的装配器。

它回答的问题是：

- 给定一个 ViewModel，应该创建哪个 Avalonia `Control`。
- View 的 `DataContext` 应该设置为什么。
- 没有注册映射时如何失败。

它不回答的问题是：

- 为什么要进入这个页面。
- 这个页面的导航参数是什么。
- 页面业务数据如何加载。
- 弹窗应该什么时候打开。

## 2. 基本接口

第一版推荐接口：

```csharp
public interface IViewFactory
{
    Control CreateView(object viewModel);
}
```

`CreateView` 接收已经创建好的 ViewModel，返回可显示的 Avalonia `Control`。

## 3. 显式映射

ViewFactory 必须使用显式注册：

```text
RemoteBrowserViewModel -> RemoteBrowserView
TransferQueueViewModel -> TransferQueueView
TransferHistoryViewModel -> TransferHistoryView
SettingsViewModel -> SettingsView
AccountManagementViewModel -> AccountManagementView
```

禁止通过程序集扫描、命名约定反射或动态加载来发现 View。

AOT / trimming 目标下，显式映射不是啰嗦，是必要的工程纪律。靠反射扫描偷懒，后面发布裁剪时会把自己坑死。

## 4. 创建规则

ViewFactory 创建 View 时必须遵守：

- 每次创建 View 时设置 `DataContext`。
- ViewFactory 不创建 ViewModel。
- ViewFactory 不缓存 ViewModel。
- ViewFactory 默认不缓存 View。
- ViewFactory 不调用 Application 用例服务。
- ViewFactory 不处理导航参数。

View 缓存不是第一版默认能力。后续如果某些页面需要保留复杂 UI 状态，必须单独讨论缓存策略，不能把 ViewFactory 偷偷改成页面缓存池。

## 5. 与 ContentControl 的关系

右侧内容区推荐绑定当前 ViewModel：

```text
MainWindowViewModel.CurrentViewModel
  -> ContentControl.Content
  -> ViewFactory 创建 View
```

具体实现可以采用 Avalonia DataTemplate 或自定义 ContentControl 适配器，但必须保持显式映射原则。

如果使用 Avalonia DataTemplate，DataTemplate 内部也必须是显式 ViewModel -> View 映射，不允许运行时扫描。

## 6. 错误处理

没有注册映射时，ViewFactory 应该明确失败。

推荐行为：

- 开发阶段抛出清晰异常。
- 生产阶段显示最小错误 View，并通过 `IMessageService` 或日志记录错误。

错误信息必须包含 ViewModel 类型名，不能只报空白页面。

## 7. 禁止事项

- 不使用运行时程序集扫描。
- 不使用命名约定反射创建 View。
- 不在 ViewFactory 中创建或解析导航目标。
- 不在 ViewFactory 中调用 Application、Provider、Transfer 或 Infrastructure。
- 不把 ViewFactory 做成导航服务。
- 不缓存 secret material、provider、SDK client 或业务运行对象。
