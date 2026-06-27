# AtomBox 架构设计文档

> 文档状态：第一版发布基线冻结
>
> 冻结时间：2026-06-22
>
> 冻结范围：当前文档定义的架构、模块边界、依赖关系、目录规划或实现约束
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的设计边界，必须先说明原因，再同步更新相关设计文档。

本目录维护 AtomBox 软件的正式设计、工程规范、模块文档。

## 1. 产品说明

AtomBox 是一款跨平台的数据管理与传输软件，用途如下：

- 远程连接管理各家云服务商的 OSS 对象存储。
- FTP / SFTP / WebDAV 传输文件。
- 通过统一 provider 抽象扩展网盘 API。阿里云盘和百度网盘 provider 代码已存在，但第一版发布基线不把它们作为充分验收主路径能力。

## 2. 核心设计原则

AtomBox 遵循以下原则：

- dotnet 标准库优先：底层库优先选择使用现代化 .NET Standard 库。
- AtomUI 优先：UI 层优先使用 AtomUI 库提供的 UI 展示能力。
- AOT 优先：默认要尽量支持 AOT、trimming，可以通过 source generator、显式注册等方式优化性能减小体积。

## 3. 禁止行为列表

- 不使用早期的 freamework 库。
- 不引入 DDD 作为默认编程模型。
- 不使用 ReactiveUI。
- 不把 IObservable 作为状态、命令、路由和事件系统的主公共 API。
- 不把运行时反射扫描作为默认发现机制。

## 4. 执行流程

从运行流程这个维度看，AtomBox 的整体执行结构分为以下层次：

| 顺序 | 层 / 模块 | 职责 |
| --- | --- | --- |
| 0 | Core | Core 不是普通运行步骤，而是贯穿所有运行层的产品内核。它定义 AtomBox 的核心模型、接口、能力、错误和领域规则，例如 `RemoteItem`、`RemotePath`、`StorageAccount`、`IStorageProvider`、`StorageCapability`。 |
| 1 | Presentation / Desktop UI | 基于 Avalonia 和 AtomUI 展示界面，接收用户操作，维护 UI 状态，负责 View、ViewModel、Command、Binding 等桌面端交互逻辑。 |
| 2 | Application | 编排用户用例，把 UI 动作转换为应用流程，例如账号管理、连接测试、远程目录浏览、创建上传下载任务、触发删除或刷新。 |
| 3 | Transfer Engine | 管理传输任务生命周期，包括任务队列、并发调度、取消、重试、进度聚合、速度统计和任务状态更新；`Paused` / `Interrupted` 状态用于关闭、中断和后续恢复设计，第一版不承诺对外暂停/恢复调度能力。 |
| 4 | Provider Abstraction | 表达统一存储能力，定义 provider 需要暴露的最小公共行为和可选能力。该层的核心抽象通常定义在 Core 中。 |
| 5 | Provider Implementation | 适配具体存储后端，把阿里云 OSS、腾讯 COS、七牛 Kodo、又拍云 USS、华为云 OBS、百度智能云 BOS、京东云 OSS、青云 QingStor、火山引擎 TOS、FTP/SFTP/WebDAV、阿里云盘、百度网盘等 SDK/API 转换为 AtomBox 的统一模型和错误语义。 |
| 6 | External Systems | 真正的外部系统，包括云厂商 SDK、FTP/SFTP 服务、网盘 HTTP API、远程对象存储服务等。 |

从上至下的典型运行流程如下：

```text
Presentation / Desktop UI
  -> Application
    -> Transfer Engine
      -> Provider Abstraction
        -> Provider Implementation
          -> External Systems
```

例如，用户在上传页面触发上传时，运行流程可以表达为：

```text
UploadViewModel
  -> TransferAppService
    -> ITransferTaskScheduler
      -> Transfer Runtime
        -> IStorageAccountRepository
        -> IStorageProviderFactory
        -> IStorageProvider
          -> AliyunOssProvider
            -> Aliyun OSS SDK
```

传输任务创建和传输任务执行必须分开理解：

- Application 只负责创建传输任务描述，例如账号 ID、本地路径、远程路径、传输方向和用户选项。
- Transfer Engine 执行任务时，通过 Core 中的账号仓储和 provider factory 解析运行时所需的 `IStorageProvider`；凭据读取和 secret material 处理由 provider factory / provider 内部完成。
- Application 不持有长期 provider 实例，也不把 provider 实例塞进传输任务。

## 5. 依赖关系

运行流程描述的是“谁在运行时调用谁”，代码依赖关系描述的是“哪个模块在编译时引用哪个模块”。这两个维度不能混为一谈。AtomBox 的代码依赖必须围绕 Core 向内收敛，不能让 Core 反向依赖 UI、Provider、Infrastructure 等外层实现。

