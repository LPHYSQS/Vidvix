# Vidvix 本地化 Key 注册表

## 维护约定

- 所有新 key 必须先登记到本文件，再进入代码或语言包。
- key 使用 `{module}.{area}.{element}.{state}`，仅允许英文小写与点分层。
- 同一语义只保留一个主 key；近义重复 key 在 `R11` 统一收敛。
- 动态文本统一使用占位符，不允许继续拼接硬编码中文。

## 资源文件与前缀映射

| 资源文件 | 允许前缀 | 说明 | 首次建立轮次 |
| --- | --- | --- | --- |
| `common.json` | `common.*` | 跨模块复用文案、语言名称、通用按钮与提示 | `R2` |
| `settings.json` | `settings.*` | 设置页、偏好页、语言切换入口 | `R2` |
| `main-window.json` | `mainWindow.*` | 主窗口外壳、工具栏、页头、常驻状态区 | `R2` |
| `trim.json` | `trim.*` | 裁剪模块 | `R2` |
| `split-audio.json` | `splitAudio.*` | 拆音模块 | `R2` |
| `merge.json` | `merge.*` | 合并模块 | `R2` |
| `terminal.json` | `terminal.*` | 终端模块 | `R2` |
| `media-details.json` | `mediaDetails.*` | 媒体详情面板与浮层 | `R2` |

## 已注册前缀

