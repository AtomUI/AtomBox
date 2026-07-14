# 远程文件预览

> 状态：Done
>
> 目标版本：v0.1.3
>
> 创建时间：2026-07-05
>
> 影响模块：Core / Application / Desktop

> 后续演进：本文记录 v0.1.3 首次实现。图片预览的数据契约和渲染方式已由 v0.1.5 的 `atomui-image-previewer.md` 替代；文本预览规则继续有效。

## 背景

远程浏览页当前可以列出文件、进入目录、下载文件，但用户查看图片或小文本文件时必须先创建下载任务。对配置文件、日志、JSON、Markdown、截图等轻量文件来说，这个流程偏重。

本功能提供轻量预览能力，让用户在远程文件列表中直接查看图片和小文本内容。预览不进入传输队列，也不写入用户选择的本地下载目录。

这里的“不需要下载”是产品语义：不创建下载任务、不要求用户选择本地保存路径、不写入传输历史。技术实现仍需要从远程 provider 读取内容，并写入 Application 创建的内存流。

## 目标

- 支持远程图片文件直接预览。
- 支持小体积简单文本文件直接预览。
- 预览入口来自远程浏览页文件行，不对 bucket 或文件夹开放。
- 预览读取通过 Application 用例进入系统，Desktop 不直接调用 Provider。
- Provider 第一版不新增内存专用接口，复用现有 `DownloadAsync(RemotePath, Stream, ...)` 写入 `MemoryStream`。
- 预览失败或不支持时，给出清晰提示，不创建下载任务。

## 非目标

- 不支持 Office 文档内嵌预览，包括 `.doc`、`.docx`、`.xls`、`.xlsx`、`.ppt`、`.pptx`。
- 不支持 PDF、音频、视频预览。
- 不支持系统默认应用打开。
- 不支持远程文件真实缩略图生成。
- 不支持 Range 读取、流式预览或预览缓存。
- 不修改 Transfer 队列、传输历史和下载任务语义。
- 不修改 Providers 契约。
- 不在 Provider 中新增 `DownloadToMemoryAsync`、`DownloadForPreviewAsync` 等场景化方法。

## 支持范围

图片格式第一版支持：

- `.png`
- `.jpg`
- `.jpeg`
- `.bmp`
- `.gif`
- `.webp`

文本格式第一版支持：

- `.txt`
- `.log`
- `.md`
- `.markdown`
- `.json`
- `.xml`
- `.yaml`
- `.yml`
- `.csv`
- `.ini`
- `.conf`
- `.config`
- `.html`
- `.css`
- `.js`
- `.ts`
- `.cs`
- `.py`
- `.java`
- `.go`
- `.rs`
- `.cpp`
- `.c`
- `.h`
- `.sql`
- `.sh`
- `.ps1`

默认大小限制：

- 文本最大 `1 MB`。
- 图片最大 `10 MB`。

编码支持：

- UTF-8
- UTF-8 BOM
- UTF-16 LE
- UTF-16 BE

无法识别编码、疑似二进制内容、文件大小超过限制或扩展名不支持时，返回不支持预览。

## 用户体验

- 文件行右键菜单新增“预览”。
- 双击文件第一版可以继续保持无默认打开行为；是否把双击文件改为预览，后续单独决定。
- 图片预览使用弹窗展示图片、文件名、大小和基础状态。
- 文本预览使用弹窗展示只读文本、文件名、大小、编码和基础状态。
- 加载期间显示远程读取状态，避免用户误以为界面卡住。
- 预览失败时复用统一错误详情弹窗或消息服务。
- 超过大小限制时提示用户下载后查看。

## 设计方案

### 模块边界

第一版修改范围锁定在 Core、Application 和 Desktop。

- Core 定义预览相关模型和跨层语义，不引用 UI 类型。
- Application 实现预览用例，负责校验、创建 provider、读取内存流、解码文本和返回结果。
- Desktop 负责预览入口、弹窗、图片渲染、文本展示、加载状态和错误提示。
- Providers 不新增接口。现有 `IStorageProvider.DownloadAsync(RemotePath, Stream, ...)` 已经表达“把远程内容写入调用方提供的 stream”，调用方传入 `FileStream` 时是普通下载，传入 `MemoryStream` 时就是预览读取。
- Transfer 不参与。预览不是下载任务，不进入队列，不进入历史。

### Core 模型

Core 新增预览相关模型，模型不能引用 Avalonia、AtomUI 或 Desktop 类型：