| 模块 | 可以依赖 | 不应该依赖 | 依赖原则 |
| --- | --- | --- | --- |
| AtomBox.Core | .NET BCL，以及极少数不污染领域模型的基础类型 | Avalonia、AtomUI、Serilog、具体云厂商 SDK、数据库、配置文件实现、HTTP API 实现 | Core 是产品内核，只定义 AtomBox 的核心模型、抽象、错误、能力和领域规则。 |
| AtomBox.Application | AtomBox.Core，`Microsoft.Extensions.DependencyInjection.Abstractions` | Avalonia、AtomUI、具体云厂商 SDK、具体协议库、ViewModel、View、`IServiceProvider` 直接解析 | Application 编排用户用例，只依赖核心抽象，不直接接触 UI 和具体后端实现；可提供 `IServiceCollection` 注册扩展供 Desktop 组合根调用。 |
| AtomBox.Transfer | AtomBox.Core，`Microsoft.Extensions.DependencyInjection.Abstractions` | Avalonia、AtomUI、Application、具体云厂商 SDK、具体 Provider 实现、`IServiceProvider` 直接解析 | Transfer Engine 负责调度传输任务，通过 Core 中的 provider 抽象执行实际读写；可提供 `IServiceCollection` 注册扩展供 Desktop 组合根调用。 |
| AtomBox.Providers | AtomBox.Core，具体云厂商 SDK、FTP/SFTP 协议库、网盘 API 客户端，`Microsoft.Extensions.DependencyInjection.Abstractions` | Avalonia、AtomUI、ViewModel、Desktop UI、`IServiceProvider` 直接解析 | Provider 实现 Core 中的存储抽象，把外部 SDK/API 类型转换为 Core 类型；可提供 `IServiceCollection` 注册扩展供 Desktop 组合根调用。 |
| AtomBox.Infrastructure | AtomBox.Core，必要的系统库、加密库、持久化库、日志库，`Microsoft.Extensions.DependencyInjection.Abstractions` | Avalonia、AtomUI、ViewModel、具体页面逻辑、`IServiceProvider` 直接解析 | Infrastructure 提供配置、凭据、日志、缓存、本地持久化等技术实现；可提供 `IServiceCollection` 注册扩展供 Desktop 组合根调用。 |
| AtomBox.Desktop | AtomBox.Application、AtomBox.Transfer、AtomBox.Providers、AtomBox.Infrastructure、AtomBox.Core | 不应被 Core、Application、Transfer、Providers、Infrastructure 反向依赖 | Desktop 是 Avalonia/AtomUI 启动项目和组合根，负责 UI、DI 注册和应用启动。 |

推荐的项目引用方向如下：

```text
AtomBox.Desktop
  -> AtomBox.Application
  -> AtomBox.Transfer
  -> AtomBox.Providers
  -> AtomBox.Infrastructure
  -> AtomBox.Core

AtomBox.Application
  -> AtomBox.Core
  -> Microsoft.Extensions.DependencyInjection.Abstractions

AtomBox.Transfer
  -> AtomBox.Core
  -> Microsoft.Extensions.DependencyInjection.Abstractions

AtomBox.Providers
  -> AtomBox.Core
  -> Microsoft.Extensions.DependencyInjection.Abstractions

AtomBox.Infrastructure
  -> AtomBox.Core
  -> Microsoft.Extensions.DependencyInjection.Abstractions
```

从依赖收敛角度看，Core 位于中心：

```text
AtomBox.Core
  <- AtomBox.Application
  <- AtomBox.Transfer
  <- AtomBox.Providers
  <- AtomBox.Infrastructure
  <- AtomBox.Desktop
```

强约束：

- Core 不依赖任何外层模块。
- Application 不依赖 Avalonia、AtomUI 或具体云厂商 SDK。
- Transfer 不依赖 Application，也不依赖具体 Provider 实现，只依赖 Core 中定义的 provider 抽象和传输端口。
- Provider 不依赖 Desktop，也不依赖 ViewModel。
- Infrastructure 不承载业务用例，只提供技术能力。
- Desktop 可以引用所有实现模块，但只作为组合根，不把 UI 类型泄漏到 Core、Application、Transfer、Provider 或 Infrastructure。
- Desktop 是生产组合根。Application 不是组合根，不能负责组装 Providers、Transfer、Infrastructure 或 Desktop UI。
- Application、Transfer、Providers、Infrastructure 可以暴露 `IServiceCollection` 注册扩展，供 Desktop 组合根调用；这些模块内部业务对象不得持有、注入或直接解析 `IServiceProvider`。
- 跨模块稳定端口统一定义在 Core，例如 provider factory、账号仓储、设置仓储、凭据存储、传输任务存储和传输调度端口。
- Providers、Infrastructure、Transfer 可以提供 Core 端口的实现；Application 只消费 Core 端口，不定义跨模块端口。
- ViewModel 不能直接消费 Transfer 运行时对象、Transfer 内部状态发布机制或传输队列；传输状态必须通过 Application 暴露的用例服务或查询结果进入 UI。

