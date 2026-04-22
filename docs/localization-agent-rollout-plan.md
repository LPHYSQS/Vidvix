# Vidvix 本地化迁移执行手册（供 AI Agent 使用）

## 顶部状态区

当前执行本计划的 AI Agent 必须优先更新本区块。开始本轮前更新“当前轮次”和“当前状态”；完成本轮后更新“最近完成轮次”“交接摘要”和下方轮次看板。

- 计划版本：`v2`
- 项目：`Vidvix`
- 当前阶段：`Stage 3`
- 当前轮次：`R12`
- 当前状态：`Completed`
- 当前执行 Agent：`Codex`
- 最近完成轮次：`R12`
- 最近完成时间：`2026-04-22 13:14`
- 构建验证：`Passed`
- 热切换验证：`Passed`
- 字符串盘点产物：`docs/localization-string-inventory.csv`
- 验证日志产物：`docs/localization-validation-log.md`

## 顶部交接区

当前执行本计划的 AI Agent 在完成本轮后，必须只修改本区块内容，不得删除本区块字段。

- 本轮完成项：已完成 `R12` 最终回归与冻结：修复 `SplitAudioOfflineSmoke` 对 `MediaInfoService` 构造函数签名的回归失配，并为测试工程补上默认隔离 `OutDir` / `ProjectReference` 透传；已重导字符串盘点、完成双语 key 对齐校验、补齐最终热切换烟测与前台启动冒烟；已新增 `docs/localization-agent-quick-reference.md`，为后续 AI agent 提供精确的本地化定位说明。
- 本轮修改文件：`tests/SplitAudioOfflineSmoke/Program.cs`、`tests/SplitAudioOfflineSmoke/SplitAudioOfflineSmoke.csproj`、`docs/localization-agent-rollout-plan.md`、`docs/localization-string-inventory.csv`、`docs/localization-validation-log.md`
- 本轮新增文件：`docs/localization-agent-quick-reference.md`
- 本轮验证结果：`dotnet build .\\Vidvix.sln -c Debug -v minimal` 通过，`0` 警告、`0` 错误；`dotnet build .\\tests\\SplitAudioOfflineSmoke\\SplitAudioOfflineSmoke.csproj -c Debug -p:UseAppHost=false -v minimal` 通过，`0` 警告、`0` 错误；`powershell -ExecutionPolicy Bypass -File .\\scripts\\test-split-audio-offline.ps1 -RepoRoot .` 通过；双语 key 对齐校验通过，`common=164`、`settings=42`、`main-window=146`、`trim=130`、`split-audio=120`、`merge=330`、`terminal=30`、`media-details=72`，全部 `MISSING_IN_EN=0` / `MISSING_IN_ZH=0`；临时热切换烟测 `dotnet run -c Debug --project %TEMP%\\VidvixR12Smoke-79b338f332ca4e94b81c12ff24a9f8f7\\VidvixR12Smoke-79b338f332ca4e94b81c12ff24a9f8f7.csproj` 通过；启动 `bin\\x64\\Debug\\net8.0-windows10.0.19041.0\\Vidvix.exe` 前台持续运行超过 `12` 秒并保持响应，`MainWindowHandle` 为 `5441680`，窗口标题为 `Vidvix`，未出现黑屏、白屏或闪退。
- 当前遗留问题：无阻塞项；允许保留的少量中文仍仅限非 UI 的安全 fallback / 诊断文本，不影响 `en-US` 可用性，也不会造成成片 key 缺失。
- 下一轮必须处理：无；本计划已在 `R12` 完成并冻结，下一轮为 `N/A`。
- 下一轮禁止扩展：不要再以 `R13` 形式继续扩张本地化迁移；后续新增功能 / 新文案 / 新语言仅按 `docs/localization-agent-quick-reference.md` 做增量维护。

## 执行协议

如果你是当前执行本计划的 AI Agent，按以下顺序执行：

