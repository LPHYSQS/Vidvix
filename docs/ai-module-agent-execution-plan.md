# Vidvix AI 模块执行手册（供 AI Agent 使用）

## 顶部状态区

当前执行本计划的 AI Agent 必须优先更新本区块。开始本轮前更新“当前轮次”和“当前状态”；完成本轮后更新“最近完成轮次”“最近完成时间”和下方轮次看板。

- 计划版本：`v5`
- 项目：`Vidvix`
- 当前阶段：`Stage 1`
- 当前轮次：`R4`
- 当前状态：`Completed`
- 当前执行 Agent：`Codex`
- 最近完成轮次：`R4`
- 最近完成时间：`2026-04-23`
- 构建验证：`Passed`
- 运行验证：`Passed`
- 首发发布目标：`Offline-win-x64`
- 计划文档：`DOCS/ai-module-agent-execution-plan.md`

## 顶部交接区

当前执行本计划的 AI Agent 在完成本轮后，必须只修改本区块内容，不得删除本区块字段。

- 本轮完成项：已完成 `R4`，补齐 AI 导航选中态与共享悬停 / 按下反馈，微调 AI 图标，并将 AI 页面收口为与合并模块同级的三栏壳层与窄窗自适应布局。
- 本轮修改文件：`App.xaml`, `Views/MainWindow.xaml`, `Views/MainWindow.Chrome.cs`, `Views/MainWindow.xaml.cs`, `Views/AiPage.xaml`, `Views/AiPage.xaml.cs`, `ViewModels/AiWorkspaceViewModel.cs`, `Resources/Localization/zh-CN/ai.json`, `Resources/Localization/en-US/ai.json`, `DOCS/ai-module-agent-execution-plan.md`, `DOCS/ai-ui-validation-report.md`
- 本轮新增文件：`None`
- 本轮验证结果：`dotnet build .\Vidvix.sln -c Debug -v minimal` 通过，`0` 警告、`0` 错误；`dotnet test .\Vidvix.sln -c Debug --no-build -v minimal` 退出码 `0`；Debug 产物已成功启动，主窗口句柄就绪，默认窗口与 `1100x900` 重设窄窗下持续运行未黑屏、白屏或闪退；AI 导航选中态、共享悬停 / 按下反馈、AI 图标微调与 AI 页面宽窗 / 窄窗壳层已完成回填验证。
- 当前遗留问题：AI 页面仍为 `R5` 前的稳定壳层，占位素材列表、模式状态、输出设置与顶部命令栏扩展按计划留待下一轮接入；当前无 `R4` 阻断项。
- 下一轮必须处理：执行 `R5`，只完成素材列表、单视频约束、模式切换壳层与基础输出状态，不提前接入 runtime 或推理执行。
- 下一轮禁止扩展：不要提前进入 `R6` 资产下载、`R7` runtime 探测、`R8/R9` workflow、CPU fallback、烟测封板或发布验证开发。

## 执行协议

如果你是当前执行本计划的 AI Agent，按以下顺序执行：

1. 完整阅读本文件。
2. 再阅读以下约束文档：
   - `DOCS/architecture-overview.md`
   - `DOCS/localization-agent-quick-reference.md`
   - `DOCS/maintainability-review.md`
   - `DOCS/refactoring-handover-guide.md`
3. 只领取“轮次看板”中当前应执行的一轮，不得跨轮合并。
4. 开始前更新“顶部状态区”，将“当前轮次”改为本轮编号，将“当前状态”改为 `In Progress`。
5. 只修改本轮允许范围内的代码和文档。
6. 完成本轮后更新“顶部交接区”。
7. 完成本轮后更新“轮次看板”，将本轮状态改为 `Completed`，写入执行日期与摘要。
8. 如果本轮未完成，必须将本轮状态标为 `Blocked` 或 `Review Needed`，并在“顶部交接区”明确阻塞项。
9. 未通过本轮验收前，不得开始下一轮。

## 总体目标

本计划的唯一目标是为 Vidvix 新增独立的 `AI` 工作区，并在首发范围内稳定交付以下能力：