## 6. 项目目录规划

AtomBox 从零重建时采用 `src/` + `tests/` 的标准仓库结构。生产代码按物理项目拆分，测试代码按模块独立放置。不要把所有代码塞进一个桌面项目里，那是小工具写法，不是这个产品该走的路。

推荐目录结构如下：

```text
AtomBox/
  AtomBox.sln
  .gitignore
  Directory.Build.props
  Directory.Packages.props

  Docs/
    overview.md
    architecture/
      overview.md
    modules/
    ui/
    implementation/
      README.md
      roadmap.md
      phases/
      testing/
    test-reports/

  src/
    AtomBox.Desktop/
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

    AtomBox.Application/
      AtomBox.Application.csproj
      Accounts/
      Browsing/
      Transfers/
      Settings/
      DependencyInjection/

    AtomBox.Core/
      AtomBox.Core.csproj
      Accounts/
      Capabilities/
      Credentials/
      Errors/
      Providers/
      RemoteItems/
      Results/
      Settings/
      Transfers/
      ValueObjects/

    AtomBox.Transfer/
      AtomBox.Transfer.csproj
      Queue/
      Workers/
      Scheduling/
      Progress/
      Policies/
      Persistence/
      DependencyInjection/

    AtomBox.Providers/
      AtomBox.Providers.csproj
      Common/
      ObjectStorage/
      FileTransfer/
      NetDisk/
      DependencyInjection/

    AtomBox.Infrastructure/
      AtomBox.Infrastructure.csproj
      Configuration/
      Credentials/
      Logging/
      Storage/
      Caching/
      DependencyInjection/

  tests/
    AtomBox.Core.Tests/
    AtomBox.Application.Tests/
    AtomBox.Transfer.Tests/
    AtomBox.Providers.Tests/
    AtomBox.Infrastructure.Tests/
```

各项目职责如下：

| 项目 | 职责 |
| --- | --- |
| AtomBox.Desktop | Avalonia / AtomUI 启动项目，承载 Views、ViewModels、资源、桌面端交互逻辑和 DI 组合根。 |
| AtomBox.Application | 用户用例编排层，负责账号管理、远程浏览、连接测试、创建传输任务、触发删除或刷新等应用流程。 |
| AtomBox.Core | 产品内核，定义核心模型、接口、能力、错误、领域规则和值对象。 |
| AtomBox.Transfer | 独立传输调度器，负责任务队列、并发、取消、重试、进度聚合和任务状态更新；`Paused` / `Interrupted` 状态用于关闭、中断和后续恢复设计，第一版不承诺对外暂停/恢复调度能力。 |
| AtomBox.Providers | 具体后端适配层，第一版内部按 `ObjectStorage`、`FileTransfer`、`NetDisk` 分目录组织。 |
| AtomBox.Infrastructure | 技术实现层，负责配置、凭据、日志、本地持久化、缓存等基础能力。 |

资源目录约定：

- 现有 `Assets` 必须迁入 `src/AtomBox.Desktop/Assets/`。
- 图片、图标、封面等静态 UI 资源归 Desktop 层所有。
- 仓库根目录不长期保留 UI 资源目录，避免 Core、Providers、Infrastructure 的视野被 UI 资源污染。

项目拆分约定：

- 第一版直接创建 6 个生产项目：`Desktop`、`Application`、`Core`、`Transfer`、`Providers`、`Infrastructure`。
- `Transfer` 独立成 csproj，不塞进 Application，也绝不能塞进 Core。
- `Providers` 第一版不拆成多个 provider 项目，先在一个项目内按目录区分；当阿里云 OSS、SFTP、网盘等实现变重后，再拆成更细的 provider 项目。
- 测试项目先按模块规划目录，后续随模块实现逐步补齐。

## 7. 生命周期文档

AtomBox 的生命周期设计记录在 `Docs/architecture/lifecycle.md`。

本文件定义模块分层、运行流程、依赖关系和项目目录；生命周期文档定义业务对象、运行时服务、模块启动停止的存活边界。

实现阶段不能只看本文件就直接写 DI 注册。涉及 Singleton、Transient、后台服务、provider 创建、传输任务恢复、账号删除、凭据轮换、关闭保存等问题时，必须同时遵守生命周期文档。
