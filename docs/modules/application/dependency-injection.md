# AtomBox.Application DI 设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的 Application 第一版依赖注入使用规则。
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的依赖注入使用规则，必须先说明原因，再同步更新相关 Application 文档和生命周期文档。

本文档定义 `AtomBox.Application` 如何参与依赖注入。Application 可以提供注册扩展方法，但不能成为组合根。

## 1. 基本原则

Application 使用 `Microsoft.Extensions.DependencyInjection` 的抽象注册能力，但不直接创建 `ServiceProvider`。

Application 不持有 `IServiceProvider`。这是全局规则，不是 Application 专属规则。

## 2. 注册入口

Application 第一版提供：

```text
services.AddAtomBoxApplication()
```

该方法只注册 Application 自己的服务：

- `AccountAppService`
- `RemoteBrowserAppService`
- `TransferAppService`
- `SettingsAppService`

该方法不能：

- 创建 `ServiceProvider`。
- 创建主窗口。
- 启动后台服务。
- 连接远程 provider。
- 读取配置文件。
- 访问数据库。

## 3. 生命周期

Application Service 是应用级无状态服务，第一版推荐注册为 Singleton。

| 服务 | 推荐生命周期 | 说明 |
| --- | --- | --- |
| `AccountAppService` | Singleton | 账号用例编排，不持有运行时资源。 |
| `RemoteBrowserAppService` | Singleton | 远程浏览短用例编排，不缓存 provider。 |
| `TransferAppService` | Singleton | 传输任务用例入口，不持有 worker。 |
| `SettingsAppService` | Singleton | 设置读取和保存用例编排，不直接持久化。 |

这些服务虽然是 Singleton，但内部依赖的 repository、store、factory 不能因此长期占用文件句柄、数据库连接、网络连接或 secret material。

## 4. 依赖来源

Application Service 只依赖 Core 中定义的端口：

- `IStorageAccountRepository`
- `IApplicationSettingsRepository`
- `IStorageProviderFactory`
- `ITransferTaskScheduler`
- `ITransferTaskStore`
- `ITransferStateStore`

Application 不能依赖：

- Desktop 服务。
- ViewModel。
- Provider 具体实现。
- Infrastructure 具体实现。
- Transfer worker。
- `IServiceProvider`。

## 5. Provider Factory 使用边界

Application 可以在短用例中使用 `IStorageProviderFactory`：

- 连接测试。
- 目录列表。
- 能力探测。
- 远程对象删除。

Application 不能使用 `IStorageProviderFactory`：

- 为 Transfer 创建 provider。
- 缓存 provider。
- 把 provider 实例放入任务。
- 跨多个独立用户提交批次复用 provider。

## 6. 禁止事项

- Application 不创建 `ServiceProvider`。
- Application 不持有 `IServiceProvider`。
- Application 不注册 Core。
- Application 不注册 Desktop。
- Application 不启动 Transfer Runtime。
- `AddAtomBoxApplication()` 注册阶段不连接远程 provider；运行期短用例只能按 `IStorageProviderFactory` 使用边界创建短生命周期 provider。
- Application 不把 provider、SDK client、secret material 注册为 Singleton。
