# AtomBox.Providers 模块设计

> 文档状态：第一版发布基线冻结
>
> 冻结时间：2026-06-22
>
> 冻结范围：当前文档定义的架构、模块边界、依赖关系、目录规划或实现约束
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的设计边界，必须先说明原因，再同步更新相关设计文档。

## 1. 模块定位

`AtomBox.Providers` 是 AtomBox 的具体存储后端适配层。

它负责把阿里云 OSS、腾讯 COS、七牛 Kodo、又拍云 USS、华为云 OBS、百度智能云 BOS、京东云 OSS、青云 QingStor、火山引擎 TOS、FTP/SFTP/WebDAV、阿里云盘、百度网盘等外部 SDK/API/协议转换为 AtomBox Core 中定义的统一模型、统一能力和统一错误语义。

第一版发布基线中，对象存储、SFTP、FTP、WebDAV 是主要可用路径；阿里云盘、百度网盘 provider 代码和注册已经存在，但未作为第一版充分验收主路径承诺。

Providers 不是 UI 层，也不是任务调度层。Provider 只负责“怎么和这个后端说话”，不负责“任务什么时候跑、失败怎么重试、UI 怎么显示”。

## 2. 边界规则

Providers 允许包含：

- 具体云厂商 SDK、FTP/SFTP 协议库、网盘 HTTP API 客户端。
- Provider 实现类，例如 OSS、SFTP、网盘 provider。
- SDK/API DTO 到 Core 模型的转换。
- SDK/API 异常到 Core 错误模型的转换。
- provider 能力声明，以及对外部传入的 Core provider options 进行解析。
- 面向 DI 的 `IServiceCollection` provider 注册扩展。

Providers 禁止包含：

- Avalonia、AtomUI、View、ViewModel、Command、Binding 等 UI 类型。
- 传输队列、worker、并发调度、重试调度等 Transfer Engine 实现。
- JSON 配置文件读写、数据库访问、凭据加密等 Infrastructure 实现。
- Application 用户用例编排。
- 读取配置文件或决定 provider 配置持久化格式。
- 把 SDK DTO 泄漏给 Application、Transfer 或 Desktop。
- 持有、注入或直接解析 `IServiceProvider`。

Providers 可以依赖外部 SDK，但必须把外部 SDK 的复杂性关在模块内部。SDK 类型一旦泄漏到外层，架构就已经开始烂。

## 3. 推荐目录

```text
src/AtomBox.Providers/
  AtomBox.Providers.csproj
  Common/
  ObjectStorage/
  FileTransfer/
  NetDisk/
  DependencyInjection/
```

## 4. 目录职责

| 目录 | 职责 |
| --- | --- |
| `Common/` | 保存 provider 通用转换、错误映射、能力声明辅助代码。 |
| `ObjectStorage/` | 保存对象存储 provider，例如阿里云 OSS、腾讯 COS、七牛 Kodo、又拍云 USS，以及华为云 OBS、百度智能云 BOS、京东云 OSS、青云 QingStor、火山引擎 TOS 等 S3 compatible 适配。 |
| `FileTransfer/` | 保存 FTP/SFTP/WebDAV provider。 |
| `NetDisk/` | 保存网盘 provider，例如阿里云盘、百度网盘；第一版作为实验/后续验收范围。 |
| `DependencyInjection/` | 定义 provider 注册扩展，例如 `AddAtomBoxProviders()`。 |

## 5. 首批核心对象范围

第一阶段 Providers 只建立 provider 分组和基础适配对象，不提前拆成多个 provider csproj。

通用对象：

- `StorageProviderFactory`
- `StorageProviderRegistry`
- `ProviderCapabilityMapper`
- `ProviderErrorMapper`
- `ProviderCredentialResolver`

Factory / Registry 分工：

- `StorageProviderRegistry` 实现 Core 的 `IStorageProviderRegistry`，负责登记 provider 类型、暴露 Core 中定义的 `ProviderDescriptor`、查询能力声明和配置表单描述，不创建 provider 实例。
- `StorageProviderFactory` 实现 Core 的 `IStorageProviderFactory`，负责根据账号、凭据引用和 provider 配置创建具体 `IStorageProvider` 实例，不承担 provider 目录查询职责。

必须严格区分三类数据：

| 类型 | 示例 | 归属 | 说明 |
| --- | --- | --- | --- |
| provider 类型元数据 | `aliyun-oss`、`sftp`、显示名、能力声明、账号配置字段描述 | `StorageProviderRegistry` | 描述 AtomBox 支持哪类后端。 |
| 账号实例配置 | 某个账号的名称、endpoint、region、bucket、`CredentialRef` | Core `StorageAccount`，由账号仓储持久化 | 描述用户配置的某个具体连接。 |
| 运行时 provider 对象 | `AliyunOssProvider`、`S3CompatibleProvider`、`SftpProvider` 实例 | `StorageProviderFactory` 创建，调用方短期持有 | 描述一次短用例或一次传输批次中真正访问远端的对象。具体云厂商 creator 可以创建同一个通用运行时 provider。 |