1. 完整阅读本文件。
2. 只领取“轮次看板”中当前应执行的一轮，不得跨轮合并。
3. 开始前更新“顶部状态区”，将“当前轮次”改为本轮编号，将“当前状态”改为 `In Progress`。
4. 只修改本轮允许范围内的代码和文档。
5. 完成本轮后更新“顶部交接区”。
6. 完成本轮后更新“轮次看板”，将本轮状态改为 `Completed`，写入执行日期与摘要。
7. 如果本轮未完成，必须将本轮状态标为 `Blocked` 或 `Review Needed`，并在“顶部交接区”明确阻塞项。
8. 未通过本轮验收前，不得开始下一轮。

## 总体目标

本计划的唯一目标是将 Vidvix 当前写死在代码中的简体中文 UI 文案迁移为可配置、可扩展、可热切换的本地化资源体系，并满足以下硬性要求：

- 应用设置中必须提供语言切换入口。
- 切换语言后当前窗口和当前页面必须即时刷新。
- 切换语言不得依赖重启应用。
- 本地化缺失不得导致黑屏、闪退或窗口无法加载。
- 每一轮只交付一个清晰、可验证、可交接的小目标。

## 当前仓库基线

本执行手册基于以下已知仓库事实制定：

- 技术栈：`WinUI 3`、`.NET 8`、桌面应用。
- 现有设置入口：`Views/Controls/ApplicationSettingsPane.xaml`
- 现有用户偏好持久化：`Services/UserPreferencesService.cs`
- 当前用户偏好序列化：`System.Text.Json`
- 当前疑似包含中文文案的源码文件：约 `96` 个
- 当前疑似中文命中数：约 `1287` 处
- 当前热点文件：
  - `ViewModels/MergeViewModel.cs`
  - `ViewModels/MergeViewModel.AudioVideoCompose.cs`
  - `Core/Models/ApplicationConfiguration.cs`
  - `Views/MergePage.xaml`
  - `Views/MainWindow.xaml`
  - `Services/AudioSeparationWorkflowService.cs`
  - `ViewModels/SplitAudioWorkspaceViewModel.cs`
  - `ViewModels/MainViewModel.Execution.cs`

## 统一设计结论

除非后续轮次明确批准变更，本计划默认采用以下设计结论：

- 语言包格式：`JSON`
- 读取方式：启动时或切换语言时加载到内存，不在每次绑定时访问磁盘
- 回退语言：`zh-CN`
- 第二语言：`en-US`
- 用户偏好新增字段：`CurrentUiLanguage`
- 本地化主服务：`ILocalizationService` / `LocalizationService`
- 热切换事件：`LanguageChanged`
- 资源目录：

```text
Resources/
  Localization/
    manifest.json
    zh-CN/
      common.json
      settings.json
      main-window.json
      trim.json
      split-audio.json
      merge.json
      terminal.json
      media-details.json
    en-US/
      common.json
      settings.json
      main-window.json
      trim.json
      split-audio.json
      merge.json
      terminal.json
      media-details.json
```

## 强制实现约束

所有轮次必须同时遵守以下约束：

- 不得在同一轮内同时做大规模逻辑重构和大规模文案替换。
- 每个新接入的用户可见字符串必须同时具备：
  - 稳定 key
  - `zh-CN` 文案值
  - 缺失时的回退策略
- 本地化资源缺失时必须回退到 `zh-CN` 或明确的安全默认值，不得抛出未处理异常。
- 动态文本必须使用占位符，不得继续拼接硬编码中文。
- 语言切换完成后，当前视图和相关 ViewModel 必须刷新，不得要求用户重开窗口。
- 每轮完成后必须构建验证。
- 从 `R3` 开始，每轮完成后必须进行热切换验证。

## key 命名规范

所有新 key 必须遵循稳定前缀和语义分层。默认格式如下：

```text
{module}.{area}.{element}.{state}
```

示例：

