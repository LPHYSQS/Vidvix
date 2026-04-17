# 全量转码模式修复交接说明

## 目标

把全局“转码方式 + GPU 加速”真正打通到以下全部处理链路，并让行为语义一致：

- 视频模块
  - 视频转换
  - 视频提取
  - 音频提取
- 音频模块
  - 音频转换
- 裁剪模块
  - 视频裁剪
  - 音频裁剪
- 合并模块
  - 视频拼接
  - 音频拼接
  - 音视频合成

本次修复默认继续复用现有全局偏好字段：

- `UserPreferences.PreferredTranscodingMode`
- `UserPreferences.EnableGpuAccelerationForTranscoding`

不要引入第二套“模块级转码设置”，也不要让合并模块自己维护一套与全局设置重复的偏好。

## 非目标

- 不新建新的设置页面。
- 不新增新的转码模式枚举。
- 不把字幕提取纳入本轮“全部打通”的主范围。
  - 字幕提取当前不适合套用“快速换封装 / 真正转码 / GPU”语义。
  - 本轮只需保证字幕提取在运行日志或说明中明确“不适用该设置”。

## 先读这些文件

建议严格按这个顺序读，能最快锁定问题位置：

1. `docs/architecture-overview.md`
2. `Core/Models/UserPreferences.cs`
3. `ViewModels/MainViewModel.Preferences.cs`
4. `ViewModels/MainViewModel.Execution.cs`
5. `Services/MediaProcessingWorkflowService.cs`
6. `Services/FFmpeg/MediaProcessingCommandFactory.cs`
7. `Services/FFmpeg/FFmpegVideoEncodingPolicy.cs`
8. `ViewModels/VideoTrimWorkspaceViewModel.cs`
9. `Services/TrimWorkflowService.cs`
10. `Services/VideoTrimWorkflowService.cs`
11. `Services/FFmpeg/VideoTrimCommandFactory.cs`
12. `Services/FFmpeg/AudioTrimCommandFactory.cs`
13. `ViewModels/MergeViewModel.cs`
14. `ViewModels/MergeViewModel.AudioVideoCompose.cs`
15. `Services/MergeMediaAnalysisService.cs`
16. `Core/Models/VideoJoinExportRequest.cs`
17. `Core/Models/AudioJoinExportRequest.cs`
18. `Core/Models/AudioVideoComposeExportRequest.cs`
19. `Services/VideoJoinWorkflowService.cs`
20. `Services/AudioJoinWorkflowService.cs`
21. `Services/AudioVideoComposeWorkflowService.cs`

## 当前事实快照

### 已经基本接通的链路

- 视频模块与音频模块的主处理链已经能把全局转码偏好带入 `MediaProcessingContext`。
- 视频裁剪已经能把全局转码偏好带入 `VideoTrimExportRequest`，并能单独探测 GPU 编码器。

### 当前不一致点

- 视频模块中的视频相关流程已经有较清晰的三态：
  - 快速换封装
  - 真正转码 CPU
  - 真正转码 GPU
- 音频模块、音频提取、音频裁剪虽然读取了 `TranscodingMode`，但绝大部分输出格式下“快速换封装”和“真正转码 CPU”命令几乎相同，只有个别格式存在差异。
- 视频裁剪支持 GPU 探测，但没有像主处理链那样提供 GPU 失败后回退 CPU 的二次重试。
- 合并模块三条导出链没有接入全局转码偏好：
  - `VideoJoinExportRequest` 没有 `TranscodingMode`
  - `AudioJoinExportRequest` 没有 `TranscodingMode`
  - `AudioVideoComposeExportRequest` 没有 `TranscodingMode`
  - 对应工作流也完全没有读取全局转码偏好

### 现有代码里的关键事实

- 全局设置持久化入口：
  - `ViewModels/MainViewModel.Preferences.cs`
- 主处理链执行上下文入口：
  - `ViewModels/MainViewModel.Execution.cs`
- 主处理链转码与 GPU 决策：
  - `Services/MediaProcessingWorkflowService.cs`
- 主处理链命令拼装：
  - `Services/FFmpeg/MediaProcessingCommandFactory.cs`
- 裁剪模块执行入口：
  - `ViewModels/VideoTrimWorkspaceViewModel.cs`
  - `Services/TrimWorkflowService.cs`
  - `Services/VideoTrimWorkflowService.cs`
