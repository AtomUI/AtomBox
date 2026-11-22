# 对象存储精确取消与 SDK 收敛

> 状态：Implemented
>
> 日期：2026-11-22
>
> 影响模块：Providers / Desktop（账号迁移）/ Transfer（验收）

## 业务问题

AtomBox 过去分别依赖阿里云、腾讯云、七牛、华为云、百度云和火山引擎的厂商 SDK，并为又拍云维护同步原生 HTTP 客户端。多数实现不能把 `CancellationToken` 传入或贯穿实际数据请求，导致用户点击取消后任务可能长期停留在 `Canceling`，甚至继续上传并最终成功。

本次迭代只处理取消，不引入暂停、恢复或断点续传。业务完成语义保持不变：用户取消后，执行体必须退出，任务收敛到 `Canceled`，从活动队列移除并进入传输历史。Multipart 取消后应尝试 Abort，但清理失败不能把取消改写为失败或让任务永久停留在 `Canceling`。

## 业务范围

纳入统一迁移：

- 阿里云 OSS
- 腾讯云 COS
- 七牛云 Kodo
- 又拍云 USS
- 华为云 OBS
- 百度智能云 BOS
- 京东云 OSS
- 火山引擎 TOS

青云 QingStor 明确后置：本次不迁移、不删除现有实现、不承诺精确取消增强。通用 `s3-compatible` Provider 继续使用 AWS SDK。

## 验收要求

- 八家供应商的上传、下载、列表、删除、复制和 Multipart 网络请求使用 AWS SDK 的异步 API，并接收 Runtime 提供的同一个 token。
- 普通上传的源流、Multipart 本地分片读取和下载响应复制都必须观察同一个 token。
- `PutObject`、`UploadPart`、`GetObject`、`CopyObject`、`CopyPart`、`CompleteMultipartUpload` 等调用不得退回同步厂商 SDK。
- Multipart 取消或失败时使用独立、最长 10 秒的清理 token 调用 Abort；不得用已经取消的业务 token，也不得无限等待。
- Provider 声明上传/下载为 `Asynchronous` 且支持 cooperative cancellation；不虚报 active abort。
- 保持原 Provider ID、账号和传输历史引用稳定。
- 不修改、fork、patch、vendoring 或反射介入 AWS SDK。
- 不在 AWS 请求失败后动态 fallback 到厂商 SDK 或另一套协议实现。
- 厂商 SDK NuGet 与生产代码引用全部删除。

## 账号迁移

除又拍云外，既有 AK/SK 字段继续使用 `accessKeyId` 和 `accessKeySecret`；可选临时令牌使用 `securityToken` 或 `sessionToken`。

又拍云原“操作员名称/操作员密码”不能作为 S3 凭据复用。账号记录、Provider ID 和历史保留，但用户必须编辑账号，填写又拍云 S3 API 独立生成的 AccessKey/SecretAccessKey。保存后账号配置写入 `credentialMode=s3`；没有该标记的旧账号拒绝连接，并给出迁移提示，禁止把旧操作员密码误发给 S3 Endpoint。

## 完成结果

- `AWSSDK.S3` 成为对象存储唯一厂商级 SDK 依赖。
- 八家 Creator 通过供应商 Profile 创建 `AwsSdkS3CompatibleClient` 和通用 `S3CompatibleProvider`。
- 阿里云只有 Service 级 `ListBuckets` 使用原生 OSS HTTP + `OSS4-HMAC-SHA256`；其余操作固定走 AWS SDK。
- 青云继续使用原 `S3CompatibleAwsV4Client`，且该客户端不再被其他供应商复用。
- 已移除 `Aliyun.OSS.SDK.NetCore`、`Tencent.QCloud.Cos.Sdk`、`Qiniu`、`HuaweiCloud.Obs`、`BceSdkDotNetCore`、`Volcengine.TOS.SDK.NetCore` 及其间接遗留的 `log4net`、`Newtonsoft.Json` 直接引用。

## 关联设计

- [对象存储 AWS SDK 适配层平台决策](../decisions/2026-11-22-object-storage-aws-sdk-adapter-platform.md)
- [阿里云 OSS 使用 AWS S3 SDK 与专用适配层](../decisions/2026-11-22-aliyun-oss-aws-sdk-adapter.md)
- [阿里云 OSS 传输精确取消与 SDK 迁移](aliyun-oss-precise-cancellation.md)