```text
settings.appearance.title
settings.language.label
mainWindow.toolbar.importFiles
mainWindow.progress.running
trim.export.button
splitAudio.progress.running
merge.videoJoin.emptyTrack
terminal.output.copyAll
mediaDetails.audio.sectionTitle
```

命名要求：

- key 只使用英文、小写、点分层。
- 不得直接把中文拼音整段写入 key。
- 不得使用与界面位置无关的模糊 key，例如 `text1`、`message2`、`labelA`。
- 同一语义只能存在一个主 key，避免重复注册。

## 动态文本规范

所有动态文案使用占位符。示例：

```text
trim.export.success = 已导出到 {path}
merge.result.summary = 已输出 {count} 个文件
splitAudio.progress.step = 正在处理第 {index} / {total} 项
```

动态文本要求：

- 参数命名必须语义化。
- 不得通过字符串拼接组装整句文案。
- 同一动态句式的参数命名在不同语言下必须保持一致。

## 热切换设计要求

从 `R2` 起，以下设计为强制目标：

- `LocalizationService` 负责：
  - 加载语言包
  - 缓存当前语言字典
  - 缓存回退字典
  - 提供 `GetString` 与格式化能力
  - 发出 `LanguageChanged` 事件
- 用户设置负责：
  - 持久化当前语言
  - 启动时恢复上次语言选择
- 设置页负责：
  - 展示可选语言
  - 触发语言切换
- 视图层负责：
  - 在收到语言变化通知后刷新绑定
  - 必要时调用 `Bindings.Update()`
- ViewModel 负责：
  - 对基于字符串生成的属性执行重新计算
  - 触发 `PropertyChanged`

热切换验收标准：

- `zh-CN -> en-US -> zh-CN` 全程无需重启应用。
- 当前设置页文本即时更新。
- 当前主窗口文本即时更新。
- 当前轮涉及的页面文本即时更新。
- 页面不空白，不崩溃，不出现批量未绑定显示。

## 统一交付物

除轮次中特别注明外，各轮次交付物使用以下统一命名：

- 字符串盘点：`docs/localization-string-inventory.csv`
- key 注册表：`docs/localization-key-registry.md`
- 验证日志：`docs/localization-validation-log.md`
- 计划与交接：本文件

如果某轮未生成上述文件，必须在“顶部交接区”明确说明原因。

## 轮次看板

执行状态只能使用以下枚举值：

- `Pending`
- `In Progress`
- `Review Needed`
- `Completed`
- `Blocked`