- 合并模块执行入口：
  - `ViewModels/MergeViewModel.cs`
  - `ViewModels/MergeViewModel.AudioVideoCompose.cs`
- 合并模块导出工作流：
  - `Services/VideoJoinWorkflowService.cs`
  - `Services/AudioJoinWorkflowService.cs`
  - `Services/AudioVideoComposeWorkflowService.cs`

## 修复前必须先统一的语义

如果不先统一语义，后面每个模块都会按自己的理解分叉，最后继续不一致。

### 1. 快速换封装

定义为：

- 尽可能复用原始流。
- 如果目标输出和输入流天然兼容，则优先 `copy`。
- 如果只有部分流可以复用，则允许“可复用的流 copy，不可复用的流转码”。
- 如果当前流程本身存在滤镜、时间轴拼接、混音、补帧、补时长、裁剪填充等不可避免的重建步骤，则应：
  - 明确记录“快速换封装不可完全生效”
  - 决定是“部分流复用”还是“整体回落到兼容转码”

不要把“快速换封装”理解成“所有流程都必须做到全量 `-c copy`”。对合并和音视频合成来说，这本来就不现实。

### 2. 真正转码 CPU

定义为：

- 相关输出流全部显式重新编码。
- 视频流统一走 CPU 视频编码器。
- 音频流统一走目标格式对应的 CPU 音频编码器。
- 不做“能 copy 就 copy”的优化。

### 3. 真正转码 GPU

定义为：

- 只对视频编码链启用 GPU 视频编码器。
- 音频编码仍然走 CPU。
- 仅当输出格式支持硬件 H.264 编码时才真正启用 GPU。
- 如果流程里没有视频编码，或当前步骤只处理音频，则必须明确提示“GPU 不适用，本次继续走 CPU”。

### 4. GPU 失败的统一策略

统一为：

- 如果本次流程确实进入了 GPU 视频编码，并且 FFmpeg 执行失败，则自动回退到 CPU 再试一次。
- 如果本次流程从一开始就没有进入 GPU 编码，则不需要“回退”概念。

主处理链已经有这套机制，裁剪和合并要向它看齐，不要各写一半。

## 推荐的最短实施路线

### 第一步：补结构化媒体元数据，不要继续从 UI 文案反向解析

当前 `MediaDetailsSnapshot` 里只有：

- `HasVideoStream`
- `HasAudioStream`
- `MediaDuration`
- `OverviewFields / VideoFields / AudioFields / AdvancedFields`

这不足以可靠判断“是否可以快速换封装”。

建议补齐最少以下结构化字段：

- `PrimaryVideoCodecName`
- `PrimaryAudioCodecName`
- `PrimaryVideoFrameRate`
- `PrimaryAudioSampleRate`
- `PrimaryAudioChannelLayout`
- `PrimaryVideoWidth`
- `PrimaryVideoHeight`

如果 `MediaInfoService` 已经拿到了 ffprobe 的结构化结果，就直接在快照构建时填这些字段，不要再从中文标签文本里倒推。

原因：

- 中文展示字段可能改文案、改单位、改格式。
- 合并模块现在已经在 `MergeMediaMetadataParser` 里通过显示字段取分辨率 / 采样率 / 码率，这条路继续扩展到 codec 会越来越脆。

### 第二步：抽一层共享“转码决策”逻辑

不要让主处理链、裁剪链、合并链各自散落写一套判断。

建议新增一个共享决策层，名称可自定，例如：

- `TranscodingDecision`
- `TranscodingDecisionResolver`
- `TranscodingCompatibilityEvaluator`

最少需要统一输出这些结果：

- 当前流程是否处于 `FastContainerConversion` 还是 `FullTranscode`
- 当前流程是否允许尝试 GPU
- 当前输出格式是否支持硬件视频编码
- 当前输入是否允许走快速 copy
- 如果是合并 / 合成，是否允许“部分流复用”
- GPU 最终解析出的 `VideoAccelerationKind`
- 不适用原因或回退原因，用于日志提示

这层逻辑应被以下三条主线复用：

- 主处理链
- 裁剪链
- 合并链

### 第三步：把音频相关流程的“快速换封装”做成真的分支

这是本轮最容易被忽视，但必须补齐的点。

#### 涉及流程

- 视频模块中的音频提取
- 音频模块中的音频转换
- 裁剪模块中的音频裁剪
- 合并模块中的音频拼接

