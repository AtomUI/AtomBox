# Phase 08：真实 Provider 扩展

> 文档状态：历史阶段归档
>
> 来源：本文从 docs/implementation/roadmap.md 原始 Phase 8 内容拆分而来。
>
> 说明：本文保留原阶段边界、任务和完成条件，不新增事后验收结论。

## 10. Phase 8：真实 Provider 扩展

目标：在最小闭环稳定后，逐步接入真实后端。

推荐顺序：

1. 阿里云 OSS。
2. FTP / SFTP。
3. 其他对象存储 provider。
4. 阿里云盘 API。
5. 百度云盘 API。

允许做：

- 引入具体 SDK / 协议库。
- 实现 provider 配置字段。
- 实现连接测试。
- 实现目录列表。
- 实现上传 / 下载 / 删除。
- 实现 provider 错误映射。

禁止做：

- 让 SDK 类型进入 Core / Application / Presentation。
- 让 SDK exception 穿透到外层。
- 让真实 provider 绕过 `IStorageProviderFactory`。
- 让真实 provider 自己启动后台 keepalive 或 token refresh 线程。

完成条件：

- 每接入一个真实 provider，都必须通过统一 Core 契约暴露。
- UI 不因 provider 类型不同而出现分叉式业务逻辑。
- 错误、能力、配置字段、远程对象模型保持统一。
