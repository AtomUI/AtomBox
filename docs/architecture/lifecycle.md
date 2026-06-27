# AtomBox 生命周期设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-15
>
> 冻结范围：当前文档定义的生命周期原则、业务对象生命周期、运行时服务生命周期、模块启动停止约束和 Phase 9 收口生命周期规则
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的生命周期边界，必须先说明原因，再同步更新相关设计文档。

本文档记录 AtomBox 的生命周期设计结论。

生命周期讨论必须先于 DI 注册方式。DI 是实现手段，不是设计起点。一个对象应该是应用级单例、短生命周期服务、任务执行期对象、快照对象，必须由它的业务语义、状态归属、资源占用、并发边界和错误恢复方式决定，而不是由注册容器是否方便决定。

## 1. 生命周期分类

AtomBox 的生命周期分为三类：

| 分类 | 关注点 | 典型对象 |
| --- | --- | --- |
| 业务对象生命周期 | 对象代表什么业务事实、是否持久化、何时创建、何时失效 | `StorageAccount`、`TransferTask`、`RemoteItem`、`RemotePath`、`LocalPath`、`StorageError` |
| 运行时服务生命周期 | 服务是否持有运行时状态、资源、队列、缓存、订阅关系 | `TransferManager`、`IStorageProviderFactory`、仓储服务、缓存服务、ViewModel |
| 模块生命周期 | 模块在应用启动、运行、关闭过程中的初始化顺序和故障处理 | Desktop、Application、Core、Transfer、Providers、Infrastructure |

三类生命周期不能混写。业务对象的生命周期回答“这个对象代表的事实活多久”；运行时服务生命周期回答“这个服务实例何时创建和释放”；模块生命周期回答“整个模块在应用进程中的启动、运行、关闭顺序”。

## 2. 业务对象生命周期

### 2.1 StorageAccountId

`StorageAccountId` 是稳定且不可变的账号标识。

账号重命名、endpoint 修改、region 修改、凭据轮换，都不能改变 `StorageAccountId`。如果这些操作导致 ID 改变，传输任务、缓存、历史状态、书签都会被打断，这是严重设计错误。

### 2.2 StorageAccount

`StorageAccount` 描述一个远程存储账号的配置事实。

第一版 `StorageAccount` 不设计 enabled / disabled 状态。账号存在即表示可被用户选择；账号是否连接成功由连接测试或具体操作结果表达，不把临时可用性写成账号生命周期状态。

`StorageProviderId` 是账号绑定的具体 provider 标识。账号创建后不应随意改变 `StorageProviderId`；把阿里云 OSS 账号编辑成 SFTP 账号不是普通账号修改，而应视为新账号创建。

账号删除必须检查是否存在未完成传输任务引用它。只要仍有未完成任务引用该账号，账号删除就必须被禁止。

账号修改不影响已经运行中的传输任务。运行中的任务使用启动时形成的执行快照；修改后的账号配置只影响后续新启动或重新恢复的操作。

### 2.3 CredentialRef、CredentialLease 与 Secret Material

`CredentialRef` 是 Core 可见的凭据引用，不是凭据明文。

`CredentialRef` 是钥匙编号，secret material 是钥匙本体。`CredentialRef` 可以进入 `StorageAccount`、`TransferTask` 运行上下文和日志中的脱敏诊断信息；secret material 不能进入这些对象。

凭据轮换时，账号替换为新的 `CredentialRef`。旧 `CredentialRef` 不能立刻强删，必须等没有运行中的任务或 provider 会话引用它之后，再由 Infrastructure 做延迟清理。

运行时必须引入 `CredentialLease` 或等价的占用声明机制。`CredentialLease` 不是持久化业务对象，而是运行时资源占用声明，用来表达某个 worker、provider 或 provider session 正在使用某个 `CredentialRef`。旧凭据只有在没有活动 lease 后才能被物理清理。

Core 只能定义 `CredentialLease` 的抽象句柄或端口契约，不能实现 lease 计数、pending-delete、物理清理或并发锁。这些运行时行为必须由 Infrastructure 实现。

Secret material 包括 AccessKey、client secret、password、private key、refresh token、access token 等真实认证材料。Secret material 不进入 Core，不进入 TransferTask，不进入日志，不进入普通配置文件，不进入 ViewModel。

Secret material 的合法存活边界不是“provider 创建瞬间”，而是“单次 provider 操作或单次 Transfer 执行批次所需窗口”。OSS 分片上传、SFTP 重连、网盘 token refresh 等场景可能需要 provider 在操作过程中继续持有或间接使用认证材料。这个现实不能回避，否则长任务会在中途认证失败。