#### 目标行为

- 快速换封装：
  - 如果输入主音频编码和目标输出格式兼容，则 `-c:a copy`
  - 否则才转码
- 真正转码 CPU：
  - 始终显式音频编码
- 真正转码 GPU：
  - 记录“不适用 GPU，本次继续使用 CPU 音频编码”
  - 行为上应等价于“真正转码 CPU”

#### 建议兼容规则

至少先把最明确的一批做起来：

- `.mp3` -> 仅 `mp3` 可 copy
- `.aac` -> 仅 `aac` 可 copy
- `.m4a` -> 建议先只允许 `aac` copy
- `.flac` -> 仅 `flac` 可 copy
- `.wav` -> 仅 `pcm_s16le` 可 copy
- `.aif` / `.aiff` -> 仅 `pcm_s16be` 可 copy
- `.opus` -> 仅 `opus` 可 copy
- `.ogg` -> 仅 `vorbis` 可 copy
- `.wma` -> 仅 `wmav2` 可 copy
- `.mka` -> 如果要稳妥，先只允许已知 matroska 兼容并且无需重编码的情况 copy；否则直接保守转码

这一批规则宁可保守，也不要“看起来像快封装，实际和真正转码一样”。

### 第四步：让视频裁剪对 GPU 失败具备 CPU 回退

视频裁剪已经有：

- `TranscodingMode`
- `VideoAccelerationKind`
- GPU 编码器探测

但它缺的是：

- GPU 失败后二次回退 CPU 的完整执行链

建议直接向主处理链的模式靠齐：

- 第一次按 GPU 编码器执行
- 失败后把 `VideoAccelerationKind` 置为 `None`
- 第二次按 CPU 重试
- 日志和进度要明确提示“已回退 CPU”

这部分不要再另起一套完全不同的语义。

### 第五步：把合并模块三条工作流全部接入全局转码设置

这是本轮最大的缺口，也是必须修的部分。

#### 1. 视频拼接

当前 `VideoJoinExportRequest` 没有以下字段：

- `TranscodingMode`
- `VideoAccelerationKind`

至少先把这两个字段补进去。

#### 2. 音频拼接

当前 `AudioJoinExportRequest` 没有：

- `TranscodingMode`

如果想把日志保持一致，也可以补一个“是否请求 GPU”的上下文字段，但行为上音频拼接不需要真正启用 GPU。

#### 3. 音视频合成

当前 `AudioVideoComposeExportRequest` 没有：

- `TranscodingMode`
- `VideoAccelerationKind`

这两个字段要补，否则设置页里选了也没地方落。

## 合并模块的推荐实现方式

### 视频拼接：双路径

#### 快速换封装路径

只有在以下条件都满足时才走：

- 所有片段视频编码一致
- 分辨率一致
- 帧率一致或允许直接拼接
- 目标输出格式可容纳该视频编码
- 如果需要保留音频，则音频编码 / 采样率 / 声道布局也兼容
- 当前没有任何必须触发滤镜链的需求：
  - 不需要缩放
  - 不需要裁剪
  - 不需要黑边填充
  - 不需要补静音
  - 不需要统一 fps

这条路径建议改用 concat demuxer 或等价的无重编码拼接方案，而不是当前基于 `filter_complex` 的全量重建。

#### 真正转码路径

继续保留当前基于 `filter_complex` 的路径，但分成两支：

- CPU：沿用现有显式 CPU 编码器
- GPU：对视频编码套用 `FFmpegVideoEncodingPolicy.ApplyH264Encoding(...)`

#### GPU 失败处理

和主处理链一致：

- GPU 失败后自动回退 CPU 再试一次

### 音频拼接：双路径

#### 快速换封装路径

只有在以下条件满足时才走：

- 所有片段主音频编码一致
- 采样率一致
- 声道布局一致
- 当前参数模式不要求重采样或重定码率
- 目标输出格式允许保留该音频编码

可使用 concat demuxer + `-c:a copy`。

#### 真正转码路径

继续走当前滤镜链与音频编码逻辑。

#### GPU

不启用。只做说明和日志提示。

### 音视频合成：分流复用，不追求全量 copy

这里不要强行追求“快封装 = 整体 `-c copy`”，那会让设计失真。

推荐语义：

