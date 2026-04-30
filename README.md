# Vidvix

[简体中文](README.zh-CN.md)

<p align="center">
  <img src="Assets/Square44x44Logo.targetsize-256.png" alt="Vidvix logo" width="160" />
</p>

<p align="center">
  Local-first Windows media toolbox for conversion, trimming, merging, split-audio, and offline AI video workflows.
</p>

## Overview

Vidvix is a WinUI 3 desktop application for Windows that brings several media workflows into one focused workspace. Rather than acting as a full nonlinear editor, it is built around practical task-based operations such as conversion, trimming, merging, track extraction, split-audio, and offline AI video processing.

The project is designed around local-first and offline-oriented use. Media tooling, runtime checks, queue execution, preview, output planning, and result reveal are all kept inside the desktop shell instead of depending on cloud services.

## Highlights

- Task-oriented workspaces for video, audio, trim, merge, AI, split audio, terminal, and about
- Dual-language UI with hot switching between `zh-CN` and `en-US`
- Bundled media and AI runtimes for offline-friendly workflows
- Interactive preview and media inspection for trimming and review-heavy tasks
- Unified progress, cancellation, and output reveal flows across workspaces
- System tray integration, theme preferences, window state persistence, and desktop shortcut creation

## Workspace Overview

| Workspace | Purpose | Key capabilities |
| --- | --- | --- |
| Video | Batch video processing | Format conversion, video-track extraction, audio-track extraction, subtitle-track extraction, queue execution |
| Audio | Audio conversion | Audio format conversion with shared output planning |
| Trim | Single-item clip extraction | Audio/video trimming, preview, timeline selection, fast container switch vs. full transcode |
| Merge | Multi-source assembly | Video join, audio join, audio-video compose, output naming and directory planning |
| AI | Offline AI video processing | Frame interpolation, enhancement, runtime inspection, GPU-first execution, status tracking |
| Split Audio | Stem separation | Demucs-based four-stem separation into `vocals`, `drums`, `bass`, and `other` |
| Terminal | Controlled tool access | Built-in `ffmpeg`, `ffprobe`, and `ffplay` execution with real-time logs |
| About | Product information | About, license, and privacy information inside the app |

## Supported Formats

| Category | Values |
| --- | --- |
| Video input | `mp4`, `mkv`, `mov`, `avi`, `wmv`, `m4v`, `flv`, `webm`, `ts`, `m2ts`, `mpeg`, `mpg` |
| Audio input | `mp3`, `m4a`, `aac`, `wav`, `flac`, `wma`, `ogg`, `opus`, `aiff`, `aif`, `mka` |
| Video output | `MP4`, `MKV`, `MOV`, `AVI`, `WMV`, `M4V`, `FLV`, `WEBM`, `TS`, `M2TS`, `MPEG`, `MPG` |
| Audio output | `MP3`, `M4A`, `AAC`, `WAV`, `FLAC`, `WMA`, `OGG`, `OPUS`, `AIFF`, `AIF`, `MKA` |
| Subtitle extraction output | `SRT`, `ASS`, `SSA`, `VTT`, `TTML`, `MKS` |

## Bundled Engines

| Asset | Role |
| --- | --- |
| `Tools/ffmpeg` | Core media processing, probing, and terminal execution |
| `Tools/mpv` | Embedded playback and preview |
| `Tools/Demucs` | Offline stem separation for the Split Audio workspace |
| `Tools/AI/Rife` | Offline AI frame interpolation |
| `Tools/AI/RealEsrgan` | Offline AI video enhancement |

## Build From Source

Requirements:

- Windows 10 `1809` (`10.0.17763.0`) or later
- .NET 8 SDK
- A Windows development environment capable of building WinUI 3 applications

Build the solution:

```powershell
dotnet build .\Vidvix.sln -c Debug -v minimal
```

Run the local build:

```powershell
.\bin\x64\Debug\net8.0-windows10.0.19041.0\Vidvix.exe
```

Publish with the primary maintained offline profile:

```powershell
dotnet publish .\Vidvix.csproj -c Release -p:PublishProfile=Offline-win-x64 -p:PublishDir=.\artifacts\publish\ -v minimal
```

Current validation status:

- `Offline-win-x64` is the primary validated offline publish target at the moment.
- The checked-in publish profile is maintained around the project's current release workflow, so contributors may want to override `PublishDir` or create a local profile for their own machine.

## Project Structure

```text
Vidvix/
  Assets/                 Application icons and artwork
  Core/                   Interfaces, models, enums, and workflow contracts
  docs/                   Architecture and maintenance notes
  Resources/Localization/ Localization manifest and language resources
  Services/               Workflow, runtime, media, and Windows integrations
  Tools/                  Bundled media and AI runtime assets
  Utils/                  Shared infrastructure and composition root
  ViewModels/             Workspace state and shell orchestration
  Views/                  WinUI pages, controls, and shell behavior
```

## Notes

- Vidvix is intentionally a collection of focused workflows, not a full nonlinear editing suite.
- The Terminal workspace intentionally allowlists only `ffmpeg`, `ffprobe`, and `ffplay`.
- On the current validated baseline, AI enhancement CPU fallback should be treated as limited support rather than a primary delivery path.

## Documentation

- [Architecture overview](docs/architecture-overview.md)
- [Maintainability review](docs/maintainability-review.md)
- [AI runtime asset inventory](docs/ai-runtime-asset-inventory.md)
- [AI offline publish validation checklist](docs/ai-offline-publish-validation-checklist.md)

## License

Released under the [PolyForm Noncommercial License 1.0.0](LICENSE). See [PRIVACY.md](PRIVACY.md) for the privacy policy included with the project.
