# AtomBox × AtomUI.City MVVM 迁移

> 状态：Planned
>
> 目标版本：TBD
>
> 创建时间：2026-10-13
>
> 影响模块：Desktop / Application / Transfer / Providers / Tests

## 背景

AtomBox Desktop 已完成 Host、Module、Routing、Presentation 和 Localization 的 City 化，但仍保留自建 `ViewModelBase`、同步/异步 Command、构造阶段刷新、游离后台任务以及若干由页面自行维护的订阅和定时器。它们没有形成第二套 Host 或 Router，却会绕过 City 已建立的激活、停用、取消、释放和诊断边界。

本轮审计不是把 City 类型下沉到全部业务模块。依赖原则保持不变：`AtomBox.Desktop` 是 City 的应用组合层；Application、Transfer、Providers、Infrastructure 和 Core 保持 City 无关。若取消令牌需要向下传递，这些模块只接收 .NET `CancellationToken`，不引用 City。

本文件是 21 项审计问题和已确认方案的历史记录。City 通用能力的正式设计由 City 自己的 ADR、Feature、API、Lifecycle、Testing 和 Compatibility 文档定义；本文只记录 AtomBox 为什么需要它、如何消费它以及迁移验收条件。

## 目标

- AtomBox Desktop ViewModel 统一使用 City 的激活、停用和最终释放语义。
- 页面异步工作都有明确 owner，不再存在游离 `Task`、`async void` 业务回调或构造函数 I/O。
- Command 支持 AtomBox 需要的参数、`CanExecute`、并发、取消和异常链路。
- Shell 根对象、StatusBar 子对象和路由页面分别具有明确且可测试的所有权。
- 21 项问题均能追溯到决定、实施仓库和自动化验收证据。

## 非目标

- 不把 City 引用加入 AtomBox 的 Core、Application、Transfer、Providers 或 Infrastructure。
- 不要求 Dialog、Message、文件选择器或托盘菜单迁移为 City 通用能力。
- 不把业务 `OperationResult<T>` 与 City 命令执行结果合并。
- 不在 City 内实现磁盘异常日志、日志目录、保留周期或文件轮转。
- 不以反射扫描 ViewModel 属性推导父子关系，也不引入 `RootViewModelBase` 或 `ChildViewModelBase`。

## 约束和术语

- `OnActivatedAsync` / `OnDeactivatedAsync` 管理一次可重复的激活会话；`OnDisposed` 只管理对象生命终点。
- ActivationScope 是一段激活会话的资源和异步工作 owner，不是全局任务调度器。
- Root 和 Child 是 Presentation 中的组织角色，不是 MVVM 基类层级；父对象持有 child activation lease，子对象拥有独立且链接父取消的 ActivationScope。
- Command 的 `CanExecute` 是 UI 提示和前置判断，真正的并发准入必须由原子状态保证。
- 持久传输在成功提交后由应用级 `TransferTaskScheduler` 所有，不能继续挂在页面 ActivationScope 下。

## 21 项审计结论

### 01. ViewModel 基类能力

**AtomBox：** 自建轻量 `ViewModelBase` 提供属性通知和释放，业务 ViewModel 已经依赖这套继承入口。

**City：** `ViewModelBase` 已提供属性通知、激活状态机、ActivationScope 和最终释放入口。

**问题：** City 不是能力不足，而是 AtomBox 现有基类绕开了 City 生命周期，导致后续订阅、任务和 Command 无法统一归属。

**已决方案：** AtomBox Desktop ViewModel 迁移到 City `ViewModelBase`。不为此修改 City。

**实施归属：** AtomBox。

**验收：** Desktop 不再存在第二个 ViewModel 基类；属性通知和重复释放行为有测试。

### 02. Dispose 扩展模型

**AtomBox：** 当前清理逻辑倾向于统一放进 `Dispose`，没有区分页面暂时离开和对象永久销毁。

**City：** `OnDeactivatedAsync` 结束一次激活会话；`OnDisposed` 是对象永久终点，且 `Dispose` 语义不可逆。

**问题：** 若把可重建资源只放在 `Dispose` 清理，路由离开时资源会继续运行；若在停用时永久销毁全部状态，重新激活又无法恢复。