- 快速换封装：
  - 视频流在满足条件时尽量 copy
  - 音频流因为混音 / 替换 / 淡入淡出 / 音量调节，通常仍然要重编码
- 真正转码 CPU：
  - 视频和音频都显式重编码
- 真正转码 GPU：
  - 视频走 GPU 编码
  - 音频仍走 CPU 编码

#### 视频流可直接 copy 的条件

至少满足：

- 不需要 `loop` 视频
- 不需要 `freeze last frame`
- 输出容器支持当前视频编码
- 不需要改变视频尺寸、像素格式、帧率

如果这些条件不满足，就自动退到视频转码。

这条路径的核心目标不是“所有内容都 copy”，而是让“快速换封装”在音视频合成里仍然具备真实价值：

- 能保留视频就保留视频
- 必须处理的音频仍按目标格式编码

## 推荐新增或扩展的模型

### 应直接扩展而不是绕开的模型

- `MediaDetailsSnapshot`
- `VideoJoinSegment`
- `AudioJoinSegment`
- `AudioVideoComposeSourceAnalysis`
- `VideoJoinExportRequest`
- `AudioJoinExportRequest`
- `AudioVideoComposeExportRequest`

### `VideoJoinSegment` 建议新增

- `VideoCodecName`
- `AudioCodecName`
- `AudioSampleRate`
- `AudioChannelLayout`
- `InputContainerExtension`

### `AudioJoinSegment` 建议新增

- `AudioCodecName`
- `AudioChannelLayout`
- `InputContainerExtension`

### `AudioVideoComposeSourceAnalysis` 建议新增

- `VideoCodecName`
- `AudioCodecName`
- `VideoContainerExtension`
- `AudioContainerExtension`

至少保证“能否走快速路径”不需要再回头重新探测一次媒体信息。

## 哪些地方不要再复制逻辑

### 不要复制 GPU 能力探测

现有 GPU 能力探测已经在：

- `Services/FFmpeg/FFmpegVideoAccelerationService.cs`

应该统一复用，不要在合并模块再手写一套 `ffmpeg -encoders` 解析。

### 不要复制 H.264 GPU 编码参数

现有编码参数策略已经在：

- `Services/FFmpeg/FFmpegVideoEncodingPolicy.cs`

主处理链、裁剪链、合并链都应复用同一套：

- `h264_nvenc`
- `h264_qsv`
- `h264_amf`
- `libx264`

### 不要把全局设置重新存到 MergeViewModel 自己的私有字段里

合并模块已经通过 `_userPreferencesService` 维护自己的输出格式、目录、音量、淡入淡出等偏好。

这次修复不需要再给合并模块新增：

- `PreferredMergeTranscodingMode`
- `PreferredMergeGpuAcceleration`

应直接读取现有全局设置：

- `PreferredTranscodingMode`
- `EnableGpuAccelerationForTranscoding`

## 建议的代码改动顺序

按下面顺序改，风险最小：

1. 扩展 `MediaDetailsSnapshot` 的结构化字段。
2. 让 `MediaInfoService` 在构建快照时填好这些字段。
3. 新建或抽出共享的“转码决策层”。
4. 让音频提取 / 音频模块 / 音频裁剪先走真实的快封装与真正转码分支。
5. 给视频裁剪补 GPU -> CPU 回退。
6. 扩展合并模块三个 request 模型。
7. 扩展 `MergeMediaAnalysisService` 让 segment / analysis 拿到足够元数据。
8. 修视频拼接双路径。
9. 修音频拼接双路径。
10. 修音视频合成的“视频可复用，音频按需重编码”路径。
11. 最后统一补日志与设置说明文案。

## 易错点

### 1. 不要把“快速换封装”写成“无条件 copy”

这在合并模块和音视频合成里会直接失败。

### 2. 不要把“真正转码 GPU”理解成“音频也 GPU”

本项目里 GPU 只对视频编码有意义。

### 3. 不要继续靠展示字段里的中文标签做核心业务判断

显示字段适合 UI，不适合作为“能否 copy / 该用哪个编码器”的唯一依据。

### 4. 不要在多个工作流里各自维护一套“是否可硬编”的格式列表

现有列表已经在 `FFmpegVideoEncodingPolicy`。

### 5. 合并模块的快速路径大概率需要临时 concat list 文件

如果采用 concat demuxer，请集中处理临时文件创建与清理，不要把临时文件逻辑散到三个工作流里。

