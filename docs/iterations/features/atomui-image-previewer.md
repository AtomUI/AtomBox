# AtomUI ImagePreviewer 图片预览

> 状态：Done
>
> 目标版本：v0.1.5
>
> 创建时间：2026-07-14
>
> 影响模块：Core / Application / Desktop

## 背景

AtomBox 当前使用 Avalonia `Image + Bitmap + ScrollViewer` 展示远程图片，只提供基础适应窗口显示。AtomUI.Desktop.Controls 6.0.8 的 `ImagePreviewer` 已支持 `StreamImagePreviewSource`，可以在预览窗口打开后按需获取远程图片 Stream，并提供加载、失败、缩放、旋转、翻转、拖动和适应窗口能力。

## 目标

- 远程图片使用 AtomUI `ImagePreviewer` 预览。
- 用户触发预览后立即打开 ImagePreviewer，由控件加载状态承接远程读取过程。
- 图片数据通过 `StreamImagePreviewSource` 按需请求 Application，不先写入磁盘。
- 保留现有图片格式范围和默认 10 MB 大小限制。
- 关闭预览时取消未完成读取并释放图片 Stream。
- 文本预览保持现有交互和实现。

## 非目标

- 不修改 Providers、Transfer 或 Infrastructure。
- 不新增 Provider 预览专用接口，继续复用 `DownloadAsync(RemotePath, Stream, ...)`。
- 不创建 Transfer 任务，不写入传输队列或历史。
- 不增加图片缓存、临时文件或持久化配置。
- 不实现 Range 请求、渐进式图片解码、多图切换或远程缩略图。
- 不调整支持的图片格式和大小上限。

## 用户体验

- 右键“预览”或双击支持的图片文件后，打开 ImagePreviewer 原生预览窗口。
- 标题栏只显示文件名，不再显示原图片弹窗中的格式和大小信息行。
- 加载期间显示 ImagePreviewer 原生加载状态；失败时显示原生失败状态。
- 用户可以缩放、旋转、翻转、拖动图片并切换适应窗口状态。
- 关闭加载中的预览会取消远程读取。
- 文本文件仍使用现有 AtomDialog 和只读 TextArea。

## 设计方案

### 模块边界

- Core 定义图片预览元数据和远程预览 Stream 结果，不引用 Avalonia 或 AtomUI。
- Application 负责预检、创建短生命周期 Provider、把远程内容写入受限内存流并返回可读 Stream。
- Desktop 负责创建 `StreamImagePreviewSource`、打开 ImagePreviewer、桥接取消和清理生命周期。
- Providers 保持现有 `DownloadAsync` 契约和各供应商实现不变。
- Transfer 与 Infrastructure 不参与。

### Core 契约

- `PreviewRemoteFileResult` 不再携带图片 `byte[] Content`。图片结果只返回类型、文件名、Content-Type 和列表大小；文本结果继续返回 `Text` 与 `EncodingName`。
- 新增 `RemotePreviewStreamResult`，包含 `Stream Content` 和实际读取字节数 `Size`。
- `RemotePreviewStreamResult.Content` 返回后由调用方负责释放；每次打开必须返回新的、可读且位置为 `0` 的 Stream。
- `RemotePreviewOptions.DefaultMaxImageBytes` 继续为 10 MB。

### Application 用例

- `PreviewRemoteFileAsync` 继续作为预览预检入口。图片只做账号、文件类型、扩展名和列表大小校验，不创建 Provider、不下载内容；文本继续完成读取和解码。
- 新增 `OpenRemoteImagePreviewStreamAsync`。该方法重新校验请求，只接受受支持的图片文件，然后创建 Provider 并调用现有 `DownloadAsync`。
- 下载目标为受 10 MB 上限保护的内存流。实际读取超限时返回不支持预览错误；成功后把 Position 重置为 `0` 再返回。
- Provider 在下载完成后释放，不延长到 ImagePreviewer 窗口生命周期。
- 取消令牌从 ImagePreviewer 一直传递到 Provider。

### Desktop 交互与生命周期

- 主窗口视觉树中放置一个尺寸为零、不可交互的专用 `ImagePreviewer` 宿主，确保控件可以解析所属 `TopLevel`。
- 图片预览时，`DialogService` 为宿主设置 `StreamImagePreviewSource`、文件名标题和模态窗口配置，然后打开 ImagePreviewer 自带窗口。
- Stream 工厂调用 Application 的 `OpenRemoteImagePreviewStreamAsync`；Application 失败转换为图片源加载异常，由 ImagePreviewer 进入失败状态。
- `DialogService.ShowPreviewAsync` 等待 `DialogClosed`，随后解除事件、关闭残留状态并清空 Source，避免保留远程路径、回调和图片资源。
- `StreamImagePreviewSource` 可能被控件重新请求，因此工厂每次调用都重新打开独立 Stream。

## 测试与验收

Application 自动化测试：

- 图片预检成功时不创建 Provider、不下载内容。
- 图片流读取成功时返回新的可读 Stream，Position 为 `0`，内容和实际大小正确。
- 不支持格式、文件夹、列表大小超限不会创建 Provider。
- Provider 下载失败、实际读取超限和取消正确向上返回。
- 文本预览、编码识别和大小限制保持原有行为。

Desktop 手工验收：

- 支持的图片通过右键和双击均可打开 ImagePreviewer。
- 窗口打开后可见加载状态，加载成功后图片正确显示。
- 缩放、旋转、翻转、拖动和适应窗口可用。
- 标题栏显示文件名。
- 关闭加载中的窗口不会继续占用预览状态。
- 连续预览不同图片不会显示上一次结果。
- 文本预览没有回归。

发布前执行完整 `dotnet build` 和 `dotnet test`。真实 Provider smoke test为非阻塞项，因为本次不修改 Provider 契约或实现。

## 当前实现验收记录

- `dotnet build AtomBox.slnx --no-restore`：通过，0 警告、0 错误。
- `dotnet test AtomBox.slnx --no-restore --no-build`：通过，共 622 项测试，0 失败。
- Providers、Transfer、Infrastructure 没有因本功能产生代码改动。
- Desktop 真实账号图片加载、工具栏交互和加载中关闭仍需发布前手工 smoke test。

## 文档同步

- `docs/modules/core/models-and-values.md`
- `docs/modules/application/use-cases.md`
- `docs/ui/remote-browser.md`
- `docs/iterations/releases/v0.1.5.md`