- `RemotePreviewKind`：`Text`、`Image`。
- `RemotePreviewOptions`：`MaxTextBytes`、`MaxImageBytes`。
- `PreviewRemoteFileRequest`：`StorageAccountId`、`RemotePath`、`FileName`、`Size`。
- `PreviewRemoteFileResult`：`Kind`、`FileName`、`ContentType`、`Size`、`Content`、`Text`、`EncodingName`。

字段说明：

| 字段 | 所属对象 | 说明 |
|---|---|---|
| `StorageAccountId` | `PreviewRemoteFileRequest` | 指定从哪个远程账号读取。 |
| `RemotePath` | `PreviewRemoteFileRequest` | 指定远程文件路径。 |
| `FileName` | `PreviewRemoteFileRequest` | 用于扩展名判断、标题展示和 content type 推断。 |
| `Size` | `PreviewRemoteFileRequest` | 来自列表项的远程文件大小，用于读取前大小预检；允许为空。 |
| `Kind` | `PreviewRemoteFileResult` | 表示结果是文本还是图片。 |
| `ContentType` | `PreviewRemoteFileResult` | 根据扩展名推断的 MIME 类型，例如 `image/png` 或 `application/json`。 |
| `Size` | `PreviewRemoteFileResult` | 实际读取到的字节数。 |
| `Content` | `PreviewRemoteFileResult` | 原始字节；图片预览必须使用，文本预览也保留原始内容。 |
| `Text` | `PreviewRemoteFileResult` | 文本预览解码后的字符串；图片预览为空。 |
| `EncodingName` | `PreviewRemoteFileResult` | 文本编码名称，例如 `utf-8`、`utf-16le`；图片预览为空。 |

不支持预览、文件超限、编码不支持、疑似二进制内容等情况通过 `OperationResult<PreviewRemoteFileResult>.Failure(...)` 返回，不在 `PreviewRemoteFileResult` 中表达失败状态。

### Application 用例

Application 在 `RemoteBrowserAppService` 或同一 browsing 用例边界中新增预览方法：

- 校验账号 id、路径、文件名和文件类型。
- 只允许 `RemoteItemKind.File` 对应的文件进入预览。
- 根据扩展名判断预览类型和 content type。
- 根据列表返回的 `Size` 做预检；超限直接返回 Validation 或 NotSupported 错误。
- 创建短生命周期 provider。
- 创建受限 `MemoryStream`，调用现有 `provider.DownloadAsync(...)`。
- 读取完成后再次校验实际字节数，避免列表 size 缺失或不准确导致大文件进入内存。
- 文本结果解码为字符串；图片结果保留原始字节。

### Desktop 交互

Desktop 在远程浏览页新增预览入口：

- ViewModel 增加 `PreviewSelectedCommand`。
- 文件行菜单传入当前行 path、name、size 和 kind。
- 调用 Application 预览用例。
- 文本预览弹窗展示只读文本。
- 图片预览弹窗把 `byte[]` 转成 Avalonia 可显示图片对象。
- bucket、文件夹、未知类型、超限和不支持格式不显示或禁用预览入口。

## 测试与验收

本功能的最低测试线与 `../releases/v0.1.3.md` 保持一致。Core / Application 单元测试是阻塞项；Desktop 至少完成手工 smoke test；真实云 provider opt-in 测试不是 v0.1.3 阻塞项。

Core / Application 测试：

- 支持扩展名识别为 Text 或 Image。
- 不支持扩展名返回不支持预览。
- bucket 和文件夹不能预览。
- 文本超过 `MaxTextBytes` 时不读取 provider。
- 图片超过 `MaxImageBytes` 时不读取 provider。
- provider 下载成功后文本内容正确解码。
- provider 下载成功后图片字节正确返回。
- provider 下载失败时错误向上返回。
- 实际读取字节超过限制时返回失败。

Desktop 手工验收：

- 右键图片文件可以打开图片预览弹窗。
- 右键小文本文件可以打开文本预览弹窗。
- bucket 和文件夹不出现可用预览动作。
- 不支持格式提示清晰，不创建下载任务。
- 超大文件提示下载后查看。
- 预览操作不进入传输队列和传输历史。

## 文档同步

进入实现前和实现完成后需要同步确认：

- `docs/modules/core/models-and-values.md`
- `docs/modules/application/use-cases.md`
- `docs/ui/remote-browser.md`
- 必要时更新 `docs/implementation/testing/phase-11-provider-test-matrix.md` 或新增 Application 预览测试说明。
