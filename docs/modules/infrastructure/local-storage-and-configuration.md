# Infrastructure 本地持久化与配置设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-15
>
> 冻结范围：当前文档定义的本地配置、账号仓储、设置仓储、传输任务存储、状态存储、迁移、损坏处理边界和 Phase 9 启动诊断规则
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的设计边界，必须先说明原因，再同步更新相关 Infrastructure、Core、Application、Transfer 文档。

## 1. 模块定位

Infrastructure 负责把 Core 端口映射到本地持久化实现。

它可以选择 JSON、SQLite、LiteDB、文件目录、平台应用数据目录等技术方案，但这些技术选择不能污染 Core 模型、Application 结果对象、Transfer 任务对象或 Presentation 状态。

Infrastructure 是仓库和存储实现，不是业务流程控制器。

## 2. 存储对象边界

| 存储对象 | Core 端口 | 说明 |
| --- | --- | --- |
| 账号配置 | `IStorageAccountRepository` | 保存 `StorageAccount` 事实，不保存 secret material。 |
| 应用设置 | `IApplicationSettingsRepository` | 保存应用设置快照，不保存 UI 临时状态。 |
| 传输任务 | `ITransferTaskStore` | 保存任务事实，例如账号 ID、本地路径、远程路径、方向、选项和状态。 |
| 传输状态 | `ITransferStateStore` | 保存或查询状态快照，供 Application 组织队列和历史结果。 |
| 凭据引用 | `IStorageAccountRepository` / `ICredentialStore` 协作 | 账号只保存 `CredentialRef`，secret payload 由 Credential Store 管。 |

本地持久化模型可以与 Core 模型分离。需要持久化优化字段时，必须通过 mapper 转换，不能把数据库表结构反向塞进 Core。

## 3. 配置文件边界

普通配置文件可以保存：

- 应用设置。
- 账号非敏感配置。
- `CredentialRef`。
- 本地缓存索引。
- 传输任务事实。
- schema version。

普通配置文件禁止保存：

- AccessKey。
- client secret。
- password。
- private key。
- refresh token。
- access token。
- SDK client 序列化对象。
- provider session。
- ViewModel 状态。

如果某个字段会让远端系统通过认证，它就是 secret material，不要装傻把它塞进 JSON。

## 4. 写入与一致性

本地写入必须避免半写入破坏用户数据。

第一版最低要求：

- 写入关键配置时使用临时文件加原子替换，或使用数据库事务。
- 迁移前保留备份。
- 配置损坏时不静默覆盖。
- 运行期传输任务状态写入失败必须返回错误，由 Application 组织结果，再由 Desktop / Presentation 给出可见反馈；关闭阶段写入失败只能进入关闭错误处理和日志，不能重新启动 Application 用例。
- 关闭阶段尽力 flush 账号配置、凭据引用、传输任务状态。

关键状态：

- 账号配置。
- 凭据引用。
- 传输任务事实。
- 传输任务最终状态。

普通缓存：

- thumbnail cache。
- metadata cache。
- 目录浏览临时缓存。

关键状态不能按普通缓存处理。

## 5. 迁移策略

所有本地持久化格式必须有 schema version。

迁移规则：

- 迁移逻辑属于 Infrastructure。
- 迁移失败不能静默吞掉。
- 迁移前必须保留可恢复备份。
- 迁移结果必须可被 Desktop 启动错误界面呈现。
- Core 不知道迁移细节。
- Application 不负责迁移文件格式。

迁移不是业务用例。不要把“修配置文件”写成 Application 用户流程，除非未来明确设计了用户可交互的修复向导。

## 6. 启动失败

如果配置损坏、凭据服务不可用、本地状态存储不可用，Infrastructure 应返回明确初始化错误。

Infrastructure 不能自己弹 UI。

正确路径：

```text
Desktop 启动
  -> 注册 Infrastructure / Providers / Transfer / Application 服务
  -> BuildServiceProvider
  -> 初始化 / 校验 Infrastructure
  -> Infrastructure 返回初始化结果
  -> Desktop 决定进入 Main Shell 或启动错误界面
```

Application 不负责启动错误界面。

Phase 9 初始化错误至少区分：

- 配置文件不存在且无法创建。
- 配置文件损坏或 schema 不兼容。
- 迁移失败。
- 凭据存储不可用。
- 传输任务或状态存储不可用。
- 日志目录不可写。
- 未知 Infrastructure 初始化错误。

初始化错误可以携带诊断路径，例如配置目录、备份文件路径、日志文件路径，但不得携带 secret material。恢复入口第一版可以是打开目录、查看错误详情、退出应用或重试初始化；不要求实现复杂修复向导。

## 7. 第一版约束

第一版可以采用简单本地文件方案，但必须保留迁移、备份、原子写入和敏感信息边界。

第一版暂不要求：

- 复杂数据库。
- 多进程并发写入。
- 云同步。
- 本地全文索引。
- 长期远程目录缓存。

但不要因为第一版简单，就把普通 JSON 写成没有版本、没有备份、没有错误处理的玩具。
