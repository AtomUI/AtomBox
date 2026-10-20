# AtomBox.Infrastructure × AtomUI.City 迁移审计

> 状态：Decision Complete / No Migration
>
> Matrix ID：`AB-INFRASTRUCTURE × CITY-*`
>
> 实施分支：`feature/with-city`
>
> 决策时间：2026-10-14

## 结论

`AtomBox.Infrastructure` 本轮不采用任何 City 能力，不新增 City 项目引用，也不修改生产代码。Infrastructure 继续实现 Core 定义的配置、仓储、凭据、本地文件、缓存和日志端口；Desktop 仍是唯一生产组合根。

Desktop 的 `AtomBoxDesktopModule` 已在 City Host 初始化阶段调用 `InfrastructureInitializer`。这是 Desktop 对标准 .NET/Core 服务的组合根接线，不等于 Infrastructure 迁移到 City，也不要求 Infrastructure 依赖 City.Core。

## 能力对照

| City 能力 | 决策 | 原因 |
| --- | --- | --- |
| Core | `否` | 启动协调已经由 Desktop 组合根完成；Infrastructure 当前没有需要绑定 City scope 的后台任务或资源。 |
| Mvvm / Routing / Presentation / Localization | `否` | Infrastructure 不拥有 ViewModel、导航、视图或文化投影。 |
| State | `否` | Infrastructure 保存磁盘事实；运行时账号/设置投影归 Application，传输投影归 Transfer。City.State 不能取代持久化。 |
| EventBus | `否` | 仓储操作已有明确 `OperationResult`；额外事件通道会制造一致性和敏感信息风险。 |
| Data | `否` | City.Data 是远程通信管线，不适用于本地 JSON、文件、缓存和凭据仓储。 |
| Security | `否` | City.Security 的 Host 用户/access token 与 AtomBox 多外部存储账号的密码、私钥、AccessKey 和 lease 不是同一安全边界。 |
| PluginSystem | `否` | Infrastructure 当前没有动态基础设施贡献或卸载需求。 |
| Build / Generators / Testing / CLI / Templates | `否` | 工程能力统一归 Engineering 审计；生产 Infrastructure 和纯单元测试不反向依赖 City 工程包。 |

## 已识别的 Infrastructure 自有问题

以下问题是真实工程债，但不是 City 迁移问题，本轮只记录、不施工：

1. 部分 Repository/Store 的复合“读—改—写”没有覆盖整个事务，存在并发丢更新风险。
2. pending-delete 凭据在 lease 释放后没有可靠的物理清理和重启恢复流程。
3. AES-GCM 密文与原始密钥文件位于同一应用存储边界，无法防御能够读取两者的同用户进程。
4. `ConfigurationMigration` 只在版本文件缺失时写入 v1，没有校验现有版本、迁移或迁移前备份。
5. `LoggingInitializer` 只创建目录，`Flush()` 为空，尚未形成真实日志管线。
6. `MetadataCache` 与 `ThumbnailCache` 只创建目录，尚无容量、TTL、逐账号隔离和淘汰实现。

这些问题应按各自业务风险独立立项。不得为了修复它们而把 City.State、City.Security 或 City.EventBus 引入 Infrastructure。

## 证据与验收

- `AtomBox.Infrastructure` 当前只引用 `AtomBox.Core` 和 DI abstractions。
- Infrastructure 自动化基线：160/160 通过。
- Desktop City Host 集成测试：2/2 通过，证明组合根能够初始化 Infrastructure。
- 本审计不修改 City 或 Infrastructure 生产代码，不要求人工 UI 验收。