实现上不得把“释放引用”等同于“密钥安全清除”。第一版至少必须禁止 secret material 被日志、缓存、持久化和 UI 状态捕获；后续如引入受控 byte buffer、credential lease payload、平台安全句柄等机制，必须继续遵守本节边界。

### 2.4 TransferTask

`TransferTask` 是持久化业务对象。

传输任务保存 `StorageAccountId`、本地路径、远程路径、传输方向、传输选项和任务状态。传输任务不保存 provider 实例，不保存 SDK client，不保存 secret material，也不保存完整账号配置快照。

传输执行开始时，Transfer 通过 Core 端口读取账号，并形成本次执行的内存快照。任务重启或恢复时，仍然通过 `StorageAccountId` 重新读取当前账号和当前 `CredentialRef`。

失败的传输任务必须可持久化、可重试、可删除。失败不是对象消亡，只是任务状态变化。

### 2.5 IStorageProvider

`IStorageProvider` 是短生命周期运行对象，不是账号对象本身。

默认策略是按 Application 短用例或 Transfer 执行批次创建 provider，操作结束后释放。第一版不做账号级 provider 缓存，不承诺 provider 线程安全。并发通过创建多个 provider 实例实现，而不是多个线程共享同一个 provider 实例。

SDK client、协议 session、HTTP client wrapper 等具体资源由 provider 内部持有，并随 provider 一起释放。provider 不能把 SDK 对象暴露给 Core、Application、Transfer 或 Presentation。

Provider 生命周期中的“操作”必须严格分层：

| 层级 | 名称 | 含义 | provider 生命周期影响 |
| --- | --- | --- | --- |
| 1 | UI 操作 | 用户点击按钮、选择文件、刷新界面 | 不直接决定 provider 生命周期 |
| 2 | Application 短用例 | 连接测试、目录列表、能力探测、远程对象删除等短流程 | 可以一次用例创建一个 provider |
| 3 | Transfer 执行批次 | `TransferWorker` 从队列中执行一组可合并传输任务 | 传输 provider 的主要生命周期边界 |
| 4 | Provider 内部请求 | SDK HTTP 请求、分片上传请求、SFTP write、API 轮询 | 绝不能每个内部请求创建 provider |

Provider 生命周期的最小合理边界是 Application 短用例或 Transfer 执行批次，不是单个文件、单个分片、单个 HTTP 请求或单个 SDK 方法调用。

同一次批量传输中，同账号、同方向、同目标、同策略的一组文件可以复用同一个 provider 或 provider session。不同用户提交批次第一版不跨批次共享 provider 实例。未来如需性能优化，只能引入明确设计过的 `ProviderSessionPool` 或 `ProviderClientPool`，不能把 provider 偷偷做成账号级全局单例。

### 2.6 RemoteItem、RemotePath 与 LocalPath

`RemoteItem` 是远程资源在某一时刻的不可变快照，不是远程文件的活动代理对象。

远程对象被重命名、移动、覆盖、删除后，旧 `RemoteItem` 不自动变化。UI 或 Application 需要重新请求列表或详情，获取新的快照。

第一版远程目录列表不做持久化缓存。目录浏览结果是短期内存数据，刷新时整体替换。

`RemotePath` 是不可变值对象，用来表达远程路径语义。不能用裸 `string` 长期代替远程路径。

`LocalPath` 是不可变值对象，用来表达本地路径文本语义。Core 可以持有 `LocalPath`，但不能使用 `FileInfo`、`DirectoryInfo`、文件流或本地文件系统访问 API 作为 Core 模型字段。

### 2.7 Application Result 与 ViewModel State

Application 返回的结果对象是一次用例调用的短生命周期结果快照。

ViewModel 可以持有展示快照和 UI 状态，但不能把这些状态伪装成 Core 业务对象。刷新远程列表、传输状态、账号列表时，ViewModel 应以替换快照的方式更新展示状态。

### 2.8 StorageError

`StorageError` 是结构化错误结果快照。

Providers 必须把 SDK/API/协议异常转换为 Core 统一错误模型。`StorageError` 不长期持有原始 SDK Exception，不把具体 SDK 类型泄漏给 Application、Transfer 或 Presentation。

## 3. 运行时服务生命周期

### 3.1 Provider Factory 与 Registry

`IStorageProviderRegistry` 和 `IStorageProviderFactory` 是应用级单例服务。

Registry 只负责 provider 类型、描述、能力和注册信息，不创建 provider 实例。Factory 负责基于账号、凭据引用和 provider 配置创建短生命周期 provider 实例。

Factory 和 Registry 不持有业务运行状态，不缓存 secret material，不缓存 provider 实例。

### 3.2 Repository 与 Store

`IStorageAccountRepository`、`IApplicationSettingsRepository`、`ITransferTaskStore`、`ITransferStateStore` 是应用级服务。

