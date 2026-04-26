# Vidvix 离线发布验证清单

## 适用范围

- 项目：`Vidvix`
- 发布配置：`Offline-win-x64`
- 目标架构：`win-x64`
- 正式发布目录：`E:\SoftwareBuild\Vidvix\`
- 内部镜像目录：`artifacts\publish-offline\`
- 发布模式：`PublishSingleFile=true`、`SelfContained=true`
- 当前封板轮次：`R13`

## 封板前必须通过的检查

| 检查项 | 目标 | 当前封板口径 |
| ---- | ---- | ---- |
| Debug 构建 | `0` 警告、`0` 错误 | 必须通过 |
| Release 构建 | `0` 警告、`0` 错误 | 必须通过 |
| AI 补帧 smoke | `RIFE` 至少 1 条离线最小样例可跑通 | 必须通过 |
| AI 增强 smoke（精确倍率） | 至少 1 条精确倍率路径可跑通 | 必须通过 |
| AI 增强 smoke（超采样后回缩） | 至少 1 条超采样后回缩路径可跑通 | 必须通过 |
| 拆音 smoke | `Demucs` 首次离线解压与 `CPU / GPU 优先` 两条路径可跑通 | 必须通过 |
| 发布阶段自动校验 | `dotnet publish` 会拦截源资产缺失、成品缺失、镜像缺失 | 必须通过 |
| 发布参数检查 | 发布目录固定 `E:\SoftwareBuild\Vidvix\`，必须勾选“生成单个文件”，目标运行时固定 `win-x64`，部署方式固定“独立” | 必须通过 |
| 发布产物检查 | `Vidvix.exe`、`Vidvix.pri`、`Tools\\ffmpeg`、`Tools\\mpv`、`Tools\\AI`、`Tools\\Demucs`、本地化资源齐全 | 必须通过 |
| 启动验证 | publish 产物可启动并拉起 `Vidvix` 主窗口 | 必须通过 |
| 黑屏 / 白屏 / 闪退 | 主窗口可响应，无异常退出 | 必须通过 |

## 推荐执行顺序

1. `dotnet build .\Vidvix.sln -c Debug -v minimal`
2. `dotnet build .\Vidvix.sln -c Release -v minimal`
3. `powershell -ExecutionPolicy Bypass -File .\scripts\test-ai-offline.ps1 -RepoRoot .`
4. `powershell -ExecutionPolicy Bypass -File .\scripts\test-split-audio-offline.ps1 -RepoRoot .`
5. `dotnet publish .\Vidvix.csproj -c Release -p:PublishProfile=Offline-win-x64 -v minimal`
6. 启动 `E:\SoftwareBuild\Vidvix\Vidvix.exe`

## 正式发布参数

`Offline-win-x64` 的正式口径固定如下，不允许再改回旧配置：

- 发布目录：`E:\SoftwareBuild\Vidvix\`
- 配置：`Release`
- 目标运行时：`win-x64`
- 部署方式：`独立` / `SelfContained=true`
- 生成单个文件：`true`
- Windows App SDK 自包含：`true`

说明：

- “生成单个文件”必须勾选。
- 勾选单文件后，主程序与大部分运行库会尽量收口到单文件发布模型里，降低漏带运行库导致崩溃的风险。
- `Tools\` 目录不会并入 `Vidvix.exe`，而是继续作为外置资产随包输出；这属于刻意设计，其中 `Demucs` 运行时包本来就是为了配合单文件发布而单独打包保留的。

## 发布阶段自动校验范围

从 `R13` 起，`Offline-win-x64` 发布会在 `Vidvix.csproj` 中自动验证以下内容：

- 源目录存在：`FFmpeg`、`mpv`、`AI`、`Demucs`、本地化资源、图标等离线发布必需文件
- `E:\SoftwareBuild\Vidvix\` 中存在：
  `Vidvix.exe`、`Vidvix.pri`
- `E:\SoftwareBuild\Vidvix\Tools\` 中存在：
  `ffmpeg`、`mpv`、`AI`、`Demucs` 的关键可执行文件、模型、脚本、许可证和离线压缩包
- `artifacts\publish-offline\` 镜像目录中存在与正式产物相同的关键文件

如果上述任一文件缺失，`dotnet publish` 必须直接失败，不能继续产出“看起来成功、实际上缺依赖”的离线包。

## 本轮最终验证记录

- 执行日期：`2026-04-26`
- Debug 构建：`dotnet build .\Vidvix.sln -c Debug -v minimal` 通过，`0` 警告、`0` 错误。
- Release 构建：`dotnet build .\Vidvix.sln -c Release -v minimal` 通过，`0` 警告、`0` 错误。
- AI 补帧 smoke：覆盖 `x2 + CPU fallback`，验证输出存在、帧率翻倍、原音轨保留。
- AI 增强 smoke（精确倍率）：覆盖 `Anime 2x`，验证精确倍率链路、输出分辨率与原音轨保留。
- AI 增强 smoke（超采样后回缩）：覆盖 `Standard 3x -> 4x overscale -> 3x downscale`，验证回缩链路、输出分辨率与原音轨保留。
- 拆音 smoke：覆盖 `CPU` 与 `GPU 优先` 两条路径；首次运行会强制验证 `Demucs` 运行时离线解压。
- 拆音运行时修正：当应用目录写入失败时，`Demucs` 运行时与模型解压会自动回退到 `%LOCALAPPDATA%\Vidvix\Tools\Demucs`，避免在其他电脑上因目录权限导致首跑失败。
- 发布配置修正：`Offline-win-x64` 已固定为 `PublishDir=E:\SoftwareBuild\Vidvix\`、`PublishSingleFile=true`、`RuntimeIdentifier=win-x64`、`SelfContained=true`。
- 发布检查：`dotnet publish .\Vidvix.csproj -c Release -p:PublishProfile=Offline-win-x64 -v minimal` 通过；`E:\SoftwareBuild\Vidvix\` 与 `publish-offline` 镜像目录均已包含单文件主程序，以及 `FFmpeg / mpv / AI / Demucs` 所需离线依赖。
- 启动检查：`E:\SoftwareBuild\Vidvix\Vidvix.exe` 已成功启动，主窗口标题为 `Vidvix`，窗口句柄非 `0`，进程处于 `Responding=True`。

## 已冻结的真实限制

- `AI增强 / Real-ESRGAN` 的 `CPU fallback` 目前仍不能宣称可正式交付。
- 当前已知探测结论是：`realesrgan-ncnn-vulkan.exe -g -1` 在本机返回 `Unsupported / invalid gpu device`，因此本轮只允许继续做 smoke、发布验证和文档封板，不允许误写成“增强 CPU 路线已打通”。
- 如果后续需要“GPU 不可用时仍能正式交付 AI 增强”，必须单开新计划评估替代 runtime 或模型路线，不能在封板脚本里伪造 CPU 可用结论。
- 当前正式离线发布目标冻结为 `Offline-win-x64`。仓库中保留的 `x86 / arm64` 发布配置暂不作为首发交付口径，原因是随仓库维护并已验证的第三方媒体二进制当前以 `x64` 路径为准。
