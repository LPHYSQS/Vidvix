# Vidvix

[简体中文](README.zh-CN.md)

Vidvix is a Windows desktop application for local-first media processing, editing-adjacent workflows, and offline AI-assisted video tasks. The project is built with WinUI 3, C#, and .NET 8, and is structured as a single Windows application with layered services, workspace-specific view models, and bundled runtime assets for media tooling.

## Overview

Vidvix is organized around dedicated workspaces instead of a single monolithic editor surface. Each workspace focuses on a well-defined task category while sharing common infrastructure such as runtime preparation, localization, user preferences, output planning, media inspection, and progress reporting.

## Workspace Matrix

| Workspace | Purpose | Notable capabilities |
| --- | --- | --- |
| Video | Batch-oriented video processing | Video format conversion, video-track extraction, audio-track extraction, subtitle-track extraction, queue-based processing, thumbnail-aware import items |
| Audio | Audio format conversion | Audio-only conversion workflow with shared output planning and centralized job execution |
| Trim | Single-item clip extraction | Audio/video trimming, interactive preview, timeline-based range selection, fast container conversion vs. full transcode, detailed media metadata display |
| Merge | Multi-source assembly | Video join, audio join, audio-video compose, track-aware import rules, output naming and directory planning |
| AI | Offline AI video workflows | AI interpolation and AI enhancement, runtime inspection, GPU-first execution, cancellation, status tracking, output reveal |
| Split Audio | Stem separation | Demucs-based four-stem separation into vocals, drums, bass, and other, CPU/GPU-preferred runtime selection, preview support |
| Terminal | Controlled FF tool execution | Built-in `ffmpeg`, `ffprobe`, and `ffplay` command execution with real-time output and command allowlisting |
| About | Product information | About, license, and privacy sections inside the desktop application |

## User-Facing Highlights

- Dual-language UI with hot-switch support for `zh-CN` and `en-US`
- Theme selection with system, light, and dark preferences
- System tray integration with hide-to-tray and restore behavior
- Desktop shortcut creation from within the application
- Window size and position persistence across launches
- Media detail side panel with section-level copy support
- Workspace-aware progress, status, cancellation, and output reveal flows
- Offline-oriented runtime packaging for media engines and AI assets

## Supported Media Formats

The following catalogs are derived from the repository configuration and represent the current application-level format registry.

| Category | Values |
| --- | --- |
| Video input | `mp4`, `mkv`, `mov`, `avi`, `wmv`, `m4v`, `flv`, `webm`, `ts`, `m2ts`, `mpeg`, `mpg` |
| Audio input | `mp3`, `m4a`, `aac`, `wav`, `flac`, `wma`, `ogg`, `opus`, `aiff`, `aif`, `mka` |
| General trim input | Combined video and audio catalogs above |
| AI input | Video input catalog |
| Split-audio input | Combined video and audio catalogs above |
| Video output catalog | `MP4`, `MKV`, `MOV`, `AVI`, `WMV`, `M4V`, `FLV`, `WEBM`, `TS`, `M2TS`, `MPEG`, `MPG` |
| Audio output catalog | `MP3`, `M4A`, `AAC`, `WAV`, `FLAC`, `WMA`, `OGG`, `OPUS`, `AIFF`, `AIF`, `MKA` |
| Subtitle extraction output catalog | `SRT`, `ASS`, `SSA`, `VTT`, `TTML`, `MKS` |

## AI and Media Workflow Coverage

- AI interpolation uses the bundled RIFE runtime and exposes interpolation-focused workflow controls.
- AI enhancement uses bundled Real-ESRGAN runtime assets with Standard and Anime-oriented model tiers.
- Merge supports three operational modes: video join, audio join, and audio-video composition.
- Audio-video composition includes reference-mode selection, extend strategies, original-audio mixing, gain adjustment, and fade-in/fade-out controls.
- Split Audio separates a single source item into four stems and keeps result management inside a dedicated workspace.
- Trim combines preview, selection, export strategy, and metadata in a single focused workspace rather than a full timeline editor.

## Technology Stack

| Area | Implementation |
| --- | --- |
| Desktop UI | WinUI 3, XAML, Windows App SDK `1.8.260209005` |
| Language and runtime | C#, .NET 8, unpackaged Windows desktop application |
| Application structure | Layered `Core`, `Services`, `ViewModels`, `Views`, and `Utils` folders |
| Composition model | Manual composition root via `Utils/AppCompositionRoot.cs` |
| Media tooling | FFmpeg, FFprobe, FFplay, mpv |
| Audio separation | Demucs runtime packages and model repository |
| AI video runtimes | RIFE and Real-ESRGAN bundled under `Tools/AI` |
| Windows integration | `AppWindow`, Win32 interop, Windows Forms `NotifyIcon`, COM shell-link creation |
| Localization | JSON resource files with manifest-driven language loading |

## Architecture Overview

- `Core/`
  Defines interfaces, application models, option catalogs, workflow requests/results, and shared domain-level abstractions.
- `Services/`
  Implements media workflows, runtime resolution, localization, file picking, media inspection, terminal execution, preview integration, and Windows-specific services.
- `ViewModels/`
  Coordinates workspace state, commands, progress, preferences, and user-facing orchestration without embedding the runtime engines directly into the views.
- `Views/`
  Contains WinUI windows, pages, controls, and code-behind for shell behavior, visual interaction, drag/drop, overlays, and tray-aware window lifecycle.
- `Utils/`
  Hosts shared infrastructure such as commands, observable base classes, path helpers, playback coordination, and the composition root.

