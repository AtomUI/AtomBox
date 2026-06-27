# Bug: CredentialStore 在 AcquireLeaseAsync 中存在 TOCTOU 竞态条件

## 基本信息

- 标题：CredentialStore.AcquireLeaseAsync TOCTOU 竞态条件
- 发现方式：代码审查
- 日期：2026-06-17
- 严重程度：高
- 当前状态：已修复，已测试
- 修复日期：2026-06-19

## 问题描述

`AcquireLeaseAsync` 存在经典的检查时间/使用时间（TOCTOU）竞态条件：

```
TOC（检查点）: 第 62 行 await ExistsAsync(credentialRef, cancellationToken)
    ↓ 另一线程在此窗口期调用 MarkPendingDeleteAsync
TOU（使用点）: 第 73 行 CreateLease(credentialRef)
```

**正常路径：**
1. `AcquireLeaseAsync` 调用 `ExistsAsync` 确认凭据存在且未被标记为 pending-delete
2. `CreateLease` 在内存中创建租约

**竞争路径：**
1. `AcquireLeaseAsync` 调用 `ExistsAsync`，返回 true（凭据正常）
2. 另一线程调用 `MarkPendingDeleteAsync`，将凭据标记为 PendingDelete=true 并写回文件
3. `AcquireLeaseAsync` 调用 `CreateLease`，成功创建租约
4. **结果：一个已被标记为 pending-delete 的凭据获得了有效租约**

## 受影响代码

- **文件：** `src/AtomBox.Infrastructure/Credentials/CredentialStore.cs`
- **方法：** `AcquireLeaseAsync`、`AcquireMaterialAsync`、`MarkPendingDeleteAsync`
- **原竞争窗口：** `AcquireLeaseAsync` 的可用性检查返回后到 `CreateLease` 前；`AcquireMaterialAsync` 的凭据材料读取后到 `CreateLease` 前。

## 受影响方法详情

### AcquireLeaseAsync（第 58-74 行）

```csharp
public async Task<Result<Unit>> AcquireLeaseAsync(CredentialRef credentialRef, CancellationToken cancellationToken)
{
    // TOC: 读文件判断凭据是否存在且未被标记删除
    var exists = await ExistsAsync(credentialRef, cancellationToken);
    if (!exists)
        return Result<Unit>.Failure(...);

    // ⚠ 竞态窗口：此时另一线程可调用 MarkPendingDeleteAsync

    // TOU: 内存操作创建租约
    return CreateLease(credentialRef);
}
```

### MarkPendingDeleteAsync（第 119-144 行）

```csharp
public async Task<Result<Unit>> MarkPendingDeleteAsync(CredentialRef credentialRef, ...)
{
    // 1. 读文件 — ReadCredentialsAsync（第 123 行）
    // 2. 设 PendingDelete=true（第 139 行）
    // 3. 写回文件 — _store.WriteAsync（第 143 行）
    //
    // 在 AcquireLeaseAsync 的 ExistsAsync 和 CreateLease 之间
    // 执行上述三步即可使 ExistsAsync 的结果失效
}
```

### 同模式存在于 AcquireMaterialAsync（第 76-101 行）

`AcquireMaterialAsync` 虽然将存在性检查与 pending-delete 过滤放在了同一次文件读取中（第 80 行），但从文件读取到 `CreateLease`（第 100 行）之间仍存在 stale-read 窗口。

## 修复结果

已在 `CredentialStore` 内新增凭据级异步门闩，将以下操作放入同一临界区：

- 凭据索引读取
- `PendingDelete` 判断
- 凭据材料解密
- 租约创建
- pending-delete 标记写回

`_leaseGate` 继续只负责 `_activeLeases` 内存计数；凭据状态与租约授予的一致性由新的异步门闩保护。

## 测试覆盖

已新增以下测试：

- `AcquireLease_PendingDelete_ReturnsNotFound`
- `AcquireLease_DoesNotInterleavePendingDeleteBeforeLeaseCreation`
- `AcquireMaterial_DoesNotInterleavePendingDeleteBeforeLeaseCreation`

验证命令：

```powershell
dotnet test tests\AtomBox.Infrastructure.Tests\AtomBox.Infrastructure.Tests.csproj --no-restore
```

验证结果：147 passed / 0 failed / 0 skipped。
