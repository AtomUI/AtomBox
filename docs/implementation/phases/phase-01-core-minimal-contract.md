# Phase 01：Core 最小契约

> 文档状态：历史阶段归档
>
> 来源：本文从 docs/implementation/roadmap.md 原始 Phase 1 内容拆分而来。
>
> 说明：本文保留原阶段边界、任务和完成条件，不新增事后验收结论。

## 3. Phase 1：Core 最小契约

目标：落地跨模块稳定模型、值对象、错误和端口。

允许做：

- 定义 `OperationResult<T>`。
- 定义错误模型和错误分类。
- 定义账号模型和值对象：
  - `StorageAccount`
  - `StorageAccountId`
  - `StorageProviderId`
  - `CredentialRef`
- 定义路径和值对象：
  - `RemotePath`
  - `LocalPath`
- 定义远程对象模型：
  - `RemoteItem`
  - `RemoteItemKind`
- 定义 provider 契约：
  - `IStorageProvider`
  - `IStorageProviderFactory`
  - `IStorageProviderRegistry`
  - provider descriptor / capability 相关模型
- 定义凭据、账号、设置、传输任务相关端口。

禁止做：

- 在 Core 中引用 Avalonia、AtomUI、Serilog、数据库库、云厂商 SDK。
- 在 Core 中实现平台加密、文件 IO、网络 IO。
- 在 Core 中写具体 provider。

完成条件：

- Core 可以独立 build。
- 端口边界与 `docs/modules/core/` 一致。
- Application、Transfer、Providers、Infrastructure 后续可以只通过 Core 契约协作。
