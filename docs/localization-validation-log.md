# Vidvix 本地化验证日志

## R1 · 2026-04-20 17:23

- 轮次范围：字符串盘点与任务分桶，仅新增扫描脚本、盘点产物与文档更新。
- 构建命令：`dotnet build .\Vidvix.sln -c Debug -v minimal`
- 构建结果：通过，`0` 警告，`0` 错误。
- 启动冒烟：启动 `bin/x64/Debug/net8.0-windows10.0.19041.0/Vidvix.exe` 后持续运行超过 `8` 秒，无立即退出。
- 盘点产物：`docs/localization-string-inventory.csv`，共 `1220` 条记录。
- 热点模块排序：`merge (426)`、`common (245)`、`main-window (201)`、`split-audio (135)`、`trim (78)`、`media-details (72)`、`terminal (40)`、`settings (23)`。
- 备注：`R1` 不要求热切换验证；`docs/localization-key-registry.md` 由 `R2` 按计划创建。

## R2 · 2026-04-20 17:37

- 轮次范围：本地化基础设施与资源目录，仅新增服务、模型、资源清单、语言包骨架与注册表，不接入 UI 热切换。
- 构建命令：`dotnet build .\Vidvix.sln -c Debug -v minimal`
- 构建结果：通过，`0` 警告，`0` 错误。
- 启动冒烟：启动 `bin/x64/Debug/net8.0-windows10.0.19041.0/Vidvix.exe` 后持续运行超过 `8` 秒；主窗口标题为 `Vidvix`，`MainWindowHandle` 非 `0`，进程保持响应。
- 资源验证：`Resources/Localization/manifest.json` 与 `zh-CN`、`en-US` 目录均已复制到输出目录；缺失资源按 `Services/LocalizationService.cs` 中的 `zh-CN` 回退策略处理。
- 本轮交付：`ILocalizationService`、`LocalizationService`、`CurrentUiLanguage` 偏好字段、`docs/localization-key-registry.md`、双语资源骨架。
- 热切换验证：`R2` 不适用，设置页语言切换与当前窗口刷新链路留给 `R3`。

## R3 · 2026-04-20 18:28

- 轮次范围：设置页语言切换与热切换打通，仅接入设置页语言入口、当前语言持久化、`LanguageChanged` 刷新链路，以及主窗口少量公共文本试点。
- 递归修复：修复 `ViewModels/MainViewModel.Localization.cs` 中 `SelectedLanguageOption` 与 `ComboBox.SelectedItem` 双向绑定反复写回导致的 `System.StackOverflowException`；修复后不再出现启动阶段白屏卡死后闪退。
- 下拉显示修复：将设置页语言下拉从 `SelectedItem` 对象选中改为 `SelectedValuePath="Code"` + `SelectedValue` 代码选中，解决 `zh-CN -> en-US -> zh-CN` 往返切换后 `ComboBox` 选中框显示空白的问题。
- 构建命令：`dotnet build .\Vidvix.sln -c Debug -v minimal`
- 构建结果：通过，`0` 警告，`0` 错误。
- 启动冒烟：启动 `bin/x64/Debug/net8.0-windows10.0.19041.0/Vidvix.exe` 后持续运行超过 `15` 秒，`MainWindowHandle` 非 `0`，进程保持响应，未复现白屏闪退。
- 热切换验证：通过临时专用 `DispatcherQueue` 烟测在运行时验证 `zh-CN -> en-US -> zh-CN` 往返切换；`SettingsPaneTitleText`、`WorkspaceHeaderCaption`、`MainWindowSettingsButtonLabel` 和 `CurrentUiLanguage` 偏好字段均在往返切换后回到预期值，刷新事件计数为 `3`。
- 资源验证：`bin/x64/Debug/net8.0-windows10.0.19041.0/Resources/Localization` 已包含 `common.json`、`settings.json`、`main-window.json` 中本轮新增 key。
- 备注：本轮主窗口仅试点页头 caption、设置按钮和处理进度标题，集中式配置显示文案留给 `R4`，整面主窗口 shell 留给 `R5`。

## R4 · 2026-04-20 19:28

- 轮次范围：集中式配置与公共显示文案迁移，仅处理 `ApplicationConfiguration` 及其派生的工作区配置、处理模式、输出格式描述、合并模式和拆音加速模式，不提前展开主窗口整面 shell 文案。
- 构建命令：`dotnet build .\Vidvix.sln -c Debug -v minimal`
- 构建结果：通过，`0` 警告，`0` 错误。
- 启动冒烟：启动 `bin/x64/Debug/net8.0-windows10.0.19041.0/Vidvix.exe` 后持续运行超过 `8` 秒，`MainWindowHandle` 为 `5638648`，进程保持响应，未出现黑屏或闪退。
- 热切换验证：通过临时独立控制台烟测验证 `ApplicationConfiguration + LocalizationService` 的 `zh-CN -> en-US -> zh-CN` 往返链路；`WorkspaceProfiles`、`SupportedProcessingModes`、`SupportedAudioOutputFormats`、`MergeModeProfiles`、`SupportedSplitAudioAccelerationModes` 在往返切换后均命中预期双语值。
- 刷新链路验证：`MainViewModel` 已在 `ApplyLocalizationState` 中重建本轮涉及的配置型工作区快照、处理模式和输出格式列表，并联动 `VideoTrimWorkspaceViewModel`、`MergeViewModel`、`SplitAudioWorkspaceViewModel` 刷新其配置型选项列表，避免语言切换后继续持有旧对象引用。
- 回归修复验证：主窗口输出格式下拉继续使用 `SelectedItem`，本轮补充保证 `MainViewModel.SelectedOutputFormat` 始终返回当前 `AvailableOutputFormats` 快照中的同一实例；通过 UI 自动化烟测验证视频工作区显示 `视频格式转换 / MP4`，音频工作区目标格式显示 `MP3`，首次启动不再为空白。
- 资源验证：`Resources/Localization/zh-CN/common.json` 与 `Resources/Localization/en-US/common.json` 已补齐 `common.workspace.*`、`common.processingMode.*`、`common.outputFormat.*`、`common.mergeMode.*`、`common.splitAudio.acceleration.*`。
- 备注：主窗口按钮标签、占位符、输出目录按钮等非集中式 shell 文案仍留给 `R5`；裁剪 / 拆音 / 合并页面内的私有交互文案仍分别留给 `R6`、`R7`、`R9` / `R10`。