| 前缀 | 资源文件 | 用途 | 首次建立轮次 |
| --- | --- | --- | --- |
| `common.language.*` | `common.json` | 语言显示名与语言列表项 | `R2` |
| `common.action.*` | `common.json` | 通用操作文案（关闭、应用设置） | `R3` |
| `common.toggle.*` | `common.json` | 通用开关状态文案（开 / 关） | `R3` |
| `common.workspace.*` | `common.json` | 集中式工作区标题、描述、导入提示、空态与批量导入反馈 | `R4` |
| `common.processingMode.*` | `common.json` | 主工作区集中式处理模式名称与说明 | `R4` |
| `common.outputFormat.*` | `common.json` | 视频 / 裁剪 / 音频 / 字幕输出格式说明 | `R4` |
| `common.mergeMode.*` | `common.json` | 合并模式名称、时间线提示与空轨提示 | `R4` |
| `common.splitAudio.acceleration.*` | `common.json` | 拆音加速模式名称与说明 | `R4` |
| `settings.language.*` | `settings.json` | 设置页语言切换试点文案 | `R2` |
| `settings.pane.*` | `settings.json` | 设置页标题与说明 | `R3` |
| `settings.appearance.*` | `settings.json` | 外观与主题选项文案 | `R3` |
| `settings.systemTray.*` | `settings.json` | 系统托盘设置与反馈状态 | `R3` |
| `settings.desktopShortcut.*` | `settings.json` | 桌面快捷方式区与反馈状态 | `R3` |
| `settings.processingBehavior.*` | `settings.json` | 处理完成行为与说明 | `R3` |
| `settings.transcoding.*` | `settings.json` | 转码策略、GPU 加速与说明 | `R3` |
| `mainWindow.title.*` | `main-window.json` | 主窗口与应用标题试点文案 | `R2` |
| `mainWindow.header.*` | `main-window.json` | 主窗口页头 caption | `R3` |
| `mainWindow.toolbar.*` | `main-window.json` | 主窗口工具栏按钮与主操作 | `R3` |
| `mainWindow.progress.*` | `main-window.json` | 主窗口公共进度区标题、摘要、当前项与细节状态 | `R3` |
| `mainWindow.queue.*` | `main-window.json` | 主窗口待处理队列标题、摘要、队列项动作与状态 | `R5` |
| `mainWindow.settings.*` | `main-window.json` | 主窗口处理设置区、输出目录与选择器文案 | `R5` |
| `mainWindow.results.*` | `main-window.json` | 主窗口处理结果区标题 | `R5` |
| `mainWindow.message.*` | `main-window.json` | 主窗口常驻状态、批量结果、异常与反馈消息 | `R5` |
| `mainWindow.processingContext.*` | `main-window.json` | 主窗口处理前预检与模式上下文提示 | `R5` |
| `mainWindow.transcoding.*` | `main-window.json` | 主窗口转码策略、GPU 检测与回退说明 | `R5` |
| `trim.editor.*` | `trim.json` | 裁剪页标题、说明与当前文件 caption | `R6` |
| `trim.placeholder.*` | `trim.json` | 裁剪页空态、导入按钮与导入失败详情 | `R6` |
| `trim.preview.*` | `trim.json` | 预览覆盖层、播放控制、时间轴与音量提示 | `R6` |
| `trim.selection.*` | `trim.json` | 裁剪区间标签、自动化名称、摘要与规则说明 | `R6` |
| `trim.settings.*` | `trim.json` | 裁剪输出设置、目录按钮与计划输出路径 | `R6` |
| `trim.mediaInfo.*` | `trim.json` | 裁剪媒体信息分区、字段名与未知值兜底 | `R6` |
| `trim.status.*` | `trim.json` | 裁剪导入、导出、取消、异常与运行态状态消息 | `R6` |
| `trim.progress.*` | `trim.json` | 裁剪导出进度摘要、百分比与明细状态 | `R6` |
| `trim.import.*` | `trim.json` | 裁剪导入拒绝原因、失败原因与异常兜底 | `R6` |
| `trim.export.*` | `trim.json` | 裁剪导出兼容性、失败兜底与转码说明 | `R6` |
| `trim.smartTrim.*` | `trim.json` | smart trim 结果与回退原因 | `R6` |
| `trim.log.*` | `trim.json` | 裁剪预览加载与边界预热日志文本 | `R6` |
| `splitAudio.action.*` | `split-audio.json` | 拆音页主操作按钮与结果区动作 | `R7` |
| `splitAudio.common.*` | `split-audio.json` | 拆音模块内部通用分隔符与共享显示片段 | `R7` |
| `splitAudio.input.*` | `split-audio.json` | 拆音页导入区标题、空态、输入摘要与文件元信息 | `R7` |
| `splitAudio.settings.*` | `split-audio.json` | 拆音页设置区标题、输出目录、加速模式与状态标签 | `R7` |
| `splitAudio.results.*` | `split-audio.json` | 拆音结果区标题、说明、空态与清空动作 | `R7` |
| `splitAudio.preview.*` | `split-audio.json` | 拆音预览按钮、不可预览提示与卡片交互文案 | `R7` |
| `splitAudio.status.*` | `split-audio.json` | 拆音页运行态状态、导入反馈、完成 / 失败 / 取消消息 | `R7` |
| `splitAudio.validation.*` | `split-audio.json` | 拆音输入合法性校验消息 | `R7` |
| `splitAudio.stem.*` | `split-audio.json` | 四轨 stem 名称 | `R7` |
| `splitAudio.picker.*` | `split-audio.json` | 输出目录选择器标题 | `R7` |
| `splitAudio.progress.*` | `split-audio.json` | 拆音运行阶段、百分比与明细进度文本 | `R7` |
| `splitAudio.error.*` | `split-audio.json` | 拆音工作流错误、FFmpeg / Demucs 失败兜底 | `R7` |
| `splitAudio.executionPlan.*` | `split-audio.json` | Demucs 执行方案、GPU 回退与设备切换摘要 | `R7` |
| `splitAudio.planner.*` | `split-audio.json` | Demucs 设备探测与启动脚本校验消息 | `R7` |
| `splitAudio.runtime.*` | `split-audio.json` | Demucs 运行时包、模型仓与 Python 环境缺失提示 | `R7` |

## 已注册 Key

