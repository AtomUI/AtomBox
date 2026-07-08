# AtomBox.Presentation 模块设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的架构、模块边界、依赖关系、目录规划或实现约束
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的设计边界，必须先说明原因，再同步更新相关设计文档。

## 1. 模块定位

`AtomBox.Presentation` 是 AtomBox 的桌面端展示与交互层。

在物理项目上，Presentation 第一阶段归属于 `AtomBox.Desktop`，负责 Avalonia / AtomUI 应用启动、窗口、页面、ViewModel、命令绑定、主题资源、静态资源和桌面交互体验。

Presentation 不是业务编排层，也不是 provider 访问层。把远程存储 SDK、配置读写、凭据处理、传输调度细节塞进 ViewModel，是桌面应用最常见的烂法，前期看起来快，后期一定把 ViewModel 写成上帝对象。

## 2. 边界规则

Presentation 允许包含：

- Avalonia / AtomUI 的 View、Window、UserControl、Style、Resource。
- ViewModel、UI 状态、命令绑定、选择状态、加载状态、错误展示状态。
- UI 专用模型，例如列表行、树节点、按钮状态、菜单项、对话框输入状态。
- 桌面端组合根和 DI 注册入口。
- UI 资源，例如图标、封面、图片、主题资源。

Presentation 禁止包含：

- 具体云厂商 SDK、FTP/SFTP 库、网盘 API 客户端类型。
- Provider 实现细节和 SDK DTO。
- 配置文件读写、数据库访问、凭据加密、文件持久化实现。
- 传输队列 worker、重试策略、并发调度等 Transfer Engine 逻辑。
- 直接 `new` 具体 provider 或直接访问 Infrastructure 实现绕过 Application。
- 把 Avalonia / AtomUI 类型泄漏给 Core、Application、Transfer、Providers 或 Infrastructure。

Presentation 可以决定界面如何展示，但不能决定业务流程如何执行。用户动作必须通过 Application 暴露的用例服务进入系统。

## 3. 推荐目录

```text
src/AtomBox.Desktop/
  AtomBox.Desktop.csproj
  App.axaml
  App.axaml.cs
  Program.cs
  Assets/
  Composition/
  Shell/
  Navigation/
  ViewFactory/
  Dialogs/
  Services/
  Views/
  ViewModels/
  Resources/
```

## 4. 目录职责

| 目录 | 职责 |
| --- | --- |
| `Assets/` | 保存图片、图标、封面等静态 UI 资源。 |
| `Composition/` | 保存桌面端组合根、模块注册、应用启动阶段的 DI 装配。 |
| `Shell/` | 保存主窗口、顶部品牌区、左侧菜单、右侧内容承载区和底部状态栏。 |
| `Navigation/` | 保存 Desktop 内部页面导航、导航目标、导航参数、菜单项 ViewModel 和页面 ViewModel 显式工厂。 |
| `ViewFactory/` | 保存 ViewModel 到 View 的显式映射和 View 创建规则。 |
| `Dialogs/` | 保存统一弹窗服务、弹窗 View 和弹窗 ViewModel。 |
| `Services/` | 保存 Desktop UI 专用服务，例如消息提示和 UI 线程调度。 |
| `Views/` | 保存 Avalonia / AtomUI 页面、窗口、用户控件。 |
| `ViewModels/` | 保存页面状态、命令绑定、UI 交互状态，不承载 provider 或传输调度实现。 |
| `Resources/` | 保存样式、主题、字体、资源字典等 UI 资源定义。 |

## 5. 首批核心对象范围

第一阶段 Presentation 只定义支撑主界面骨架的 UI 对象，不提前把复杂页面全部铺满。

应用启动：

- `App`
- `Program`
- `DesktopCompositionRoot`

主窗口：

- `MainWindow`
- `MainWindowViewModel`
- `NavigationItemViewModel`

远程浏览：

