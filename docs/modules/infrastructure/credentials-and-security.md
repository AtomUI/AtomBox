# Infrastructure 凭据与安全设计

> 文档状态：阶段性冻结
>
> 冻结时间：2026-06-14
>
> 冻结范围：当前文档定义的凭据存储、凭据引用、secret material、CredentialLease、凭据轮换和敏感信息处理边界
>
> 变更规则：实现阶段不得随意修改本文件；如需调整本文档定义的设计边界，必须先说明原因，再同步更新相关 Infrastructure、Core、Providers、Transfer 文档。

## 1. 模块定位

Infrastructure 负责实现 Core 中定义的凭据端口。

它可以接触平台加密 API、本地安全存储、凭据文件、keychain、DPAPI、credential manager 等技术细节，但不能把这些技术细节泄漏给 Core、Application、Transfer、Providers 或 Presentation。

凭据系统只解决“如何安全保存、读取、轮换和清理 secret material”。它不决定用户何时添加账号、何时测试连接、何时上传下载，也不弹 UI。

## 2. 核心概念

| 概念 | 归属 | 说明 |
| --- | --- | --- |
| `CredentialRef` | Core | 凭据引用，不是凭据明文。可以进入 `StorageAccount` 和脱敏日志。 |
| secret material | Infrastructure 存储，provider 操作窗口内临时使用 | AccessKey、client secret、password、private key、refresh token、access token 等真实认证材料。 |
| `CredentialLease` | Core 定义抽象，Infrastructure 实现运行时机制 | 表示某个 worker、provider 或 provider session 正在使用某个 `CredentialRef`。 |
| pending-delete | Infrastructure 实现状态 | 表示凭据已被新凭据替换，但还不能物理删除。 |

严禁把 `CredentialRef` 当 secret material，也严禁把 secret material 塞进 `StorageAccount`、`TransferTask`、ViewModel、日志或普通配置文件。

## 3. Credential Store 职责

`CredentialStore` 实现 Core 中定义的 `ICredentialStore`。

它负责：

- 保存 secret material。
- 读取 provider factory / provider 操作窗口所需的凭据材料。
- 轮换凭据并返回新的 `CredentialRef`。
- 标记旧 `CredentialRef` 为 pending-delete。
- 维护 `CredentialLease` 或等价占用计数。
- 在没有活动 lease 后物理清理旧凭据。
- 使用平台安全能力或加密文件保护凭据载荷。

它禁止：

- 缓存 plaintext secret。
- 把 secret material 写入普通配置文件。
- 把 secret material 写入日志。
- 把 secret material 放进异常 message。
- 把 secret material 暴露给 Application、Transfer 或 Presentation。
- 自己触发 UI 提示、弹窗或账号管理流程。

## 4. 凭据读取窗口

secret material 的合法存活边界是单次 Application 短用例或单次 Transfer 执行批次所需窗口。

典型链路：

```text
Application 短用例 / Transfer worker
  -> IStorageProviderFactory
    -> ICredentialStore.Acquire(...)
      -> CredentialLease
      -> secret material
    -> 创建短生命周期 provider
    -> provider 完成远程操作
    -> 释放 provider
    -> 释放 CredentialLease
```

Credential Store 可以把凭据材料交给 provider factory / provider 操作链路，但不能让 Application、Transfer、ViewModel 直接读取或持有。

实现上不得把“释放引用”等同于“密钥安全清除”。第一版至少必须保证 secret material 不被日志、普通配置、缓存、任务状态和 UI 状态捕获。

## 5. 凭据轮换

凭据轮换流程：

```text
用户提交新凭据
  -> Application 账号用例
    -> 调用 Core 端口完成凭据轮换与账号 CredentialRef 更新
      -> ICredentialStore 保存受保护的新 secret payload
      -> ICredentialStore 返回新的 CredentialRef
      -> IStorageAccountRepository 更新账号 CredentialRef
      -> ICredentialStore 将旧 CredentialRef 标记为 pending-delete
      -> ICredentialStore 在旧 CredentialRef 无活动 lease 后物理清理旧 secret payload
```

Application 只编排“用户要替换账号凭据”这个用例事实，不管理 `CredentialLease` 计数，不等待 lease 释放，不直接执行旧凭据物理清理。

lease 释放等待、pending-delete 状态维护、并发锁和旧 secret payload 物理清理都属于 Infrastructure 中 `ICredentialStore` 的实现责任。凭据轮换用例可以在账号引用切换成功后返回；旧凭据清理可以在后续安全时机完成。

运行中的任务使用启动时形成的执行快照。账号凭据轮换不应破坏已经运行中的 provider session。

旧凭据不能因为账号已经切到新 `CredentialRef` 就立刻删除。否则长传输、SFTP session、OSS 分片上传、网盘 refresh 过程都可能被硬切断。

## 6. 错误边界

Credential Store 错误必须转换为 Core 统一错误模型。

典型错误：

- 凭据不存在。
- 凭据已被标记删除。
- 凭据解密失败。
- 平台凭据服务不可用。
- 凭据格式不兼容。
- 凭据仍被活动 lease 占用，暂不能物理删除。

错误结果可以包含错误分类、脱敏 credential id、操作类型，但不能包含 secret material。

## 7. 第一版约束

第一版必须做到：

- 普通配置文件不保存 secret 明文。
- 日志不保存 secret 明文。
- `StorageAccount` 只保存 `CredentialRef`。
- `TransferTask` 不保存 secret material。
- `CredentialLease` 或等价机制存在。
- 凭据轮换后旧凭据延迟清理。

第一版暂不追求：

- 硬件安全模块。
- 跨设备凭据同步。
- 多用户系统级凭据共享。
- 后台自动 token refresh。
- 完全意义上的内存安全擦除。
