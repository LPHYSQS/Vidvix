# Vidvix

[English](README.md)

Vidvix 是一个面向本地优先与离线交付场景的 Windows 桌面媒体处理应用，基于 WinUI 3、C# 与 .NET 8 构建。项目采用单应用、多工作区的组织方式，将媒体转换、裁剪、合并、AI 处理、拆音与内置终端等能力整合在同一套桌面壳层中，并通过内置运行时资产保障离线可用性。

## 项目概览

Vidvix 并不是单一页面上的“大杂烩”式工具，而是围绕明确的任务边界构建多个独立工作区。各工作区共享运行时准备、本地化、用户偏好、输出规划、媒体详情、进度反馈与结果定位等基础能力，同时保留各自的交互模型与执行流程。

## 功能矩阵

| 工作区 | 主要职责 | 代表能力 |
| --- | --- | --- |
| 视频 | 批量视频处理 | 视频格式转换、视频轨提取、音频轨提取、字幕轨提取、队列式处理、缩略图导入项 |
| 音频 | 音频格式转换 | 音频格式互转、统一输出规划、集中式任务执行 |
| 裁剪 | 单素材精确裁剪 | 音频/视频裁剪、交互式预览、时间轴选区、快速换封装与完整转码两类导出策略、媒体详情展示 |
| 合并 | 多素材编排输出 | 视频拼接、音频拼接、音视频合成、轨道感知导入规则、输出命名与目录规划 |
| AI | 离线 AI 视频工作区 | AI 补帧、AI 增强、运行时探测、GPU 优先执行、取消处理、结果定位 |
| 拆音 | 人声/伴奏等音轨分离 | 基于 Demucs 的四轨分离，输出 `vocals`、`drums`、`bass`、`other`，支持 CPU/GPU 优先运行时选择 |
| 终端 | 受控媒体命令执行 | 内置 `ffmpeg`、`ffprobe`、`ffplay` 命令执行，实时输出日志与状态 |
| 关于 | 产品信息入口 | 关于、许可证、隐私等应用内信息区块 |

## 产品侧亮点

- 支持 `zh-CN` 与 `en-US` 双语界面，并支持界面级热切换
- 支持系统主题、浅色主题、深色主题偏好
- 支持系统托盘隐藏/恢复
- 支持在应用内创建桌面快捷方式
- 支持窗口位置与尺寸记忆
- 支持媒体详情侧边栏与分区复制
- 各工作区统一支持进度、状态、取消、输出定位等反馈机制
- 面向离线分发场景，仓库内维护媒体与 AI 运行时资产

## 支持的媒体格式

以下格式目录来自仓库中的当前配置，代表应用层已注册的输入/输出格式集合。

| 类别 | 内容 |
| --- | --- |
| 视频输入 | `mp4`, `mkv`, `mov`, `avi`, `wmv`, `m4v`, `flv`, `webm`, `ts`, `m2ts`, `mpeg`, `mpg` |
| 音频输入 | `mp3`, `m4a`, `aac`, `wav`, `flac`, `wma`, `ogg`, `opus`, `aiff`, `aif`, `mka` |
| 裁剪输入 | 上述视频与音频输入格式的并集 |
| AI 输入 | 视频输入格式目录 |
| 拆音输入 | 上述视频与音频输入格式的并集 |
| 视频输出目录 | `MP4`, `MKV`, `MOV`, `AVI`, `WMV`, `M4V`, `FLV`, `WEBM`, `TS`, `M2TS`, `MPEG`, `MPG` |
| 音频输出目录 | `MP3`, `M4A`, `AAC`, `WAV`, `FLAC`, `WMA`, `OGG`, `OPUS`, `AIFF`, `AIF`, `MKA` |
| 字幕提取输出目录 | `SRT`, `ASS`, `SSA`, `VTT`, `TTML`, `MKS` |

## AI 与媒体工作流覆盖范围

- AI 补帧使用仓库内置的 RIFE 运行时。
- AI 增强使用仓库内置的 Real-ESRGAN 运行时，并提供 Standard 与 Anime 两类模型档位。
- 合并工作区包含三种主模式：视频拼接、音频拼接、音视频合成。
- 音视频合成支持参考模式选择、视频补足策略、原视频音频混合、音量调节以及淡入淡出控制。
- 拆音工作区围绕单文件输入与四轨结果管理构建，不与主转换队列混用。
- 裁剪工作区以“单素材预览 + 选区 + 导出”为核心，不试图模拟完整非线性编辑器。

## 技术栈与引用

| 领域 | 实现方式 |
| --- | --- |
| 桌面 UI | WinUI 3、XAML、Windows App SDK `1.8.260209005` |
| 语言与运行时 | C#、.NET 8、非打包 Windows 桌面应用 |
| 应用结构 | `Core`、`Services`、`ViewModels`、`Views`、`Utils` 分层 |
| 依赖装配 | 手工组合根，入口位于 `Utils/AppCompositionRoot.cs` |
| 媒体工具链 | FFmpeg、FFprobe、FFplay、mpv |
| 拆音引擎 | Demucs 运行时包与模型仓 |
| AI 运行时 | RIFE、Real-ESRGAN |
| Windows 集成 | `AppWindow`、Win32 互操作、Windows Forms `NotifyIcon`、COM 快捷方式 |
| 本地化 | 基于 JSON 的资源清单与语言资源文件 |