这三类东西混在一起，Providers 迟早会烂：Registry 会变成账号仓库，Factory 会变成全局连接池，Provider 会变成带状态的半单例。

对象存储：

- `AliyunOssProvider`
- `TencentCosProvider`
- `QiniuKodoProvider`
- `UpyunProvider`
- `S3CompatibleProvider`
  - `HuaweiObsProviderCreator`
  - `BaiduBosProviderCreator`
  - `JdCloudOssProviderCreator`
  - `QingStorProviderCreator`
  - `VolcengineTosProviderCreator`

文件传输：

- `FtpProvider`
- `SftpProvider`
- `WebDavProvider`

网盘：

- `AliyunDriveProvider`
- `BaiduNetDiskProvider`

第一版发布说明：

- OSS 主路径已覆盖阿里云 OSS、腾讯 COS、七牛 Kodo、又拍云 USS、华为云 OBS、火山引擎 TOS 等真实账号验证；百度 BOS、京东云 OSS、青云 QingStor 已接入但真实验收依赖用户后续提供账号。
- SFTP、FTP、WebDAV 已进入第一版文件传输主路径。
- FTPS、完整 OAuth 授权流程、网盘 token refresh 和更复杂网盘产品体验不作为第一版发布承诺。

注册入口：

- `ProviderServiceCollectionExtensions`

这些对象属于具体后端适配语言，不是 Core 模型，也不是 Application 用例对象。

## 6. 设计约束

- Provider 必须实现 Core 中定义的 provider 抽象，返回 Core 中定义的 `RemoteItem`、`RemotePath`、`StorageError` 等统一模型。
- Providers 提供 Core 中 `IStorageProviderFactory` / `IStorageProviderRegistry` 的实现，Application 只消费 Core 端口。
- Providers 必须在模块内部捕获 SDK/API/协议异常，并转换为 Core 中定义的 `StorageError` 或 `OperationResult<T>`；异常类型不得穿透到 Application、Transfer 或 Desktop。
- Providers 不定义跨模块公共模型。凡是需要被 Application、Transfer、Presentation 共同识别的对象、枚举、错误和端口，都必须先进入 Core。
- `ProviderDescriptor`、provider 能力模型、配置字段描述等跨模块稳定契约必须定义在 Core。Providers 只能创建、填充、注册这些 Core 类型的实例。
- Provider 不依赖 Desktop，不依赖 ViewModel，不依赖 Avalonia 或 AtomUI。
- Provider 不负责传输任务调度，不维护全局任务队列。
- Provider 不直接读取普通配置文件，不决定配置持久化格式，不持久化、不缓存、不跨生命周期保存 secret material；凭据材料只能在 Application 短用例或 Transfer 执行批次窗口内通过 Core 端口解析并临时使用。
- Provider 可以封装 SDK client/session，但必须明确连接释放边界。
- 第一版所有 provider 放在 `AtomBox.Providers` 一个项目中；只有当某个 provider 引入重 SDK、复杂 OAuth、依赖冲突或需要单独发布时，才拆成独立 csproj。
- SDK DTO、API response、协议库对象不能跨出 Providers 边界。

## 7. 生命周期约束

Provider 是短生命周期运行对象，不是账号对象本身。

- 默认按 Application 短用例或 Transfer 执行批次创建 provider，操作结束后释放。
- 第一版不做账号级 provider 缓存，不承诺 provider 线程安全。
- 并发通过创建多个 provider 实例实现，不能多个线程共享同一个 provider 实例。
- SDK client、协议 session、HTTP client wrapper 等具体资源由 provider 内部持有，并随 provider 一起释放。
- Provider 内部可以在一次批量传输中复用 SDK client/session，但不能把 provider 偷偷做成跨批次账号级全局单例。
- Provider 内部请求不是 provider 生命周期边界；OSS 分片、SFTP write、网盘 API 轮询都不能导致每个请求创建一个 provider。
- `IStorageProviderRegistry` 是应用级单例，只负责 provider 类型、描述、能力和注册信息，不创建 provider 实例。
- `IStorageProviderFactory` 是应用级单例，负责基于账号、凭据引用和 provider 配置创建短生命周期 provider 实例。
- Factory 和 Registry 不缓存 secret material，不缓存 provider 实例。
- Factory 的执行时机是远程操作真正开始时，例如连接测试、远程列表、远程删除、Transfer worker 开始执行某个传输批次时。
- Factory 不在应用启动、左侧菜单渲染、账号列表展示、TransferTask 创建、队列查询时执行。
- Providers 启动阶段只注册描述、能力和 factory 实现，不连接远程服务，不刷新 token，不启动后台线程。
- Providers 可以提供 `IServiceCollection` 注册扩展，供 Desktop 组合根调用；Providers 内部业务对象、provider、factory、registry 都不能持有或直接解析 `IServiceProvider`。

OAuth refresh 如后续需要，应发生在 provider factory / provider 操作链路中，并通过 Core 错误模型反馈失败。