这些服务可以作为长期服务实例存在，但文件句柄、数据库连接、事务、锁等内部资源必须按操作打开和释放，不能因为服务是长期实例就长期占用底层资源。

### 3.3 Credential Store

`ICredentialStore` 是应用级服务。

Credential Store 负责保存和读取凭据，但不得缓存 plaintext secret。读取 secret 的结果只允许进入 provider factory / provider 操作链路，并在对应 Application 短用例或 Transfer 执行批次结束后释放引用。

Credential Store 必须支持 `CredentialLease` 或等价运行时占用机制。凭据轮换后，旧 `CredentialRef` 可以被标记为 pending-delete，但只要仍存在活动 lease，就不能物理删除其 secret payload。

### 3.4 Transfer Runtime

`TransferManager` / `ITransferTaskScheduler` 是应用级运行时服务。

Transfer Runtime 拥有任务队列、并发调度、取消、重试、进度聚合和任务状态更新。`Paused` / `Interrupted` 状态用于关闭、中断和后续恢复设计，第一版不承诺对外暂停/恢复调度能力。`TransferQueue` 是 Transfer Runtime 的内部状态，不直接暴露给 UI 或 Application。

`TransferWorker` 是单个执行批次生命周期对象，由 Transfer Runtime 创建和释放。`TransferProgressReporter`、`TransferSpeedMeter` 等对象属于任务执行期，输出进度快照，不作为应用级单例。

一个 Transfer 执行批次可以包含一个任务，也可以包含同账号、同方向、同目标、同策略的一组可合并任务。批次内部可以复用 provider 或 provider session；批次之间第一版不共享 provider 实例。

Retry、throttle、concurrency policy 应优先设计为不可变配置或无状态策略服务。

### 3.5 Application Services

`AccountAppService`、`RemoteBrowserAppService`、`TransferAppService`、`SettingsAppService` 是应用级无状态服务。

Application Service 可以作为长期服务实例存在，但不持有 provider、SDK client、secret material、ViewModel 或 UI 状态。

Application Service 只编排用例流程，不拥有传输队列，不拥有 provider 缓存，不直接管理底层资源生命周期。

### 3.6 Remote Browsing Runtime

第一版不引入长期 `BrowserContext` 或 `BrowserSession`。

远程浏览是短操作：Application 调用 provider 抽象，provider 返回 `RemoteItem` 快照，ViewModel 持有展示结果。后续如果引入分页游标、会话缓存或目录预取，必须重新补充生命周期设计。

### 3.7 Presentation Runtime

ViewModel 的生命周期跟随页面、窗口或工作区。

ViewModel 可以长期存在于 UI 会话中，但只持有展示状态、筛选条件、选中项、加载状态、错误展示状态等 UI 状态。ViewModel 不持有 provider、Transfer worker、SDK client、secret material 或 Infrastructure 资源。

### 3.8 Infrastructure Runtime

`LoggingInitializer` 属于启动初始化对象，应用退出时必须 flush / release。

`MetadataCache`、`ThumbnailCache` 是应用级缓存服务。缓存必须有容量、时间、账号边界或清理策略，不能无限增长。

第一版 Infrastructure 不启动后台缓存清理线程。缓存清理只能发生在读写路径、容量淘汰、启动维护或关闭维护中。

`ApplicationSettings` 读取结果是配置快照；设置保存必须通过 `SettingsAppService` 显式触发。

## 4. 模块生命周期

### 4.1 启动顺序

Desktop 是唯一组合根。

应用启动顺序必须区分“服务注册”和“运行时初始化”。注册阶段只描述依赖关系，不能执行重 IO、不能连接远程服务、不能启动后台业务流程。初始化阶段在 `IServiceProvider` 构建后，由 Desktop 组合根显式协调。

应用启动顺序如下：

```text
Desktop 启动
  -> 注册 Infrastructure 服务
  -> 注册 Providers
  -> 注册 Transfer Runtime
  -> 注册 Application Services
  -> BuildServiceProvider
  -> 初始化 / 校验 Infrastructure
  -> 初始化 Transfer Runtime
  -> 创建 Presentation Shell
  -> 进入 UI 主循环
```

Core 没有运行时初始化。Core 只提供类型、端口、模型和领域规则。

Providers 注册 provider 描述、能力和 factory 实现，但不在启动阶段创建具体 provider 实例，不连接远程服务，不刷新 token，不启动后台线程。

### 4.2 Transfer 启动策略

Transfer Runtime 在应用启动时初始化，并读取未完成任务或队列状态；这些状态只能通过 Core 端口和 Application 查询用例形成不可变快照后供 UI 展示。

第一版不自动恢复执行历史未完成任务。未完成任务应展示为 Paused、Interrupted 或 recoverable 状态，由用户手动恢复。

