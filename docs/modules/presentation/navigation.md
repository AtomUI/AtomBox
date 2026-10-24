# AtomBox Desktop Navigation 设计

> 文档状态：当前实现合同
>
> 最近更新：2026-10-24，City `7f273ea`

本文定义 AtomBox Desktop 的应用导航适配边界。路由真相、提交回执、条件恢复和页面 Outlet 生命周期由 City 提供；AtomBox 只定义业务导航目标、参数编解码、页面计划以及菜单选中投影。

## 1. 职责与依赖方向

当前主链路为：

```text
菜单 City Command
  -> CommandExecutionDomain（latest-wins）
  -> AtomBox INavigationService
  -> City IRoutePresentationCoordinator
      -> City IRouter commit
      -> AtomBox IRoutePresentationPlanFactory adapter
          -> City ViewModelLease / ViewLocator / ViewFactory / ViewBinder
      -> City RouteOutlet commit
      -> 失败时按 NavigationCommitReceipt 条件恢复 Router
  -> 成功后发布 CurrentMenuKey
```

`NavigationService` 是 Desktop 应用级 adapter。它可以引用 Desktop 参数类型、City Router 和 Presentation 服务，但不能调用 Provider、持有 SDK client、访问 Infrastructure 持久化实现或拥有传输 worker。

## 2. 稳定导航目标

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

`NavigationTarget` 只属于 Desktop，不进入 Core、Application、Transfer、Providers 或 Infrastructure。

六个目标由 `AtomBoxRouteCatalog` 映射到 City route graph。当前唯一页面 Outlet 为 `primary`。

## 3. 参数合同

AtomBox 参数在 Router 提交前编码为字符串字典；页面计划必须从 Router 已提交的最终参数快照重新解码，不能继续使用调用方的可变对象。

- `RemoteBrowserNavigationParameter`：资源分组、可选账号 ID、可选远端路径；
- `TransferHistoryNavigationParameter`：可选页码；
- `AccountManagementNavigationParameter`：可选选中账号 ID。

参数可以包含 Core 稳定值对象的序列化表示，但不得包含 secret material、Provider、SDK client、repository 或 View 实例。

页面计划在 ViewModel 首次激活前应用最终参数。因此 `RemoteBrowserViewModel` 的第一次 Activation 加载能够直接观察最终账号和路径，不需要先加载默认页面再修正。

## 4. 原子提交与失败恢复

一次导航必须通过 `IRoutePresentationCoordinator.NavigateAsync` 完成，AtomBox 不再直接先调用 Router、再手工调用 Outlet。

协调器负责：

1. 为同一 Outlet 串行化技术提交；
2. 执行 Router 导航并取得 `NavigationCommitReceipt`；
3. 请求 AtomBox 创建单次使用的 `RouteOutletCommitPlan`；
4. 把计划提交给 RouteOutlet；
5. 当计划创建、View 绑定、ViewModel 激活或 Outlet 提交失败时，按回执尝试恢复提交前 Router 快照；
6. 若已有更新的 Router 提交，拒绝用旧回执覆盖新状态，并返回结构化结果。

只有 `RoutePresentationResultStatus.Succeeded` 才能更新 `CurrentMenuKey`。失败结果必须保留 City 的状态、消息和异常链；调用取消必须继续表现为 `OperationCanceledException`，不能伪装成普通导航失败。

## 5. 页面候选所有权

AtomBox 计划工厂负责取得 ViewModel lease、定位并创建 View、绑定 DataContext，然后生成 `RouteOutletCommitPlan`。

- 计划返回前发生异常：AtomBox 释放尚未转交的 BoundViewHandle 和 ViewModel lease；
- 计划返回并提交 Outlet：候选所有权转交 City；
- Outlet 成功：City 持有新 entry，并在提交后停用和释放旧 entry；
- Outlet 失败或取消：City 回滚可见内容并释放被拒绝候选。

应用代码不得在计划提交后再次释放候选，也不得在 ViewModel 中保存 View 引用。

## 6. 菜单并发与取消

`MainWindowViewModel.NavigateByMenuKeyCommand` 使用由 DI/Host 所有的 `CommandExecutionDomain`，并采用 `CommandConcurrencyPolicy.Allow`。这样，同一命令的新执行原子取得新 generation，并请求取消此前执行。

latest-wins 只提供执行所有权、取消请求和 currentness，不会强杀不观察 token 的第三方任务。AtomBox 必须把 City 给出的标准 `CancellationToken` 原样传到 Router、计划创建、ViewModel Activation 和下游业务调用。

Domain 是 Host 单例异步可释放对象。Host 关闭时停止接收新执行、取消活动执行并等待 drain；ViewModel 不自行维护额外的 CTS、锁或版本号。

## 7. 菜单 key 投影

菜单 key 是 AtomBox NavMenu 的展示状态，不是第二套路由真相。导航与页面呈现都成功后，`NavigationService` 才根据目标和最终参数映射：

- `Home` -> 首页；
- RemoteBrowser 分组 -> 远程存储、OSS、FTP/SFTP 或 WebDAV；
- RemoteBrowser 指定账号 -> 对应账号节点；
- TransferQueue、TransferHistory、Settings、AccountManagement -> 对应功能节点。

上传完成后跳转传输队列等页面内导航同样必须经过 `INavigationService`，以同步左侧菜单。

## 8. 验证要求

- City Host 必须能解析 `IRoutePresentationCoordinator` 和共享 `CommandExecutionDomain`；
- 导航参数编码/解码必须往返一致；
- 最终参数必须在目标 ViewModel 首次 Activation 前应用；
- 快速菜单点击必须取消旧 generation，最终只提交最新目标；
- Router 成功而页面计划失败时，必须由协调器恢复或返回明确的恢复拒绝状态；
- 构建、Desktop 自动化测试与最终人工 UI 菜单烟测全部通过后，升级才可合并。
