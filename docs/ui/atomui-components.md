# AtomBox Desktop AtomUI 组件选型

> 文档状态：第一版发布基线冻结
>
> 冻结时间：2026-06-22
>
> 冻结范围：AtomBox Desktop 第一版 UI 使用的 AtomUI 组件选型、替代策略和禁止清单
>
> 变更规则：第一版发布前代码已冻结；如需调整本文档定义的组件选型，必须先说明原因，再同步更新相关 UI 文档。

本文档记录 AtomBox Desktop 第一版 UI 组件选型。本文档只定义组件使用边界，不定义具体颜色、字体、间距、圆角或页面布局。

## 1. 组件选型原则

- 优先使用 AtomUI Desktop 控件。
- 不为 AtomUI 基础控件再包一层 AtomBox 控件。
- 只封装跨页面基础设施：导航、弹窗、消息、主题。
- 页面级控件选择必须和本文档保持一致。
- AtomUI 没有合适轻量控件时，允许直接使用 Avalonia 原生控件简单实现。

## 2. 第一版组件选型表

| AtomBox 场景 | 首选组件 | 允许替代 | 用途 | 备注 |
| --- | --- | --- | --- | --- |
| 主窗口 | `Window` | 不允许替代为 Avalonia 原生 `Window` | Shell 主窗口 | 使用 AtomUI Window。 |
| 左侧菜单 | `NavMenu` / `NavMenuNode` | 不允许用 `TreeView` 替代 | 功能菜单、连接实例入口 | 左侧菜单是功能导航，不是远程目录树。 |
| 远程存储汇总 | `Statistic` + `Card` + `Tag` | 原生轻量布局 | 全部账号统计和分组入口 | 当前汇总页已采用该组合。 |
| 远程文件列表 | Avalonia 原生轻量行列表 | AtomUI `ListView` / `ListBox` | 文件、文件夹、bucket 行列表 | 禁止使用 `DataGrid`。 |
| 传输队列 | Avalonia 原生轻量行列表 | AtomUI `ListView` / `ListBox` | 当前传输任务列表 | 禁止使用 `DataGrid`。 |
| 传输历史 | Avalonia 原生轻量行列表 | AtomUI `ListView` / `ListBox` | 历史记录列表 | 禁止使用 `DataGrid`。 |
| 账号管理 | Avalonia 原生轻量行列表 | AtomUI `ListView` / `ListBox` | 账号列表 | 禁止使用 `DataGrid`。 |
| 账号弹窗 | `Dialog` / `Modal` + `Form` | 不允许手写弹窗壳子 | 新增/编辑账号 | 统一弹窗。 |
| 确认弹窗 | `Modal` | `Dialog` | 删除、重置、清理确认 | 统一二次确认。 |
| 错误详情 | `Dialog` / `Modal` | 不允许页面内展开 SDK 详情 | 展示错误详情 | 详情必须脱敏。 |
| 空状态 | `Empty` | Avalonia 原生轻量布局 | 无账号、空目录、无任务 | 不要每个页面随手自定义一套空状态。 |
| 加载状态 | `Spin` | Avalonia 原生轻量布局 | 页面或局部加载中 | 网络请求期间使用遮罩。 |
| 普通提示 | `Message` | 不允许页面直接散落实现 | 轻量成功、失败、警告提示 | 必须通过 Presentation 服务封装。 |
| 弹窗内测试反馈 | `MessageCard` | 固定高度轻量提示区 | 连接测试成功/失败 | 避免消息出现时挤压弹窗主体。 |
| 表单文本输入 | `LineEdit` | Avalonia `TextBox` | 文本、密码、路径输入 | 优先 AtomUI 输入控件。 |
| 下拉选择 | `ComboBox` | Avalonia `ComboBox` | Provider、类型、设置项等 | 优先 AtomUI ComboBox。 |
| 数字输入 | `NumberUpDown` | 原生数字输入 | 最大并发数 | 必须保持整数。 |
| 简单分页 | `Button` / AtomUI 分页控件 | 两个原生按钮 | 上一页、下一页 | OSS 列表和历史记录使用。 |
| 路径导航 | `Breadcrumb` | 原生文本路径 | 远程路径导航 | 支持点击返回路径节点。 |
| 浮动操作 | `FloatButton` | AtomUI Button | 文件列表、历史、账号页右下角操作 | 统一右下角页面操作入口。 |
| 标签和状态 | `Tag` / `Badge` | Avalonia `TextBlock` | provider 类型、状态摘要 | 状态展示不要做复杂卡片。 |
| 工具按钮 | `Button` | Avalonia `Button` | 保存、重置、打开、编辑、删除 | 优先 AtomUI Button 系列。 |
| 长文本提示 | `Tooltip` | 原生 ToolTip | 历史记录长路径 | 仅在文本确实过长时启用。 |

## 3. DataGrid 禁止规则

第一版明确禁止在以下页面使用 AtomUI `DataGrid` 或 Avalonia 原生 `DataGrid`：

- 远程文件列表。
- 传输队列。
- 传输历史。
- 账号管理。

原因：

- 第一版列表都是轻量行列表，不需要完整表格控件能力。
- `DataGrid` 会引入列编辑、排序、复杂选择、自动列、数据访问器等额外复杂度。
- 当前 UI 文档已经限制了多选、复杂筛选、复杂分页和行内复杂操作，使用 `DataGrid` 是过度设计。

## 4. 禁止清单

- 不用 Avalonia 原生 `Window` 做主窗口。
- 不用 AtomUI `DataGrid` 或 Avalonia 原生 `DataGrid` 做第一版业务列表。
- 不用 `TreeView` 做左侧功能菜单。
- 不手写弹窗壳子。
- 不在页面里直接散落通知、消息、确认弹窗实现。
- 不在页面里硬编码大批样式。
