# Phase 04：Infrastructure 最小本地实现

> 文档状态：历史阶段归档
>
> 来源：本文从 docs/implementation/roadmap.md 原始 Phase 4 内容拆分而来。
>
> 说明：本文保留原阶段边界、任务和完成条件，不新增事后验收结论。

## 6. Phase 4：Infrastructure 最小本地实现

目标：实现最小可用的本地技术能力。

允许做：

- 实现应用配置目录解析。
- 实现设置仓储骨架。
- 实现账号仓储骨架。
- 实现传输任务存储骨架。
- 实现传输状态存储骨架。
- 实现凭据存储接口骨架。
- 实现日志初始化骨架。
- 实现本地 schema version / migration 骨架。

禁止做：

- 把 secret material 写入普通配置。
- 让 Infrastructure 弹 UI。
- 让 Infrastructure 调用 Application 用例。
- 让 Infrastructure 引用 Transfer、Providers、Desktop。
- 实现后台缓存清理线程。

完成条件：

- Infrastructure 只依赖 Core。
- 仓储和 store 可通过 Core 端口被调用。
- 启动初始化失败可以返回结构化错误。