- 左侧导航新增 `AI` 模块。
- `AI` 工作区只包含两个子模式：
  - `AI补帧`
  - `AI增强`
- AI 工作区允许导入多个视频素材，但单次执行只处理一个选中视频。
- 首发必须支持离线运行，不依赖用户自行安装 Python、PyTorch、CUDA、Conda。
- 首发必须可随 `Offline-win-x64` 一起发布。
- 首发不做批量 AI 队列，不做模型在线下载，不做实时对比预览。
- `AI增强` 的 UI 倍率固定为 `2x` 到 `16x`，默认 `2x`。

## 当前仓库基线

本执行手册基于以下已知仓库事实制定：

- 技术栈：`C#`、`WinUI 3`、`.NET 8`
- 现有左侧模块：`视频`、`音频`、`裁剪`、`合并`、`AI`、`拆音`、`终端`
- 现有集中式工作区配置：
  - `Core/Models/ApplicationConfiguration.cs`
  - `Core/Models/ProcessingWorkspaceProfile.cs`
- 现有大工作区与架构热点：
  - `ViewModels/MainViewModel.*`
  - `ViewModels/AiWorkspaceViewModel.cs`
  - `ViewModels/MergeViewModel.*`
  - `ViewModels/VideoTrimWorkspaceViewModel*`
  - `ViewModels/SplitAudioWorkspaceViewModel*`
  - `Views/AiPage.xaml`
- 现有运行时打包经验：
  - `Tools/ffmpeg`
  - `Tools/mpv`
  - `Tools/Demucs`
- 现有离线发布结论：当前已实际验证的官方离线路径是 `win-x64`
- 现有本地化体系：
  - `Resources/Localization/manifest.json`
  - `Resources/Localization/zh-CN/*.json`
  - `Resources/Localization/en-US/*.json`

## 统一设计结论

除非后续轮次明确批准变更，本计划默认采用以下设计结论。

### 产品边界冻结

- 左侧新增一级导航：`AI`
- `AI` 是独立工作区，不挂靠在视频、合并或拆音页内部
- 页面布局参考“合并模块”的结构感，但不复用其时间轴/轨道模型
- 页面固定为三块：
  - 素材列表
  - AI 工作区
  - 输出设置
- 素材列表只允许视频格式，不允许纯音频格式
- 可以导入多个视频，但单次执行时只允许一个当前输入
- 输出视频默认保留原音轨，除非源文件无音轨
- 首发输出格式只开放：
  - `MP4`
  - `MKV`
- `AI增强` 的倍率选项固定为：
  - `2x`
  - `3x`
  - `4x`
  - `5x`
  - `6x`
  - `7x`
  - `8x`
  - `9x`
  - `10x`
  - `11x`
  - `12x`
  - `13x`
  - `14x`
  - `15x`
  - `16x`
- `AI增强` 默认倍率：`2x`
- 高倍率告警阈值先冻结为：`>= 8x`

### 模型路线冻结

- `AI补帧` 首发路线：`RIFE`
- 首发可移植执行后端：`nihui/rife-ncnn-vulkan`
- `AI增强` 首发路线：`Real-ESRGAN`
- 首发可移植执行后端：`Real-ESRGAN-ncnn-vulkan`
- `AI增强` 首发模型档位固定为：
  - `Standard`：`realesrgan-x4plus`
  - `Anime`：`realesr-animevideov3`
- `AI增强` UI 倍率固定为 `2x` 到 `16x`
- `AI增强` 默认倍率：`2x`
- `AI增强` 不保留 `1x`
- `AI增强` 的底层原生倍率以实现期实测为准，默认按“模型只稳定支持 `2x` 和/或 `4x`”来设计执行规划
- 执行规划冻结如下：
  - 目标倍率等于原生倍率时：直接跑模型
  - 目标倍率可以由原生倍率精确组合时：按组合链路多次执行模型
  - 目标倍率不能精确组合时：先放大到最近可实现的更高倍率，再用 FFmpeg 缩回目标倍率
