# Infrastructure 日志与缓存设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的日志初始化、日志脱敏、metadata cache、thumbnail cache 和缓存生命周期边界
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的设计边界，必须先说明原因，再同步更新相关 Infrastructure、Presentation、Application 文档。

## 1. 模块定位

Infrastructure 负责日志技术实现和本地缓存技术实现。

日志和缓存都是技术能力，不是业务流程。它们不能决定上传、下载、删除、账号修改、远程刷新等用户行为。

## 2. 日志边界

Infrastructure 可以包含：

- 日志框架适配。
- 日志初始化。
- 日志文件路径选择。
- 日志 rolling 策略。
- 日志 flush / release。
- 日志脱敏辅助。

Infrastructure 禁止：

- 日志中写入 secret material。
- 日志中写入 authorization header、cookie、access token、refresh token。
- 日志中无脑写入 SDK 原始异常全文。
- 日志中保存 provider SDK 原始请求体。
- 通过日志框架控制业务流程。

日志可以记录：

- provider id。
- account id。
- operation id。
- transfer task id。
- 操作类型。
- 脱敏远程路径。
- 错误分类。
- 耗时、大小、速度等非敏感指标。

日志不是垃圾桶。把所有异常 `.ToString()` 扔进去，等于主动制造泄密面。

## 3. LoggingInitializer

`LoggingInitializer` 是启动初始化对象。

它负责：

- 建立日志目录。
- 初始化日志框架。
- 设置日志级别。
- 设置 rolling 策略。
- 注册必要的脱敏规则。
- 应用退出时 flush / release。

它禁止：

- 依赖 ViewModel。
- 弹出 UI。
- 访问 provider。
- 读取 secret material。
- 直接解析 `IServiceProvider`。

启动日志失败可以阻断或降级启动，具体用户可见行为由 Desktop 决定。

## 4. 缓存边界

第一版缓存只包括：

- `MetadataCache`
- `ThumbnailCache`

缓存可以提升体验，但不能成为业务事实来源。

缓存禁止保存：

- secret material。
- provider SDK client。
- provider session。
- Transfer worker。
- ViewModel。
- 未脱敏请求。
- 账号完整配置快照。

缓存可以保存：

- 缩略图文件。
- 已脱敏 metadata。
- 缓存索引。
- 失效时间。
- 缓存归属账号 ID。
- 缓存容量统计。

## 5. 缓存生命周期

`MetadataCache`、`ThumbnailCache` 是应用级缓存服务。

约束：

- 必须有容量上限。
- 必须有过期策略。
- 必须有账号边界，避免不同账号数据混淆。
- 不能无限增长。
- 第一版不启动后台缓存清理线程。
- 缓存清理只能发生在读写路径、容量淘汰、启动维护或关闭维护中。
- 关闭阶段 best-effort flush 即可。

缓存写入失败不应破坏关键业务数据，但必须有日志记录或诊断结果。

## 6. 与 Presentation 的关系

Presentation 可以通过 Application 获取展示所需结果。

Presentation 不能直接操纵 Infrastructure 缓存实现。

第一版不为缩略图缓存或 metadata cache 定义跨模块公共端口。`MetadataCache`、`ThumbnailCache` 先作为 Infrastructure 内部技术能力存在，不能被 ViewModel 直接调用。

如果未来 UI 确实需要动态缩略图或 metadata cache 查询，必须先在 Core 中定义稳定端口，再由 Application 暴露明确用例结果，最后由 Presentation 消费 Application 结果。不能让 Presentation 为了显示图片直接引用 Infrastructure。

第一版如果缩略图仅为本地 UI 占位资源，应由 Presentation 自己管理静态资源。不要为了一个占位缩略图把缓存系统提前复杂化。

## 7. 第一版约束

第一版必须做到：

- 日志脱敏。
- 日志 flush。
- 缓存容量或过期策略。
- 缓存不保存 secret material。
- 缓存不作为业务事实唯一来源。

第一版暂不追求：

- 后台缓存清理服务。
- 分布式日志。
- 远程日志上传。
- 复杂 thumbnail pipeline。
- 长期远程目录离线索引。