| Key | 模块 | 资源文件 | `zh-CN` | `en-US` | 状态 | 首次建立轮次 |
| --- | --- | --- | --- | --- | --- | --- |
| `common.language.option.zh-cn` | `common` | `common.json` | `简体中文` | `Simplified Chinese` | `Active` | `R2` |
| `common.language.option.en-us` | `common` | `common.json` | `English (United States)` | `English (United States)` | `Active` | `R2` |
| `common.action.applicationSettings` | `common` | `common.json` | `应用设置` | `App settings` | `Active` | `R3` |
| `common.action.close` | `common` | `common.json` | `关闭` | `Close` | `Active` | `R3` |
| `common.toggle.off` | `common` | `common.json` | `关闭` | `Off` | `Active` | `R3` |
| `common.toggle.on` | `common` | `common.json` | `开启` | `On` | `Active` | `R3` |
| `settings.language.label` | `settings` | `settings.json` | `界面语言` | `Display language` | `Active` | `R2` |
| `settings.language.description` | `settings` | `settings.json` | `切换后当前窗口会即时刷新，无需重新启动。` | `Changes refresh the current window immediately without restarting.` | `Active` | `R2` |
| `settings.language.currentValue` | `settings` | `settings.json` | `当前语言：{language}` | `Current language: {language}` | `Active` | `R2` |
| `settings.pane.title` | `settings` | `settings.json` | `应用设置` | `App settings` | `Active` | `R3` |
| `settings.pane.description` | `settings` | `settings.json` | `应用级偏好统一集中在这里管理，不占用主处理区域；不同模块对处理完成行为和转码方式的生效范围可查看标题旁的说明提示。` | `Manage app-level preferences here without taking over the main workspace. Scope details for processing completion behavior and transcoding are shown in the nearby tooltips.` | `Active` | `R3` |
| `settings.appearance.title` | `settings` | `settings.json` | `外观` | `Appearance` | `Active` | `R3` |
| `settings.appearance.theme.label` | `settings` | `settings.json` | `软件主题` | `App theme` | `Active` | `R3` |
| `settings.appearance.theme.option.system` | `settings` | `settings.json` | `跟随系统` | `Follow system` | `Active` | `R3` |
| `settings.appearance.theme.option.systemDescription` | `settings` | `settings.json` | `根据 Windows 当前主题自动切换明亮和暗黑外观。` | `Switches between light and dark automatically based on the current Windows theme.` | `Active` | `R3` |
| `settings.appearance.theme.option.light` | `settings` | `settings.json` | `明亮主题` | `Light` | `Active` | `R3` |
| `settings.appearance.theme.option.lightDescription` | `settings` | `settings.json` | `始终使用明亮外观，适合高亮环境。` | `Always uses a light appearance that works well in bright environments.` | `Active` | `R3` |
| `settings.appearance.theme.option.dark` | `settings` | `settings.json` | `暗黑主题` | `Dark` | `Active` | `R3` |
| `settings.appearance.theme.option.darkDescription` | `settings` | `settings.json` | `始终使用暗黑外观，适合低亮环境。` | `Always uses a dark appearance that works well in low-light environments.` | `Active` | `R3` |
| `settings.systemTray.title` | `settings` | `settings.json` | `系统托盘` | `System tray` | `Active` | `R3` |
| `settings.systemTray.toggleHeader` | `settings` | `settings.json` | `关闭窗口时保留在系统托盘` | `Keep the app in the system tray when closing the window` | `Active` | `R3` |
| `settings.systemTray.description` | `settings` | `settings.json` | `开启后，点击右上角关闭按钮不会退出应用，而是隐藏到系统托盘继续运行。右键托盘图标可选择“显示窗口”或“退出”。` | `When enabled, clicking the close button hides the app to the system tray instead of exiting. Right-click the tray icon to show the window or quit.` | `Active` | `R3` |
| `settings.systemTray.status.enabled` | `settings` | `settings.json` | `已启用系统托盘，点击关闭按钮后会隐藏到托盘中继续运行。` | `System tray support is enabled. Clicking the close button will hide the app to the tray.` | `Active` | `R3` |
| `settings.systemTray.status.disabled` | `settings` | `settings.json` | `已关闭系统托盘，点击关闭按钮将直接退出应用。` | `System tray support is disabled. Clicking the close button will exit the app.` | `Active` | `R3` |
| `settings.desktopShortcut.title` | `settings` | `settings.json` | `桌面快捷方式` | `Desktop shortcut` | `Active` | `R3` |
| `settings.desktopShortcut.description` | `settings` | `settings.json` | `检测桌面是否已存在当前应用快捷方式；如果不存在，会自动为你创建。` | `Checks whether a desktop shortcut for this app already exists and creates one automatically if needed.` | `Active` | `R3` |
| `settings.desktopShortcut.button` | `settings` | `settings.json` | `生成桌面快捷方式` | `Create desktop shortcut` | `Active` | `R3` |
| `settings.desktopShortcut.notification.created` | `settings` | `settings.json` | `已在桌面创建应用快捷方式。` | `A desktop shortcut was created.` | `Active` | `R3` |
| `settings.desktopShortcut.notification.exists` | `settings` | `settings.json` | `桌面快捷方式已存在。` | `The desktop shortcut already exists.` | `Active` | `R3` |
| `settings.desktopShortcut.notification.failed` | `settings` | `settings.json` | `创建桌面快捷方式失败，请稍后重试。` | `Couldn't create the desktop shortcut. Please try again later.` | `Active` | `R3` |
| `settings.desktopShortcut.status.created` | `settings` | `settings.json` | `已创建桌面快捷方式：{path}` | `Created desktop shortcut: {path}` | `Active` | `R3` |
| `settings.desktopShortcut.status.exists` | `settings` | `settings.json` | `桌面快捷方式已存在，无需重复创建。` | `The desktop shortcut already exists. No duplicate was created.` | `Active` | `R3` |
| `settings.desktopShortcut.status.failed` | `settings` | `settings.json` | `创建桌面快捷方式失败，请稍后重试。` | `Couldn't create the desktop shortcut. Please try again later.` | `Active` | `R3` |
| `settings.processingBehavior.title` | `settings` | `settings.json` | `处理完成行为` | `After processing` | `Active` | `R3` |
| `settings.processingBehavior.infoTooltip` | `settings` | `settings.json` | `适用范围：...` | `Applies to:...` | `Active` | `R3` |
| `settings.processingBehavior.toggleHeader` | `settings` | `settings.json` | `处理完成后定位输出文件` | `Reveal the output file after processing` | `Active` | `R3` |
| `settings.processingBehavior.description` | `settings` | `settings.json` | `开启后，处理完成时会打开输出文件所在文件夹，并自动选中对应文件。批量任务会定位最后一个成功输出的文件。` | `When enabled, the output folder opens after processing and the exported file is selected automatically. For batch jobs, the last successful output is selected.` | `Active` | `R3` |
| `settings.transcoding.title` | `settings` | `settings.json` | `转码方式` | `Transcoding` | `Active` | `R3` |
| `settings.transcoding.infoTooltip` | `settings` | `settings.json` | `生效范围：...` | `Applies to:...` | `Active` | `R3` |
| `settings.transcoding.label` | `settings` | `settings.json` | `默认转码策略` | `Default transcoding strategy` | `Active` | `R3` |
| `settings.transcoding.option.fast` | `settings` | `settings.json` | `快速换封装（默认）` | `Fast remux (default)` | `Active` | `R3` |
| `settings.transcoding.option.fastDescription` | `settings` | `settings.json` | `保持当前默认行为：优先直接复用原始流，必要时沿用现有的兼容编码策略，速度更快。` | `Keeps the current default behavior by reusing original streams whenever possible and falling back to the existing compatibility strategy only when needed, which is usually faster.` | `Active` | `R3` |
| `settings.transcoding.option.full` | `settings` | `settings.json` | `真正转码（重新编码）` | `Full transcode (re-encode)` | `Active` | `R3` |
| `settings.transcoding.option.fullDescription` | `settings` | `settings.json` | `先解码再编码，重新生成音视频数据；更适合需要统一编码格式、兼容性或后续编辑的场景。` | `Decodes and re-encodes the media to produce new audio and video data, which is better for unified codecs, compatibility, or follow-up editing.` | `Active` | `R3` |
| `settings.transcoding.gpu.title` | `settings` | `settings.json` | `GPU 加速` | `GPU acceleration` | `Active` | `R3` |
| `settings.transcoding.gpu.toggleHeader` | `settings` | `settings.json` | `是否开启 GPU 加速` | `Enable GPU acceleration` | `Active` | `R3` |
| `settings.transcoding.gpu.description.enabled` | `settings` | `settings.json` | `开启后，会先检测当前电脑是否存在可用的 GPU 视频硬件编码能力；若不适用或不可用，会自动回退为 CPU 转码，不会影响任务继续执行。音频任务、字幕任务与部分旧式视频格式仍会继续使用 CPU。` | `When enabled, the app checks whether GPU video encoding is available first. If it is unavailable or not applicable, the workflow falls back to CPU transcoding automatically so the task can continue. Audio jobs, subtitle jobs, and some older video formats still use the CPU.` | `Active` | `R3` |
| `settings.transcoding.gpu.description.disabled` | `settings` | `settings.json` | `关闭后，真正转码会始终使用 CPU 重新编码，速度更稳定，也不会额外检测显卡能力。` | `When disabled, full transcoding always re-encodes on the CPU for steadier behavior without checking GPU capabilities.` | `Active` | `R3` |
| `mainWindow.title.application` | `main-window` | `main-window.json` | `Vidvix` | `Vidvix` | `Active` | `R2` |
| `mainWindow.header.caption` | `mainWindow` | `main-window.json` | `当前模块` | `Current module` | `Active` | `R3` |
| `mainWindow.toolbar.applicationSettings` | `mainWindow` | `main-window.json` | `应用设置` | `App settings` | `Active` | `R3` |
| `mainWindow.progress.title` | `mainWindow` | `main-window.json` | `处理进度` | `Processing progress` | `Active` | `R3` |