**已决方案：** 逐个审计资源。激活期订阅、循环和短期资源在 `OnDeactivatedAsync` 或 ActivationScope 中结束；真正终身资源才在 `OnDisposed` 释放。同一资源只设一个清晰 owner，禁止在两个钩子中重复释放业务逻辑。

**实施归属：** AtomBox。

**验收：** 页面停用后无激活期资源残留；重复激活可重新建立资源；最终 Dispose 幂等。

### 03. 页面激活与停用

**AtomBox：** 页面初始化和清理散落在构造函数、命令、事件回调和 `Dispose` 中。

**City：** `OnActivatedAsync` / `OnDeactivatedAsync` 明确表示可重复的可见或可用会话，ActivationScope 随会话创建和结束。

**问题：** AtomBox 页面资源目前无法严格跟随路由 Entry 的激活状态。

**已决方案：** 页面初始化进入 `OnActivatedAsync`；由激活产生的订阅、循环和任务跟随当前 ActivationScope；页面停用后不得再提交 UI 状态。

**实施归属：** AtomBox。

**验收：** 激活、停用、再激活测试验证状态机、资源重建和无迟到 UI 更新。

### 04. 构造函数中的首次刷新

**AtomBox：** 部分 ViewModel 在构造期间触发刷新或启动异步工作。

**City：** 构造只负责建立对象；真正异步初始化应在激活阶段执行。现有 ActivationScope 尚缺结构化任务监控入口。

**问题：** 构造函数无法被异步等待，也没有可靠取消和所有权；失败、页面快速离开和重复激活都难以处理。

**已决方案：** 删除构造函数 I/O。按业务逐页选择“激活完成前等待首次加载”或“先显示加载态，再由激活域任务异步渲染”，两者都不得同步阻塞 UI 线程。后台路径使用 City 的 ActivationScope 任务监督能力。

**实施归属：** City 先补任务监督；AtomBox 再迁移调用点。

**验收：** 构造阶段不启动 I/O；首次加载只在激活时发生；取消和失败可观测。

### 05. 导航参数与首次刷新顺序

**AtomBox：** 导航参数应用和首次刷新可能各自触发加载，存在使用默认参数先加载、随后再刷新一次的风险。

**City：** Routing/Presentation 已在 Entry 激活前提供最终导航参数，但不会替应用决定业务加载时机。

**问题：** 参数和 I/O 的先后若不固定，页面可能短暂展示错误账号、路径或筛选结果。

**已决方案：** `ApplyNavigationParameter` 只同步归一化并保存最终状态；`OnActivatedAsync` 在参数应用后执行唯一一次首次加载。允许页面在最终结果到达前显示空白或加载态，不允许用错误参数抢跑。

**实施归属：** AtomBox。

**验收：** 测试断言导航参数先于首次加载，且一次激活只产生一次初始查询。

### 06. 本地化事件订阅

**AtomBox：** ViewModel 通过同步本地化投影读取文本，部分对象需要响应语言版本变化。

**City：** Localization 提供异步包、revision、handle 和订阅能力；AtomBox 已有 City 到 Avalonia 同步 Binding 的原子快照投影。

**问题：** 若为了订阅语言变化强制所有 ViewModel 继承 `LocalizedViewModelBase`，会形成与 City `ViewModelBase` 竞争的第二条继承轴，并扩大不必要耦合。

**已决方案：** 不建立强制本地化基类。继续使用 AtomBox 的 City 本地化投影；确实需要响应语言变化的 ViewModel 显式取得 handle/订阅 lease，并把 lease 注册到 ActivationScope。

**实施归属：** AtomBox；City 不新增专用基类。

**验收：** 语言切换时当前对象更新；停用后订阅释放；无需响应语言的 ViewModel 不增加本地化依赖。

### 07. TransferQueue 定时器

**AtomBox：** 传输队列使用 `DispatcherTimer` 和异步 tick 刷新，容易产生 `async void`、重入或生命周期漂移。

**City：** ActivationScope 可保存资源，但当前没有按 key 唯一启动并监督循环任务的能力。

**问题：** 重复激活可能创建多个刷新循环，停用时也无法确定循环已经退出。

**已决方案：** AtomBox 改为 `PeriodicTimer` 循环；City 提供 `StartUniqueAsync(key, ...)`。同一 ActivationScope 内同 key 只允许一个任务，重复准入拒绝并诊断，任务终止后释放 key；v1 不隐式替换旧任务。