| 轮次 | 阶段 | 标题 | 状态 | 最近执行人 | 最近更新 | 摘要 |
| --- | --- | --- | --- | --- | --- | --- |
| R1 | Stage 1 | 字符串盘点与任务分桶 | Completed | Codex | 2026-04-20 | ~~未开始~~ 已生成 1220 条字符串盘点与验证日志，热点排序为 merge > common > main-window > split-audio。 |
| R2 | Stage 1 | 本地化基础设施与资源目录 | Completed | Codex | 2026-04-20 | ~~未开始~~ 已建立 JSON 语言包目录、本地化服务、`CurrentUiLanguage` 偏好字段与 key 注册表，回退语言固定为 `zh-CN`。 |
| R3 | Stage 1 | 设置页语言切换与热切换打通 | Completed | Codex | 2026-04-20 | ~~未开始~~ 已接入设置页语言下拉、当前语言持久化与 `LocalizationRefreshRequested` 刷新链路；修复 `SelectedLanguageOption` 递归导致的启动白屏/闪退，并将语言下拉改为按 `Code` 选中以消除往返切换后的空白显示；`zh-CN -> en-US -> zh-CN` 热切换烟测通过，主窗口试点覆盖页头 caption、设置按钮与处理进度标题。 |
| R4 | Stage 2 | 公共配置与集中式文案迁移 | Completed | Codex | 2026-04-20 | ~~未开始~~ 已完成集中式配置文案迁移，并修复主窗口视频 / 音频目标格式首次启动空白显示。 |
| R5 | Stage 2 | 主窗口外壳与公共进度区迁移 | Completed | Codex | 2026-04-20 | ~~未开始~~ 已完成主窗口外壳、待处理队列、输出目录与公共进度 / 结果日志文案迁移，并补齐 `mainWindow.*` 的 shell、message、processingContext、transcoding key。 |
| R6 | Stage 2 | 裁剪模块迁移 | Completed | Codex | 2026-04-21 | ~~未开始~~ 已完成裁剪模块页面层、预览层与工作流层文案迁移，并补齐导出进度、导入校验、smart trim 回退与运行时热切换刷新链路。 |
| R7 | Stage 2 | 拆音模块迁移 | Completed | Codex | 2026-04-21 | ~~未开始~~ 已完成拆音页、运行态状态、结果卡片与服务层用户可见文案迁移，补齐 `splitAudio.*` 双语资源、Demucs 运行时回退消息与拆音页往返热切换验证。 |
| R8 | Stage 2 | 终端与媒体详情迁移 | Completed | Codex | 2026-04-21 | ~~未开始~~ 已完成终端页与媒体详情浮层文案迁移，补齐 `terminal.*` / `mediaDetails.*` 双语资源、终端失败原因 resolver 刷新与详情浮层重建刷新链路；构建、运行态往返热切换烟测与前台启动截图验证通过。 |
| R9 | Stage 2 | 合并模块状态层迁移 | Completed | Codex | 2026-04-22 | ~~未开始~~ 已完成合并模块状态层与模式层文案迁移，补齐 `merge.*` 双语资源、模式切换 / 轨道空态 / 状态摘要热切换刷新链路；构建、R9 临时烟测与前台启动冒烟通过。 |
| R10 | Stage 2 | 合并模块界面层迁移 | Completed | Codex | 2026-04-22 | ~~未开始~~ 已完成合并模块主界面层文案迁移，`MergePage` 全量改为 `merge.page.*` / `merge.dialog.*` 绑定，输出设置 / 拖拽提示 / 对话框按钮支持热切换；构建、R10 烟测与前台启动验证通过。 |
| R11 | Stage 3 | 残余文案清理与英语补齐 | Completed | Codex | 2026-04-22 | ~~未开始~~ 已清理主窗口导入 / 输出目录残余反馈、补齐 `merge.page.item.*` 双语资源，并改为按稳定 `mediaDetails.field.*` key 解析媒体字段；构建、R11 热切换烟测与前台启动验证通过。 |
| R12 | Stage 3 | 总回归、冻结与收尾 | Completed | Codex | 2026-04-22 | ~~未开始~~ 已完成最终构建、离线 smoke、热切换与启动回归，并新增后续 AI agent 本地化快速定位手册。 |

## 各轮详细执行说明

### R1 字符串盘点与任务分桶

目标：

- 建立可持续更新的字符串盘点产物。
- 为后续轮次建立精确任务边界。

允许范围：

- 只允许新增扫描脚本、盘点文档、统计产物。
- 不修改业务逻辑。
- 不修改运行时绑定行为。

必须覆盖目录：

- `Views`
- `ViewModels`
- `Services`
- `Core`
- `Utils`

必须排除目录：

- `bin`
- `obj`
- `artifacts`
- 自动生成代码

必须交付：

- `docs/localization-string-inventory.csv`
- 至少包含字段：
  - `Path`
  - `LineNumber`
  - `SourceText`
  - `Category`
  - `Priority`
  - `SuggestedKey`
  - `Module`
  - `Status`
- 在本文件顶部记录盘点产物路径。

分类要求：

- `Category` 仅允许：
  - `XamlText`
  - `XamlLabel`
  - `ViewModelMessage`
  - `ServiceUserMessage`
  - `ConfigurationDisplayText`
  - `NonUiIgnore`
- `Priority` 仅允许：
  - `P0`
  - `P1`
  - `P2`

验收标准：

- 热点文件全部出现在盘点产物中。
- 不将注释、测试文本、自动生成代码误计入高优先级文案。
- `P0` 与 `P1` 能按模块筛选。

