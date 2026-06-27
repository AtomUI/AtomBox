# AtomBox Desktop Navigation 设计

> 文档状态：第一版发布基线冻结
>
> 冻结时间：2026-06-22
>
> 冻结范围：当前文档定义的 Desktop 第一版导航模型、导航目标、导航参数、菜单 key 同步和页面 ViewModel 工厂边界
>
> 变更规则：第一版发布前代码已冻结；如需调整本文档定义的导航边界，必须先说明原因，再同步更新相关 Presentation 文档。

本文档定义 Desktop 内部导航边界。Navigation 只管理“当前应该进入哪个页面、带什么参数、当前页面 ViewModel 是谁”，不负责创建 Avalonia View。

## 1. 定位

Navigation 是应用内位置管理，不是 View 创建器。

它回答的问题是：

- 用户现在应该看到哪个页面。
- 页面切换需要哪些参数。
- 当前页面 ViewModel 是哪个。
- 通过哪个显式页面 ViewModel 工厂获取目标 ViewModel。
- 左侧菜单选中状态应同步到哪个菜单 key。

它不回答的问题是：

- 某个 ViewModel 对应哪个 `.axaml`。
- AtomUI 控件如何布局。
- 远程 provider 如何连接。
- 传输任务如何执行。

## 2. 第一版导航目标

第一版使用轻量导航服务：

```csharp
public interface INavigationService
{
    object? CurrentViewModel { get; }

    Task NavigateAsync(NavigationTarget target, object? parameter = null);
}
```

`CurrentViewModel` 由 `MainWindowViewModel` 暴露给右侧 `ContentControl`，View 创建交给 `ViewFactory`。

`NavigationService` 不直接创建页面 ViewModel。目标页面 ViewModel 必须通过 `IPageViewModelFactory` 创建或获取。

推荐关系如下：

```text
NavigationService
  -> IPageViewModelFactory
    -> 显式 Func<TViewModel> / 具体页面工厂
      -> 页面 ViewModel
```

## 3. NavigationTarget

第一版稳定页面目标：

```csharp
public enum NavigationTarget
{
    Home,
    RemoteBrowser,
    TransferQueue,
    TransferHistory,
    Settings,
    AccountManagement
}
```

枚举不是业务模型，只是 Desktop 内部导航目标。它不能进入 Core、Application、Transfer、Providers、Infrastructure。

## 4. 参数模型

导航参数必须是 Desktop 内部 UI 参数对象，不能直接塞 SDK 类型或 Provider 实例。

当前参数：

```text
RemoteBrowserNavigationParameter
  - ResourceGroup
    - All
    - ObjectStorage
    - FileTransfer
  - StorageAccountId?
  - RemotePath?

TransferHistoryNavigationParameter
  - PageIndex?

AccountManagementNavigationParameter
  - SelectedAccountId?
```

参数可以引用 Core 的稳定值对象，例如 `StorageAccountId`、`RemotePath`。参数不能包含 secret material、provider、SDK client、repository 实现。

## 5. 点击行为映射

左侧菜单点击行为进入 `NavigationService`：

| 菜单节点 | NavigationTarget | 参数 |
| --- | --- | --- |
| 首页 | `Home` | 无。 |
| 远程存储 | `RemoteBrowser` | `ResourceGroup = All`。 |
| OSS | `RemoteBrowser` | `ResourceGroup = ObjectStorage`。 |
| FTP/SFTP | `RemoteBrowser` | `ResourceGroup = FileTransfer`。 |
| 具体连接实例 | `RemoteBrowser` | `ResourceGroup`、`StorageAccountId` 和可选路径。 |
| 传输队列 | `TransferQueue` | 无。 |
| 历史记录 | `TransferHistory` | 可选页码。 |
| 应用设置 | `Settings` | 无。 |
| 账号管理 | `AccountManagement` | 可选选中账号。 |

添加账号入口不作为左侧菜单独立节点；它由远程存储页、空状态或账号管理页触发，并调用统一账号弹窗流程。

## 6. 菜单 key 同步

`NavigationService` 在页面切换后解析当前菜单 key：

- `Home` -> 首页。
- `RemoteBrowser + ResourceGroup.All` -> 远程存储。
- `RemoteBrowser + ResourceGroup.ObjectStorage` -> OSS。
- `RemoteBrowser + ResourceGroup.FileTransfer` -> FTP/SFTP。
- `RemoteBrowser + StorageAccountId` -> 对应真实账号节点。
- `TransferQueue`、`TransferHistory`、`Settings`、`AccountManagement` -> 对应功能节点。

右侧页面之间的跳转必须同步左侧菜单选中状态。例如上传或下载创建任务后跳转传输队列，左侧菜单也应选中 `传输队列`。

## 7. Router 规则

第一版不单独引入 Router。

当前第一版没有外部深链、URL 地址、导航历史栈、可序列化路由状态，因此不需要 Router。

如果后续引入 Router，它必须放在 `Navigation/` 下，并位于 `NavigationService` 前面。

## 8. 生命周期和禁止事项

`NavigationService` 是 Desktop 应用级服务。

它可以长期持有当前页面 ViewModel 引用，但不能长期持有：

- View 实例。
- Provider 实例。
- SDK client。
- Transfer worker。
- Secret material。

禁止事项：

- Navigation 不创建 Avalonia View。
- Navigation 不调用 Provider。
- Navigation 不直接访问 Infrastructure 持久化实现。
- Navigation 不直接持有或调用 `IServiceProvider`。
- Navigation 不直接 `new` 页面 ViewModel。
- Navigation 不承担弹窗职责。
- Navigation 不解析 AtomUI 控件事件细节。
