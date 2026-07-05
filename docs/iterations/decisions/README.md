# Decisions 文档入口

> 文档状态：长期决策记录入口
>
> 创建时间：2026-07-05

本目录记录影响 AtomBox 长期维护、跨平台能力、外部依赖、架构边界或产品体验的设计决策。

普通功能细节优先放入 `../features/`。只有当某个选择会影响后续多个版本或带来明显维护成本时，才需要新增 decision 文档。

## 建议命名

- `YYYY-MM-DD-short-topic.md`
- 示例：`2026-07-05-office-preview-policy.md`

## 建议结构

```md
# 决策标题

> 状态：Accepted / Superseded
> 日期：YYYY-MM-DD

## 背景

## 决策

## 影响

## 替代方案
```
