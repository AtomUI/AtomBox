# Desktop 多语言支持

> 状态：Implemented
>
> 目标版本：v1.0.0
>
> 创建时间：2026-08-03
>
> 影响模块：Desktop

## City 迁移后的当前实现

本页主体保留 v1.0.0 最初建立本地化功能时的设计背景。`feature/with-city` 完成后，当前实现以 [with-city.md](with-city.md) 为准：RESX 仍是 AtomBox 的翻译源文件，但由 City Build 在编译期转换为 embedded locpack；City Localization 是 culture、fallback、package 与 revision 的唯一运行时权威。旧的 AtomBox `ILocalizationService`/`LocalizationService`（ResourceManager 实现）已经删除。

Desktop 只保留两个适配合同：`IDesktopLanguageService` 负责 `system`/`zh-CN`/`en-US` 偏好与 City culture switch，`IAtomBoxLocalizedText` 把 City 的异步 localized-text handles 投影成 Avalonia XAML 和现有 ViewModel 可同步读取的原子快照。若下文旧接口名或加载流程与本节冲突，以本节及 City 迁移审计为准。

MVVM 第二阶段后不再存在 `LocalizedViewModelBase`。需要即时语言刷新的 ViewModel 统一继承 City `ViewModelBase`，显式订阅 `IAtomBoxLocalizedText.LanguageChanged`，并把退订 lease 登记到当前 ActivationScope；不需要刷新的对象不承担本地化继承耦合。

## 背景

AtomBox 当前界面文案主要直接写在 Desktop 的 XAML、ViewModel、导航树和 DialogService 中，默认使用简体中文。AtomUI 已具备 `LanguageVariant`、语言 Provider 和运行时切换能力，但 AtomBox 自有文案尚未建立统一资源目录、当前语言状态和动态刷新机制。

v1.0.0 需要建立可长期扩展的本地化基础设施，而不是通过页面级 `if/else` 判断语言。该体系必须支持简体中文和英文、首次启动跟随系统、用户偏好持久化，以及无需重启的运行时切换。

## 目标

- 使用标准 .NET `resx` 管理 AtomBox 自有文案和英文回退。
- 以单一 Localization Service 维护当前语言并发布语言变化。
- XAML、ViewModel、代码生成导航、Message 和弹窗统一从资源键获取文案。
- 语言切换后当前窗口立即刷新，并同步切换 AtomUI 内置控件。
- 将业务语义与已翻译字符串分离，确保动态状态能够重新本地化。
- 保存 `system`、`zh-CN` 或 `en-US`，并在创建主窗口前恢复语言。
- 为将来增加其他 BCP 47 语言保留稳定扩展点。

## 非目标

- 不在本版本提供机器翻译、在线语言包或用户自定义翻译。
- 不在业务模块中传递 `ResourceManager`、资源键或当前 UI Culture。
- 不翻译用户数据、远程资源名称、协议字段、技术日志和供应商原始响应。
- 不因为 UI Culture 改变 Provider 签名、Endpoint、序列化、持久化和协议日期数字格式。
- 不要求切换语言后追溯修改已经消失的 Toast；切换后产生的新消息必须使用新语言。

## 核心原则

1. `ILocalizationService` 是当前语言的唯一权威来源，页面不得自行保存或判断当前语言。
2. `resx` 是 AtomBox 翻译文本的唯一权威来源，代码中不得维护中英文分支。
3. ViewModel 保存状态、错误码和格式化参数，不把已翻译文本作为长期业务状态。
4. `LanguageChanged` 是运行时刷新文案的统一通知，AtomUI 通过适配器同步切换。
5. Core 和 Application 描述“发生了什么”，Desktop 决定“如何向用户表达”。

## 模块边界

### Desktop

Desktop 承担全部本地化职责：

- 定义支持语言、语言偏好和系统语言映射。
- 加载 `resx`、格式化文案、执行资源回退并发布语言变化。
- 提供 XAML 动态本地化扩展和 C# 本地化接口。
- 同步设置 .NET CultureInfo 与 AtomUI `LanguageVariant`。
- 本地化导航、页面、弹窗、Message、状态栏和用户可见错误摘要。
- 在 `desktop-preferences.json` 中保存语言偏好。

建议目录：

