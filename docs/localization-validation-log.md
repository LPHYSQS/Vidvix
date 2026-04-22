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

## R5 · 2026-04-20 23:08

- 轮次范围：主窗口外壳与公共进度区迁移，仅处理 `Views/MainWindow.xaml`、`MainViewModel` 公共消息 / 进度链路、队列项状态与直接映射到主窗口的服务层提示，不扩展到 `trim`、`split-audio`、`merge`、`terminal`、`media-details` 的私有页面文案。
- 构建命令：`dotnet build .\Vidvix.sln -c Debug -v minimal`
- 构建结果：通过，`0` 警告，`0` 错误。
- 启动冒烟：启动 `bin/x64/Debug/net8.0-windows10.0.19041.0/Vidvix.exe` 后持续运行超过 `8` 秒，`MainWindowHandle` 为 `3016856`，进程保持响应，未出现黑屏或闪退；UI 自动化采样可见 `Vidvix`、`当前模块`、`视频处理`、`批量处理视频，支持提取轨道。`、`导入文件`、`导入文件夹`、`清空列表`。
- 资源验证：`Resources/Localization/zh-CN/main-window.json` 与 `Resources/Localization/en-US/main-window.json` 已补齐 `mainWindow.queue.*`、`mainWindow.settings.*`、`mainWindow.results.*`、`mainWindow.message.*`、`mainWindow.processingContext.*`、`mainWindow.transcoding.*`，并扩展 `mainWindow.toolbar.*`、`mainWindow.progress.*` 为主窗口完整壳层与公共进度区 key。
- 刷新链路验证：`MainViewModel.RefreshLocalizedTextProperties()` 已联动 `RefreshWorkspaceLocalization()`、`RefreshExecutionProgressLocalization()` 与队列项 `RefreshLocalization()`，保证主窗口外壳、当前进度摘要、队列项状态与输出目录文案在语言切换后按当前语言重建。
- 热切换验证：通过 UI 自动化采样确认运行态主窗口壳层文案可见；本轮启动冒烟时观察到中文壳层文本，前序采样中曾观察到英文 `Current module`；用户已确认“本质修改没有任何问题”。完整 UI 自动化往返脚本因控件定位间歇性失败未保留单次整链路日志，但未发现功能性回退、黑屏或崩溃。
- 备注：主窗口中与 `media-details` 浮层相关的私有文案仍留给 `R8`，裁剪模块私有交互文案留给 `R6`。

## R6 · 2026-04-21 13:26

- 轮次范围：裁剪模块迁移，仅处理 `Views/Controls/VideoTrimWorkspaceView.xaml`、`VideoTrimWorkspaceViewModel`、裁剪预览子流程与 `TrimWorkflowService` / `VideoTrimWorkflowService` 直接暴露到裁剪页的私有文案，不扩展到 `split-audio`、`merge`、`terminal`、`media-details`。
- 构建命令：`dotnet build .\Vidvix.sln -c Debug -v minimal`
- 构建结果：通过，`0` 警告，`0` 错误。
- 启动冒烟：启动 `bin/x64/Debug/net8.0-windows10.0.19041.0/Vidvix.exe` 后持续运行超过 `10` 秒，`MainWindowHandle` 为 `6227994`，进程保持响应，未出现黑屏、白屏或闪退。
- 资源验证：`Resources/Localization/zh-CN/trim.json` 与 `Resources/Localization/en-US/trim.json` 已补齐 `trim.editor.*`、`trim.placeholder.*`、`trim.preview.*`、`trim.selection.*`、`trim.settings.*`、`trim.mediaInfo.*`、`trim.status.*`、`trim.progress.*`、`trim.import.*`、`trim.export.*`、`trim.smartTrim.*`、`trim.log.*`。
- 刷新链路验证：`VideoTrimWorkspaceViewModel.RefreshLocalization()` 现已联动重建裁剪策略选项、媒体信息字段、预览覆盖文案与导出进度状态；`TrimWorkflowService` / `VideoTrimWorkflowService` 已直接注入 `ILocalizationService`，因此服务层导入 / 导出消息不会继续保留单语言缓存。
- 热切换验证：通过 UI 自动化在 `TrimWorkspaceToggle` 与 `SettingsLanguageComboBox` 上完成裁剪页占位文案往返采样，确认 `请导入文件或拖拽到此处开始裁剪 -> Import a file or drag it here to start trimming -> 请导入文件或拖拽到此处开始裁剪` 可在运行时即时刷新，无需重启应用。
- 回归补记：本轮顺带修复了裁剪导出进度实现中遗漏的 `ShowExportFinishingProgress()` 缺口，并为“空导入请求”补上安全拒绝消息，避免异常路径再次触发隐藏崩溃点。

