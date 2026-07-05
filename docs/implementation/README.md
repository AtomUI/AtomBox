# Implementation 文档入口

> 文档状态：第一版发布基线整理
>
> 目的：说明 `docs/implementation/` 的阅读顺序、目录职责和阶段文档组织方式。

`docs/implementation/` 记录 AtomBox 的工程实现路线、阶段边界、阶段执行细节和长期测试矩阵。

## 阅读顺序

1. 先读 `roadmap.md`，确认当前工程阶段、总体原则、跳步规则和发布基线状态。
2. 需要 v0.1 后专业化能力方向时，读 `professionalization-roadmap.md`。
3. 需要阶段细节时，进入 `phases/` 阅读对应 Phase 文档。
4. 需要测试覆盖范围、回归测试要求或真实 provider 验收规则时，进入 `testing/` 阅读测试矩阵。

## 目录职责

| 路径 | 职责 |
|---|---|
| `roadmap.md` | 实现路线总览、Phase 索引、测试矩阵索引、跳步规则和当前发布基线状态。 |
| `professionalization-roadmap.md` | v0.1 后专业化能力路线，记录除新增 Provider 外的产品能力方向、优先级和实施边界。 |
| `phases/` | Phase 0-11 的阶段细节、历史阶段归档和阶段验收说明。 |
| `testing/` | Provider、Transfer 等可长期复用的测试矩阵和回归测试规范。 |

## 维护规则

- 不在 `roadmap.md` 中继续堆叠大段阶段细节；新增阶段应先在 `roadmap.md` 建索引，再在 `phases/` 写详细文档。
- 测试矩阵优先放入 `testing/`，避免混入阶段路线文档。
- 历史阶段文档可以补充“发布基线说明”或“后续状态说明”，但不要删除原始阶段边界和完成条件。
- 调整文件位置时必须同步修正 `docs/` 下所有 Markdown 引用。