交接要求：

- 在顶部交接区写明热点模块排序。
- 明确后续建议先做的模块顺序。

### R2 本地化基础设施与资源目录

目标：

- 建立本地化资源读取骨架。
- 建立语言包目录结构。
- 定义 key 注册规则与回退规则。

允许范围：

- 可以新增 `Localization` 相关服务、模型、接口、资源目录。
- 可以新增或扩展用户偏好模型。
- 不做大面积 UI 文案替换。

建议主文件范围：

- `Core`
- `Services`
- `Utils`
- `Core/Models/UserPreferences.cs`
- `Services/UserPreferencesService.cs`

必须交付：

- `ILocalizationService`
- `LocalizationService`
- `Resources/Localization/manifest.json`
- `Resources/Localization/zh-CN/*.json` 初始文件
- `Resources/Localization/en-US/*.json` 空壳或最小可运行文件
- `docs/localization-key-registry.md`

必须具备能力：

- 读取语言包
- 缓存语言包
- 以 `zh-CN` 作为回退语言
- 在 key 缺失时返回安全值
- 记录缺失 key 日志

验收标准：

- 语言包加载失败时应用设计上仍可继续运行。
- 用户偏好能表示当前语言。
- key 注册表开始可维护。

交接要求：

- 在顶部交接区写明语言包目录、服务入口、回退逻辑位置。
- 明确 `R3` 需要接入的设置页和主窗口文件。

### R3 设置页语言切换与热切换打通

目标：

- 将语言切换入口接入应用设置。
- 打通首次可用的“无需重启即可生效”链路。

允许范围：

- 设置页
- 用户偏好
- 本地化服务
- 主窗口中少量公共文本试点

建议主文件范围：

- `Views/Controls/ApplicationSettingsPane.xaml`
- 与设置页直接相关的 ViewModel
- `Views/MainWindow.xaml`
- 相关 code-behind 或 ViewModel 刷新逻辑

必须交付：

- 设置页语言选择控件
- 当前语言持久化
- `SetLanguageAsync` 或等价能力
- 视图刷新机制
- 最小试点语言 key

必须验证：

- 打开应用
- 打开设置页
- 切换 `zh-CN -> en-US -> zh-CN`
- 不重启应用
- 当前设置页文本即时变化
- 当前主窗口至少一个区域即时变化

验收标准：

- 热切换链路真实生效。
- 无需重启应用。
- 无黑屏、无闪退、无设置页空白。

交接要求：

- 在顶部交接区写明具体刷新机制位于哪些文件。
- 明确 `R4` 可以复用的读取方式和绑定模式。

### R4 公共配置与集中式文案迁移

目标：

- 迁移集中存放于模型或配置中的展示文案。

建议主文件范围：

- `Core/Models/ApplicationConfiguration.cs`
- 集中式 profile / option / display text 模型

必须交付：

- 将工作区标题、描述、模式名、输出格式描述等迁移到语言包。
- 为集中式显示配置建立稳定 key。

验收标准：

- 公共配置文案不再直接写死为 UI 展示最终值。
- 原功能行为不变。

交接要求：

- 在顶部交接区写明本轮新增的公共 key 前缀。
- 标明 `R5` 可直接复用的公共 key。

### R5 主窗口外壳与公共进度区迁移

目标：

- 迁移主窗口公共 UI 文案。

建议主文件范围：

- `Views/MainWindow.xaml`
- `ViewModels/MainViewModel.cs`
- `ViewModels/MainViewModel.ProcessingMessages.cs`
- 必要的主窗口 code-behind

必须交付：

- 工具栏按钮
- 页头标题与说明
- 公共进度区
- 常驻状态提示

验收标准：

- 主窗口常驻区域切换语言后即时更新。
- 不出现空白按钮、空标题、空 Toast。

交接要求：

- 在顶部交接区写明仍残留在主窗口中的未迁移文案。
- 明确 `R6` 之后不再回头扩大主窗口范围。

