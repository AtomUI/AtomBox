# AtomBox.Application 结果与契约设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的 Application 第一版请求对象、结果对象和错误返回边界。
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的请求对象、结果对象或错误返回边界，必须先说明原因，再同步更新相关 Application、Core、Presentation 文档。

本文档定义 Application 用例的输入输出形态。Application 契约服务于用户流程，不服务于 UI 控件，也不服务于具体 SDK。

## 1. 基本原则

- 请求对象表达用户意图。
- 结果对象表达一次用例调用的结果快照。
- 错误信息使用 Core 的统一错误模型。
- Application DTO 不能包含 Avalonia、AtomUI、SDK、数据库、配置文件实现类型。
- Application DTO 可以引用 Core 稳定模型和值对象。

## 2. 结果模型

第一版只使用 Core 中定义的统一结果形态：

```text
OperationResult
OperationResult<T>
```

建议语义：

| 字段 | 含义 |
| --- | --- |
| `IsSuccess` | 操作是否成功完成。 |
| `Value` | 成功结果快照。 |
| `Error` | 失败时的 Core 统一错误对象。 |

Application 第一版不定义 `ApplicationResult<T>`。用例级分页、警告、能力提示、上下文信息必须放入 `T` 对应的结果对象中，例如 `ListRemoteItemsResult`、`TransferHistoryPage`、`RemoteEntryResult`。

Desktop / Presentation 也不定义 `DesktopResult<T>`。Presentation 负责把 `OperationResult<T>` 转换为 UI state、message 或 dialog。

## 3. 错误边界

Application 可以组织错误结果，但不能转换具体 SDK/API/协议异常。

正确流程：

```text
Provider SDK Exception
  -> Provider 转换为 Core StorageError
    -> Application 返回 OperationResult<T>
      -> Presentation 展示摘要或详情
```

禁止流程：

```text
SDK Exception
  -> Application 判断异常类型
  -> ViewModel 展示 SDK 异常
```

## 4. 请求对象规则

请求对象必须只包含：

- Core ID，例如 `StorageAccountId`。
- Core 值对象，例如 `RemotePath`。
- 用户选项，例如覆盖策略、是否递归、分页方向。
- 必要的普通 .NET BCL 类型。

请求对象禁止包含：

- ViewModel。
- Avalonia / AtomUI 控件类型。
- Provider 实例。
- SDK client。
- SDK DTO。
- `IServiceProvider`。
- secret material。
- Infrastructure repository 实现。

## 5. 结果对象规则

结果对象可以包含：

- Core 模型，例如 `RemoteItem`、`TransferTask`、`StorageAccountId`。
- Application 视角的快照，例如 `StorageAccountSummary`、`TransferQueueSnapshot`。
- 页面所需但不属于 UI 控件的状态，例如是否可上传、是否空状态。

结果对象禁止包含：

- View。
- ViewModel。
- AtomUI / Avalonia 类型。
- Provider 实例。
- SDK DTO。
- 原始 SDK Exception。
- secret material。

## 6. DTO 命名

第一版命名约定：

| 类型 | 命名 |
| --- | --- |
| 用例输入 | `XxxRequest` |
| 用例结果 | `XxxResult` |
| 列表快照 | `XxxSnapshot` |
| 分页结果 | `XxxPage` |
| 摘要对象 | `XxxSummary` |

命名必须表达用户流程，不要写成 provider 方法名的包装。例如 `ListRemoteItemsRequest` 可以，`CallAliyunListObjectsRequest` 不可以。

## 7. 禁止事项

- 不把 Application DTO 当 UI DTO。
- 不把 Application DTO 当 SDK DTO。
- 不定义 `ApplicationResult<T>`。
- 不定义 `DesktopResult<T>`。
- 不在 Application 结果对象中携带弹窗指令。
- 不在 Application 结果对象中携带 secret material。
- 不让 Application 捕获并判断具体 SDK 异常类型。