## R7 · 2026-04-21 14:58

- 轮次范围：拆音模块迁移，仅处理 `Views/SplitAudioPage.xaml`、`SplitAudioWorkspaceViewModel`、拆音结果卡片 / 预览卡，以及 `AudioSeparationWorkflowService`、`SplitAudioExecutionCoordinator`、`DemucsExecutionPlanner`、`DemucsRuntimeService` 直接暴露到拆音页的私有文案，不扩展到 `terminal`、`media-details`、`merge`。
- 构建命令：`dotnet build .\Vidvix.sln -c Debug -v minimal`
- 构建结果：通过，`0` 警告，`0` 错误。
- 资源验证：`Resources/Localization/zh-CN/split-audio.json` 与 `Resources/Localization/en-US/split-audio.json` key 对齐校验通过，双语均为 `120` 个 key，未发现缺失项。
- 离线烟测：`powershell -ExecutionPolicy Bypass -File .\scripts\test-split-audio-offline.ps1 -RepoRoot .` 通过；CPU 音频烟测与 GPU 优先视频烟测均完成，日志最终输出 `Offline smoke regression passed.`。
- 启动冒烟：启动 `bin/x64/Debug/net8.0-windows10.0.19041.0/Vidvix.exe` 后持续运行超过 `10` 秒，`MainWindowHandle` 为 `20317592`，进程保持响应，未出现黑屏、白屏或闪退。
- 热切换验证：通过 UI Automation 在 `SplitAudioWorkspaceToggle` 与 `SettingsLanguageComboBox` 上完成拆音页和设置页往返采样，确认 `请导入文件或拖拽到此处 -> Import a file or drag it here -> 请导入文件或拖拽到此处`、`界面语言 -> Display language -> 界面语言`、`当前语言：简体中文 -> Current language: English (United States) -> 当前语言：简体中文` 可在运行时即时刷新，无需重启应用。
- 刷新链路验证：`SplitAudioWorkspaceViewModel.RefreshLocalization()` 已联动重建输入摘要、进度快照、结果卡片按钮与完成 / 失败状态；`SplitAudioExecutionCoordinator` 现通过 failure reason resolver 保持异常原因可重算，`AudioSeparationWorkflowService`、`DemucsExecutionPlanner`、`DemucsRuntimeService` 已直接注入 `ILocalizationService` 与本地化异常封装，因此运行中切换语言不会继续持有旧语言错误字符串。
- 回归补记：本轮顺带修复 `ApplicationPaths` 在 `dotnet xxx.dll` 框架依赖宿主下错误解析到 `dotnet.exe` 目录的问题；修复后离线拆音烟测能够稳定定位 Demucs 启动脚本与运行时资源。

## R8 · 2026-04-21 17:49