```text
src/AtomBox.Desktop/Localization/
├── ILocalizationService.cs
├── LocalizationService.cs
├── SupportedLanguage.cs
├── LanguagePreference.cs
├── LocExtension.cs
├── AtomUILanguageAdapter.cs
└── Resources/
    ├── CommonStrings.resx
    ├── CommonStrings.zh-CN.resx
    ├── ShellStrings.resx
    ├── ShellStrings.zh-CN.resx
    ├── AccountStrings.resx
    ├── AccountStrings.zh-CN.resx
    ├── RemoteBrowserStrings.resx
    ├── RemoteBrowserStrings.zh-CN.resx
    ├── TransferStrings.resx
    ├── TransferStrings.zh-CN.resx
    ├── SettingsStrings.resx
    └── SettingsStrings.zh-CN.resx
```

### Core 与 Application

- 不引用 Desktop、本地化服务或资源文件。
- 继续返回稳定的状态枚举、错误码、错误类别、数量、路径和上下文参数。
- Desktop 根据结构化语义生成用户摘要；已有原始 `StorageError.Message` 作为技术详情保留。
- v1.0.0 若现有错误码不足以区分用户场景，Desktop 使用操作级通用摘要；后续确需增强时只能补充语言中立的结构化语义，不能在底层增加中文或英文分支。

### Providers、Transfer 与 Infrastructure

- 保持当前协议和任务实现，不感知 UI 语言。
- Provider 原始错误、协议字段和签名继续使用稳定技术格式。
- Infrastructure 可继续提供路径和 JSON 存储能力，但不决定支持语言或执行界面切换。
- 语言偏好属于 Desktop 专用配置，第一版继续由 `DesktopPreferencesService` 持久化。

## 资源体系

### 资源文件

- 无语言后缀的 `.resx` 使用英文，作为最终回退资源。
- `.zh-CN.resx` 保存简体中文。
- 资源按 Shell、Account、RemoteBrowser、Transfer、Settings 和 Common 等界面边界拆分，避免单一巨型资源文件。
- 使用稳定语义键，例如 `Transfer.Status.Downloading`，禁止使用中文原文作为键。
- 相同语义只定义一次；通用“保存”“取消”“删除”等进入 Common 资源。

### 回退规则

资源读取顺序为：

```text
目标语言精确资源 -> 中性语言资源 -> 英文基础资源 -> 可诊断的资源键占位
```

发布构建必须保证英文基础资源完整。缺失资源不得静默返回空字符串，应在调试日志中记录资源键，并以资源键占位暴露问题。

### 格式化与单复数

- 参数化文案使用具名语义资源和格式化参数，不通过字符串拼接组句。
- 中英文资源中的占位符数量和含义必须一致。
- 英文数量文案使用明确的单数/复数资源键或统一 plural helper；第一版不因仅支持两种语言引入在线服务。
- 用户可见日期、数字和大小使用当前 CultureInfo；网络协议、签名、持久化和日志继续使用 `InvariantCulture`。

## 核心接口

本地化服务提供以下能力，具体命名可遵循 Desktop 现有代码风格：

```csharp
public interface ILocalizationService
{
    CultureInfo CurrentCulture { get; }

    long Revision { get; }

    IReadOnlyList<SupportedLanguage> SupportedLanguages { get; }

    string GetString(string catalog, string key);

    string Format(string catalog, string key, params object[] arguments);

    Task SetLanguageAsync(string languageTag, CancellationToken cancellationToken = default);

    event EventHandler<LanguageChangedEventArgs>? LanguageChanged;
}
```

约束：

- `SetLanguageAsync` 只接受 `system`、`zh-CN` 和 `en-US`。
- 所有切换过程在 Avalonia UI 线程协调。
- 服务注册为 Desktop 单例，所有页面共享同一个当前语言。
- 具体实现同时实现 `INotifyPropertyChanged`；`Revision` 只作为翻译缓存失效信号，不表示具体语言。
- ViewModel 和 DialogService 通过构造函数注入接口，不直接访问静态资源类。
- XAML 的 `LocExtension` 创建可观察绑定，而不是返回一次性的静态字符串。

### Catalog 注册

多个 `.resx` 通过单一 Catalog Registry 注册：

```text
Common        -> CommonStrings.ResourceManager
Shell         -> ShellStrings.ResourceManager
Account       -> AccountStrings.ResourceManager
RemoteBrowser -> RemoteBrowserStrings.ResourceManager
Transfer      -> TransferStrings.ResourceManager
Settings      -> SettingsStrings.ResourceManager
```

`ILocalizationService.GetString(catalog, key)` 是唯一资源读取入口。页面不得直接访问具体 ResourceManager；未知 Catalog 和缺失 Key 按统一回退及诊断规则处理。

## 语言偏好

`DesktopPreferences` 增加稳定语言标识，持久化值为：

