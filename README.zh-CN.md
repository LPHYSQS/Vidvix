# Vidvix

[English](README.md)

<p align="center">
  <img src="Assets/Square44x44Logo.targetsize-256.png" alt="Vidvix 标志" width="160" />
</p>

<p align="center">
  <a href="https://get.microsoft.com/installer/download/9NWP4JVRJS74?referrer=appbadge" target="_self">
    <img src="https://get.microsoft.com/images/zh-cn%20dark.svg" width="200" alt="从 Microsoft Store 下载" />
  </a>
</p>

<p align="center">
  免费试用版可长期使用，不设使用期限，且与付费版提供相同的功能体验。
  如 Vidvix 对你有所帮助，欢迎通过 Microsoft Store 购买付费版，以支持作者持续进行后续更新与维护。
</p>

<p align="center">
  面向本地优先与离线场景的 Windows 媒体工具箱，覆盖转换、裁剪、合并、拆音与离线 AI 视频处理。
</p>

## 项目简介

Vidvix 是一个基于 WinUI 3、C# 与 .NET 8 构建的 Windows 桌面应用，把多种常见媒体工作流收拢到同一套桌面壳层里。它并不试图做成一款完整的非线性编辑器，而是更聚焦于任务型操作，例如格式转换、精确裁剪、媒体合并、轨道提取、拆音，以及离线 AI 视频处理。

这个项目从一开始就偏向本地优先与离线可用。媒体工具链、运行时检查、任务队列、预览、输出规划与结果定位都放在桌面端内部完成，不依赖云端服务才能工作。

## 主要亮点

- 以工作区为中心组织功能，覆盖视频、音频、裁剪、合并、AI、拆音、终端与关于页面
- 支持 `zh-CN` 与 `en-US` 双语界面，并可在应用内热切换
- 内置媒体与 AI 运行时资产，更适合离线使用和本地分发
- 裁剪与预览相关流程支持交互式预览与媒体详情查看
- 各工作区统一支持进度、取消、状态反馈与输出定位
- 支持系统托盘、主题偏好、窗口状态记忆与桌面快捷方式创建

## 工作区概览

| 工作区 | 主要职责 | 代表能力 |
| --- | --- | --- |
| 视频 | 批量视频处理 | 格式转换、视频轨提取、音频轨提取、字幕轨提取、队列执行 |
| 音频 | 音频格式转换 | 音频互转与统一输出规划 |
| 裁剪 | 单素材精确裁剪 | 音频/视频裁剪、预览、时间轴选区、快速换封装与完整转码 |
| 合并 | 多素材编排输出 | 视频拼接、音频拼接、音视频合成、输出命名与目录规划 |
| AI | 离线 AI 视频处理 | 补帧、增强、运行时探测、GPU 优先执行、状态跟踪 |
| 拆音 | 音轨分离 | 基于 Demucs 的四轨分离，输出 `vocals`、`drums`、`bass`、`other` |
| 终端 | 受控工具执行 | 内置 `ffmpeg`、`ffprobe`、`ffplay` 命令与实时日志 |
| 关于 | 产品信息入口 | 应用内关于、许可证与隐私信息 |

## 支持的格式

| 类别 | 内容 |
| --- | --- |
| 视频输入 | `mp4`, `mkv`, `mov`, `avi`, `wmv`, `m4v`, `flv`, `webm`, `ts`, `m2ts`, `mpeg`, `mpg` |
| 音频输入 | `mp3`, `m4a`, `aac`, `wav`, `flac`, `wma`, `ogg`, `opus`, `aiff`, `aif`, `mka` |
| 视频输出 | `MP4`, `MKV`, `MOV`, `AVI`, `WMV`, `M4V`, `FLV`, `WEBM`, `TS`, `M2TS`, `MPEG`, `MPG` |
| 音频输出 | `MP3`, `M4A`, `AAC`, `WAV`, `FLAC`, `WMA`, `OGG`, `OPUS`, `AIFF`, `AIF`, `MKA` |
| 字幕提取输出 | `SRT`, `ASS`, `SSA`, `VTT`, `TTML`, `MKS` |

## 内置引擎

| 资产 | 作用 |
| --- | --- |
| `Tools/ffmpeg` | 核心媒体处理、探测与终端执行 |
| `Tools/mpv` | 预览与嵌入式播放 |
| `Tools/Demucs` | 拆音工作区的离线音轨分离 |
| `Tools/AI/Rife` | 离线 AI 补帧 |
| `Tools/AI/RealEsrgan` | 离线 AI 视频增强 |

## 从源码构建

环境要求：

- Windows 10 `1809`（`10.0.17763.0`）或更高版本
- .NET 8 SDK
- 可构建 WinUI 3 应用的 Windows 开发环境

构建解决方案：

```powershell
dotnet build .\Vidvix.sln -c Debug -v minimal
```

运行本地构建产物：

```powershell
.\bin\x64\Debug\net8.0-windows10.0.19041.0\Vidvix.exe
```

使用当前主要维护的离线发布配置：

```powershell
dotnet publish .\Vidvix.csproj -c Release -p:PublishProfile=Offline-win-x64 -p:PublishDir=.\artifacts\publish\ -v minimal
```

当前验证状态：

- `Offline-win-x64` 是目前主要完成验证的离线发布目标。
- 仓库内的发布配置仍然围绕当前项目的发布流程维护；如果你在自己的机器上发布，建议覆盖 `PublishDir`，或者自行准备本地发布配置。

## 项目结构

```text
Vidvix/
  Assets/                 应用图标与视觉资源
  Core/                   接口、模型、枚举与工作流契约
  docs/                   架构与维护文档
  Resources/Localization/ 本地化清单与语言资源
  Services/               工作流、运行时、媒体与 Windows 集成
  Tools/                  内置媒体与 AI 运行时资产
  Utils/                  共享基础设施与组合根
  ViewModels/             工作区状态与主壳编排
  Views/                  WinUI 页面、控件与壳层行为
```

## 补充说明

- Vidvix 的定位是“多个聚焦工作流组成的工具箱”，而不是完整的非线性编辑软件。
- 终端工作区有明确白名单，只允许执行 `ffmpeg`、`ffprobe`、`ffplay`。
- 按当前验证基线来看，AI 增强的 CPU fallback 更适合视为有限支持，而不是主要交付路径。

## 相关文档

- [架构概览](docs/architecture-overview.md)
- [可维护性评审](docs/maintainability-review.md)
- [AI 运行时资产清单](docs/ai-runtime-asset-inventory.md)
- [AI 离线发布验证清单](docs/ai-offline-publish-validation-checklist.md)

## 许可证

项目采用 [PolyForm Noncommercial License 1.0.0](LICENSE)。隐私政策见 [PRIVACY.md](PRIVACY.md)。
