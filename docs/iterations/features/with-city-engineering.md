# AtomBox Engineering × AtomUI.City 工程能力审计

> 状态：Automated
>
> Matrix ID：`AB-ENGINEERING × CITY-*`
>
> 实施分支：`feature/with-city`
>
> 决策时间：2026-10-14

## 结论

Engineering 负责测试、构建、发布、模板和开发工具。本轮保留并收口已经产生实际价值的 City.Build 与 City.Generators；测试中继续验证生产代码实际使用的 Core、Mvvm、Routing、Presentation、State 和 Localization 合同。EventBus、Data、Security、PluginSystem、City.Testing、CLI 和 Templates 均不采用。

## Build 与 Generators

Desktop 已通过 City.Build 把标准 RESX 编译为嵌入式 locpack，并通过 City.Generators 生成 Localization 和 Presentation registrar。现有工程只导入 `AtomUI.City.Build.props` 与单独的 Localization targets，属于局部接入。

本轮改为导入完整 `AtomUI.City.Build.targets`：

- 继续使用 vendored City submodule 中的 Build.Tasks 和 Generators 项目。
- 保持 RESX、locpack、generated registrar 和运行时行为不变。
- 获得完整的 Build property validation、diagnostics 和可选择启用的 manifest/AOT targets。
- 显式把 manifest 与 build diagnostics 指向 `MSBuildProjectExtensionsPath` 下的配置/目标框架隔离目录，避免构建产物进入源码树。
- `AtomUICityPackageApplication`、Strict AOT 和插件打包保持关闭；不替换 AtomBox 的 ZIP、DMG、AppImage 和签名流程。

## Release submodule 门禁

AtomBox 通过 git submodule 固定 `external/AtomUI.City`，生产项目又直接引用其中的项目。`actions/checkout@v4` 默认不检出 submodule，因此 Windows、macOS 和 Linux 三个主仓 checkout 必须显式设置 `submodules: recursive`；AtomUITools 的独立 checkout 不受影响。

## 不采用的工程能力

| City 能力 | 决策 | 原因 |
| --- | --- | --- |
| Testing | `否` | 现有测试会启动真实 AtomBox City Host；轻量 TestHost 不能替代该证据。Testing 包还会引入几乎全部 City 模块和 Roslyn，收益不足。 |
| CLI | `否` | `atomui city build/test` 无法替代签名、DMG、AppImage、多 RID 与真实后端门禁。 |
| Templates | `否` | 模板适用于创建新工程、模块或页面，不改善既有 AtomBox 的运行和发布。未来脚手架需求应独立审计。 |
| EventBus / Data / Security / PluginSystem | `否` | 这些运行时能力必须由实际业务所有者采用，Engineering 不建立第二套运行时实现。 |

## 非目标

- 不引入常规 push/pull-request CI；当前缺失作为独立工程改进记录。
- 不修改 AtomUI.City 源码或 submodule revision。
- 不启用 City application packaging 或 AOT 发布。
- 不修改 Providers、Infrastructure 或安装器业务逻辑。

## 验收

- Desktop 本地化和 City Host 集成测试通过。
- AtomBox 全解决方案 Release 测试、构建零失败、零警告。
- 完整 City.Build targets 已加载，Localization targets 没有重复导入。
- manifest、build diagnostics 与 locpack 均只写入 `.artifacts` 中间目录，不污染源码树。
- Release 的三个主仓 checkout 均递归检出 submodule。

2026-10-14 自动化结果：

- `AtomBox.Desktop.Tests`：32/32 通过；Release locpack 与 generated localization source 均正常生成。
- `AtomBox.Infrastructure.Tests`：160/160 通过，确认本轮未改变 Infrastructure 行为。
- AtomBox 全解决方案 Release：693/693 通过。
- `dotnet build AtomBox.slnx -c Release --no-restore -v:minimal`：0 warning / 0 error。
- Release workflow 中 Windows、macOS、Linux 三个主仓 checkout 均已设置 `submodules: recursive`；两个 AtomUITools checkout 保持独立配置。