- 轮次范围：终端与媒体详情迁移，仅处理 `TerminalWorkspaceViewModel`、`TerminalOutputEntryViewModel`、`FFmpegTerminalService`、`MediaInfoService` 家族、`MainViewModel` 详情 / 复制 / 本地化接线，以及 `Views/MainWindow.xaml` 中媒体详情浮层与终端区的私有文案，不扩展到 `merge` 模块。
- 构建命令：`dotnet build .\Vidvix.sln -c Debug -v minimal`
- 构建结果：通过，`0` 警告，`0` 错误。
- 资源验证：`Resources/Localization/zh-CN/terminal.json` 与 `Resources/Localization/en-US/terminal.json` key 对齐校验通过，双语均为 `30` 个 key；`Resources/Localization/zh-CN/media-details.json` 与 `Resources/Localization/en-US/media-details.json` key 对齐校验通过，双语均为 `72` 个 key；终端与媒体详情代码引用扫描结果为 `102` 个 key 引用、`0` 个缺失。
- 运行态往返热切换烟测：一次性临时项目 `dotnet run -c Debug --project %TEMP%\VidvixR8Smoke\VidvixR8Smoke.csproj` 通过，确认终端已有输出项的 source / status / failure line 与媒体详情打开态的 section title / field label 在 `zh-CN -> en-US -> zh-CN` 往返切换下均可即时重算，无需重启应用。
- 启动冒烟：启动 `bin/x64/Debug/net8.0-windows10.0.19041.0/Vidvix.exe` 后持续运行，`MainWindowHandle` 为 `330182`，进程保持响应；前台截图 `C:\Users\30106\AppData\Local\Temp\vidvix-r8-foreground.png` 可见主窗口壳层与拆音页入口，未出现黑屏、白屏或闪退。
- 刷新链路验证：`TerminalWorkspaceViewModel.RefreshLocalization()` 已联动刷新现有 `OutputEntries`，`TerminalCommandExecutionResult` 现持有 failure reason resolver，因此“已拒绝 / 已取消 / 退出码 / 运行时不可用”等输出行不再锁定旧语言；`MediaInfoService` 以 probe 原始结果重建快照，`MainViewModel.RefreshMediaDetailsLocalization()` 与 `MediaDetailPanelViewModel.RefreshLocalization()` 会在详情浮层打开态下同步刷新节标题、字段标签和复制反馈。
- 回归补记：本轮额外修复终端结果模型只保存静态失败文本导致的热切换残留问题，并将取消结果重新落到输出流中；终端页与详情浮层在现有内容已渲染的状态下都能正确跟随语言切换刷新。

## R9 · 2026-04-22 09:54

- 轮次范围：合并模块状态层迁移，仅处理 `MergeViewModel` 模式切换提示、轨道空态、状态摘要、运行态进度 / 完成 / 失败消息与 `ApplicationConfiguration` 中的合并模式本地化前缀，不扩展到 `Views/MergePage.xaml` 的主界面层文案。
- 构建命令：`dotnet build .\Vidvix.sln -c Debug -v minimal`
- 构建结果：通过，`0` 警告，`0` 错误。
- 资源验证：`Resources/Localization/zh-CN/merge.json` 与 `Resources/Localization/en-US/merge.json` key 对齐校验通过，双语均为 `256` 个 key；`merge.mode.videoJoin`、`merge.mode.audioJoin`、`merge.mode.audioVideoCompose` 作为动态前缀的展开项也已在双语资源中补齐。
- 运行态往返热切换烟测：临时项目 `dotnet run -c Debug --project %TEMP%\VidvixR9Smoke\VidvixR9Smoke.csproj` 通过，确认 `TimelineHintText`、`AudioVideoComposeDurationSummaryText`、`AudioVideoComposeStrategySummaryText`、`AudioVideoComposeOutputDirectoryHintText`、`AudioVideoComposeOutputNameHintText` 与处理锁定 `StatusMessage` 在 `zh-CN -> en-US -> zh-CN` 往返切换下都能即时重算，无需重启应用。
- 启动冒烟：启动 `bin/x64/Debug/net8.0-windows10.0.19041.0/Vidvix.exe` 后持续运行超过 `10` 秒，`MainWindowHandle` 为 `24773294`，窗口标题为 `Vidvix`，`Responding = True`，未出现黑屏、白屏或闪退。
- 刷新链路验证：`ApplicationConfiguration.MergeModeProfiles` 已改用 `merge.mode.*` 前缀承接显示名、模式切换提示、时间线提示与空轨道文本；`MergeViewModel.RefreshLocalization()` 会重建模式摘要、音视频合成策略 / 时长摘要、输出提示与当前 `StatusMessage`，`SetStatusMessage()` / `LocalizedArgument()` / `ResolveMergeTranscodingMessage()` 则保证处理中锁定提示、转码回退说明和完成 / 失败消息在语言切换后不保留旧语言字符串。
- 回归补记：本轮把原先散落在 `common.mergeMode.*` 和硬编码中文中的合并状态层文案统一收敛到 `merge.json`，因此 `R10` 可以直接复用现有的 `merge.*` 资源与摘要刷新链路，只补界面层按钮、标签和说明文案。

## R10 · 2026-04-22 10:50