### R6 裁剪模块迁移

目标：

- 完成裁剪模块 P0/P1 文案迁移。

建议主文件范围：

- `Views/Controls/VideoTrimWorkspaceView.xaml`
- `ViewModels/VideoTrimWorkspaceViewModel.cs`
- `ViewModels/VideoTrimWorkspaceViewModel.Preview.cs`

必须交付：

- 裁剪页按钮、标签、提示
- 导出进度、取消提示、成功失败文本
- 相关语言包条目

验收标准：

- 裁剪页停留时切换语言可即时刷新。
- 导出流程中的文案无缺 key 崩溃。

交接要求：

- 在顶部交接区标注裁剪模块 key 前缀。
- 标注仍未迁移的非关键文案。

### R7 拆音模块迁移

目标：

- 完成拆音模块 P0/P1 文案迁移。

建议主文件范围：

- `Views/SplitAudioPage.xaml`
- `ViewModels/SplitAudioWorkspaceViewModel.cs`
- `ViewModels/SplitAudioWorkspaceViewModel.Preview.cs`
- `ViewModels/SplitAudioExecutionCoordinator.cs`
- `Services/AudioSeparationWorkflowService.cs`

必须交付：

- 拆音页按钮、输入提示、结果说明
- 进度文本、错误文本、完成文本
- 相关语言包条目

验收标准：

- 拆音页运行中切换语言不导致异常。
- 拆音结果区文本正常显示。

交接要求：

- 在顶部交接区说明拆音模块是否仍存在服务层直写用户可见文案。

### R8 终端与媒体详情迁移

目标：

- 完成终端区和媒体详情区用户可见文案迁移。

建议主文件范围：

- `ViewModels/TerminalWorkspaceViewModel.cs`
- `Services/MediaInfo/*`
- `Services/VideoPreview/*`
- 主窗口详情浮层相关视图文件

必须交付：

- 终端页按钮与提示
- 媒体详情区标题、节标题、复制提示、错误提示
- 相关语言包条目

验收标准：

- 终端页文本热切换正常。
- 详情浮层打开状态下切换语言仍显示正常。

交接要求：

- 在顶部交接区说明详情浮层刷新依赖哪些属性通知。

### R9 合并模块状态层迁移

目标：

- 先迁移合并模块状态层和模式层文案，不在本轮处理全部界面层。

建议主文件范围：

- `ViewModels/MergeViewModel.cs`
- `ViewModels/MergeViewModel.TrackState.cs`
- `ViewModels/MergeViewModel.MediaMetadata.cs`
- `ViewModels/MergeWorkspaceModeState.cs`

必须交付：

- 模式切换提示
- 轨道空态文本
- 基础状态摘要
- 相关语言包条目

验收标准：

- 模式切换不受影响。
- 状态层文案热切换正常。

交接要求：

- 在顶部交接区明确哪些界面层文案留给 `R10`。

### R10 合并模块界面层迁移

目标：

- 完成合并模块主界面文案迁移。

建议主文件范围：

- `Views/MergePage.xaml`
- `ViewModels/MergeViewModel.AudioVideoCompose.cs`
- `ViewModels/MergeViewModel.Progress.cs`

必须交付：

- 音视频合成、视频拼接、音频拼接三种模式下的主界面文案
- 进度文案
- 操作按钮文案
- 相关语言包条目

验收标准：

- 三种模式切换后文案完整。
- 热切换后当前模式界面可即时刷新。

交接要求：

- 在顶部交接区写明合并模块是否仍存在残余硬编码中文。

### R11 残余文案清理与英语补齐

目标：

- 清理剩余零散 P0/P1 文案。
- 将 `en-US` 从试点补齐到可用状态。

允许范围：

- 全仓库剩余零散高优先级文案
- 语言包整理
- key 收敛

必须交付：

- `en-US` 语言包达到可切换可用
- 重复 key、模糊 key、近义 key 清理结果
- 更新后的 key 注册表