**实施归属：** City + AtomBox。

**验收：** 多次激活不会并存两个定时循环；停用取消并等待循环退出；异常可诊断。

### 08. RemoteBrowser 账号变化

**AtomBox：** 账号变化事件通过 `Dispatcher.UIThread.Post(async ...)` 触发刷新；短时间连续事件可能乱序完成并覆盖较新的状态。

**City：** City 应负责 worker 的生命周期，但不应把 AtomBox 特定的“账号变化合并刷新”固化为通用消息语义。

**问题：** 事件发生顺序不等于异步刷新提交顺序，UI 可能回退到旧账号状态。

**已决方案：** AtomBox 使用容量为 1 的 .NET `Channel` 和单一消费者，将多个信号合并为“刷新到最新状态”；消费者由 ActivationScope 唯一监督。删除业务代码中的 `Dispatcher.UIThread.Post(async ...)`。

**实施归属：** AtomBox 使用标准库 Channel；City 只提供任务 owner 和唯一 worker。

**验收：** 突发账号变化最终收敛到最新状态；同一时刻只有一个消费者；停用后无刷新提交。

### 09. Shell 生命周期

**AtomBox：** `MainWindowViewModel` 是主窗口逻辑根，持有 `StatusBarViewModel`；它们不是 RouteOutlet 页面，现有页面 lease 无法自然表达这种关系。

**City：** MVVM 提供无角色的原子生命周期；Presentation 已有 Route Entry 所有权，但没有显式的逻辑 Root/Child ViewModel 组织合同。

**问题：** 这是 City Presentation 的能力缺口。若只靠 AtomBox 手工 Dispose，父子激活、取消、释放顺序和 Host/Window 关闭将缺少统一契约。

**已决方案：** City.Mvvm 保持无 Root/Child 逻辑；City.Presentation 提供显式 Root/Child 组合。Root 是角色而非基类；父对象显式取得并持有 `IAsyncDisposable` child activation lease；子对象拥有独立、链接父取消的 ActivationScope；禁止反射扫描属性。AtomBox 将 MainWindow 注册为逻辑 Root，将 StatusBar 作为 owned child；路由页面仍由 RouteOutlet 管理。

**实施归属：** City Presentation + AtomBox。

**验收：** 父停用/释放会取消并等待子对象；子失败不破坏释放链；RouteOutlet 页面不被误归入 Root child 集合。

### 10. 异步命令参数

**AtomBox：** 表格行操作依赖 `CommandParameter`，需要把具体行或任务 ID 传给异步命令。

**City：** 当前异步工厂只接受无参数 `Func<CancellationToken, Task>`。

**问题：** 迁移会丢失参数化行命令，迫使业务闭包化或保留自建 Command。

**已决方案：** City 增加泛型同步/异步工厂，返回 CommunityToolkit 的 `IRelayCommand<T>` / `IAsyncRelayCommand<T>`，执行委托接收 `T?`，异步版本同时接收 `CancellationToken`。

**实施归属：** City；AtomBox 改用泛型命令。

**验收：** null、合法参数、非法业务参数和取消路径都有单元测试；行命令不再依赖 AtomBox 自建实现。

### 11. 异步命令 CanExecute

**AtomBox：** 业务规则决定按钮何时可用，异步命令需要同步 `CanExecute` 谓词。

**City：** 当前异步工厂只按内部执行状态判断，不能接收业务谓词。

**问题：** City 无法表达“当前业务状态不允许执行”，或开发者只能在 execute 内晚期拒绝。

**已决方案：** 开发者提供同步业务谓词；City 把业务谓词和并发策略组合为最终 `CanExecute`。异步权限查询先更新可观测状态，再通知命令；业务 execute 仍做防御性校验。

**实施归属：** City + AtomBox。

**验收：** 业务谓词为 false 时 UI 禁用且直接执行仍被可靠拒绝；状态变化能重新计算。

### 12. CanExecute 变化通知

**AtomBox：** 自建命令通过 `RaiseCanExecuteChanged` 手工刷新，依赖关系散落在 ViewModel 中。

**City：** 基于 CommunityToolkit，可使用 `NotifyCanExecuteChanged()` 和 `[NotifyCanExecuteChangedFor]`，但框架内部状态变化也必须主动通知。