- 轮次范围：合并模块界面层迁移，仅处理 `Views/MergePage.xaml`、`Views/MergePage.xaml.cs` 与合并页直连的 UI 文本属性，不回头修改 `R9` 已稳定的状态 / 进度 resolver。
- 构建命令：`dotnet build .\Vidvix.sln -c Debug -v minimal`
- 构建结果：通过，`0` 警告，`0` 错误。
- 资源验证：`Resources/Localization/zh-CN/merge.json` 与 `Resources/Localization/en-US/merge.json` key 对齐校验通过，双语均为 `317` 个 key，`MISSING_IN_EN=0`、`MISSING_IN_ZH=0`。
- 界面层残留扫描：`Views/MergePage.xaml` 与 `Views/MergePage.xaml.cs` 的中文硬编码扫描结果为 `0`，本轮界面层主标题、按钮、Tooltip、占位符、拖拽提示与对话框关闭按钮均已转为语言 key 绑定。
- 热切换验证：临时项目 `dotnet run -c Debug --project %TEMP%\VidvixR10Smoke\VidvixR10Smoke.csproj` 通过，确认 `MediaLibrarySectionTitleText`、`OutputSettingsSectionTitleText`、`MixOriginalAudioHeaderText`、`MediaListDragDropCaptionText` 与 `InvalidTrackDialogCloseButtonText` 在 `zh-CN -> en-US -> zh-CN` 往返切换下均可即时重算，无需重启应用。
- 启动冒烟：启动 `bin/x64/Debug/net8.0-windows10.0.19041.0/Vidvix.exe` 后持续运行超过 `10` 秒，`MainWindowHandle` 为 `461484`，窗口标题为 `Vidvix`，`Responding = True`，未出现黑屏、白屏或闪退。
- 刷新链路验证：新增 `ViewModels/MergeViewModel.UiText.cs` 后，`MergeViewModel.RefreshLocalization()` 已会统一触发合并页界面属性的 `PropertyChanged`；`MergePage.xaml` 不再保留静态中文，也不需要再引入单独的页面级刷新总线。
- 回归补记：本轮把失效轨道对话框关闭按钮和拖拽导入 caption 也纳入了 `merge.dialog.*` / `merge.page.*`，因此 `R11` 可以把精力集中在全仓残余 fallback 中文、英语补齐与 key 收敛，而不是回头补漏合并页界面层。

## R11 · 2026-04-22 11:03

- 轮次范围：残余文案清理与英语补齐，优先处理主窗口导入 / 输出目录残余反馈、合并页素材卡 / 轨道卡条目文案，以及会在 `en-US` 下破坏媒体参数解析的字段标签耦合问题，不扩展到新的页面重构。
- 构建命令：`dotnet build .\Vidvix.sln -c Debug -v minimal`
- 构建结果：通过，`0` 警告，`0` 错误。
- 资源验证：`Resources/Localization/zh-CN/main-window.json` 与 `Resources/Localization/en-US/main-window.json` key 对齐校验通过，双语均为 `146` 个 key；`Resources/Localization/zh-CN/merge.json` 与 `Resources/Localization/en-US/merge.json` key 对齐校验通过，双语均为 `330` 个 key；未发现新增 `MISSING_IN_EN` 或 `MISSING_IN_ZH`。
- 运行态往返热切换烟测：临时项目 `dotnet run -c Debug --project %TEMP%\VidvixR11Smoke\VidvixR11Smoke.csproj` 通过，确认 `mainWindow.message.importBusy` / `mainWindow.message.outputDirectorySelected` 双语格式化正常，`MediaItem` / `TrackItem` 在 `RefreshLocalization()` 后会为 `DurationText`、`ResolutionText`、`TypeDisplayText`、`SummaryText`、`ResolutionDisplayText` 等属性重新发出通知，且在 `zh-CN -> en-US -> zh-CN` 往返切换下能即时切换为目标语言。
- 稳定 key 解析验证：同一份 `R11` 烟测额外验证了英文标签场景下的 `MediaDetailsSnapshot`，确认 `MergeMediaMetadataParser` 现可通过 `mediaDetails.field.resolution`、`mediaDetails.field.frameRate`、`mediaDetails.field.sampleRate`、`mediaDetails.field.audioBitrate` 等稳定 key 成功解析分辨率、帧率、采样率与码率；`128000 bits/s` 英文码率写法也可被正确识别。
- 启动冒烟：启动 `bin/x64/Debug/net8.0-windows10.0.19041.0/Vidvix.exe` 后持续运行超过 `12` 秒，`MainWindowHandle` 为 `1638822`，窗口标题为 `Vidvix`，`Responding = True`，未出现黑屏、白屏或闪退。
- 刷新链路验证：`MediaInfoService.Snapshot` 现会持久化 `MediaDetailField.Key`，`MergeMediaMetadataParser` 与 `VideoTrimWorkspaceViewModel` 已改为 key-first 解析；`MergeViewModel.RefreshLocalization()` 现会联动刷新现有 `MediaItem` / `TrackItem` 集合，因此合并页已有素材卡片和轨道卡在语言切换后不再保留旧语言摘要或未知值占位。
- 回归补记：本轮保留的少量中文仅限非 UI 的安全 fallback / 诊断文本，不影响 `en-US` 主要页面可用性；`R12` 只需在总回归阶段决定这些安全默认值是否继续保留，不应再回头拆改本轮已稳定的 key-first 解析与条目刷新方案。