验收标准：

- 主要页面在 `en-US` 下可用。
- 不出现成片 key 缺失。

交接要求：

- 在顶部交接区列出仍允许保留的少量回退中文位置。

### R12 总回归、冻结与收尾

目标：

- 完成最终回归、状态冻结和收尾文档。

允许范围：

- 小范围修复
- 文档收尾
- 最终校验

不得进行：

- 新增大范围结构改动
- 再次扩张本地化方案

必须交付：

- 最终构建通过
- 最终热切换验证通过
- 最终盘点更新
- 最终验证日志更新
- 本文件顶部状态区完成收口

最终验收标准：

- 应用可正常启动
- 设置页可切换语言
- 无需重启应用即可生效
- 主窗口、设置页、裁剪、拆音、合并、终端、详情等主要区域均可用
- 无黑屏、无闪退、无成片 missing key

交接要求：

- 将“当前状态”改为 `Completed`
- 将“当前轮次”改为 `R12`
- 将“最近完成轮次”改为 `R12`
- 在顶部交接区写明最终结论

## 每轮统一验证清单

从 `R1` 起，每轮都必须最少完成以下验证中的适用项，并在交接区写明结果：

- 构建通过
- 修改文件范围与本轮计划一致
- 新 key 已注册
- `zh-CN` 文案已补齐
- 缺失 key 有回退
- 无新增未处理异常

从 `R3` 起，额外必须完成：

- 打开应用
- 打开设置页
- 切换 `zh-CN -> en-US -> zh-CN`
- 不重启应用
- 当前轮涉及页面即时刷新

## 顶部状态区更新模板

开始本轮前使用以下模板更新：

```text
- 当前阶段：`Stage X`
- 当前轮次：`RX`
- 当前状态：`In Progress`
- 当前执行 Agent：`<agent-name>`
```

完成本轮后使用以下模板更新：

```text
- 最近完成轮次：`RX`
- 最近完成时间：`YYYY-MM-DD HH:mm`
- 构建验证：`Passed` / `Failed`
- 热切换验证：`Passed` / `Failed` / `Not Applicable`
```

## 顶部交接区更新模板

完成本轮后，按以下模板更新：

```text
- 本轮完成项：<一句话概括本轮已完成内容>
- 本轮修改文件：<文件列表，逗号分隔>
- 本轮新增文件：<文件列表，逗号分隔>
- 本轮验证结果：<构建、运行、热切换、特殊验证结果>
- 当前遗留问题：<仍存在的问题，没有则写 None>
- 下一轮必须处理：<明确写出下一轮的唯一主目标>
- 下一轮禁止扩展：<明确禁止跨轮扩展的范围>
```

## 轮次看板更新规则

完成本轮后，必须同步修改“轮次看板”中的本轮一行：

- `状态` 改为 `Completed`
- `最近执行人` 改为当前 Agent 名称
- `最近更新` 改为实际日期
- `摘要` 改为本轮交付结论

如果本轮卡住：

- `状态` 改为 `Blocked`
- `摘要` 写明阻塞原因
- 顶部交接区必须明确下一个 Agent 是否应继续本轮还是先修阻塞

## 禁止事项

执行任何一轮时，默认禁止以下行为：

- 未更新顶部状态区即直接改代码
- 一轮内同时吞并多个模块
- 不记录交接信息就结束任务
- 未构建验证就宣告完成
- 未做热切换验证就宣告 `R3` 及之后轮次完成
- 删除本文件中的轮次说明、顶部状态区、顶部交接区、轮次看板

## 完成定义

只有在以下条件全部满足时，才能将本计划视为完成：

- `R1` 至 `R12` 全部标记为 `Completed`
- 顶部状态区显示：
  - 当前轮次：`R12`
  - 当前状态：`Completed`
  - 最近完成轮次：`R12`
- 顶部交接区写明最终结论
- 验证日志存在且可追踪
- 应用支持在设置页中即时切换语言且无需重启
