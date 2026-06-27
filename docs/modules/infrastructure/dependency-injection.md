# Infrastructure DI 设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的 Infrastructure DI 注册入口、服务生命周期、禁止注册对象和组合根边界
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的 DI 边界，必须先说明原因，再同步更新相关 Infrastructure、Architecture、Core 文档。

## 1. 模块定位

Infrastructure 可以提供 `IServiceCollection` 注册扩展，供 Desktop 生产组合根调用。

推荐入口：

```csharp
services.AddAtomBoxInfrastructure();
```

这个注册扩展只描述 Infrastructure 如何把 Core 端口绑定到本地技术实现。

它不是组合根。它不能构建 `IServiceProvider`，不能解析服务，不能启动 UI，也不能触发业务流程。

## 2. 组合根边界

Desktop 是唯一生产组合根。

正确关系：

```text
Desktop 组合根
  -> services.AddAtomBoxInfrastructure()
  -> services.BuildServiceProvider()
  -> 初始化日志、配置、凭据、本地状态
  -> 创建 Presentation Shell
```

Infrastructure 只能暴露注册扩展。

Infrastructure 内部业务对象禁止：

- 持有 `IServiceProvider`。
- 注入 `IServiceProvider`。
- 直接调用 `IServiceProvider.GetService(...)`。
- 在注册扩展里 `BuildServiceProvider()`。
- 通过容器解析结果决定业务流程。

如果某个对象需要创建其他对象，应使用显式工厂接口、具体工厂类或 `Func<T>`，并由 Desktop 组合根完成装配后传入。

## 3. 允许注册的服务

Infrastructure 可以注册 Core 端口实现：

- `IStorageAccountRepository`
- `IApplicationSettingsRepository`
- `ICredentialStore`
- `ITransferTaskStore`
- `ITransferStateStore`

Infrastructure 可以注册技术服务：

- `AppConfigurationStore`
- `ConfigurationMigration`
- `CredentialProtectionService`
- `LoggingInitializer`
- `MetadataCache`
- `ThumbnailCache`

Infrastructure 可以注册内部 mapper、serializer、path resolver、clock、file-system adapter 等无 UI 技术对象。

这些对象必须仍然遵守模块边界：不能依赖 Application、Transfer、Providers、Desktop、ViewModel、Avalonia 或 AtomUI。

## 4. 禁止注册的对象

Infrastructure 禁止注册：

- View / ViewModel / Command。
- Transfer worker、Transfer queue、Transfer manager。
- 具体 provider 实例。
- `IStorageProvider` 短生命周期运行对象。
- SDK client 或 provider session 的全局单例。
- plaintext secret。
- secret material holder。
- `CredentialLease` 的持久化业务对象。
- Application 用例服务。
- Desktop shell、window、dialog。

`CredentialLease` 可以作为运行时占用句柄由 Credential Store 创建和释放，但不能注册为应用级服务，也不能进入持久化模型。

## 5. 生命周期建议

| 服务 | 建议生命周期 | 说明 |
| --- | --- | --- |
| `IStorageAccountRepository` | Singleton | 应用级仓储服务；内部文件句柄、数据库连接、事务、锁必须按操作打开和释放。 |
| `IApplicationSettingsRepository` | Singleton | 应用级设置仓储；返回设置快照，不长期持有写入事务。 |
| `ITransferTaskStore` | Singleton | 应用级任务存储；不能持有 Transfer worker 或运行时队列。 |
| `ITransferStateStore` | Singleton | 应用级状态存储；只暴露可查询状态快照，不暴露 Transfer Runtime 内部对象。 |
| `ICredentialStore` | Singleton | 应用级凭据服务；不能缓存 plaintext secret。 |
| `CredentialProtectionService` | Singleton | 无状态或轻状态加密适配服务；不能持有 plaintext secret。 |
| `LoggingInitializer` | Singleton | 启动初始化对象，由 Desktop 协调初始化和关闭 flush。 |
| `MetadataCache` | Singleton | 应用级缓存服务；必须有容量、过期和账号边界。 |
| `ThumbnailCache` | Singleton | 应用级缓存服务；必须有容量、过期和账号边界。 |

Singleton 不等于全局资源常驻。

Repository / Store 可以是 Singleton，但文件句柄、数据库连接、事务、锁必须按操作创建和释放。Credential Store 可以是 Singleton，但 secret material 不能成为字段缓存。Cache 可以是 Singleton，但必须有容量和清理策略。

## 6. 启动与关闭

`AddAtomBoxInfrastructure()` 只注册服务，不执行重 IO 初始化。

允许在 Desktop 启动阶段显式执行：

- 配置目录检查。
- 配置 schema 校验。
- 配置迁移。
- 凭据服务可用性检查。
- 日志初始化。
- 本地状态存储可用性检查。

这些初始化动作由 Desktop 组合根协调。Infrastructure 应优先返回明确初始化结果或 Core 错误语义；不可恢复的底层异常必须在组合根边界被转换为启动错误结果。Infrastructure 不能自己弹 UI。

关闭阶段由 Desktop 协调：

- flush logs。
- flush 关键配置和传输状态。
- best-effort flush metadata / thumbnail cache。
- release Infrastructure resources。

## 7. 凭据 DI 边界

`ICredentialStore` 可以被注册为应用级服务。

但是：

- plaintext secret 不能注册进 DI。
- secret material 不能作为 options 对象注册。
- secret material 不能成为 Singleton 字段。
- secret material 不能进入日志、缓存、ViewModel、TransferTask 或普通配置文件。

provider factory / provider 操作链路可以在单次 Application 短用例或单次 Transfer 执行批次窗口内，通过 `ICredentialStore` 获取凭据材料和 `CredentialLease`。

操作窗口结束后必须释放 provider、释放 lease，并停止持有凭据材料引用。

## 8. 缓存 DI 边界

`MetadataCache`、`ThumbnailCache` 可以作为应用级缓存服务注册。

它们必须：

- 有容量上限。
- 有过期策略。
- 有账号边界。
- 不保存 secret material。
- 不保存 provider session。
- 不保存 Transfer worker。
- 不保存 ViewModel。
- 不作为业务事实唯一来源。

第一版缓存不定义跨模块公共端口。不要为了 UI 缩略图让 Presentation 直接依赖 Infrastructure cache。

## 9. 反模式

禁止：

```csharp
public sealed class StorageAccountRepository
{
    public StorageAccountRepository(IServiceProvider services)
    {
    }
}
```

禁止：

```csharp
public static IServiceCollection AddAtomBoxInfrastructure(this IServiceCollection services)
{
    var provider = services.BuildServiceProvider();
    var settings = provider.GetRequiredService<IApplicationSettingsRepository>();
    return services;
}
```

禁止：

```csharp
services.AddSingleton(secretMaterial);
services.AddSingleton<IStorageProvider, AliyunOssProvider>();
services.AddSingleton<TransferWorker>();
```

正确方向是显式依赖：

```csharp
public sealed class StorageAccountRepository
{
    public StorageAccountRepository(
        ILocalStoragePathResolver paths,
        IJsonSerializer serializer)
    {
    }
}
```

依赖是什么就写什么，不要拿 `IServiceProvider` 当万能钥匙。
