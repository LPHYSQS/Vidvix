# 拆音模块离线运行时联调记录

核对日期：2026-04-18

## 结论

当前这套 `Demucs 4.0.1 + htdemucs_ft + 内置 FFmpeg + 内置离线 Python Runtime` 的方案，已经在本仓库内完成了两类真实输入的端到端离线验证：

1. 纯音频输入：通过
2. 带音轨视频输入：通过

本次验证结论可以明确写给后来者：

- 运行时阶段不依赖系统 Python
- 运行时阶段不依赖系统 PyTorch
- 运行时阶段不依赖联网下载模型
- 运行时阶段不依赖联网安装任何包
- 仓库内现有 `Tools/Demucs` 资产已经足够支撑首轮本地离线拆音

## 本次实际使用的仓库资产

### 模型仓

位置：`Tools/Demucs/Models`

仅保留了 `htdemucs_ft` 需要的 5 个文件：

- `htdemucs_ft.yaml`
- `f7e0c4bc-ba3fe64a.th`
- `d12395a8-e57c48e6.th`
- `92cfc3b6-ef3bcb9c.th`
- `04573f0d-f3cf25b2.th`

### 离线运行时包

位置：`Tools/Demucs/Packages/demucs-runtime-win-x64-cpu.zip`

本次通过的离线运行时组成如下：

- Python embeddable runtime：`3.10.11`
- `demucs==4.0.1`
- `torch==2.5.1+cpu`
- `torchaudio==2.5.1+cpu`
- `soundfile==0.13.1`

说明：

- 这是一个“运行时包”，不是开发环境快照
- 仓库里只保留了一个 zip，而不是把成千上万散文件直接铺进仓库
- zip 当前大小约 `251,495,590` 字节
- zip 当前约 `16,010` 个条目，后续仍有进一步瘦身空间，但不影响当前离线可运行性

## 本次验证入口

为了验证真实 C# 工作流而不是只验证 Python CLI，本次新增了一个可保留在仓库内的离线联调烟雾测试入口：

- `tests/SplitAudioOfflineSmoke/SplitAudioOfflineSmoke.csproj`
- `tests/SplitAudioOfflineSmoke/Program.cs`

这个入口直接复用了正式接入代码里的这些服务：

- `FFmpegRuntimeService`
- `FFmpegService`
- `MediaInfoService`
- `DemucsRuntimeService`
- `AudioSeparationWorkflowService`

也就是说，这次不是“Demucs 单独能跑”，而是“Vidvix 当前拆音核心链路能跑”。

## 本次已补完的工程化后续项

### 1. 可重复生成离线运行时包的脚本：已完成

新增：

- `scripts/demucs-runtime-lock.json`
- `scripts/build-demucs-runtime.ps1`

当前打包脚本已经把下面这些事情固化下来了：

- 锁定 Python embeddable runtime 版本
- 锁定 `demucs / torch / torchaudio / soundfile` 版本
- 自动下载 embeddable Python 与 `get-pip.py`
- 自动改写 `python310._pth`
- 自动安装运行时依赖
- 自动清理 `pip / wheel / __pycache__ / pyc`
- 自动重新输出 `Tools/Demucs/Packages/demucs-runtime-win-x64-cpu.zip`
- 自动做一次导入验证，确认 `demucs`、`torch`、`torchaudio`、`soundfile` 都能在清理后的运行时里正常导入

后来者如果需要重打包，直接在仓库根目录运行：

```powershell
.\scripts\build-demucs-runtime.ps1
```

### 2. 固定离线回归脚本：已完成

新增：

- `scripts/test-split-audio-offline.ps1`

这个脚本会自动完成：

- 构建 `tests/SplitAudioOfflineSmoke`
- 清掉 smoke harness 输出目录中的 `Tools/Demucs/Current`
- 先跑一次音频输入，验证“首次解压 + 拆音 + 导出”
- 再跑一次视频输入，验证“已解压复用 + 抽音 + 拆音 + 导出”
- 自动校验四轨输出文件是否齐全

直接运行命令：

```powershell
.\scripts\test-split-audio-offline.ps1
```

## 已完成的实际验证

### 1. 运行时解压验证

已验证 `DemucsRuntimeService` 在目标目录缺失时，会从：

- `Tools/Demucs/Packages/demucs-runtime-win-x64-cpu.zip`

自动解压到本地可运行目录，再由 C# 正常调用 `python.exe -m demucs.separate ...`。

这一步已经在首轮音频测试里实际触发并通过，不是理论判断。

### 2. 音频输入验证

验证方式：

- 先生成一个本地测试音频
- 调用 `SplitAudioOfflineSmoke`
- 输出格式设为 `.mp3`

验证结果：

- `FFmpeg` 标准化输入通过
- `Demucs` 四轨分离通过
- `FFmpeg` 二次导出四轨通过
- 最终输出 4 个文件：
  - `*_vocals.mp3`
  - `*_drums.mp3`
  - `*_bass.mp3`
  - `*_other.mp3`

### 3. 视频输入验证

验证方式：

- 先用仓库内 `Tools/ffmpeg/ffmpeg.exe` 生成带音轨的测试 MP4
- 调用 `SplitAudioOfflineSmoke`
- 输出格式设为 `.flac`