## R4 批量登记

- 本轮把集中式配置显示文本批量收敛到 `Resources/Localization/zh-CN/common.json` 与 `Resources/Localization/en-US/common.json`，并由 `ApplicationConfiguration` 中的稳定 key / key-prefix 驱动。
- 已登记的 R4 key family：
  - `common.workspace.video.*`
  - `common.workspace.audio.*`
  - `common.workspace.trim.*`
  - `common.workspace.merge.*`
  - `common.workspace.splitAudio.*`
  - `common.workspace.terminal.*`
  - `common.processingMode.videoConvert.*`
  - `common.processingMode.videoTrackExtract.*`
  - `common.processingMode.audioTrackExtract.*`
  - `common.processingMode.subtitleTrackExtract.*`
  - `common.mergeMode.videoJoin.*`
  - `common.mergeMode.audioJoin.*`
  - `common.mergeMode.audioVideoCompose.*`
  - `common.splitAudio.acceleration.cpu.*`
  - `common.splitAudio.acceleration.gpuPreferred.*`
  - `common.outputFormat.video.*`
  - `common.outputFormat.trim.*`
  - `common.outputFormat.audio.*`
  - `common.outputFormat.subtitle.*`
- R4 绑定补记：
  - 主窗口视频 / 音频输出格式的 `ComboBox` 仍使用 `SelectedItem`，因此 `MainViewModel.SelectedOutputFormat` 必须始终返回 `AvailableOutputFormats` 当前快照中的对象实例，不能在 getter 中重新生成另一份本地化对象，否则首次启动或热切换后会出现空白选中框。
