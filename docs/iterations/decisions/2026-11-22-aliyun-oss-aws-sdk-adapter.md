# 阿里云 OSS 使用 AWS S3 SDK 与专用适配层

> 状态：Accepted / Implemented
>
> 日期：2026-11-22

## 背景

决策前，AtomBox 依赖 `Aliyun.OSS.SDK.NetCore` 实现阿里云 OSS。该 SDK 的主要传输接口不能接收 `CancellationToken`，导致运行中取消只能通过流和回调尽力中断，无法让 Transfer Runtime 稳定控制执行体退出。

阿里云 OSS 提供 S3 兼容 Endpoint，并明确支持 AtomBox 当前使用的 Bucket、Object 和 Multipart API。AWS 官方 .NET S3 SDK 的异步 API 可以接收 `CancellationToken`，项目也已通过通用 S3 Provider 使用该依赖。

与此同时，阿里云公开的 S3 兼容 API 清单没有承诺 Service 级 `ListBuckets`，OSS 与 S3 在 Endpoint、虚拟主机寻址、chunked encoding、校验和及签名上也存在差异。因此不能直接把阿里云账号当作普通通用 S3 账号使用。

## 决策

AtomBox 的阿里云 OSS Provider 采用以下组合：

1. 使用官方 NuGet `AWSSDK.S3` 作为主要 S3 数据访问实现。
2. 在 `AtomBox.Providers` 内建立阿里云 OSS 专用适配层，封装 Endpoint、凭据、寻址、请求策略、错误和取消语义。
3. S3 兼容 API 已覆盖的操作固定走 AWS SDK。
4. S3 兼容 API 未承诺的 `ListBuckets` 固定走原生 OSS HTTP API。
5. 原生 HTTP 请求使用项目内独立的 OSS V4 signer。
6. 请求发出后不进行 AWS/原生 HTTP 动态 fallback。
7. 完全移除 `Aliyun.OSS.SDK.NetCore` 依赖和旧实现，不保留双栈开关。

AWS SDK 必须作为不可修改的第三方依赖使用。禁止：

- fork 或 vendoring AWS SDK 源码。
- patch NuGet 包或复制 SDK 私有实现到项目中。
- 通过反射、私有 hook 或运行时替换改变 SDK 内部行为。
- 为阿里云维护 AtomBox 专属 AWS SDK 分支。

所有兼容差异只能通过 AWS SDK 的公共配置、公共 request 类型和 AtomBox 外层适配器解决。

## 固定边界

| 能力 | 路径 |
|---|---|
| `ListBuckets` | 原生 OSS HTTP + `OSS4-HMAC-SHA256` |
| `PutBucket` | AWS SDK |
| `ListObjectsV2` | AWS SDK |
| `PutObject` / `GetObject` / `DeleteObject` / `CopyObject` | AWS SDK |
| Multipart initiate/upload/complete/abort | AWS SDK |

AWS 路径使用阿里云 S3 兼容 Endpoint、虚拟主机寻址、禁用 `aws-chunked`，并把 checksum 策略限制为 `WHEN_REQUIRED`。原生 HTTP 路径使用对应地域的原生 OSS Endpoint。

Provider 对外继续表现为 `aliyun-oss`，不得把 AWS SDK 类型、S3 兼容差异或原生 OSS DTO 泄漏到 Core、Transfer、Application 或 Desktop。

## 影响

正面影响：

- 上传、下载和 Multipart 可以把 Runtime token 传入真实异步网络调用。
- 阿里云与通用 S3 可以复用成熟的对象存储运行时行为。
- 无需自行实现绝大多数对象存储协议、AWS Signature V4 和重试管线。
- 阿里云专用兼容规则集中在单一适配层，边界清晰。

成本和风险：

- 项目需要维护一个最小 OSS V4 signer 和原生 `ListBuckets` 客户端。
- AWS SDK 升级可能改变默认 checksum、chunked encoding 或 endpoint 行为，升级时必须运行阿里云专项门禁。
- 阿里云 S3 兼容范围变化需要显式评估并修改固定路由，不能依赖运行时猜测。
- 原生 HTTP 与 AWS SDK 的异常需要统一映射，避免用户看到两套错误语义。

## 替代方案

### 继续使用阿里云官方 SDK

拒绝。现有 SDK 无法把 `CancellationToken` 贯彻到主要传输请求，不能满足本次取消目标。可取消流和进度回调只能继续提供尽力取消。

### 修改或维护阿里云 SDK / AWS SDK 分支

拒绝。维护第三方 SDK 分支会持续承担协议、安全、兼容和升级成本，也违反本项目“不修改 AWS SDK”的依赖原则。

### 完全基于 OSS 原生 HTTP API 自研

拒绝作为当前方案。它可以获得最大的控制力，但需要自行承担所有 Bucket、Object、Multipart、签名、重试、流传输、错误解析和兼容测试，成本与风险明显高于只补充确认缺口。

### 直接把阿里云配置成通用 S3 Provider

拒绝。通用 S3 当前面向路径寻址服务，而 OSS 要求虚拟主机寻址；阿里云还需要专用 Endpoint、`aws-chunked`、checksum、原生 `ListBuckets` 和错误映射规则。

### AWS 请求失败后自动改走原生 HTTP

拒绝。自动 fallback 会重复执行非幂等操作，模糊鉴权和配置错误，并使取消、日志和故障诊断不可预测。每个操作必须只有一条静态路由。

## 关联需求

- [阿里云 OSS 传输精确取消与 SDK 迁移](../features/aliyun-oss-precise-cancellation.md)
- [对象存储 AWS SDK 适配层平台决策](2026-11-22-object-storage-aws-sdk-adapter-platform.md)