- 典型例子冻结如下：
  - `2x`：若存在原生 `2x`，直接跑一次；若只有原生 `4x`，则先跑 `4x` 再缩回 `2x`
  - `3x`：先跑到 `4x`，再缩回 `3x`
  - `4x`：直接跑一次 `4x`
  - `5x` / `6x` / `7x`：先跑到 `8x`，再缩回目标倍率
  - `8x`：优先走精确组合；若有 `2x` 与 `4x`，则优先 `4x * 2x`；若只有 `2x`，则 `2x * 2x * 2x`；若只有 `4x`，则先跑到 `16x` 再缩回 `8x`
  - `9x` 到 `15x`：先跑到 `16x`，再缩回目标倍率
  - `16x`：优先走精确组合；若有原生 `4x`，则 `4x * 4x`；若只有原生 `2x`，则 `2x * 2x * 2x * 2x`
- `AI增强` 在 `>= 8x` 时必须给出高负载提醒，提示用户机器负载、显存占用、耗时和失败风险会明显上升

### 运行时冻结

- 首发优先采用“外部进程 + FFmpeg 前后处理”的实现方式
- AI runtime 独立于 FFmpeg / Demucs，建议目录如下：

```text
Tools/
  AI/
    Rife/
      Bin/
      Models/
      Configs/
    RealEsrgan/
      Bin/
      Models/
      Configs/
    Licenses/
    Manifests/
```

- 不允许依赖用户电脑已有 Python、PyTorch、CUDA、Conda
- 不允许把模型或可执行文件散落到 `Views`、`ViewModels`、`Resources` 或仓库根目录

### 架构冻结

- 不允许把 AI 业务分支塞进：
  - `MergeViewModel`
  - `MainViewModel.Execution`
  - `MpvVideoPreviewService`
- AI 模块必须独立拆分状态对象、协调器和 workflow service
- 不允许新增一个包揽全部职责的 `AiWorkflowService`
- 不允许直接把合并模块的 ViewModel/XAML 整体复制为 AI 模块

### 本地化冻结

- AI 模块新增文案必须继续走 `Resources/Localization`
- 必须同时补齐：
  - `zh-CN`
  - `en-US`
- 必须新增：
  - `Resources/Localization/zh-CN/ai.json`
  - `Resources/Localization/en-US/ai.json`
- AI 工作区入口文案继续放在 `common.json`
- 推荐 key 前缀：
  - `common.workspace.ai.*`
  - `ai.page.*`
  - `ai.interpolation.*`
  - `ai.enhancement.*`
  - `ai.status.*`
  - `ai.dialog.*`

### 体积与法务冻结

- 软目标：AI 模块新增静态资产尽量控制在 `1.5 GB` 内
- 硬门槛：新增静态资产超过 `3 GB` 时，不允许直接进入主线首发包
- 不直接集成 `Video2X` 代码：`AGPL-3.0`
- 不直接集成 `Flowframes` 代码：`GPL-3.0`
- `VFIMamba`、`AnimeSR`、`SAFA` 等 Python/PyTorch 路线只做研究参考，不作为首发直接运行时

## 强制实现约束

所有轮次必须同时遵守以下约束：

- 不得在同一轮内同时做大规模 UI 改造、运行时接入、模型升级、测试铺设。
- 每一轮只交付一个清晰、可验证、可交接的小目标。
- AI 工作区首发不做：
  - 批量 AI 队列
  - 模型在线下载器
  - 自动内容分类切模型
  - 实时前后对比预览
  - 多视频连续补帧或增强
- `AI增强` 不允许保留 `1x` 占位选项或伪增强选项
- 下载上游 AI runtime、模型和配置时，不允许把整个上游仓库或压缩包原样塞进项目。
- 下载完成后，必须在同一轮内完成筛选、归位和清理，只保留运行所必需的 `exe`、`dll`、模型权重、配置文件、许可证和最小来源说明。
- 必须删除无用内容，包括但不限于：`.git`、示例媒体、截图、训练脚本、测试数据、CI 配置、issue 模板、无关文档、重复 README、压缩包残留和未使用模型。
- 新增用户可见文案时必须同时具备：
  - 稳定 key
  - `zh-CN` 文案
  - `en-US` 文案
  - 缺失回退策略