- R4 代表性 key：

| Key | 模块 | 资源文件 | `zh-CN` | `en-US` | 状态 | 首次建立轮次 |
| --- | --- | --- | --- | --- | --- | --- |
| `common.workspace.video.headerTitle` | `common` | `common.json` | `视频处理` | `Video processing` | `Active` | `R4` |
| `common.workspace.audio.fixedProcessingModeDisplayName` | `common` | `common.json` | `音频格式转换` | `Audio format conversion` | `Active` | `R4` |
| `common.workspace.merge.headerDescription` | `common` | `common.json` | `拼接音视频并完成合成。` | `Join audio and video, then complete the composition.` | `Active` | `R4` |
| `common.processingMode.videoConvert.displayName` | `common` | `common.json` | `视频格式转换` | `Video format conversion` | `Active` | `R4` |
| `common.processingMode.subtitleTrackExtract.description` | `common` | `common.json` | `默认提取第一条字幕轨道；文本字幕会按目标格式输出，图形字幕建议导出为 MKS 以保留原始字幕编码。` | `Extracts the first subtitle track by default. Text subtitles are exported in the target format, while image subtitles are best exported as MKS to preserve the original encoding.` | `Active` | `R4` |
| `common.mergeMode.audioVideoCompose.timelineHintText` | `common` | `common.json` | `当前为音视频合成模式，请分别添加 1 个视频和 1 个音频。` | `Audio-video compose mode is active. Add one video and one audio file separately.` | `Active` | `R4` |
| `common.splitAudio.acceleration.gpuPreferred.displayName` | `common` | `common.json` | `GPU 优先（独显 -> 核显 -> CPU）` | `GPU preferred (discrete -> integrated -> CPU)` | `Active` | `R4` |
| `common.outputFormat.video.mp4.description` | `common` | `common.json` | `兼容性最好，适合常见播放器和移动设备。` | `Best compatibility for common players and mobile devices.` | `Active` | `R4` |
| `common.outputFormat.trim.mkv.description` | `common` | `common.json` | `封装更宽松，更适合保留高质量剪辑片段。` | `A more flexible container that is better for preserving high-quality trimmed clips.` | `Active` | `R4` |
| `common.outputFormat.audio.mp3.description` | `common` | `common.json` | `通用音频格式，兼容性高。` | `A general-purpose audio format with broad compatibility.` | `Active` | `R4` |
| `common.outputFormat.subtitle.srt.description` | `common` | `common.json` | `通用文本字幕格式，兼容性最好，适合常见播放器和字幕平台。` | `A general-purpose text subtitle format with the broadest compatibility for common players and subtitle platforms.` | `Active` | `R4` |

