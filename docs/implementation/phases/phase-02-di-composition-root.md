# Phase 02：DI 与组合根边界

> 文档状态：历史阶段归档
>
> 来源：本文从 docs/implementation/roadmap.md 原始 Phase 2 内容拆分而来。
>
> 说明：本文保留原阶段边界、任务和完成条件，不新增事后验收结论。

## 4. Phase 2：DI 与组合根边界

目标：建立各模块注册扩展和 Desktop 组合根骨架。

允许做：

- Application 暴露 `AddAtomBoxApplication()`。
- Transfer 暴露 `AddAtomBoxTransfer()`。
- Providers 暴露 `AddAtomBoxProviders()`。
- Infrastructure 暴露 `AddAtomBoxInfrastructure()`。
- Desktop 创建组合根代码。
- Desktop 调用各模块注册扩展。
- Desktop 构建唯一生产 `IServiceProvider`。

禁止做：

- 在 Application、Transfer、Providers、Infrastructure 内部调用 `BuildServiceProvider()`。
- 把 `IServiceProvider` 注入普通业务对象、ViewModel、Provider、Repository、Store、Transfer worker。
- 把 provider、SDK client、secret material、Transfer worker 注册成全局单例。

完成条件：

- Desktop 是唯一生产组合根。
- 各模块只描述服务注册关系，不负责解析服务。
- DI 规则与生命周期文档一致。