## R12 · 2026-04-22 13:14

- 轮次范围：总回归、冻结与收尾，仅处理最终验证、离线 smoke 回归、文档冻结与后续 AI agent 快速定位说明，不再扩张本地化迁移范围。
- 构建命令：`dotnet build .\Vidvix.sln -c Debug -v minimal`
- 构建结果：通过，`0` 警告，`0` 错误。
- 测试构建回归：`dotnet build .\tests\SplitAudioOfflineSmoke\SplitAudioOfflineSmoke.csproj -c Debug -p:UseAppHost=false -v minimal` 通过，`0` 警告，`0` 错误；本轮同步修复了 `SplitAudioOfflineSmoke` 因 `MediaInfoService` 新增 `ILocalizationService` / `ILogger` 依赖而失配的问题，并为测试工程补上默认隔离 `OutDir` 与 `ProjectReference` 透传，避免 WinUI 引用工程在默认构建路径下出现 XAML / PRI 编译冲突。
- 离线烟测：`powershell -ExecutionPolicy Bypass -File .\scripts\test-split-audio-offline.ps1 -RepoRoot .` 通过；CPU 音频与 GPU 优先视频样例均成功导出四轨结果，日志最终输出 `Offline smoke regression passed.`。
- 资源验证：双语 key 对齐校验通过，`common.json=164`、`settings.json=42`、`main-window.json=146`、`trim.json=130`、`split-audio.json=120`、`merge.json=330`、`terminal.json=30`、`media-details.json=72`，均为 `MISSING_IN_EN=0`、`MISSING_IN_ZH=0`。
- 字符串盘点：`scripts/export-localization-inventory.ps1` 已重新导出 `docs/localization-string-inventory.csv`，最终记录数为 `1185`；热点仍集中在 `merge`、`main-window`、`split-audio`。
- 运行态往返热切换烟测：临时项目 `dotnet run -c Debug --project %TEMP%\VidvixR12Smoke-79b338f332ca4e94b81c12ff24a9f8f7\VidvixR12Smoke-79b338f332ca4e94b81c12ff24a9f8f7.csproj` 通过，确认主窗口设置入口、设置页语言标题、裁剪页占位文案、拆音页占位文案、合并页素材区标题、终端输入区标题与媒体详情节标题在 `zh-CN -> en-US -> zh-CN` 往返切换下均会即时重算。
- 启动冒烟：启动 `bin/x64/Debug/net8.0-windows10.0.19041.0/Vidvix.exe` 后持续运行超过 `12` 秒，`MainWindowHandle` 为 `5441680`，窗口标题为 `Vidvix`，`Responding = True`，未出现黑屏、白屏或闪退。
- 文档冻结：已新增 `docs/localization-agent-quick-reference.md`，用于后续 AI agent 精准定位资源文件、模块入口、刷新链路与新增语言接入步骤。
- 最终结论：本地化迁移 `R1` 至 `R12` 全部完成，设置页可切换语言，主窗口 / 裁剪 / 拆音 / 合并 / 终端 / 媒体详情均能在不重启的前提下完成热切换；允许保留的少量中文仅限非 UI 安全 fallback / 诊断文本，不影响 `en-US` 可用性。