**问题：** 谁负责计算和触发必须分层，否则 City 会猜测业务依赖，或业务代码要知道 City 内部执行状态。

**已决方案：** 开发者负责业务规则和业务属性依赖；City 负责执行、取消和并发状态变化引起的通知。不做反射依赖分析和轮询。

**实施归属：** City + AtomBox。

**验收：** 业务属性、开始执行、结束、取消和拒绝路径产生准确通知，不出现通知风暴。

### 13. 并发策略

**AtomBox：** 当前各命令并发含义不统一；传输队列要求同一任务 ID 单飞，不同任务可以并行。

**City：** 当前默认用短 `lock` 保护单一执行准入，只能表达整个命令的 reject。

**问题：** `CanExecute` 不是原子锁；并发点击仍必须在执行入口可靠准入。全局单飞又会错误阻塞不同表格行。

**已决方案：** City 提供 `Reject`（默认）、`Allow` 和 `RejectByKey`。默认 reject 可用短 `lock` 或 `Interlocked`；Allow 用活动计数和执行注册；按 key reject 用 `ConcurrentDictionary<TKey, ...>.TryAdd`。任何锁都不能跨 `await`。`CancelPrevious` 和排队策略留待后续独立设计。AtomBox 行命令以 `TransferTaskId` 为 key。

**实施归属：** City + AtomBox。

**验收：** 同 key 双击只接受一次，不同 key 可并发；取消、异常和完成都会释放准入状态。

### 14. Command 与 ActivationScope 绑定时机

**AtomBox：** 命令通常在 ViewModel 构造时创建，而 ViewModel 此时还没有当前 ActivationScope；对象还可能停用后再次激活。

**City：** 当前工厂捕获一个可选固定 ActivationScope，只链接取消 token，既不能监督任务，也可能永久绑定旧 Scope A。

**问题：** 这是 City 的真实设计缺陷：构造期 scope 为 null 会形成无人管理命令，重激活又可能继续使用已释放的 Scope A。

**已决方案：** 命令对象在 ViewModel 生命周期内保持稳定，但绑定 activation owner/accessor；每次执行时原子解析当前 scope，并通过 `scope.StartAsync` 注册。未激活时 `CanExecute` 为 false，直接调用稳定拒绝并诊断；重新激活自动使用 Scope B。显式固定 scope 重载只保留为低级一次性场景。

**实施归属：** City。

**验收：** Scope A 停用后不可接收新执行；同一命令在 Scope B 可执行；任务被当前 scope 取消并等待。

### 15. 异步取消令牌

**AtomBox：** 多个 Desktop 异步方法没有把命令或激活取消向 Application、Transfer 和 Provider 调用链传递。

**City：** 命令和 ActivationScope 已提供 token、取消分类和状态，只需在第 13、14 项完善后被正确消费。

**问题：** 页面已经离开时，底层 I/O 仍可能继续并回写过期 UI；另一方面，已提交的持久传输不应因页面关闭而被误取消。

**已决方案：** Desktop 异步方法接收并向下传递 .NET `CancellationToken`。页面加载跟随 ActivationScope；持久传输成功提交后把所有权转交应用级调度器，业务取消和页面生命周期取消明确区分。

**实施归属：** AtomBox；非 Desktop 模块只增加标准库 token，不引用 City。

**验收：** Provider 调用观察取消；停用后不提交 UI；已提交传输离开页面后继续运行。

### 16. Command 异常处理

**AtomBox：** 自建命令和 fire-and-forget 路径可能让异常只留在后台 Task，或被 UI 入口隐式吞掉。

**City：** 当前命令把异常写入失败结果但不向直接 await 的调用者抛出，无法同时满足调用链真实性和 `ICommand.Execute` 无 await caller 的现实。

**问题：** City 必须保证可捕获异常永不静默消失，同时不能把显示对话框、磁盘日志等应用策略塞进 MVVM。

**已决方案：** 直接 await `ExecuteAsync` 保留原堆栈并向调用者抛原异常；`ICommand.Execute` 的异常恰好一次交给必需的应用级 `ICommandExceptionHandler`，同时更新命令状态和结构化诊断；取消不进入异常 handler。City.Mvvm 不显示 UI，也不提供磁盘日志、目录、保留周期、轮转或 file sink。开发者必须 await 直接调用，禁止空 catch 和游离调用。