```text
system
zh-CN
en-US
```

不得保存 ComboBox 索引。旧版配置缺少语言字段、字段为空或字段不受支持时归一化为 `system`。

`system` 的第一版映射规则：

```text
zh-CN / zh-SG / zh-Hans-* -> zh-CN
其他系统语言             -> en-US
```

用户选择固定语言后，后续系统语言变化不覆盖用户偏好。

`system` 运行规则：

- AtomBox 仅在应用启动、用户重新选择“跟随系统”或重置设置时读取并映射操作系统语言。
- 应用运行期间不订阅 Windows、macOS 或 Linux 的系统语言变化事件。
- 如果操作系统语言在 AtomBox 运行期间发生变化，当前界面保持不变；下次启动或用户重新选择“跟随系统”后生效。
- 用户选择 `zh-CN` 或 `en-US` 后，系统语言变化始终不得覆盖固定偏好。

## 启动流程

必须在创建主窗口前应用语言，避免界面闪烁：

```text
读取 DesktopPreferences
        ↓
解析 system / zh-CN / en-US
        ↓
设置 DefaultThreadCurrentCulture 与 DefaultThreadCurrentUICulture
        ↓
使用同一 Culture 初始化 AtomUI
        ↓
注册 LocalizationService
        ↓
创建 MainWindow 和页面
```

启动读取失败时使用 `system`，并沿用现有偏好文件容错策略。AtomUI `WithDefaultCultureInfo` 必须接收已经解析后的目标 Culture，而不是尚未应用偏好的 `CurrentUICulture`。

### 启动引导器

- `DesktopLanguageBootstrapper` 在 `App.Initialize` 中同步读取小型本地 JSON，只解析 `language` 字段。
- 引导器与 `DesktopPreferencesService` 共用 `LanguagePreference.Normalize`，缺失、空值或非法值统一回退 `system`。
- 在加载 `App.axaml` 前设置当前线程与默认线程的 Culture，并使用同一个 Culture 初始化 AtomUI。
- 随后创建 `LocalizationService` 单例并放入 `LocalizationHost`；Composition Root 将相同实例注册到 DI。
- 启动失败窗口使用已初始化的 `LocalizationHost` 和最小 Shell 资源，不依赖尚未成功建立的完整 DI。

## 运行时切换流程

用户在设置中的语言下拉框完成选择后立即生效。语言偏好独立保存，不依赖设置页底部的“保存”按钮：

```text
验证目标语言
    ↓
原子保存语言偏好
    ↓
在 UI 线程设置 CurrentCulture / CurrentUICulture
    ↓
AtomUILanguageAdapter 切换 LanguageVariant
    ↓
LocalizationService 更新 CurrentCulture
    ↓
发布 LanguageChanged
    ↓
XAML、ViewModel、导航和当前页面刷新文案
```

要求：

- 切换不重新启动应用，不重建业务服务，不清空页面数据和用户输入。
- 语言下拉框的选择变化立即调用 `SetLanguageAsync`；不把语言变化并入其他设置的 Dirty 状态。
- 语言保存与其他应用设置保存相互独立；其他未保存设置不得因语言切换被提交或丢失。
- 保存失败时不切换当前语言，将下拉框恢复为原选项，并通过原语言显示错误。
- 同一语言的重复选择不重复发布事件。
- 快速连续切换必须串行执行，以最后一次成功保存的选择为准。
- AtomBox 与 AtomUI 的切换作为同一 UI 操作执行，避免长期出现混合语言。

## UI 更新策略

### XAML

使用动态本地化扩展：

```xml
<atom:Button Content="{localization:Loc Common.Save}" />
```

`LocExtension` 不直接返回字符串，而是返回绑定到 `LocalizationService.Revision` 的 Avalonia Binding。Binding Converter 固定持有当前控件的 Catalog 和 Key；Revision 变化时，仅重新读取该控件对应的资源：

```text
Common.Save Binding
        ↓ 订阅
LocalizationService.Revision
        ↓ Revision 变化
GetString("Common", "Save")
        ↓
更新当前 AvaloniaProperty
```

实现约束：