- 动态文本必须使用占位符，不得拼接整句。
- 任何轮次都不得用“先塞进大 ViewModel，后面再拆”作为临时方案。
- 在 `AI增强` 的 CPU fallback 未验证前，不得宣称 AI 模块已正式完成。

## 统一交付物

除轮次中特别注明外，各轮次交付物使用以下统一命名：

- 执行计划：`DOCS/ai-module-agent-execution-plan.md`
- AI UI 验证报告：`DOCS/ai-ui-validation-report.md`
- AI 运行时资产清单：`DOCS/ai-runtime-asset-inventory.md`
- AI 模块资源文件：`Resources/Localization/*/ai.json`
- AI 烟测项目：`tests/AiOfflineSmoke`
- AI 相关脚本：`scripts/*ai*.ps1` 或同级可识别命名

如果某轮未生成上述文件，必须在“顶部交接区”明确说明原因。

## 轮次看板

执行状态只能使用以下枚举值：

- `Pending`
- `In Progress`
- `Review Needed`
- `Completed`
- `Blocked`

| 轮次 | 阶段    | 标题                           | 状态      | 最近执行人 | 最近更新   | 摘要 |
| ---- | ------- | ------------------------------ | --------- | ---------- | ---------- | ---- |
| R1   | Stage 1 | 计划冻结与执行手册重写         | Completed | Codex      | 2026-04-23 | 已按统一执行手册格式重写文档，冻结首发范围、模型路线、法务红线、轮次拆分与交接规则。 |
| R2   | Stage 1 | AI 工作区骨架与配置接线        | Completed | Codex      | 2026-04-23 | 已接入 AI 导航、workspace profile、AiPage 空壳与双语资源注册，应用构建与启动验证通过。 |
| R3   | Stage 1 | AI UI 专项验证与对标审查       | Completed | Codex      | 2026-04-23 | 已输出 AI UI 验证报告，确认 AI 选中态未进入一致蓝色响应态，给出与合并模块的壳层差异、自适应问题和 R4 修复边界。 |
| R4   | Stage 1 | AI UI 对齐调整与视觉收口       | Completed | Codex      | 2026-04-23 | 已补齐 AI 导航蓝色选中态与共享 hover / pressed 反馈，微调 AI 图标，移除页内重复 hero，并将 AI 页面收口为 `260 / * / 320` 宽窗壳层与窄窗纵向自适应布局。 |
| R5   | Stage 1 | 素材列表、单视频约束与输出状态 | Pending   | N/A        | N/A        | 未开始 |
| R6   | Stage 2 | AI 模型与配置下载、筛选、归位  | Pending   | N/A        | N/A        | 未开始 |
| R7   | Stage 2 | AI runtime 打包与能力探测      | Pending   | N/A        | N/A        | 未开始 |
| R8   | Stage 3 | AI补帧工作流                   | Pending   | N/A        | N/A        | 未开始 |
| R9   | Stage 3 | AI增强工作流                   | Pending   | N/A        | N/A        | 未开始 |
| R10  | Stage 4 | 本地化、交互硬化与错误收口     | Pending   | N/A        | N/A        | 未开始 |
| R11  | Stage 4 | 烟测、离线发布验证与封板       | Pending   | N/A        | N/A        | 未开始 |

## 各轮详细执行说明

### R1 计划冻结与执行手册重写

目标：

- 冻结首发范围、模型路线、法务边界和轮次拆分。
- 把文档整理成后续 AI Agent 可直接执行的格式。

允许范围：

- 只允许修改计划文档和相关说明文档。
- 不修改业务逻辑。
- 不修改发布产物。

必须交付：

- 本文件
- 明确的轮次看板
- 明确的下一轮起点

验收标准：

- 后续 AI Agent 打开文档即可知道当前轮次、允许范围、验收标准和交接规则。

交接要求：

- 明确写出下一轮只做 AI 工作区骨架，不做 runtime 和 workflow。