## Bundled Runtime Assets and External Engines

| Asset | Role in Vidvix | Repository location |
| --- | --- | --- |
| FFmpeg / FFprobe / FFplay | Core media processing, probing, and terminal execution | `Tools/ffmpeg` |
| mpv | Embedded playback and preview surfaces | `Tools/mpv` |
| Demucs runtime packages and models | Offline source separation for the Split Audio workspace | `Tools/Demucs` |
| RIFE runtime and model assets | Offline AI interpolation | `Tools/AI/Rife` |
| Real-ESRGAN runtime and model assets | Offline AI enhancement | `Tools/AI/RealEsrgan` |
| AI manifests and third-party license files | Traceability and redistribution support for bundled AI assets | `Tools/AI/Manifests`, `Tools/AI/Licenses` |

The application prefers bundled runtimes when they are present. Where writable runtime extraction is required, the codebase includes fallback behavior for user-local storage under `%LOCALAPPDATA%`.
For the validated `Offline-win-x64` path, Split Audio also retries Demucs runtime/model extraction under `%LOCALAPPDATA%\\Vidvix\\Tools\\Demucs` if the application directory cannot be written reliably at first run.

## Repository Structure

```text
Vidvix/
  Assets/                 Application icons and packaged artwork
  Core/                   Interfaces, models, enums, workflow contracts
  docs/                   Architecture and validation notes
  Properties/             Launch settings and publish profiles
  Resources/Localization/ JSON localization resources and manifest
  scripts/                Runtime sync and smoke-validation scripts
  Services/               Workflow, runtime, media, and Windows service implementations
  tests/                  Script-driven offline smoke harness projects
  Tools/                  Bundled media and AI runtime assets
  Utils/                  Composition root and shared infrastructure
  ViewModels/             Workspace and shell state orchestration
  Views/                  Main window, pages, custom controls, converters
  Vidvix.csproj           Main WinUI application project
  Vidvix.sln              Standard Visual Studio solution
  Vidvix.slnx             Alternative solution representation kept in-repo
```

## Development Prerequisites

- Windows 10 version `1809` (`10.0.17763.0`) or later
- .NET 8 SDK
- A Windows development environment capable of building WinUI 3 applications
- Visual Studio with WinUI / Windows App SDK support is recommended for everyday development

## Build and Run

Build the solution:

```powershell
dotnet build .\Vidvix.sln -c Debug -v minimal
```

Launch the locally built application:

```powershell
.\bin\x64\Debug\net8.0-windows10.0.19041.0\Vidvix.exe
```

Create the primary offline release output:

```powershell
dotnet publish .\Vidvix.csproj -c Release -p:PublishProfile=Offline-win-x64 -v minimal
```

This is the only officially validated offline release path right now. The publish profile is intentionally fixed to:

- publish into `E:\SoftwareBuild\Vidvix\`
- use `win-x64`
- use self-contained deployment
- use `PublishSingleFile=true`

The publish pipeline fails fast if the single-file release is missing required external assets, `Tools` dependencies, or localization resources.

Primary publish output:

- official publish directory: `E:\SoftwareBuild\Vidvix\`
- mirrored internal offline package directory for release builds: `artifacts/publish-offline/`

## Validation and Smoke Coverage

This repository uses script-driven smoke validation for runtime-heavy workflows.

AI offline smoke validation:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-ai-offline.ps1 -RepoRoot .
```

Split-audio offline smoke validation:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-split-audio-offline.ps1 -RepoRoot .
```

Related harness projects:

- `tests/AiOfflineSmoke`
- `tests/SplitAudioOfflineSmoke`

## Publishing Model

- `WindowsPackageType=None`
- `WindowsAppSDKSelfContained=true`
- `PublishSingleFile=true` for the primary offline publish profile
- `RuntimeIdentifier=win-x64` for the primary offline publish profile
- `SelfContained=true` for the primary offline publish profile
- External runtime assets under `Tools/` are intentionally kept outside `Vidvix.exe` even in single-file publish mode
- The Demucs split-audio runtime stays packaged separately on purpose as part of this single-file release model
- `Offline-win-x64` is the only formally validated offline delivery target at the moment
- `dotnet publish -p:PublishProfile=Offline-win-x64` validates bundled `FFmpeg`, `mpv`, `Demucs`, `AI`, and localization assets before accepting the release output

Publish profiles are maintained under `Properties/PublishProfiles/`.

## Localization

Localization is manifest-driven and stored as JSON resources:

- Manifest: `Resources/Localization/manifest.json`
- Languages: `zh-CN`, `en-US`
- Resource groups: `common`, `settings`, `main-window`, `about`, `ai`, `trim`, `split-audio`, `merge`, `terminal`, `media-details`

Supplementary repository documentation under `docs/` is currently maintained primarily in Chinese.

## Notes and Constraints

- The primary validated offline release target is `win-x64`.
- The solution and project configuration expose broader architecture targets, but the repository-bundled third-party media binaries are currently verified around the x64 delivery path.
- The Terminal workspace intentionally allowlists only `ffmpeg`, `ffprobe`, and `ffplay`.
- AI enhancement CPU fallback should not be treated as production-ready on the current validated baseline.

## Additional Repository Documents

- `docs/architecture-overview.md`
- `docs/maintainability-review.md`
- `docs/ai-runtime-asset-inventory.md`
- `docs/ai-offline-publish-validation-checklist.md`
- `docs/ai-module-agent-execution-plan.md`
