# AtomUI 视觉组件迁移

> 状态：Implemented
>
> 目标版本：v0.1.6
>
> 创建时间：2026-07-21
>
> 影响模块：Desktop

## 背景

AtomBox Desktop 已广泛使用 AtomUI，但部分页面仍使用原生 Avalonia 控件或通过 `Border + Grid + TextBlock + Button` 手工组合常见视觉组件。这些实现能够工作，但会增加样式、主题、焦点状态和交互行为的一致性成本。

AtomUI.Desktop.Controls 6.0.8 已提供搜索、按钮、空状态、数字徽标、确认弹窗、描述列表、进度、标签、警告和分隔线等组件。v0.1.6 对现有 UI 做一次有边界的组件收敛。

## 目标

- 优先使用 AtomUI 已提供且语义匹配的视觉组件。
- 减少手工视觉组合、重复样式和不必要的 code-behind。
- 统一 Light/Dark 主题、焦点、悬停、禁用和加载状态。
- 保持现有业务命令、数据绑定和页面导航行为不变。
- 为后续页面迭代建立清晰的 AtomUI 组件选型基线。

## 非目标

- 不改变 Desktop 之外的模块契约或业务行为。
- 不以组件替换为由重写 ViewModel 或应用服务的理由。
- 不为了“统一”而替换仍然合适的 Avalonia 布局原语。
- 不把不匹配 AtomBox 语义的 AtomUI 控件强行用于文件传输场景。
- 不在缺少总数时使用页码型 `Pagination`。
- 不在文件列表中使用持续滚动的 `MarqueeLabel`。
- 不改造必须独立于 AtomUI 启动的 `StartupErrorWindow`。
- 不默认进行 DataGrid 迁移。

## 迁移清单

### 第一优先级：低风险直接替换

| 场景 | 当前实现 | 目标组件 | 主要位置 |
|---|---|---|---|
| 远程文件搜索 | `LineEdit + Border + Button` 手工组合 | `SearchEdit` | RemoteBrowser |
| 页面操作按钮 | 原生 Avalonia `Button` | AtomUI `Button` | RemoteBrowser / TransferQueue / TransferHistory / AccountManagement |
| 空数据提示 | 居中 `TextBlock` | `Empty` | TransferQueue / TransferHistory / Home / RemoteBrowser |
| 传输队列数量 | `Border + TextBlock` 手工徽标 | `CountBadge` | MainWindow 左侧导航 |

按钮迁移只处理页面视觉按钮。已有明确命令图标的操作应使用 AtomUI AntDesign 图标，并保留 Tooltip 或可访问名称。纯文本命令在图标不能准确表达含义时仍可保留文字。

### 第二优先级：信息与反馈组件

| 场景 | 当前实现 | 目标组件 | 约束 |
|---|---|---|---|
| 通用确认弹窗 | 自定义 `ConfirmDialogContent` | `MessageBox` Confirm | 必须保持确认/取消结果和所有者窗口语义 |
| 错误详情 | 手工 `Grid` 字段布局 | `Descriptions` / `DescriptionItem` | 长文本必须可换行、可复制 |
| 远程浏览错误 | 普通错误文案和按钮组合 | `Alert` | 保留重试等操作入口 |
| 类型与结果状态 | 普通 `TextBlock` | `Tag` | 仅用于短标签，不替代长状态说明 |
| 设置项分隔 | 手工边框线 | `Separator` | 不改变设置页滚动和行布局 |

### 第三优先级：传输进度

传输队列可使用 AtomUI `ProgressBar` 展示具有真实百分比的数据，并继续展示等待、运行、取消、失败和完成等文字状态。若当前行模型只提供格式化字符串，应先在 Desktop 行模型中暴露由现有传输数据计算出的数值属性；不得通过解析显示文本获取进度，也不得为未知进度伪造百分比。

## DataGrid 独立评估

远程文件列表、传输队列、传输历史和账号列表目前使用 `Border + Grid + ScrollViewer + ItemsControl` 组成表格。AtomUI DataGrid 可能减少列布局和选择交互代码，但它来自独立 NuGet 包 `AtomUI.Desktop.Controls.DataGrid`，迁移范围明显大于普通视觉控件替换。

纳入 v0.1.6 前必须先验证：

- 固定列、弹性列和窄窗口下的列宽行为。
- 单选、多选、双击、右键菜单和键盘导航。
- 文件列表行图标、Tooltip、命令按钮和空状态组合。
- 大列表虚拟化、滚动和刷新性能。
- 传输进度单元格和动态状态更新。
- 新增 NuGet 依赖对 Windows、macOS、Linux 发布产物的影响。

若任一关键行为无法低风险保持，DataGrid 迁移延后到独立版本，不阻塞 v0.1.6 其余组件收敛。

## 模块边界

- Desktop XAML 负责替换控件、样式和视觉状态。
- Desktop ViewModel 只在 AtomUI 组件需要结构化展示数据时增加派生属性，不增加业务规则。
- Core、Application、Providers、Transfer 和 Infrastructure 不因本次视觉迁移修改公共契约。
- 若某个目标组件要求跨层模型变更，该项应暂停并另行设计，而不是在本迭代中扩大范围。

## 交互与兼容要求

- 搜索框保留输入、清空、回车或点击搜索的既有行为。
- 按钮保留命令、参数、禁用条件、加载状态和 Tooltip。
- 空状态只在集合为空且不处于加载状态时显示。
- `CountBadge` 显示真实活动任务数量，数量为 0 时隐藏；父级“传输”菜单自动展开行为不变。
- 确认弹窗关闭、取消和确认必须映射到原有调用结果。
- 错误详情中的路径、错误码和原始信息保持可读，必要时可选择复制。
- 所有组件在 Light/Dark 主题和常见 DPI 缩放下保持可用。

## 测试与验收

自动化检查：

- 现有 ViewModel 和 Desktop 测试保持通过。
- 因迁移新增的派生属性补充针对可见性、数量和进度值的测试。
- 关键命令绑定和确认结果转换有可测试逻辑时补充单元测试。

Desktop 手工 smoke test：

- 远程搜索输入、清空和触发搜索正常。
- 页面按钮的命令、禁用态、焦点和 Tooltip 正常。
- 四类页面空状态在加载前后切换正确。
- 传输任务数量徽标更新、归零隐藏和父菜单展开正常。
- 确认弹窗的确认、取消和窗口关闭结果正确。
- 错误提示、错误详情、状态标签和分隔线在 Light/Dark 主题下清晰。
- 传输进度和状态更新不闪烁、不改变行高、不出现虚假百分比。
- 常见窗口尺寸及 100%、125%、150% DPI 下无文字或控件重叠。

发布前执行完整 `dotnet build` 和 `dotnet test`。本次不修改远程协议和业务链路，因此不要求真实 Provider 集成测试；但关键 Desktop 页面必须进行手工视觉验收。

## 文档同步

- `docs/iterations/releases/v0.1.6.md`
- `docs/ui/remote-browser.md`
- 与传输队列、传输历史、首页、账号管理和设置页对应的现有 UI 文档。