### R2 AI 工作区骨架与配置接线

目标：

- ~~将 AI 工作区作为独立工作区接入左侧导航、集中式配置和空页面。~~

建议主文件范围：

- `Core/Models/ApplicationConfiguration.cs`
- `Core/Models/ProcessingWorkspaceProfile.cs`
- `Core/Models/ProcessingWorkspaceKind.cs`
- `ViewModels/MainViewModel.Workspace.cs`
- `Views/MainWindow.xaml`
- `Views/AiPage.xaml`
- `Views/AiPage.xaml.cs`

必须交付：

- ~~`AI` 左侧导航入口~~
- ~~AI 工作区 profile~~
- ~~AI 页面空壳~~
- ~~组合根基础接线~~

验收标准：

- ~~AI 导航可见~~
- ~~AI 页面可进入~~
- ~~现有视频、音频、裁剪、合并、拆音、终端模块不回归~~

交接要求：

- 标出 AI 工作区入口文案 key
- 标出 AI 页面主 View / ViewModel 入口文件

### R3 AI UI 专项验证与对标审查

目标：

- ~~对已接入的 AI 导航入口与 AI 页面壳层做专项 UI 验证，防止后续功能开发让界面风格、交互响应和布局规范继续跑偏。~~
- ~~以现有左侧导航和“合并模块”为主要对标对象，系统记录 AI 模块当前的视觉、交互和自适应差异。~~

允许范围：

- 只允许修改计划文档、验证报告和必要的截图/说明文件。
- 本轮不修 UI 代码，不新增功能状态，不改 runtime 与 workflow。

建议主文件范围：

- `Views/MainWindow.xaml`
- `Views/AiPage.xaml`
- `DOCS/ai-module-agent-execution-plan.md`
- `DOCS/ai-ui-validation-report.md`

必须交付：

- ~~`DOCS/ai-ui-validation-report.md`~~
- ~~AI 导航按钮选中态、悬停态、按下态、禁用态验证记录~~
- ~~AI 导航图标的尺寸、重心、清晰度、风格一致性验证记录~~
- ~~AI 页面与合并模块在边距、栅格、卡片层级、标题层级、控件密度上的对标记录~~
- ~~AI 页面在常见缩放与窗口宽度下的自适应验证记录~~
- ~~明确的问题清单，区分“必须修”“建议修”“可延后”~~

验收标准：

- ~~必须明确记录“点击 AI 按钮后，当前选中态是否与其他模块一致”，包括是否出现与既有模块一致的蓝色响应态~~
- ~~必须明确给出 AI 图标“保留 / 微调 / 更换”的结论~~
- ~~必须明确记录 AI 页面与合并模块哪些地方一致、哪些地方不一致~~
- 至少覆盖以下验证场景：
  - ~~默认窗口宽度~~
  - ~~较窄窗口宽度~~
  - ~~`100%`、`125%`、`150%` 缩放~~
- ~~本轮输出的问题清单足以支撑下一轮独立完成 UI 调整~~

交接要求：

- ~~明确写出下一轮 `R4` 只修复本轮已确认的 UI 问题，不顺手追加功能需求~~
- ~~明确标出哪些问题是阻断继续功能开发的项，哪些问题可在后续轮次再处理~~

### R4 AI UI 对齐调整与视觉收口

目标：

- ~~根据 `R3` 验证结论，对 AI 导航入口与 AI 页面壳层做增量式 UI 调整与视觉收口。~~
- ~~在不引入新功能耦合的前提下，使 AI 模块的外观、响应态和基本布局更贴近既有模块规范。~~

建议主文件范围：

- `Views/MainWindow.xaml`
- `Views/AiPage.xaml`
- `Views/AiPage.xaml.cs`
- `ViewModels/AiWorkspaceViewModel.cs`
- `Resources/Localization/*/ai.json`
- `DOCS/ai-ui-validation-report.md`

必须交付：