### 6. GPU 失败回退时要保留第一次失败信息

否则用户只能看到“最后 CPU 成功了”，看不到曾经触发过 GPU 失败回退。

## 建议的运行时提示文案

这些提示比设置页静态说明更重要，因为它们能解释“本次为什么没完全按你选的模式执行”。

- 快速换封装成功：
  - “当前素材与目标输出兼容，已优先复用原始流。”
- 快速换封装不可完全生效：
  - “当前流程包含缩放 / 拼接 / 混音 / 时长补齐等步骤，本次将回退为兼容转码。”
- GPU 不适用于音频任务：
  - “已开启 GPU 加速，但当前流程仅处理音频，本次继续使用 CPU 音频编码。”
- GPU 不适用于当前输出格式：
  - “已开启 GPU 加速，但当前输出格式不支持硬件视频编码，本次自动回退为 CPU 编码。”
- GPU 失败回退 CPU：
  - “GPU 编码失败，已自动回退为 CPU 重试一次。”

## 手工验证矩阵

至少准备以下素材组合验证，不要只测 happy path。

### 视频模块

- `H.264 + AAC` 的 `mp4 -> mp4`
  - 快速换封装应出现 `copy`
  - 真正转码 CPU 应出现 `libx264 + aac`
  - 真正转码 GPU 应出现 `h264_nvenc / h264_qsv / h264_amf + aac`
- `H.264 + AAC` 的 `mp4 -> webm`
  - 快速换封装不应强行 `copy`
  - 应回退到兼容转码
- 音频提取 `mp3 -> mp3`
  - 快速换封装应允许 `-c:a copy`
  - 真正转码 CPU 应显式 `libmp3lame`

### 音频模块

- `flac -> flac`
  - 快速换封装应允许 `copy`
  - 真正转码 CPU 应显式 `flac`
- `wav -> mp3`
  - 三态应清晰区分为：
    - 快速换封装不可用，回退兼容编码
    - 真正转码 CPU
    - 真正转码 GPU 说明不适用并走 CPU

### 裁剪模块

- 视频裁剪 `mp4 -> mp4`
  - 快速换封装应优先 `copy`
  - 真正转码 CPU / GPU 应区分
  - GPU 失败时应回退 CPU
- 音频裁剪 `mp3 -> mp3`
  - 快速换封装应允许 `copy`
  - 真正转码 CPU 应显式编码

### 合并模块

- 视频拼接，全部素材分辨率 / 帧率 / 编码完全一致
  - 快速换封装应走快路径
  - 真正转码 CPU / GPU 应走重编码路径
- 视频拼接，混合分辨率
  - 快速换封装应明确回退兼容转码
- 音频拼接，全部素材编码 / 采样率一致
  - 快速换封装应走 `copy`
- 音频拼接，采样率不一致
  - 快速换封装应明确回退兼容转码
- 音视频合成，无 loop / freeze / 淡入淡出 / 混音
  - 快速换封装应尽量保留视频流
- 音视频合成，启用 loop / freeze / fade / mix
  - 快速换封装应明确说明视频无法完全复用，进入部分流复用或兼容转码

## 完成定义

以下条件全部满足，才能算这次“全量修复”完成：

- 所有目标流程都能读取现有全局转码偏好。
- 音频相关流程不再出现“快速换封装”和“真正转码 CPU”几乎完全等价的问题。
- 视频裁剪具备 GPU 失败后 CPU 回退。
- 合并模块三条分支都不再是“假开关”。
- 日志能解释以下四类情况：
  - 走了快速换封装
  - 快速换封装不适用，已回退兼容转码
  - GPU 不适用
  - GPU 失败后已回退 CPU
- 设置页不需要增加新开关，但说明文案必须与真实行为一致。

## 给下一位接手者的最后建议

不要从 UI 往下零散补 patch。最容易失控的做法是：

- 在 `MergeViewModel` 里直接 if/else
- 在三个合并工作流里各自写一套 copy 条件
- 在音频模块、音频裁剪里各自硬补 codec 判断

正确方向是：

- 先补结构化媒体元数据
- 再抽共享转码决策
- 最后让主处理链、裁剪链、合并链都调用同一套规则

这样这次修完之后，后续不管是继续加格式、加编码器，还是补单元测试，都不会再回到“同一设置，四套解释”的状态。
