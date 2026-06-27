# Phase 00：工程骨架

> 文档状态：历史阶段归档
>
> 来源：本文从 docs/implementation/roadmap.md 原始 Phase 0 内容拆分而来。
>
> 说明：本文保留原阶段边界、任务和完成条件，不新增事后验收结论。

## 2. Phase 0：工程骨架

目标：建立可编译的解决方案结构和项目依赖方向。

允许做：

- 创建 `AtomBox.sln`。
- 创建 `src/` 和 `tests/`。
- 创建 6 个生产项目：
  - `AtomBox.Core`
  - `AtomBox.Application`
  - `AtomBox.Transfer`
  - `AtomBox.Providers`
  - `AtomBox.Infrastructure`
  - `AtomBox.Desktop`
- 创建 `Directory.Build.props`。
- 创建 `Directory.Packages.props`。
- 建立项目引用关系。
- 创建各项目内的基础目录。

禁止做：

- 写具体 OSS / FTP / SFTP / 网盘业务。
- 写复杂 ViewModel。
- 写真实凭据加密实现。
- 写 Transfer worker 业务逻辑。
- 引入具体云厂商 SDK。

完成条件：

- 解决方案可以 restore / build。
- 项目引用方向符合架构文档。
- `Core` 不依赖任何外层模块。
- `Desktop` 是唯一启动项目。