验证结果：

- `FFmpeg` 从视频中抽取并标准化音轨通过
- `Demucs` 四轨分离通过
- `FFmpeg` 导出四个最终 stem 通过
- 最终输出 4 个文件：
  - `input-video_vocals.flac`
  - `input-video_drums.flac`
  - `input-video_bass.flac`
  - `input-video_other.flac`

本次视频输入联调总耗时约 `13.1s`。

### 4. 可移植性核实

这一步专门回答一个问题：

- 没有系统 Python 和相关依赖环境的电脑，能不能跑

本次做法不是口头判断，而是做了一个“隔离环境实测”：

1. 使用 `tests/SplitAudioOfflineSmoke` 作为真实 C# 工作流入口
2. 运行前手动清掉 `tests/SplitAudioOfflineSmoke/bin/Debug/net8.0-windows10.0.19041.0/Tools/Demucs/Current`
3. 启动子进程时把 `PATH` 收敛到：
   - `C:\Windows\System32`
   - `C:\Windows`
   - `C:\Program Files\dotnet`
4. 同时清空：
   - `PYTHONHOME`
   - `PYTHONPATH`
5. 在这个隔离环境里先执行 `where python`
6. 再执行拆音
7. 同时监控实际被拉起的 `python.exe` 路径

实际结果：

- `where python` 返回“找不到文件”
- 拆音流程仍然成功
- 实际被拉起的解释器路径是：
  - `tests/SplitAudioOfflineSmoke/bin/Debug/net8.0-windows10.0.19041.0/Tools/Demucs/Current/python.exe`
- 最终仍然成功导出四轨：
  - `portable-proof-input_vocals.wav`
  - `portable-proof-input_drums.wav`
  - `portable-proof-input_bass.wav`
  - `portable-proof-input_other.wav`

这说明当前拆音链路运行时并不依赖系统 Python。

从代码层面看，这个结论也成立：

- `DemucsRuntimeService` 只会从应用自身的 `Tools/Demucs/Current` 或其解压结果里解析 `python.exe`
- `AudioSeparationWorkflowService` 在启动 Demucs 时，`ProcessStartInfo.FileName` 直接使用 `demucsRuntime.PythonExecutablePath`
- 整个流程没有任何“去 PATH 里找 python”的代码

## 本次联调里真正遇到过的问题

### 问题 1：只装 `demucs + torch + torchaudio` 还不够

在 Windows 上直接运行：

```text
python.exe -m demucs.separate -n htdemucs_ft --repo <Models> -o <Output> -d cpu <input.wav>
```

第一次真实推理结束后，保存 stem 时出现过这个错误：

```text
RuntimeError: Couldn't find appropriate backend to handle uri ... drums.wav and format None.
```

原因：

- `Demucs` 在 Windows 上导出音频时，运行时还需要 `soundfile`
- 只保证能“推理”还不够，还要保证能“写出四轨结果”

本次修复：

- 已将 `soundfile==0.13.1` 补进离线运行时
- 连带引入 `cffi` / `pycparser`

修复后结果：

- 直接 CLI 验证通过
- 通过 C# 工作流验证也通过

这条是已经踩实的坑，后续不要再删掉 `soundfile`

## 当前仍可继续优化的点

下面这些不是“当前缺失”，而是“当前已经能用，但还可以继续收敛”。

### 1. 继续做运行时瘦身

当前 zip 虽然可用，但内部仍然包含较多可进一步审视的内容，例如：

- `__pycache__`
- 一些 metadata
- 可再评估是否必须保留的附带文件

建议后续在“不破坏导入和推理”的前提下做一次白名单式瘦身，而不是激进去删。

### 2. 后续如有 CI，再把离线回归脚本接进去

当前仓库内已经有：

- `scripts/test-split-audio-offline.ps1`
- `tests/SplitAudioOfflineSmoke`

所以后面如果需要接持续集成，直接复用这条脚本化入口就行，不需要再重新发明回归流程。

## 后来者继续联调时的建议顺序

1. 先确认 `Tools/Demucs/Models` 仍然只有 `htdemucs_ft` 必需文件
2. 确认 `Tools/Demucs/Packages/demucs-runtime-win-x64-cpu.zip` 仍在
3. 先跑 `.\scripts\build-demucs-runtime.ps1`
4. 再跑 `.\scripts\test-split-audio-offline.ps1`
5. 如果需要单独调试，再直接跑 `tests/SplitAudioOfflineSmoke`
6. 最后再回到真实 WinUI 页面做交互联调

## 当前可以下的判断

截至 2026-04-18，本仓库拆音模块的“核心离线运行时接入”已经不是阻塞项。

真正已经完成的事情是：

- 模型仓已就位
- 离线运行时包已就位
- C# 工作流已接上
- 音频输入已通过
- 视频输入已通过
- 首次解压路径已通过
- 无外部 Python 环境的运行方式已验证通过

所以后续工作的重点，不再是“能不能跑”，而是：

- 体积进一步收敛
- 未来接入 CI 时复用现有脚本
- WinUI 页面层面的交互细节打磨