**实施归属：** City 定义链路；AtomBox 注册应用 handler 并遵守调用规范。

**验收：** awaited、ICommand、取消和 handler 自身失败路径分别测试；异常对象和堆栈不被替换；没有 City 磁盘副作用。

### 17. 游离后台 Task.Run

**AtomBox：** 存在 `_ = Task.Run(...)`、`_ = RefreshAsync(...)` 或异步 Dispatcher 回调等没有 owner 的工作。

**City：** 这与第 04、07 项是同一个结构化任务监督缺口，不应再建立第二套任务管理器。

**问题：** 游离任务无法可靠取消、等待、诊断和阻止停用后的迟到提交。

**已决方案：** 删除游离 fire-and-forget。异步 I/O 直接 await；CPU 工作允许 `Task.Run`，但返回 Task 必须被 await 或交给明确 owner。页面工作由 Command/ActivationScope 所有；持久传输由应用级 scheduler 所有。

**实施归属：** City 的第 07 项能力 + AtomBox 清理调用点。

**验收：** 静态审计无业务 `_ = Task.Run`/`_ = XxxAsync`；停用会 drain 页面任务；测试无未观察异常。

### 18. Command 属性类型

**AtomBox：** ViewModel 暴露自建命令具体类型或宽泛 `ICommand`，并调用自建 `RaiseCanExecuteChanged`。

**City：** 以 CommunityToolkit 的 `IRelayCommand`、`IAsyncRelayCommand` 及其泛型形式为标准合同。

**问题：** 保留自建类型会让 City 工厂、并发、取消和诊断无法覆盖这些命令。

**已决方案：** 命令 owner ViewModel 暴露 `IRelayCommand` / `IRelayCommand<T>` / `IAsyncRelayCommand` / `IAsyncRelayCommand<T>`；纯展示行对象可只暴露 `ICommand`。迁移完成后删除 AtomBox 自建命令类，改用 `NotifyCanExecuteChanged` 或属性特性。

**实施归属：** AtomBox。

**验收：** 自建命令源码和引用清零；XAML binding 与参数化行命令测试通过。

### 19. OperationResult 名称冲突

**AtomBox：** `OperationResult<T>` / `OperationResult` 表示业务操作成败并携带 `StorageError`。

**City：** `OperationResult` 表示一次命令运行的 ID、状态、耗时和异常快照。

**问题：** 两者语义不同但同名，Desktop 同时引用时容易产生歧义；强行统一会把存储业务错误污染框架执行模型。

**已决方案：** AtomBox 业务结果保持原名和语义，不做转换层。City 在 1.0 Preview 冻结前把命令结果重命名为 `OperationExecutionResult`，并同步 public API、compatibility 和迁移说明。

**实施归属：** City 重命名；AtomBox 只处理引用消歧。

**验收：** 两类结果在同一文件可清晰使用；City public API 基线和全部测试同步更新。

### 20. 项目引用

**AtomBox：** Desktop 已直接引用 City Core、Presentation、Routing 和 Localization，但没有直接引用 City.Mvvm。

**City：** Presentation 传递依赖 Mvvm，但这不构成 AtomBox 使用 MVVM public API 的显式依赖声明。

**问题：** 依赖传递链变化会让 Desktop 无预警失去 MVVM 类型，也掩盖真实模块边界。

**已决方案：** `AtomBox.Desktop.csproj` 增加对 `AtomUI.City.Mvvm` 的直接 `ProjectReference`；联合开发阶段继续固定 City 子模块源码，包化留待后续。其他业务程序集不引用 City。

**实施归属：** AtomBox。

**验收：** 项目引用边界测试断言只有 Desktop 使用 City；Release build 和三平台 publish 成功。

### 21. 测试缺口

**AtomBox：** 现有测试覆盖 Host、路由、View 注册、本地化等第一阶段迁移，但没有完整覆盖 ViewModel 激活、异步所有权、Command 并发和 Shell 父子关系。

**City：** 现有测试基于旧 Command 和 ActivationScope 合同，且部分测试明确接受“异常只记结果不抛出”的旧行为。

**问题：** 没有契约、竞态和集成测试，迁移可能在编译通过时仍留下生命周期泄漏或异常丢失。