## R5 批量登记

- 本轮把主窗口常驻外壳、待处理队列、处理设置区、输出目录、处理结果区，以及直接展示到主窗口的公共进度 / 批量消息 / 预检提示 / 转码说明统一收敛到 `Resources/Localization/zh-CN/main-window.json` 与 `Resources/Localization/en-US/main-window.json`。
- 已登记的 R5 key family：
  - `mainWindow.toolbar.*`
  - `mainWindow.queue.*`
  - `mainWindow.settings.*`
  - `mainWindow.results.*`
  - `mainWindow.progress.*`
  - `mainWindow.message.*`
  - `mainWindow.processingContext.*`
  - `mainWindow.transcoding.*`
- R5 刷新补记：
  - 主窗口运行态文本不再只刷新静态标题；`MainViewModel.RefreshLocalizedTextProperties()` 现在会同时重建工作区 shell、执行进度快照与队列项状态，避免语言切换后保留旧语言缓存。
  - `MediaJobViewModel` 的队列状态与缩略图占位文本也已使用 `ILocalizationService`，因此队列项不再需要依赖初始化时的单次中文快照。
- R5 代表性 key：

| Key | 模块 | 资源文件 | `zh-CN` | `en-US` | 状态 | 首次建立轮次 |
| --- | --- | --- | --- | --- | --- | --- |
| `mainWindow.toolbar.importFiles` | `mainWindow` | `main-window.json` | `导入文件` | `Import files` | `Active` | `R5` |
| `mainWindow.queue.title` | `mainWindow` | `main-window.json` | `待处理队列` | `Queue` | `Active` | `R5` |
| `mainWindow.settings.outputDirectory.placeholder` | `mainWindow` | `main-window.json` | `留空时使用原文件夹输出` | `Leave empty to use the source folder` | `Active` | `R5` |
| `mainWindow.results.title` | `mainWindow` | `main-window.json` | `处理结果` | `Results` | `Active` | `R5` |
| `mainWindow.progress.detail.processedTotal` | `mainWindow` | `main-window.json` | `当前文件进度 {percent} · 已处理 {processed} / {total}` | `Current item {percent} · Processed {processed} / {total}` | `Active` | `R5` |
| `mainWindow.message.processingItemFailed` | `mainWindow` | `main-window.json` | `{fileName} 处理失败，用时 {duration}。原因：{reason}` | `{fileName} failed after {duration}. Reason: {reason}` | `Active` | `R5` |
| `mainWindow.message.runtimePreparing` | `mainWindow` | `main-window.json` | `正在准备运行环境...` | `Preparing the runtime environment...` | `Active` | `R5` |
| `mainWindow.processingContext.preflightUnableToInspect` | `mainWindow` | `main-window.json` | `{fileName} 未能完成轨道预检，将继续尝试处理。原因：{reason}` | `{fileName} could not complete media-track preflight inspection and will still be attempted. Reason: {reason}` | `Active` | `R5` |
| `mainWindow.transcoding.gpuDetected` | `mainWindow` | `main-window.json` | `检测到 {gpuName} 可用，本次可启用视频硬件编码。` | `{gpuName} is available, so hardware video encoding can be used for this run.` | `Active` | `R5` |
| `mainWindow.queue.item.status.running` | `mainWindow` | `main-window.json` | `处理中` | `Processing` | `Active` | `R5` |

## R6 批量登记

- 本轮把裁剪模块页面层、运行态状态、预览失败兜底、导出进度与工作流层导入 / 导出消息统一收敛到 `Resources/Localization/zh-CN/trim.json` 与 `Resources/Localization/en-US/trim.json`。
- 已登记的 R6 key family：
  - `trim.editor.*`
  - `trim.placeholder.*`
  - `trim.preview.*`
  - `trim.selection.*`
  - `trim.settings.*`
  - `trim.mediaInfo.*`
  - `trim.status.*`
  - `trim.progress.*`
  - `trim.import.*`
  - `trim.export.*`
  - `trim.smartTrim.*`
  - `trim.log.*`
