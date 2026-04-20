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
| `mainWindow.header.*` | `main-window.json` | 主窗口页头试点 caption | `R3` |
| `mainWindow.toolbar.*` | `main-window.json` | 主窗口工具栏试点按钮 | `R3` |
| `mainWindow.progress.*` | `main-window.json` | 主窗口公共进度标题试点 | `R3` |

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

## 下一轮接入提示

- `R5` 直接复用 `common.workspace.*`、`common.processingMode.*` 与 `common.outputFormat.*` 的集中式配置读取结果，不再回到 `ApplicationConfiguration` 重写展示文案。
- `R5` 开始迁移主窗口 shell 时，继续复用 `mainWindow.header.*`、`mainWindow.toolbar.*`、`mainWindow.progress.*` 的绑定模式与 `LocalizationRefreshRequested + Bindings.Update()` 刷新机制。
- `R5` 如果需要把按钮、标签、Placeholder 等非集中式 shell 文案搬入语言包，应优先落到 `main-window.json`，不要再把页面私有文案塞回 `common.json`。