- `RemoteBrowserView`
- `RemoteBrowserViewModel`
- `RemoteItemRowViewModel`

传输队列：

- `TransferQueueView`
- `TransferQueueViewModel`
- `TransferTaskRowViewModel`

账号与设置：

- `AccountManagementView`
- `AccountManagementViewModel`
- `SettingsView`
- `SettingsViewModel`

这些对象属于 UI 展示与交互语言，不是 Core 模型，也不是 Application 用例对象。

## 6. 设计约束

- ViewModel 只调用 Application 用例服务，不能直接调用 Provider、Infrastructure 或具体 SDK。
- ViewModel 不能直接调用 Transfer 运行时对象、Transfer 内部状态发布机制或传输队列；传输状态必须通过 Application 暴露的用例服务进入 UI。
- ViewModel 可以保存 UI 状态，但不能保存敏感凭据明文。
- ViewModel 可以把 Core/Application 结果转换为 UI 展示状态，但不能修改 Core 领域规则。
- Presentation 可以引用所有实现模块，但只允许在组合根中完成依赖注册。
- `Assets` 必须位于 `src/AtomBox.Desktop/Assets/`，根目录不长期保留 UI 资源。
- UI 专用类型不能被 Core、Application、Transfer、Providers、Infrastructure 引用。
- 桌面端错误展示应消费 Application 返回的统一错误结果，不能直接展示 SDK 原始异常。

上传指纹索引 UI 规则：

- Settings 页面只展示“指纹索引”开关；索引文件路径、记录数量、最近更新时间和清空全部索引入口暂不显示。
- Remote Browser 页面可以在底部状态区域展示上传前指纹计算文案。
- 指纹计算期间可以禁用上传入口，避免同一个页面并发启动多个上传准备会话。
- 历史命中确认弹窗由 Presentation 调用统一对话框服务展示。
- ViewModel 不直接读写索引 JSON 文件，不直接访问 `IFileFingerprintIndexStore` 的 Infrastructure 实现。

账号弹窗 UI 规则：

- 添加或编辑 Provider 账号时，必填项、格式和凭据文件读取等校验错误统一通过弹窗顶部 Message 展示，不在表单底部显示单独校验文本。
- 校验错误 Message 不自动消失；用户修改字段、切换 Provider 或重新发起连接测试时清除。
- SFTP 私钥认证不允许用户直接粘贴私钥文本内容。
- 私钥字段使用只读路径框加文件选择按钮，文件选择器优先打开当前用户家目录下的 `.ssh` 目录；如果 `.ssh` 不存在，则退回用户家目录。
- Presentation 只负责选择本机私钥文件和展示校验错误；私钥文件内容仍作为凭据材料进入既有凭据保存流程，不把本机私钥文件路径作为账号配置持久化。

## 7. 生命周期约束

Presentation / Desktop 是唯一组合根，负责应用启动、依赖注册、主窗口创建和关闭协调。

- ViewModel 生命周期跟随页面、窗口或工作区。
- ViewModel 只持有展示状态、筛选条件、选中项、加载状态、错误展示状态等 UI 状态。
- ViewModel 不持有 provider、Transfer worker、SDK client、secret material 或 Infrastructure 资源。
- 远程列表、传输状态、账号列表等 UI 数据应作为快照展示，刷新时整体替换或按明确规则更新。
- 启动失败展示属于 Desktop / Presentation 责任；Infrastructure 不能自己弹 UI，Application 不负责启动错误界面。
- 启动失败时 Desktop 可以创建最小启动错误界面，但不能创建正常 Main Shell；最小错误界面只提供错误展示、恢复入口和退出入口。
- 应用关闭时，Presentation 必须先停止接受新的 UI 操作，再协调 Application、Transfer、Infrastructure 完成关闭流程。

Presentation 可以引用实现模块，只是为了组合根装配。组合根权限不能扩散到 ViewModel。
