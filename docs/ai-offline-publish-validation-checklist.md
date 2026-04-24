# Vidvix AI 离线发布验证清单

## 适用范围

- 项目：`Vidvix`
- 模块：`AI 工作区`
- 发布目标：`Offline-win-x64`
- 最终封板轮次：`R11`

## 封板前必须通过的检查

| 检查项 | 目标 | 当前封板口径 |
| ---- | ---- | ---- |
| Debug 构建 | `0` 警告、`0` 错误 | 必须通过 |
| Release 构建 | `0` 警告、`0` 错误 | 必须通过 |
| AI 补帧 smoke | `RIFE` 至少 1 条离线最小样例可跑通 | 必须通过 |
| AI 增强 smoke（精确倍率） | 至少 1 条精确倍率路径可跑通 | 必须通过 |
| AI 增强 smoke（超采样后回缩） | 至少 1 条超采样后回缩路径可跑通 | 必须通过 |
| 发布产物检查 | `Vidvix.exe`、`Tools\\AI`、`Tools\\ffmpeg`、本地化资源齐全 | 必须通过 |
| 启动验证 | Debug 与 publish 产物均可启动 | 必须通过 |
| 黑屏 / 白屏 / 闪退 | 主窗口可响应，无异常退出 | 必须通过 |

## 推荐执行顺序

1. `dotnet build .\Vidvix.sln -c Debug -v minimal`
2. `dotnet build .\Vidvix.sln -c Release -v minimal`
3. `powershell -ExecutionPolicy Bypass -File .\scripts\test-ai-offline.ps1 -RepoRoot .`
4. `dotnet publish .\Vidvix.csproj -c Release -p:PublishProfile=Offline-win-x64 -v minimal`
5. 启动 `bin\x64\Debug\net8.0-windows10.0.19041.0\Vidvix.exe`
6. 启动 `artifacts\publish\win-x64\Vidvix.exe`

## 本轮最终验证记录

- 执行日期：`2026-04-24`
- AI 补帧 smoke：覆盖 `x2 + CPU fallback`，验证输出存在、帧率翻倍、原音轨保留。
- AI 增强 smoke（精确倍率）：覆盖 `Anime 2x`，验证精确倍率链路、输出分辨率与原音轨保留。
- AI 增强 smoke（超采样后回缩）：覆盖 `Standard 3x -> 4x overscale -> 3x downscale`，验证回缩链路、输出分辨率与原音轨保留。
- 发布检查：以 `Offline-win-x64` 为唯一正式离线发布目标，要求 AI runtime、FFmpeg runtime 和本地化资源均随包输出。
- 启动检查：Debug 与 publish 产物均需确认主窗口标题为 `Vidvix`，窗口处于可响应状态，不能黑屏、白屏或闪退。

## 已冻结的真实限制

- `AI增强 / Real-ESRGAN` 的 `CPU fallback` 目前仍不能宣称可正式交付。
- 当前已知探测结论是：`realesrgan-ncnn-vulkan.exe -g -1` 在本机返回 `Unsupported / invalid gpu device`，因此本轮只允许继续做 smoke、发布验证和文档封板，不允许误写成“增强 CPU 路线已打通”。
- 如果后续需要“GPU 不可用时仍能正式交付 AI 增强”，必须单开新计划评估替代 runtime 或模型路线，不能在封板脚本里伪造 CPU 可用结论。
