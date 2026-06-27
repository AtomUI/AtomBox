# Phase 06：Providers 第一条链路

> 文档状态：历史阶段归档
>
> 来源：本文从 docs/implementation/roadmap.md 原始 Phase 6 内容拆分而来。
>
> 说明：本文保留原阶段边界、任务和完成条件，不新增事后验收结论。

## 8. Phase 6：Providers 第一条链路

目标：先打通 provider 抽象链路，再接真实厂商。

允许做：

- 实现最小 fake / test provider。
- 实现 provider descriptor 注册。
- 实现 provider registry。
- 实现 provider factory 骨架。
- 返回固定或本地模拟的 `RemoteItem` 列表。

禁止做：

- 一上来接多个真实 provider。
- 让 provider 返回 SDK DTO。
- 让 provider 泄漏 SDK exception。
- 启动阶段连接远程服务。
- 把 provider 做成账号级全局单例。

完成条件：

- Application 可以通过 Core 的 provider factory 获取短生命周期 provider。
- Desktop 可以展示一组模拟远程文件。
- provider 创建、使用、释放链路可走通。
