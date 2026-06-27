# Providers Registry 与 Factory 设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的 Providers Registry、Factory、provider 类型元数据、账号实例配置和运行时 provider 对象边界
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的设计边界，必须先说明原因，再同步更新相关设计文档。

## 1. 设计目标

`IStorageProviderRegistry` 和 `IStorageProviderFactory` 是两个不同问题的答案。

Registry 回答“AtomBox 支持哪些 provider 类型，以及这些 provider 类型有什么静态能力和配置要求”。

Factory 回答“给定一个用户账号配置和凭据引用，如何创建一个可以访问远端的 provider 实例”。

这两个抽象不能合并。合并以后，查询 provider 类型列表这种静态行为，会被迫接触账号、凭据、SDK client、连接状态，整个 Providers 模块会被自己写脏。

## 2. Registry 职责

`StorageProviderRegistry` 实现 Core 中的 `IStorageProviderRegistry`。

它负责：

- 返回 AtomBox 支持的 provider 类型列表。
- 按 `StorageProviderId` 查询 Core 中定义的 `ProviderDescriptor`。
- 提供 provider 类型显示名、分类、图标标识、能力声明。
- 对支持文件管理能力的 provider，静态声明 `CreateFolder`、`Rename`、`Move` 等能力，供 Application / Presentation 做操作可用性判断。
- 提供创建账号时需要的配置字段描述，例如 endpoint、region、bucket、root path、OAuth 授权类型。
- 对 FTP/SFTP/WebDAV 这类文件传输 provider，Registry 只声明 `endpoint`、`port`、`authMode`、`rootPath`、SFTP host key 策略、FTP 传输模式、WebDAV timeout 等静态配置字段，不保存用户名、密码或私钥内容。
- 给 Application 提供“如何生成配置 UI”的静态信息，再由 Application 组织为 Presentation 可消费的页面数据。

它禁止：

- 保存用户账号实例。
- 保存账号 endpoint、bucket、root path 等用户配置值。
- 保存 `CredentialRef`。
- 读取或缓存 secret material。
- 创建 `IStorageProvider`。
- 创建或持有 SDK client、FTP session、SFTP session、HTTP API client。
- 连接远程服务。
- 根据当前账号状态动态改变 AtomBox 的业务流程。

Registry 中保存的是 provider 类型元数据，不是 provider 运行时对象，也不是用户账号仓库。

## 3. Factory 职责

`StorageProviderFactory` 实现 Core 中的 `IStorageProviderFactory`。

它负责：

- 接收 Core 账号模型，例如 `StorageAccount`。
- 根据 `StorageAccount.ProviderId` 找到对应 provider 构造逻辑。
- 通过 Core 凭据端口解析 `CredentialRef`。
- 把账号配置和凭据材料转换为具体 SDK/API/协议 client 所需参数。
- 根据账号配置中的 `authMode` 选择认证方式，例如 SFTP 密码/私钥、FTP 匿名/用户名密码。
- 根据账号配置中的文件传输选项构造短生命周期协议 client，例如 SFTP host key policy、FTP passive/active、WebDAV endpoint URL 和 timeout。
- 创建短生命周期 `IStorageProvider` 实例。
- 在创建失败时返回 Core `OperationResult<T>` 和 Core 错误模型。

它禁止：

- 承担 provider 类型目录查询职责。
- 缓存 provider 实例。
- 缓存 secret material。
- 把 SDK 异常、SDK DTO、协议库对象返回给外层。
- 在应用启动阶段主动连接所有账号。
- 为 UI 渲染左侧菜单而创建 provider。
- 持有或直接调用 `IServiceProvider`。

Factory 是 provider 对象创建器，不是连接池，不是账号仓库，也不是后台守护进程。

## 4. 执行时机

Factory 只在真实远程操作开始时执行。

允许执行 Factory 的典型场景：

- Application 执行连接测试。
- Application 执行远程目录列表。
- Application 执行远程文件或目录删除。
- Transfer worker 开始执行某个上传或下载批次。

禁止执行 Factory 的典型场景：

- 应用启动。
- 注册 provider 类型。
- 展示左侧菜单。
- 展示 provider 类型列表。
- 展示账号列表。
- 创建 `TransferTask`。
- 查询传输队列。
- 查询传输历史。

严肃一点说：如果页面只是为了显示“支持阿里云 OSS、SFTP、百度网盘”，结果代码已经创建了 OSS client 或 SFTP session，这就是设计事故。

## 5. 三类数据边界

| 数据类型 | 示例 | 正确归属 | 生命周期 |
| --- | --- | --- | --- |
| provider 类型元数据 | `aliyun-oss`、`sftp`、`baidu-netdisk`、显示名、能力声明、配置字段 schema | `StorageProviderRegistry` | 应用级单例数据 |
| 账号实例配置 | 用户命名的账号、endpoint、region、bucket、root path、`CredentialRef` | Core `StorageAccount`，由账号仓储持久化 | 用户配置生命周期 |
| 运行时 provider 对象 | `AliyunOssProvider`、`SftpProvider`、`BaiduNetDiskProvider` | `StorageProviderFactory` 创建，调用方短期持有 | 短用例或传输批次生命周期 |

示例：

- `aliyun-oss` 这个 provider 类型存在于 Registry。
- 用户账号“公司生产 OSS”存在于 `StorageAccount`。
- 用“公司生产 OSS”执行一次远程列表时创建的 `AliyunOssProvider` 实例存在于短用例运行期间。

不能把“公司生产 OSS”的 endpoint 和 bucket 放进 Registry。Registry 只知道阿里云 OSS 这种 provider 类型需要 endpoint、region、bucket 等字段，不知道某个用户实际填了什么值。

## 6. 与 Application 的关系

Application 只消费 Core 端口：

- 需要展示可新增的 provider 类型时，Application 调用 `IStorageProviderRegistry`。
- 需要测试连接、列目录、删除远程项时，Application 调用 `IStorageProviderFactory` 创建短生命周期 provider。
- 需要创建传输任务时，Application 不创建 provider，只创建任务描述。

Application 不知道 `AliyunOssProvider`、`SftpProvider` 这些具体类型，也不知道任何 SDK 类型。

## 7. 与 Transfer 的关系

Transfer 不直接引用 Providers 项目。

Transfer 通过 Core 端口获得：

- `IStorageAccountRepository`
- `IStorageProviderFactory`
- `ITransferTaskStore`
- `ITransferStateStore`

Transfer worker 在执行传输批次时，按任务中的 `StorageAccountId` 取账号配置，再通过 Factory 创建 provider。

TransferTask 中只保存账号引用、远程路径、本地路径、方向和选项，不保存 provider 实例，不保存 SDK client，不保存 secret material。

## 8. 错误边界

Registry 错误通常是设计期或配置期错误，例如未知 provider id、重复注册、descriptor 不合法。

Factory 错误通常是运行期错误，例如账号配置缺失、凭据不存在、凭据过期、SDK client 创建失败。

Provider 操作错误通常是远程系统错误，例如鉴权失败、对象不存在、网络超时、服务限流。

这些错误都必须转换为 Core 错误模型和 `OperationResult<T>`。SDK 异常不能越过 Providers 边界。