- ~~AI 导航按钮选中态 / 悬停态 / 按下态的必要调整~~
- ~~AI 图标的必要微调或替换~~
- ~~AI 页面壳层与合并模块在基础视觉规范上的对齐调整~~
- ~~`R3` 中已确认的自适应问题修复结果~~
- ~~回填后的 UI 调整验证结论~~

验收标准：

- ~~AI 按钮选中态与现有模块保持一致，不出现明显跳色、失焦或反馈缺失~~
- ~~AI 图标在左侧导航中的尺寸、重心和清晰度达到与现有模块同级的完成度~~
- ~~AI 页面在 `100%`、`125%`、`150%` 缩放和较窄窗口宽度下不出现明显错位、截断或布局崩坏~~
- ~~现有视频、音频、裁剪、合并、拆音、终端模块不因本轮 UI 调整发生回归~~

交接要求：

- ~~列出本轮已修复的 UI 问题和仍保留的非阻断项~~
- ~~明确下一轮 `R5` 才继续素材列表、单视频约束和输出状态，不要把功能开发并入本轮~~

### R5 素材列表、单视频约束与输出状态

目标：

- 完成 AI 页面的素材列表、模式切换壳层、单视频约束和基础输出设置状态。

建议主文件范围：

- `Views/AiPage.xaml`
- `ViewModels/AiWorkspaceViewModel.cs`
- `ViewModels/AiInputState.cs`
- `ViewModels/AiMaterialLibraryState.cs`
- `ViewModels/AiModeState.cs`
- `ViewModels/AiOutputSettingsState.cs`
- `Services/MediaImportDiscoveryService.cs`

必须交付：

- 视频-only 导入规则
- 多素材导入但单视频激活约束
- `AI补帧` / `AI增强` 模式切换壳层
- 输出格式基础状态

验收标准：

- 可导入多个视频
- 导入音频会被拒绝
- 任一时刻只有一个当前处理对象
- 模式切换不会污染输入状态

交接要求：

- 明确运行时所需的输入/输出参数模型
- 不要在本轮启动实际 AI 推理

### R6 AI 模型与配置下载、筛选、归位

目标：

- 从互联网下载 AI 模块首发所需的 runtime、模型权重和配置文件。
- 在同一轮内完成筛选、删杂、归位，避免把上游仓库残骸带入项目。

建议主文件范围：

- `Tools/AI/**`
- `scripts/*ai*.ps1`
- `DOCS/ai-runtime-asset-inventory.md`

必须交付：

- `RIFE` 所需的可执行文件、模型文件、配置文件
- `Real-ESRGAN` 所需的可执行文件、模型文件、配置文件
- `Tools/AI/Licenses/**`
- `Tools/AI/Manifests/**`
- `DOCS/ai-runtime-asset-inventory.md`

必须清理：

- 删除下载后无用的 `.git`、压缩包残留、临时解压目录
- 删除示例媒体、截图、训练脚本、测试数据、CI 配置、issue 模板、无关文档
- 删除未进入首发范围的多余模型和重复配置
- 删除不再需要的上游 README 副本，只保留必要许可证和最小来源说明

资产清单必须记录：

- 来源 URL
- 获取日期
- 版本或提交信息
- 保留文件列表
- 删除文件类别说明
- 每类文件的用途

验收标准：

- `Tools/AI` 下只有必要文件，没有整包上游仓库快照
- 所有保留文件都有明确用途
- 许可证文件已保留
- 资产清单能追溯每个保留文件的来源与用途

交接要求：

- 记录最终保留的目录结构
- 记录被删除的无用文件类别
- 记录后续 `R7` 需要直接引用的可执行文件、模型和配置路径

### R7 AI runtime 打包与能力探测

目标：

- 接入 AI runtime 目录、模型描述、GPU/CPU 能力探测和许可证随包输出方案。

建议主文件范围：

- `Services/*AI*`
- `Core/Models/*AI*`
- `Utils/AppCompositionRoot.cs`
- 发布相关配置文件

必须交付：

- `RIFE` runtime 解析器
- `Real-ESRGAN` runtime 解析器
- GPU/CPU 能力探测
- AI runtime 打包目录
- 第三方许可证随包输出方案

验收标准：