## 架构概览

- `Core/`
  放接口、模型、枚举、请求/结果对象与共享领域抽象。
- `Services/`
  放媒体工作流、运行时解析、本地化、文件选择、媒体探测、终端执行、预览与 Windows 集成服务。
- `ViewModels/`
  放工作区状态、命令、进度、偏好与界面编排逻辑，不直接把底层媒体引擎耦合进视图层。
- `Views/`
  放 WinUI 窗口、页面、自定义控件与必要的代码后置，用于处理壳层视觉、拖拽、浮层、托盘与窗口行为。
- `Utils/`
  放命令基类、可观察对象、路径工具、播放协调器以及应用组合根等基础设施。

## 内置运行时与外部引擎

| 资产 | 在 Vidvix 中的作用 | 仓库位置 |
| --- | --- | --- |
| FFmpeg / FFprobe / FFplay | 媒体处理、探测、终端命令执行 | `Tools/ffmpeg` |
| mpv | 预览与嵌入式播放表面 | `Tools/mpv` |
| Demucs 运行时与模型 | 拆音工作区的离线音轨分离能力 | `Tools/Demucs` |
| RIFE 运行时与模型 | AI 补帧 | `Tools/AI/Rife` |
| Real-ESRGAN 运行时与模型 | AI 增强 | `Tools/AI/RealEsrgan` |
| AI 清单与许可证 | AI 资产追溯与再分发说明 | `Tools/AI/Manifests`、`Tools/AI/Licenses` |

当应用目录中已存在内置运行时时，Vidvix 会优先直接使用；在需要可写提取目录的场景下，代码中也准备了面向 `%LOCALAPPDATA%` 的回退路径。

## 仓库结构

```text
Vidvix/
  Assets/                 应用图标与视觉资源
  Core/                   接口、模型、枚举、工作流契约
  docs/                   架构、验证与交接文档
  Properties/             启动设置与发布配置
  Resources/Localization/ 本地化清单与语言资源
  scripts/                运行时同步与离线 smoke 脚本
  Services/               工作流、运行时、媒体与系统服务实现
  tests/                  脚本驱动的离线烟测工程
  Tools/                  内置媒体与 AI 运行时资产
  Utils/                  组合根与共享基础设施
  ViewModels/             工作区与主壳状态编排
  Views/                  主窗口、页面、自定义控件、转换器
  Vidvix.csproj           主 WinUI 应用项目
  Vidvix.sln              标准 Visual Studio 解决方案
  Vidvix.slnx             仓库保留的替代解决方案表示
```

## 开发环境要求

- Windows 10 `1809`（`10.0.17763.0`）或更高版本
- .NET 8 SDK
- 可构建 WinUI 3 应用的 Windows 开发环境
- 日常开发建议使用带 WinUI / Windows App SDK 支持的 Visual Studio

## 构建与运行

构建解决方案：

```powershell
dotnet build .\Vidvix.sln -c Debug -v minimal
```

启动本地构建产物：

```powershell
.\bin\x64\Debug\net8.0-windows10.0.19041.0\Vidvix.exe
```

生成主离线发布产物：

```powershell
dotnet publish .\Vidvix.csproj -c Release -p:PublishProfile=Offline-win-x64 -v minimal
```

主要发布输出目录：

- `artifacts/publish/win-x64/`
- Release 发布后同步生成的离线目录：`artifacts/publish-offline/`

## 验证与烟测

本仓库对运行时密集型功能采用脚本驱动的 smoke 验证方式。

AI 离线 smoke：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-ai-offline.ps1 -RepoRoot .
```

拆音离线 smoke：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-split-audio-offline.ps1 -RepoRoot .
```

对应验证工程：

- `tests/AiOfflineSmoke`
- `tests/SplitAudioOfflineSmoke`

## 发布模型

- `WindowsPackageType=None`
- `WindowsAppSDKSelfContained=true`
- 主离线发布配置使用 `PublishSingleFile=false`
- `Tools/` 下的外部运行时资产有意保留为独立文件，不进入单文件束包路径

发布配置文件位于 `Properties/PublishProfiles/`。

## 本地化体系

本地化采用清单驱动的 JSON 资源体系：

- 清单：`Resources/Localization/manifest.json`
- 语言：`zh-CN`、`en-US`
- 资源分组：`common`、`settings`、`main-window`、`about`、`ai`、`trim`、`split-audio`、`merge`、`terminal`、`media-details`

## 当前边界与说明

- 当前主要完成验证的离线发布目标是 `win-x64`。
- 解决方案与项目配置层面暴露了更宽的架构目标，但仓库内随附的第三方媒体二进制当前以 x64 交付路径为主要验证基线。
- 终端工作区有明确白名单，只允许执行 `ffmpeg`、`ffprobe`、`ffplay` 三类内置命令。
- 以当前验证基线来看，AI 增强的 CPU fallback 不应被视为正式可交付能力。

## 补充文档

- `docs/architecture-overview.md`
- `docs/maintainability-review.md`
- `docs/ai-runtime-asset-inventory.md`
- `docs/ai-offline-publish-validation-checklist.md`
- `docs/ai-module-agent-execution-plan.md`

