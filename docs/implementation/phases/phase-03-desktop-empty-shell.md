# Phase 03：Desktop 空 Shell

> 文档状态：历史阶段归档
>
> 来源：本文从 docs/implementation/roadmap.md 原始 Phase 3 内容拆分而来。
>
> 说明：本文保留原阶段边界、任务和完成条件，不新增事后验收结论。

## 5. Phase 3：Desktop 空 Shell

目标：建立 Avalonia / AtomUI 可启动桌面壳。

允许做：

- 创建 Avalonia 启动文件。
- 接入 AtomUI 基础资源。
- 创建 Shell。
- 创建主布局骨架：
  - 顶栏
  - 左侧菜单
  - 内容区域
  - 底部状态栏
- 创建 Navigation 基础设施。
- 创建 ViewFactory / ViewLocator 基础设施。
- 创建空页面占位。

禁止做：

- 直接在 ViewModel 中访问 Infrastructure。
- 直接在 ViewModel 中访问 Transfer Runtime 内部对象。
- 直接在 ViewModel 中创建 provider。
- 使用 AtomUI DataGrid / Avalonia DataGrid 承载第一版业务列表。

完成条件：

- Desktop 可以启动空 Shell。
- 页面切换路径可用。
- View 和 ViewModel 创建边界符合 Presentation 文档。
