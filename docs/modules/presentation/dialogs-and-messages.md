# AtomBox Desktop 弹窗与消息设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-15
>
> 冻结范围：当前文档定义的 Desktop 第一版弹窗、确认框、错误详情、消息服务边界和 Phase 9 收口规则
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的弹窗或消息边界，必须先说明原因，再同步更新相关 Presentation 文档。

本文档定义 Desktop 中弹窗、确认框、错误详情和轻量消息的统一入口。

## 1. 定位

弹窗和消息属于 Presentation 责任。

Application 可以返回业务结果和错误结果，但 Application 不知道弹窗、通知、消息条、Toast 或 AtomUI 控件存在。

## 2. 服务划分

第一版划分两个服务：

| 服务 | 职责 |
| --- | --- |
| `IDialogService` | 处理需要用户明确输入或确认的阻塞式交互。 |
| `IMessageService` | 处理轻量成功、失败、警告、信息提示。 |

两者不能混成一个万能 `IUiService`。万能服务一开始省事，后面会变成所有页面都依赖的垃圾桶。

## 3. IDialogService

`IDialogService` 负责：

- 新增账号。
- 编辑账号。
- 删除确认。
- 重置确认。
- 错误详情。
- 连接测试结果详情。

推荐接口形态：

```csharp
public interface IDialogService
{
    Task<AccountDialogResult?> ShowAccountDialogAsync(AccountDialogRequest request);

    Task<bool> ConfirmAsync(ConfirmDialogRequest request);

    Task ShowErrorDetailsAsync(ErrorDialogRequest request);
}
```

接口参数是 Desktop UI 请求对象，不是 Core 业务对象本身。请求对象可以引用 Core 稳定 ID 或 Application 返回的错误快照，但不能携带 SDK exception 或 secret material。

## 4. IMessageService

`IMessageService` 负责：

- 保存成功。
- 删除成功。
- 上传任务已创建。
- 加载失败摘要。
- 连接测试失败摘要。

推荐接口形态：

```csharp
public interface IMessageService
{
    void Success(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message);
}
```

消息服务可以内部使用 AtomUI `Message` 或 `Notifications`。页面和 ViewModel 不得直接散落调用 AtomUI 消息 API。

## 5. AtomUI 使用

第一版弹窗和消息使用以下 AtomUI 能力：

| 场景 | AtomUI 组件 |
| --- | --- |
| 账号新增 / 编辑 | `Dialog` + `Form` |
| 删除确认 | `MessageBox` 或 `Dialog` |
| 错误详情 | `Dialog` |
| 轻量消息 | `Message` / `Notifications` |

弹窗壳子必须统一，不能每个页面手写一套弹窗布局。

## 6. 与 ViewModel 的关系

ViewModel 可以调用 `IDialogService` 和 `IMessageService`，但调用后必须仍然通过 Application 用例服务执行业务。

例如账号新增流程：

```text
AccountManagementViewModel
  -> IDialogService.ShowAccountDialogAsync()
  -> IAccountAppService.CreateAccountAsync()
  -> IMessageService.Success()
```

错误流程：

```text
Application Result
  -> ViewModel 判断失败
  -> IMessageService.Error(摘要)
  -> 用户需要详情时 IDialogService.ShowErrorDetailsAsync()
```

ViewModel 不解析 SDK 原始异常，不展示 secret material，不把错误详情写进日志外的 UI 状态长期持有。

## 7. 生命周期

`DialogService` 和 `MessageService` 是 Desktop 应用级服务。

它们可以持有主窗口引用或顶层窗口访问器，但不能持有：

- Provider。
- SDK client。
- Transfer worker。
- Infrastructure repository 实现。
- Secret material。
- `IServiceProvider`。

弹窗 ViewModel 是弹窗生命周期对象，弹窗关闭后释放。

`DialogService` 不直接持有或调用 `IServiceProvider`。弹窗 ViewModel 通过组合根传入的显式工厂委托或具体弹窗工厂创建。

## 8. 禁止事项

- 页面不直接手写弹窗壳子。
- 页面不直接散落调用 AtomUI 静态消息 API。
- Application 不返回“弹窗指令”。
- Core 不知道确认框、消息提示或 Dialog。
- DialogService 不直接持有或调用 `IServiceProvider`。
- 错误详情不展示 SDK 原始异常类型给用户。
- 弹窗输入状态不保存 secret material 明文超过必要交互窗口。

## 9. Phase 9 收口规则

Phase 9 必须落地以下阻塞式交互：

| 场景 | 入口 | 要求 |
| --- | --- | --- |
| 删除远程对象 | 远程浏览页右键或工具栏 | 必须确认对象名称、位置和不可撤销语义；确认后才调用 Application 删除用例。 |
| 清理历史 | 传输历史页工具栏 | 必须确认会清空当前历史视图或全部历史的范围。 |
| 重置设置 | 设置页 | 必须确认重置范围。 |
| 错误详情 | 加载、上传下载创建、删除、连接测试、启动失败 | 必须展示摘要、错误类别、可读原因、可选诊断信息和脱敏技术细节。 |
| 连接测试结果详情 | 账号弹窗 | 成功展示测试目标和能力摘要；失败展示错误类别和下一步提示。 |

启动失败界面属于 Presentation，但不是普通业务 Shell 的一部分。它只能提供：

- 错误摘要。
- 脱敏详情。
- 配置 / 状态 / 日志位置。
- 可选恢复入口。
- 退出应用入口。

错误详情展示必须遵守：

- 不展示 secret material。
- 不展示完整 SDK exception dump。
- 不展示可直接认证远端系统的 URL、token 或 header。
- 可以展示 provider id、错误类别、HTTP status、协议状态码、request id 或本地诊断文件路径。