这个约束很重要：桌面软件启动时自动跑网络传输，容易造成误传、误删、凭据过期风暴和难以解释的用户体验问题。

### 4.3 启动失败策略

Infrastructure 是关键基础模块。

如果配置损坏、凭据服务不可用、本地状态存储不可用，Desktop 必须阻断正常主界面，并展示启动错误或恢复入口。

启动失败时，Desktop 可以创建最小启动错误界面，但不能创建正常 Main Shell。最小启动错误界面只负责展示错误、恢复入口和退出入口，不承载账号、浏览、传输等正常功能。

Infrastructure 不能自己弹 UI。Application 不负责启动错误界面。启动失败展示属于 Desktop / Presentation 的责任。

Application 和 Providers 不需要复杂 Ready 状态。它们要么被正确注册，要么由调用结果返回错误。

Transfer Runtime 可以存在 Ready / Unavailable 状态，因为它持有运行时队列和任务执行能力。

Phase 9 要求启动失败必须具备可诊断性。启动错误结果至少应能区分配置损坏、凭据存储不可用、传输状态存储不可用、迁移失败和未知基础设施错误。Desktop 可以展示最小错误界面，但不能在关键依赖不可用时进入正常 Shell。

### 4.4 关闭顺序

应用关闭顺序如下：

```text
停止接受新的 UI 操作
  -> 停止新的 Application 用例调用
  -> Transfer Runtime 请求 worker 停止
  -> 运行中任务保存为 Paused / Interrupted
  -> flush 关键传输状态
  -> best-effort flush metadata / thumbnail cache
  -> flush logs
  -> release Infrastructure resources
  -> 进程退出
```

Transfer 停止必须有有限超时。无法及时停止的 worker，应把任务标记为 Interrupted，然后继续退出。

传输任务状态、账号配置、凭据引用属于关键状态，必须尽力保存。metadata cache、thumbnail cache 属于普通缓存，关闭时 best-effort 即可。

Phase 9 要求关闭时的运行中任务不能被保存为成功，也不能只保存成普通失败。由于应用退出、超时或 worker 无法确认远端最终状态造成的停止，必须落为 `Interrupted` 或等价的可解释状态，并在后续队列或历史中展示原因。

### 4.5 后台服务边界

第一版只有 Transfer 可以拥有后台运行活动。

Providers 不启动后台 keepalive、后台 token refresh 或后台预热任务。Infrastructure 不启动后台缓存清理线程。OAuth refresh 如后续需要，应发生在 provider factory / provider 操作链路中，并通过 Core 错误模型反馈失败。

任何新增后台服务都必须先补充本文档，说明启动时机、停止时机、错误处理、资源释放和用户可见状态。

## 5. DI 映射规则

DI 注册必须服从生命周期设计，而不是倒过来决定生命周期。

| 生命周期语义 | DI 倾向 | 说明 |
| --- | --- | --- |
| 应用级无状态服务 | Singleton | 例如 Registry、Factory、Application Service |
| 应用级运行时服务 | Singleton | 例如 Transfer Runtime、缓存服务 |
| 操作期对象 | Transient 或工厂创建 | 例如 `IStorageProvider`、SDK session、连接对象、credential lease |
| 任务执行期对象 | Transfer 内部创建 | 例如 `TransferWorker`、progress reporter、执行批次上下文 |
| 快照对象 / 值对象 | 不由 DI 管理 | 例如 `RemoteItem`、`RemotePath`、`LocalPath`、`StorageError` |

禁止为了方便把 provider、SDK client、secret material、Transfer worker、ViewModel 状态注册成全局单例。

`IServiceProvider` 是 DI 容器的解析入口，不是普通业务依赖。AtomBox 全局禁止把 `IServiceProvider` 注入普通业务对象、ViewModel、Provider、Transfer worker、Repository、Row ViewModel、参数对象或结果对象。

Application、Transfer、Providers、Infrastructure 可以暴露 `IServiceCollection` 注册扩展，供 Desktop 生产组合根调用。这些注册扩展只描述服务注册关系，不等于模块可以持有或使用 `IServiceProvider`。

只有生产组合根或测试组合根可以直接创建和持有 `IServiceProvider`。生产组合根属于 Desktop，不属于 Application。测试组合根是组合根的测试形态，不是普通业务对象例外。如果某些运行时对象需要按目标创建其他对象，必须优先使用显式工厂接口、具体工厂类或 `Func<T>` 委托，由组合根完成装配后传入。不要把 `IServiceProvider` 当成万能对象工厂到处传递。

Core 第一版不提供 DI 注册入口。Core 只提供类型、端口、模型、值对象和领域规则；如果未来 Core 出现确实需要容器管理的纯 Core 无状态服务，再补充 Core 注册设计。