- `win-x64` 发布产物里能找到 AI runtime
- UI 能得到“GPU 可用 / CPU fallback 可用 / 缺失 runtime”的明确状态
- 如果 `AI增强` CPU fallback 未打通，必须在交接区标红

交接要求：

- 记录 runtime 目录结构
- 记录各模型文件名与版本
- 记录当前 CPU fallback 实际状态

### R8 AI补帧工作流

目标：

- 完成 `AI补帧` 的首发闭环。

建议主文件范围：

- `Services/IAiInterpolationWorkflowService.cs`
- `Services/AiInterpolationWorkflowService.cs`
- `ViewModels/AiInterpolationExecutionCoordinator.cs`
- AI 补帧相关请求/结果模型
- AI 页面补帧参数区

必须交付：

- 抽帧
- 调用 `RIFE`
- 合并原音轨
- 输出视频
- 进度反馈
- 取消能力

首发 UI 建议：

- 倍率：`2x`、`4x`
- 设备：`自动 / GPU优先 / CPU`
- 高级开关：`UHD 模式`

验收标准：

- 至少一条样例视频可完成 `2x` 补帧
- 取消后能清理临时目录
- 失败原因能区分 runtime 缺失、设备不可用、输入不合法、执行失败

交接要求：

- 记录推荐测试视频规格
- 记录补帧链路中最慢步骤

### R9 AI增强工作流

目标：

- 完成 `AI增强` 的首发闭环，并独立验证 `2x` 到 `16x`、超采样回缩链路和 CPU fallback。

建议主文件范围：

- `Services/IAiEnhancementWorkflowService.cs`
- `Services/AiEnhancementWorkflowService.cs`
- `ViewModels/AiEnhancementExecutionCoordinator.cs`
- AI 增强相关请求/结果模型
- AI 页面增强参数区

必须交付：

- 模型档位切换：`Standard` / `Anime`
- 倍率切换：`2x` 到 `16x`
- 默认倍率：`2x`
- 高倍率提醒：`>= 8x`
- 原生倍率直跑策略
- 非原生倍率超采样后回缩策略
- 增强输出与原音轨回填
- 进度反馈
- 取消能力

验收标准：

- `2x`、`4x`、`8x`、`16x` 中至少覆盖“原生倍率直跑”和“多次组合放大”两类路径
- `3x`、`5x`、`6x`、`7x`、`9x` 到 `15x` 中至少覆盖一条“超采样后回缩”路径
- CPU fallback 已验证，或被明确定义为阻断正式交付的问题

交接要求：

- 记录底层原生倍率集合到底是 `2x`、`4x`，还是两者同时可用
- 记录高倍率告警阈值的实测结论是否需要从 `8x` 调整
- 记录 CPU fallback 的验证结果和限制条件

### R10 本地化、交互硬化与错误收口

目标：

- 完成 AI 模块双语文案、语言刷新、运行中锁定和错误提示收口。

建议主文件范围：

- `Resources/Localization/manifest.json`
- `Resources/Localization/zh-CN/ai.json`
- `Resources/Localization/en-US/ai.json`
- `common.json`
- `ViewModels/AiWorkspaceViewModel.cs`
- `Views/AiPage.xaml`

必须交付：

- `ai.json` 双语资源
- AI 页面语言刷新入口
- 处理中锁定交互规则
- 输出目录反馈
- 统一错误提示

验收标准：

- `zh-CN -> en-US -> zh-CN` 切换时 AI 页面即时刷新
- 处理中不能误触发重复执行
- 文案缺失不会导致页面崩溃

交接要求：

- 记录 AI 模块 key 前缀
- 记录刷新入口位于哪些文件

### R11 烟测、离线发布验证与封板

目标：

- 完成 AI 模块最小可复现烟测、离线发布验证和最终文档封板。

建议主文件范围：

- `tests/AiOfflineSmoke/*`
- `scripts/*ai*.ps1`
- 发布配置文件
- 本文件

必须交付：

- `tests/AiOfflineSmoke`
- 生成短样例视频的脚本
- 离线发布验证清单
- 最终交接更新

