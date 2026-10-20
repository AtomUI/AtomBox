# AtomBox.Core × AtomUI.City 迁移审计

> 状态：Automated
>
> 审计日期：2026-10-13
>
> 统领文档：[AtomBox × AtomUI.City 迁移主矩阵](with-city-migration-matrix.md)

## 结论

`AtomBox.Core` 是 AtomBox 的领域模型与稳定端口程序集，不是 City Host 运行时模块。Core 不直接采用 City.Core、Mvvm、Routing、Presentation、State、EventBus、Localization、Data、Security、PluginSystem 或 City 工程模块，也不通过同名概念建立反向依赖。

City 能力应由拥有运行时状态、生命周期、并发、恢复或 UI 责任的 Application、Transfer、Providers、Infrastructure、Desktop、Engineering 模块采用。AtomBox.Core 继续只使用 .NET BCL，并为这些上层模块提供稳定产品合同。

## 不可变集合缺陷

审计发现，部分 Core 模型虽然通过 `IReadOnlyDictionary` 或 `IReadOnlyList` 暴露集合，实际却保存或返回普通可变集合。调用方可以修改原始集合，或把属性返回值向下转换为可变类型后旁路修改。

这会破坏 Core 快照语义，并可能使已经发布到 City.State 的 Application 状态在没有 `Set`、版本变化和订阅通知的情况下发生变化。缺陷归属 Core 模型，不应由 Application 或 Desktop 复制数据来掩盖，也不需要 Core 引用 City。

本轮修复以下 Core 自有集合：

- `StorageAccount.ProviderConfig`：构造时规范化、复制并冻结。
- `CredentialSecretMaterial.Values`：校验、复制并冻结。
- `ProviderDescriptor.ConfigFields`：复制并封装为不可修改集合。
- `RemoteItemPage.Items`：复制并封装为不可修改集合。

端口方法返回的 `IReadOnlyList<T>` 仍由具体实现负责提供稳定快照，将在所属业务模块审计时验证。

## 兼容性

- 四个公共属性的声明类型保持不变。
- StorageAccount 的 JSON 格式保持不变。
- AtomBox.Core 不新增任何 City、Avalonia、DI、日志或 SDK 依赖。
- 有意改变的行为只有：调用方不能再修改 Core 模型已经持有并公开的集合。

## 其他审计发现

以下问题已记录但不在本轮不可变集合修复中处理：

- `CredentialSecretMaterial` 的实际公共合同与现有凭据文档边界存在矛盾，需要独立架构决策。
- Core 模型上的 `[JsonConstructor]` 使领域模型感知 JSON 技术细节。
- 当前没有自动化项目依赖边界门禁；该能力归 Engineering，而不是 Core 或 City.Testing 生产引用。

后续施工如触及上述问题，必须先更新正式设计并形成单独决策。

## 验收

- 修改构造参数中的原始集合，不得影响已经构造的 Core 模型。
- 通过 Core 属性取得的集合不得被修改。
- Application 账号状态中嵌套的 ProviderConfig 不得被旁路修改。
- Core、Application、Providers 及整库自动测试通过，Release 构建无警告和错误。

自动化结果：

- Core 测试：58/58 通过。
- Application 测试：163/163 通过。
- Providers 测试：171/171 通过。
- 整库测试：682/682 通过。
- Release 构建：0 警告、0 错误。
