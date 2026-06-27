# Phase 05：Application 用例骨架

> 文档状态：历史阶段归档
>
> 来源：本文从 docs/implementation/roadmap.md 原始 Phase 5 内容拆分而来。
>
> 说明：本文保留原阶段边界、任务和完成条件，不新增事后验收结论。

## 7. Phase 5：Application 用例骨架

目标：建立用户用例编排服务，但不写复杂业务。

允许做：

- 创建账号管理用例服务。
- 创建远程浏览用例服务。
- 创建传输任务创建用例服务。
- 创建设置读取保存用例服务。
- 返回 `OperationResult<T>`。
- 使用 Core 端口编排仓储、provider factory、transfer scheduler。

禁止做：

- Application 直接引用 Avalonia / AtomUI。
- Application 直接引用具体 SDK。
- Application 直接 new 具体 provider。
- Application 持有 provider、SDK client、secret material、ViewModel。
- Application 管理 `CredentialLease` 计数或旧凭据物理清理。

完成条件：

- Presentation 可以通过 Application 服务调用用例。
- Application 只消费 Core 端口。
- 用例结果可以被 UI 展示。