验收标准：

- 不依赖仓库内置大视频素材
- 使用 FFmpeg 生成短样例视频即可完成烟测
- `AI补帧` 和 `AI增强` 至少各通过一条离线 smoke test
- `AI增强` 的 smoke test 至少覆盖一条“精确倍率”路径和一条“超采样后回缩”路径
- 发布产物在干净 `win-x64` 环境可启动、可运行、可输出

交接要求：

- 将本文件顶部状态区收口到最终状态
- 明确 AI 模块是否达到正式交付标准

## 每轮统一验证清单

从 `R2` 起，每轮都必须最少完成以下验证中的适用项，并在交接区写明结果：

- `dotnet build .\Vidvix.sln -c Debug -v minimal`
- 修改文件范围与本轮计划一致
- 新 key 已注册
- `zh-CN` / `en-US` 已同步补齐
- 无新增未处理异常

从 `R6` 起，额外必须完成：

- 资产清单存在性验证
- `Tools/AI` 目录清洁度验证
- 无上游整仓残留、无压缩包残留、无无用样例文件

从 `R7` 起，额外必须完成：

- AI runtime 存在性验证
- 当前机器能力探测验证

从 `R8` 起，额外必须完成：

- 至少一条真实视频样例跑通当前轮工作流
- 取消能力验证
- 输出文件可播放验证

从 `R9` 起，额外必须完成：

- `AI增强` 至少验证一条精确倍率样例和一条超采样回缩样例

从 `R10` 起，额外必须完成：

- 打开 AI 页面
- 切换 `zh-CN -> en-US -> zh-CN`
- 不重启应用
- 当前页面即时刷新

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
- 最近完成时间：`YYYY-MM-DD`
- 构建验证：`Passed` / `Failed` / `Not Run`
- 运行验证：`Passed` / `Failed` / `Not Run`
```

## 顶部交接区更新模板

完成本轮后，按以下模板更新：

```text
- 本轮完成项：<一句话概括本轮已完成内容>
- 本轮修改文件：<文件列表，逗号分隔>
- 本轮新增文件：<文件列表，逗号分隔>
- 本轮验证结果：<构建、运行、热切换、runtime、workflow 等结果>
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
- 一轮内同时吞并多个主目标
- 不记录交接信息就结束任务
- 未做构建验证就宣告完成
- 在 `AI增强` CPU fallback 未验证前宣告正式交付
- 删除本文件中的顶部状态区、顶部交接区、轮次看板
- 未经批准改动本文件已冻结的模型路线和法务边界

## 完成定义

只有在以下条件全部满足时，才能将本计划视为完成：

- `R1` 至 `R11` 全部标记为 `Completed`
- 顶部状态区显示最终轮次已完成
- 左侧导航存在独立 `AI` 工作区
- AI 页面只接受视频输入
- 单次执行只处理一个视频
- `Tools/AI` 下的下载资产已完成筛选、归位和清理，不存在上游整仓残留
- `AI补帧` 可在 GPU 与 CPU 模式中运行
- `AI增强` 至少在 GPU 模式稳定可用，且 CPU 路线已明确验证或明确阻断发布
- `AI增强` 的倍率选项为 `2x` 到 `16x`，默认 `2x`
- `AI增强` 的非原生倍率链路已明确落地为“超采样后回缩”
- 输出文件可播放
- AI 页面双语文案可热切换
- 离线发布验证通过

## 选型依据

- `rife-ncnn-vulkan`：<https://github.com/nihui/rife-ncnn-vulkan>
- `Practical-RIFE`：<https://github.com/hzwer/Practical-RIFE>
- `Real-ESRGAN`：<https://github.com/xinntao/Real-ESRGAN>
- `Real-ESRGAN-ncnn-vulkan`：<https://github.com/xinntao/Real-ESRGAN-ncnn-vulkan>
- `Real-ESRGAN` 动画视频模型文档：<https://github.com/xinntao/Real-ESRGAN/blob/master/docs/anime_video_model.md>
- `ONNX Runtime DirectML`：<https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html>