- 语言切换成功后，先更新 CurrentCulture 和 AtomUI LanguageVariant，再执行 `Revision++` 并触发 `PropertyChanged(nameof(Revision))`。
- Revision 变化不是遍历并预加载全部 `.resx`；每个可见 Binding 只查询自己的 Catalog 和 Key。
- ResourceManager 负责缓存已加载资源集，正常运行期间不轮询、不重复读取磁盘。
- 不允许每个控件直接强订阅 `LanguageChanged`，避免 DataTemplate 和动态控件产生事件泄漏；Avalonia 负责 Binding 生命周期。
- `LocExtension` 通过应用级 Localization Host 获取 DI 中同一个 Localization Service 单例；Host 必须在加载使用 `LocExtension` 的 XAML 前完成初始化。
- `LocExtension` 用于 AvaloniaProperty、DataTemplate 和受支持的 Style Setter；非 Avalonia 属性或代码创建对象使用本地化服务并在 LanguageChanged 后重建或刷新。

### ViewModel

动态状态使用“资源键 + 参数”描述，例如：

```text
Key: Transfer.DownloadTasksCreated
Arguments: [3]
```

页面标题、空状态和仍然可见的状态消息在 `LanguageChanged` 后重新计算。瞬时 Toast 不追溯修改；切换后新建的 Toast 使用新语言。ViewModel 释放时必须解除事件订阅。

Revision Binding 只负责 XAML 属性。包含业务参数的 ViewModel 文案继续保存资源键和参数，并在统一 `LanguageChanged` 通知后触发对应属性的 `OnPropertyChanged`；不得把 Revision 当作业务状态存入 ViewModel。

Desktop 内部动态文案模型固定为：

```csharp
internal sealed record LocalizedText(
    string Catalog,
    string Key,
    params object[] Arguments);
```

长期显示的标题、状态和进度保存 `LocalizedText`，Getter 按当前语言解析。Message 等瞬时内容在显示时翻译。需要即时刷新的页面显式订阅投影的语言变化，并把退订 lease 注册到 City ActivationScope；RouteOutlet 停用页面时由 Scope 统一解除订阅。

### 导航和代码生成控件

主窗口左侧导航由 C# 创建，语言切换时重建其显示节点或建立动态绑定。重建必须保留：

- 当前选中菜单键。
- “远程存储”“传输”“设置”等展开状态。
- 已加载账号节点。
- 当前页面和导航参数。

菜单键和导航目标保持语言中立，不能使用显示文字作为业务标识。

语言切换时使用稳定 `ItemKey` 原地重建导航显示节点。切换前记录当前菜单键和展开节点，重建后恢复选中、展开和传输队列数字；当前页面实例、导航参数、目录、搜索条件和业务数据均不重建。

### 弹窗与错误

- DialogService 的标题、正文和按钮文本全部使用资源键。
- 语言切换发生在设置页，模态弹窗阻止并发设置操作，因此无需追溯重建已经关闭的弹窗。
- 用户可见摘要根据操作场景、错误码和错误类别在 Desktop 中本地化。
- Provider 原始消息、异常和技术上下文保留在错误详情中，避免翻译掩盖诊断信息。

错误本地化边界：

- 页面状态、Message、确认弹窗和错误弹窗正文不得直接把 `StorageError.Message` 作为主要用户提示。
- Desktop 优先使用具体操作资源，例如“远程文件列表加载失败”；能够可靠区分时，再根据 `StorageErrorCode` 或 `StorageErrorCategory` 提供“未找到”“无权限”“连接失败”等本地化原因。
- 现有错误码不足以安全区分原因时，显示当前操作的本地化通用失败摘要，不根据英文原文做字符串解析或翻译。
- 错误详情保留本地化摘要、错误码、错误类别、是否可重试、操作上下文和底层原始消息；字段标签随 UI 语言切换，原始消息内容保持原样。
- Desktop 自身产生的输入校验、空状态和操作约束必须完整本地化。
- Provider、Transfer、Infrastructure 和 Application 的技术日志、异常文本及供应商响应不承担 UI 翻译职责。
- v1.0.0 不为本地化目的修改底层错误契约；无法结构化映射的情况使用 Desktop 通用摘要兜底。

## 设置页交互

应用设置增加“界面语言”配置项，使用 AtomUI `ComboBox`：

- 跟随系统
- 简体中文
- English

语言名称使用各自原生写法，避免用户在错误语言环境中无法识别。完成语言选择后立即独立保存并应用；重置设置将语言恢复为 `system` 并立即按系统语言切换。

交互约束：

- 用户完成语言选项选择后立即禁用语言下拉框并执行保存与切换。
- 切换成功后恢复下拉框；整个过程不要求用户点击页面底部“保存”。
- 切换失败时恢复原选项和原语言，不影响页面中其他尚未保存的配置。
- 页面底部“重置”成功后，将语言偏好恢复为 `system` 并立即应用系统映射结果。

## 迁移范围

实现时必须扫描以下用户可见入口：