- R6 刷新补记：
  - `VideoTrimWorkspaceViewModel.RefreshLocalization()` 现在不仅刷新静态标题，还会重建裁剪策略选项、媒体信息字段、预览覆盖文案与导出进度状态，因此运行中切换语言时不会继续保留旧语言的裁剪页缓存。
  - `TrimWorkflowService` / `VideoTrimWorkflowService` 已直接注入 `ILocalizationService`，导入校验、成功摘要、速度优先 / 精确度优先说明和 smart trim 回退消息不再依赖单语言字符串常量。
- R6 代表性 key：

| Key | 模块 | 资源文件 | `zh-CN` | `en-US` | 状态 | 首次建立轮次 |
| --- | --- | --- | --- | --- | --- | --- |
| `trim.placeholder.title` | `trim` | `trim.json` | `请导入文件或拖拽到此处开始裁剪` | `Import a file or drag it here to start trimming` | `Active` | `R6` |
| `trim.preview.jumpToStart` | `trim` | `trim.json` | `跳转到裁剪入点` | `Jump to trim start` | `Active` | `R6` |
| `trim.selection.summary` | `trim` | `trim.json` | `裁剪区间：{start} - {end}（共 {duration}）` | `Trim range: {start} - {end} ({duration} total)` | `Active` | `R6` |
| `trim.settings.outputDirectoryLabel` | `trim` | `trim.json` | `输出目录` | `Output folder` | `Active` | `R6` |
| `trim.mediaInfo.field.duration` | `trim` | `trim.json` | `时长` | `Duration` | `Active` | `R6` |
| `trim.progress.detail.processed` | `trim` | `trim.json` | `已导出 {processed} / {duration}` | `Exported {processed} / {duration}` | `Active` | `R6` |
| `trim.import.rejected.noInput` | `trim` | `trim.json` | `未检测到可导入的文件。` | `No files were provided for import.` | `Active` | `R6` |
| `trim.export.transcoding.video.fastFallback` | `trim` | `trim.json` | `速度优先下当前片段无法完全复用原始流，本次已自动回退为兼容重编码。` | `Speed priority could not fully reuse the original streams for this clip, so the export fell back to compatible re-encoding automatically.` | `Active` | `R6` |
| `trim.smartTrim.fallback.noKeyframeRange` | `trim` | `trim.json` | `当前片段未覆盖足够的关键帧区间，已回退为整段精确重编码。` | `The selected clip did not cover a sufficient keyframe range, so it fell back to full precise re-encoding.` | `Active` | `R6` |
| `trim.log.previewLoadFailed` | `trim` | `trim.json` | `MPV 预览加载失败。` | `MPV preview loading failed.` | `Active` | `R6` |

## R7 批量登记

- 本轮把拆音模块页面层、运行态状态、结果卡片 / 预览卡，以及 `AudioSeparationWorkflowService`、`SplitAudioExecutionCoordinator`、`DemucsExecutionPlanner`、`DemucsRuntimeService` 直接暴露到拆音页的用户可见文案统一收敛到 `Resources/Localization/zh-CN/split-audio.json` 与 `Resources/Localization/en-US/split-audio.json`。
- 已登记的 R7 key family：
  - `splitAudio.action.*`
  - `splitAudio.common.*`
  - `splitAudio.input.*`
  - `splitAudio.settings.*`
  - `splitAudio.results.*`
  - `splitAudio.preview.*`
  - `splitAudio.status.*`
  - `splitAudio.validation.*`
  - `splitAudio.stem.*`
  - `splitAudio.picker.*`
  - `splitAudio.progress.*`
  - `splitAudio.error.*`
  - `splitAudio.executionPlan.*`
  - `splitAudio.planner.*`
  - `splitAudio.runtime.*`