**已决方案：** City 增加任务准入/唯一 key/取消 drain/释放竞态、泛型参数、业务 CanExecute、通知、Reject/Allow/RejectByKey、动态 Scope A/B、异常恰好一次和 Root/Child lease 测试。AtomBox 增加首次加载、导航顺序、本地化 lease、单一定时器、Channel 合并、CanExecute、token 传播、Shell child lease 和停用后无迟到提交测试。最后执行全量 build/test、三平台 publish；真实 UI 烟测由用户人工完成。

**实施归属：** City + AtomBox。

**验收：** 每个新增 City Feature ID 有单元/契约测试；AtomBox 的九类行为有自动化证据；所有门禁通过后才能把本文状态改为 Done。

## City 能力映射

| AtomBox 审计项 | City 正式能力 | 状态 |
| --- | --- | --- |
| 04、07、17 | `AUC-MVVM-007` Activation-scoped Structured Tasks | Planned |
| 10、11、12、19 | `AUC-MVVM-008` Typed Commands and CanExecute | Planned |
| 13、14、15 | `AUC-MVVM-009` Command Concurrency and Activation Ownership | Planned |
| 16 | `AUC-MVVM-010` Command Exception Delivery | Planned |
| 09 | `AUC-PRESENTATION-017` Logical Root and Owned Child ViewModels | Planned |

City 架构决策：

- [ADR 0010：ActivationScope 采用结构化并发并约束 Command 执行所有权](../../../external/AtomUI.City/docs/decisions/0010-activation-scoped-structured-concurrency.md)
- [ADR 0011：Presentation 提供逻辑 Root/Child，MVVM 保持角色无关](../../../external/AtomUI.City/docs/decisions/0011-presentation-root-child-ownership.md)

## 实施顺序

1. 先在 City 完成 `AUC-MVVM-007` 至 `010` 和 `AUC-PRESENTATION-017` 的文档、实现、测试与 public API review。
2. City 全量测试通过后提交并同步 `develop`，AtomBox 子模块固定到该提交。
3. AtomBox Desktop 增加 City.Mvvm 直接引用，先迁移基类和生命周期，再迁移 Command。
4. 按页面迁移首次加载、导航参数、订阅、定时器、账号变化队列和取消传播。
5. 迁移 MainWindow Root 和 StatusBar child lease，删除自建命令与游离任务。
6. 执行 AtomBox 全量测试、Release build 和三平台单文件 publish。
7. 用户按现有真实 UI 烟测清单完成最终人工验收。

## 测试与验收

### City 门禁

- `AUC-MVVM-007`：新任务准入、唯一 key、完成移除、关闭准入、取消、drain、Dispose 竞态、超时诊断和顽固任务。
- `AUC-MVVM-008`：同步/异步泛型参数、业务 CanExecute、属性通知、内部状态通知和 `OperationExecutionResult` API。
- `AUC-MVVM-009`：Reject、Allow、RejectByKey；活动计数；同 key 原子准入；Scope A/B；inactive rejection。
- `AUC-MVVM-010`：awaited 原异常、`ICommand.Execute` handler 恰好一次、取消排除、handler 失败诊断和无磁盘行为。
- `AUC-PRESENTATION-017`：Root attach、child lease、独立链接 scope、leaf-to-root 释放、重复释放和局部失败继续。

### AtomBox 门禁

- Desktop 行为测试覆盖 21 项中的所有 AtomBox 归属变更。
- `src` 中不存在 AtomBox 自建 Command、构造函数 I/O、业务 `async void`、游离 `_ = Task.Run` 和 `Dispatcher.UIThread.Post(async ...)`。
- 非 Desktop 业务程序集没有 `AtomUI.City` 引用。
- 全量测试、Release 构建以及 win-x64、linux-x64、osx-arm64 自包含单文件发布通过。
- 自动化完成不替代真实 UI 人工烟测。

## 文档同步

- City 实施完成时更新 City 的 MVVM/Presentation Feature、API、Lifecycle、Threading、Diagnostics、Testing、Compatibility、Public API 和全局 1.0 跟踪文档。
- AtomBox 实施完成时更新本文件状态、[第一阶段 City 迁移审计](with-city.md)、Desktop 模块文档和长期测试矩阵。
- 若实现发现方案必须变化，先修改对应仓库的正式文档和本追溯记录，再修改源码。