- MainWindow 标题、左侧导航、托盘和启动错误窗口。
- Home、RemoteBrowser、TransferQueue、TransferHistory、Settings 和 AccountManagement。
- 所有 XAML 标题、按钮、表头、Placeholder、Tooltip、空状态和说明。
- ViewModel 状态消息、进度文案和格式化数量。
- DialogService、AccountDialogWorkflow、MessageService 调用点和错误摘要。
- 文件选择器标题、过滤器名称和系统通知文案。

文件名、路径、Bucket、账号名称、Provider 显示名及 AtomBox 品牌名保持原值。

## 测试与验收

### 自动化测试

- `system`、`zh-CN`、`en-US`、空值和未知语言标识的归一化。
- 语言偏好的保存、读取、旧 JSON 兼容和重置。
- 所有英文基础资源与中文资源键集合一致。
- 参数化资源的占位符一致且格式化成功。
- 缺失资源按英文和资源键规则回退，并产生诊断记录。
- 所有错误入口以本地化摘要作为主提示，底层原始消息只出现在错误详情或日志中。
- 已知错误码映射到正确的中英文原因；未知错误使用操作级通用摘要，不解析原始消息。
- 同语言重复设置不发布事件；切换失败不改变当前语言。
- LanguageChanged 后动态绑定和本地化状态描述重新求值。
- Revision 每次成功切换只增加一次；失败或重复选择同一有效语言时不增加。
- 多个 XAML Binding 在 Revision 变化后只查询各自资源键，不建立控件级强事件订阅。
- 系统 Culture 映射和 AtomUI LanguageVariant 映射正确。
- `system` 仅在启动、重新选择和重置时重新解析；运行期间系统语言变化不触发切换。
- Desktop 面向用户的硬编码文案扫描具有明确豁免列表。

### Desktop 手工验收

- 在中文和英文系统环境首次启动，第一帧语言正确且无闪烁。
- 设置页切换中文、英文和跟随系统后当前窗口立即完整刷新。
- 当前页面、表格数据、目录路径、选择状态和未保存输入不丢失。
- 导航文字更新且选中项、展开状态和账号节点保持。
- AtomUI 内置控件与 AtomBox 文案语言一致。
- TextBlock、AtomUI Button、DataTemplate 和 Style Setter 中的 `LocExtension` 文案均能往返切换；代码创建的导航节点按既定策略刷新。
- 逐页检查导航、远程文件浏览、账号弹窗、设置、传输队列、历史记录、图片/文本预览和错误详情。
- 在连接失败、无权限、资源不存在和未知错误场景检查本地化摘要、错误详情字段及原始技术消息。
- 检查英文长文案在常用桌面和最小窗口尺寸下无重叠、关键按钮无截断。
- 重启后恢复固定语言；重置后恢复跟随系统。
- 上传、下载、搜索、预览和 Provider 连接行为不因 Culture 改变。

### Desktop 自动化测试工程

- 新增 `tests/AtomBox.Desktop.Tests`，沿用 xUnit。
- 自动覆盖语言标识归一化、系统语言映射、旧 JSON 兼容、偏好持久化及中英文资源键对称性。
- v1.0.0 不引入依赖真实屏幕环境的重型 UI 自动化；窗口布局由双语手工 smoke test 验收。

## 术语表

| 中文 | 英文 |
|---|---|
| 远程存储 | Remote Storage |
| 对象存储 | Object Storage |
| 传输 | Transfers |
| 传输队列 | Transfer Queue |
| 历史记录 | Transfer History |
| 账号管理 | Account Management |
| 应用设置 | App Settings |
| 指纹索引 | Fingerprint Index |
| 文件浏览器 | File Browser |
| 连接测试 | Test Connection |
| Bucket / 桶 | Bucket |

`OSS`、`FTP`、`SFTP`、`WebDAV`、`BOS` 等缩写保持原样。英文按钮使用简洁动词，标题采用 Title Case，说明与状态消息采用 sentence case；文件名、账号名、路径、Bucket 名称和 Provider 品牌保持原值。

发布前执行完整 `dotnet build` 和 `dotnet test`，并在 Windows、macOS、Linux 至少各完成一次中英文启动和切换 smoke test。

## 文档同步

功能完成后同步：

- `docs/modules/presentation/overview.md`
- `docs/modules/presentation/atomui-integration.md`
- `docs/ui/` 下涉及设置、导航、远程浏览和传输页面的当前有效文档
- `docs/implementation/testing/` 下的 Desktop 多语言测试矩阵
- `docs/iterations/releases/v1.0.0.md`