- R7 刷新补记：
  - `SplitAudioWorkspaceViewModel.RefreshLocalization()` 现已联动重建输入摘要、运行状态、进度快照、结果卡片按钮与预览按钮文本，因此语言切换时拆音页不会继续保留旧语言缓存。
  - `SplitAudioExecutionCoordinator` 改为持有失败原因 resolver，`AudioSeparationWorkflowService`、`DemucsExecutionPlanner`、`DemucsRuntimeService` 通过 `ILocalizationService` 与 `LocalizedExceptions` 提供可重算错误消息，避免运行中切换语言后继续显示旧的 `exception.Message`。
  - `ApplicationPaths.ResolveExecutableDirectoryPath()` 现优先使用 `AppContext.BaseDirectory`，因此 `dotnet xxx.dll` 方式运行的离线烟测也能稳定找到 Demucs 启动脚本与运行时资源，不再误解析到 `dotnet.exe` 目录。
- R7 代表性 key：

| Key | 模块 | 资源文件 | `zh-CN` | `en-US` | 状态 | 首次建立轮次 |
| --- | --- | --- | --- | --- | --- | --- |
| `splitAudio.input.placeholderTitle` | `splitAudio` | `split-audio.json` | `请导入文件或拖拽到此处` | `Import a file or drag it here` | `Active` | `R7` |
| `splitAudio.input.summary.ready` | `splitAudio` | `split-audio.json` | `{mediaType}，{duration}，{audioCodec}。拆音时会先标准化为临时 WAV，再调用 Demucs 分离四轨。` | `{mediaType}, {duration}, {audioCodec}. The workflow normalizes the source to a temporary WAV before Demucs separates the four stems.` | `Active` | `R7` |
| `splitAudio.settings.outputDirectoryHint` | `splitAudio` | `split-audio.json` | `留空时默认导出到原文件所在文件夹。` | `Leave empty to export to the source file folder by default.` | `Active` | `R7` |
| `splitAudio.results.sectionTitle` | `splitAudio` | `split-audio.json` | `处理完毕列表` | `Completed runs` | `Active` | `R7` |
| `splitAudio.preview.unavailable.withFileName` | `splitAudio` | `split-audio.json` | `已导入 {fileName}，但预览暂不可用，仍可以继续拆音。` | `{fileName} was imported, but preview is temporarily unavailable. You can still continue separating audio.` | `Active` | `R7` |
| `splitAudio.status.completed` | `splitAudio` | `split-audio.json` | `{resolutionSummary} 已生成 {count} 条分轨文件。` | `{resolutionSummary} Generated {count} stem files.` | `Active` | `R7` |
| `splitAudio.progress.detail.exportStem.duration` | `splitAudio` | `split-audio.json` | `正在导出 {stem}：{processed} / {total}` | `Exporting {stem}: {processed} / {total}` | `Active` | `R7` |
| `splitAudio.error.demucsRuntimeUnavailable` | `splitAudio` | `split-audio.json` | `Demucs 运行时不可用，请检查离线运行时包是否完整。` | `The Demucs runtime isn't available. Check whether the offline runtime package is complete.` | `Active` | `R7` |
| `splitAudio.executionPlan.directml.afterCuda.integrated` | `splitAudio` | `split-audio.json` | `独显 CUDA 执行未成功，已回退到核显继续拆音：{deviceName}。` | `CUDA on the discrete GPU failed. Falling back to the integrated GPU for separation: {deviceName}.` | `Active` | `R7` |
| `splitAudio.planner.launcherMissing` | `splitAudio` | `split-audio.json` | `未找到 Demucs 启动脚本：{path}` | `The Demucs launcher script was not found: {path}` | `Active` | `R7` |
| `splitAudio.runtime.missingRuntimePackage` | `splitAudio` | `split-audio.json` | `未找到离线 Demucs {runtimeVariant} 运行时包，请补齐 {packagePath}。` | `The offline Demucs {runtimeVariant} runtime package was not found. Please provide {packagePath}.` | `Active` | `R7` |

## 下一轮接入提示

- `R8` 直接复用 `R7` 在拆音模块已经验证通过的 resolver 化运行态刷新模式、服务层本地化异常封装方式，以及 `SplitAudioWorkspaceToggle` + `SettingsLanguageComboBox` 的 UI 热切换验证路径，不要回头重写 `split-audio.json` 的既有职责。
- `R8` 新增终端区与媒体详情区私有文案时，优先分别落到 `terminal.json` 与 `media-details.json`，不要把详情浮层或终端页内部提示继续塞进 `main-window.json` 或 `split-audio.json`。
- `R8` 只处理 `terminal` 与 `media-details` 模块私有页面文案，不要回头扩大 `trim` / `split-audio` 范围，也不要提前触碰 `merge` 私有页面文案。
