# 拆音模块接入交接说明（Demucs 路线）

本文是给后续接手拆音模块的人看的施工说明，不是功能介绍页。

核对日期：2026-04-18

## 先说结论

1. 拆音模块建议走 `Demucs 4.0.1 + htdemucs_ft` 这条线。
2. 目标不是把 Python 工程塞进仓库，而是做成和 `FFmpeg` 类似的“离线运行时组件 + C# 驱动层 + WinUI 界面层”。
3. 不要把 `Demucs` 源码仓、`pip` 缓存、整包 `site-packages`、测试文件、`__pycache__` 之类东西直接散落进仓库或发布目录。
4. 默认先只做 `win-x64 + CPU`，先保证任何没有安装 Python / PyTorch 的 Windows 机器都能跑，再考虑 GPU 和多架构。
5. 对外支持“导入带音轨的视频”与“导入纯音频”；对内统一先用现有 `FFmpeg` 抽成临时 `WAV`，再喂给 `Demucs`。
6. `Demucs` 只负责分离，最终导出格式仍然由现有 `FFmpeg` 负责，这样才能兼容当前 UI 里那一长串输出格式。

## 为什么是这条线

你当前项目的媒体能力已经很明确：

- `Vidvix.csproj` 已经在走自包含发布。
- `Tools\ffmpeg` / `Tools\mpv` 已经是“随程序一起走”的离线工具链。
- `Services\FFmpeg\FFmpegRuntimeService.cs` 已经把“运行时准备、可写目录回退、发布与运行目录差异”这类坑踩过一遍了。
- `Views\SplitAudioPage.xaml` 已经有拆音页壳子。
- `ViewModels\MainViewModel.Workspace.cs` 已经有拆音工作区切换入口。
- `Views\MainWindow.DragDrop.cs` 现在明确把拆音工作区拖拽禁掉了，说明这一块还没真正接上业务层。

也就是说，现在最适合的不是继续“实验性堆东西”，而是照着现有 `FFmpeg` 的分层方式，把拆音做成一条干净的新链路。

## 官方事实，先钉死

下面这些是本次核对过的点，后续不要再凭印象乱选版本：

- 截至 2026-04-18，`PyPI` 上 `demucs` 的最新正式发布版本仍是 `4.0.1`，发布时间是 `2023-09-07`。
- `facebookresearch/demucs` 这个 GitHub 仓库已经在 `2025-01-01` 被归档。
- 归档仓库的 README 明确提到重要修复会去 `adefossez/demucs`。
- 官方并没有给 Windows 终端用户准备一个“开箱即用 exe”；官方主路径仍然是 Python CLI。
- `htdemucs_ft` 是官方 README 里给出的四轨高质量模型路线，四轨分别是：
  - `vocals`
  - `drums`
  - `bass`
  - `other`
- `Demucs` 官方 CLI 支持：
  - `--repo` 指向本地模型仓
  - `-n` / `--name` 选择模型名
  - `-o` 指定输出目录
- 官方 CLI 自带的直接导出格式并不覆盖你当前 UI 中的所有音频格式，所以不要把最终导出格式控制权交给 `Demucs`。

这里最重要的一句是：

`Demucs` 是模型和推理能力，不是你的最终导出层；最终导出层应该继续是 `FFmpeg`。

## “最高版本”在这里具体指什么

如果后续有人看到“最高版本”就开始乱搜，那这里直接定口径：

- 模型/主版本口径：用 `Demucs 4.0.1`
- 四轨高质量口径：用 `htdemucs_ft`
- 不选六轨实验模型
- 不选来源不明的第三方魔改包
- 不追“网上谁打包了个 exe”

原因很简单：

- 你要的是稳定四轨，不是实验功能。
- 你要的是可移植和可维护，不是一次性跑通。
- 你要的是和 WinUI/C# 工程接得住的方案，不是再引入一个黑盒。

## 推荐的总架构

建议后续把拆音做成下面这四层：

1. `UI 层`
   - 继续使用 `Views\SplitAudioPage.xaml`
   - 页面只负责选择文件、选择输出目录、展示状态、展示结果

2. `工作流层`
   - 新增独立拆音工作流服务
   - 负责校验输入、调度 FFmpeg、调用 Demucs、整理结果、回写 UI 状态

3. `运行时层`
   - 新增一个 Demucs 运行时准备服务
   - 职责类似 `FFmpegRuntimeService`
   - 负责确保离线运行时包已经解压到可执行位置

4. `工具层`
   - `FFmpeg`：前处理和后处理
   - `Demucs Runtime`：纯拆音推理

建议的调用关系：

```text
SplitAudioPage / SplitAudio ViewModel
    -> AudioSeparationWorkflowService
        -> FFmpegService（抽音为 WAV）
        -> DemucsRuntimeService（确保运行时可用）
        -> Process 启动 Demucs
        -> FFmpegService（把四个 stem 转为最终输出格式）
```

## 最关键的工程决策

### 1. 不要让 Demucs 直接面对视频输入

虽然产品功能是“导入视频或音频进行拆音”，但工程内部不要这么做。

正确做法：

1. 如果输入是视频，先用现有 `FFmpeg` 提取主音轨为临时 `WAV`
2. 如果输入本来就是音频，也统一转成临时 `WAV`
3. 把这个标准化后的 `WAV` 交给 `Demucs`

这样做的好处：

- `Demucs` 运行时只需要处理 `WAV`
- 依赖面收窄很多
- 输入稳定，排错简单
- 和你现有 `FFmpeg` 体系自然衔接

这部分属于基于官方依赖说明做出的工程收敛，不是 README 的原句，但这是当前项目里最稳的做法。

### 2. 不要让 Demucs 负责最终导出格式

你当前拆音页已经列出了很多音频输出格式，但 `Demucs` CLI 的导出能力并不适合承担这件事。

正确做法：

1. `Demucs` 统一输出中间 `WAV`
2. 再用现有 `FFmpeg` 把四个 stem 转成用户选中的最终格式

好处：

- UI 不需要因为模型换掉而重写格式逻辑
- 所有格式兼容性仍然走你熟悉的 `FFmpeg`
- 后续如果要加码率、采样率、命名规则，也都还能复用现有媒体处理思路

### 3. 默认只做 CPU 运行时

第一次接入不要碰 GPU。

原因：

- 你明确要求“没装 Python / PyTorch 的电脑也要能跑”
- GPU 版会额外引入 CUDA 依赖、驱动匹配、显卡差异
- 这会直接破坏“可移植性优先”这个目标

建议顺序：

1. `win-x64 + CPU` 跑通
2. 离线发布跑通
3. 新机器干净环境跑通
4. 再讨论可选 GPU 包

## 仓库里应该保留什么，不该保留什么

### 推荐的仓库形态

最理想是把 Demucs 相关内容压成“少量受控文件”，而不是展开成大量源文件。

推荐保留：

```text
Tools/
  Demucs/
    Packages/
      demucs-runtime-win-x64-cpu.zip
    Models/
      htdemucs_ft.yaml
      f7e0c4bc-ba3fe64a.th
      d12395a8-e57c48e6.th
      92cfc3b6-ef3bcb9c.th
      04573f0d-f3cf25b2.th
    LICENSES/
      ...
```

如果还想进一步减少仓库文件数，可以把模型也打成一个独立 zip：

```text
Tools/
  Demucs/
    Packages/
      demucs-runtime-win-x64-cpu.zip
      demucs-model-htdemucs_ft.zip
```

然后在运行时首次解压到：

```text
%LOCALAPPDATA%\Vidvix\Tools\Demucs\Current\
```

这条路线和你当前 `FFmpegRuntimeService` 的设计最像，也最适合做离线发布。

### 明确禁止入库的东西

下面这些一律不要直接塞进仓库：

- `Demucs` 整个源码仓
- 本地虚拟环境
- `pip` 下载缓存
- `wheel` 缓存
- 大量 `site-packages` 散文件
- `__pycache__`
- `tests`
- `.git`
- 任何“从网上找来的 Demucs exe 黑盒包”

一句话概括：

仓库里要放“可发布资产”，不要放“开发现场垃圾”。

## 运行时应该怎么准备

推荐路线不是“要求用户安装 Python”，而是“应用自己带运行时”。

### 推荐方案

使用 Windows 的嵌入式 Python 运行时思路，或者把它进一步封成你自己的离线运行时包。

建议最终效果是：

- 用户电脑不需要预装 Python
- 用户电脑不需要预装 PyTorch
- 程序第一次运行时，C# 服务检测本地是否已有 `Demucs` 运行时
- 没有就从程序自带的离线包解压到本地
- 后续都从本地固定路径启动

### 为什么不要直接要求系统 Python

因为这样会立刻出现下面这些坑：

- 用户没装 Python
- 用户装了错误版本 Python
- 用户 PATH 里有多个 Python
- 用户没装 torch / torchaudio
- 用户装了 GPU 版 torch 但机器不支持
- 用户能运行 demo，但发布版不能运行

这些坑都和你的产品无关，没必要替用户承担。

## C# 侧怎么驱动

不要做 Python 内嵌调用，也不要做 COM，也不要直接把模型逻辑搬进 C#。

最稳妥的方式仍然是：

- C# 用 `Process` 启动外部程序
- 标准输出和标准错误都重定向
- 参数使用明确的参数列表，不要自己拼整条字符串命令

建议的命令思路：

```text
python.exe -m demucs.separate -n htdemucs_ft --repo "<本地模型目录>" -o "<临时输出目录>" -d cpu "<输入wav>"
```

注意：

- `--repo` 很关键，它能强制走本地模型仓，不让程序运行时偷偷联网下载。
- `-n htdemucs_ft` 很关键，不要让模型名漂移。
- 输出目录不要直接指向最终用户目录，先落临时目录，后面再整理。

## 输出目录为什么一定要走临时目录

官方 CLI 的默认输出结构更偏向命令行工具，不适合直接暴露给产品 UI。

它会天然带上：

- 模型名目录
- 轨道名目录
- stem 文件名

这对用户来说过于“技术化”。

正确做法：

1. 让 Demucs 输出到临时目录
2. 由 C# 工作流层读取四个 stem
3. 统一重命名
4. 按你的产品规则复制到最终输出目录

例如最终用户看到的结果应该更像：

```text
原文件名_vocals.wav
原文件名_drums.wav
原文件名_bass.wav
原文件名_other.wav
```

而不是把 Demucs 原始目录结构直接端给用户。

## 跟当前 WinUI 架构怎么接

后续接入时，优先参考这些落点：

- `Views\SplitAudioPage.xaml`
  - 已有拆音页壳子
  - 现阶段更像静态页面

- `Views\SplitAudioPage.xaml.cs`
  - 现在只有输出目录选择和格式说明更新
  - 说明页面级交互壳已经在

- `ViewModels\MainViewModel.SplitAudio.cs`
  - 当前只暴露了切换到拆音工作区的命令

- `ViewModels\MainViewModel.Workspace.cs`
  - 已经有 `SplitAudio` 工作区状态、可见性、日志集合、导入集合
  - 这是现有入口，不是完整业务层

- `Views\MainWindow.DragDrop.cs`
  - 目前拆音工作区拖拽是禁用的
  - 真正接上导入校验后再放开

- `Core\Models\ApplicationConfiguration.cs`
  - 当前 `SplitAudio` 工作区还是占位描述
  - 而且 `SupportedInputFileTypes` 现在是空的
  - 这和产品目标不符，后续必须改成“音频 + 视频输入都支持”

- `Vidvix.csproj`
  - 目前只把 `Tools\ffmpeg` / `Tools\mpv` 纳入发布
  - 后续 Demucs 离线运行时也应该走同样的发布策略

### 非常重要的一点

不要继续把拆音逻辑硬塞回 `MainViewModel`。

更合理的方向是：

- 给拆音做独立工作流服务
- 如果状态继续增多，再给拆音做独立 workspace view model

否则这个模块一旦接上运行时准备、输出列表、取消任务、错误回显、进度状态，很快又会把 `MainViewModel` 撑胀。

## 一条推荐的端到端流程

建议后续实现严格按下面的顺序走：

1. 用户导入文件
2. 校验输入
   - 音频：直接接受
   - 视频：必须至少有一条可用音轨
3. 用现有 `FFmpeg` 抽取/转成标准临时 `WAV`
4. `DemucsRuntimeService` 确保本地运行时存在
5. `DemucsRuntimeService` 确保本地模型仓存在
6. C# 启动 `Demucs`
7. 等待四个 `WAV stem` 生成
8. 用现有 `FFmpeg` 把四个 `WAV` 转为最终输出格式
9. 结果入列表
10. 清理临时目录

这里最值得强调的是：

拆音工作流本质上是“FFmpeg 前处理 -> Demucs 推理 -> FFmpeg 后处理”。

## 必踩坑，提前写死

### 坑 1：把 Demucs 当成 FFmpeg 那种单 exe 工具

这是错的。

`Demucs` 官方不是这种分发方式，所以不要到处找“神秘 exe 包”。

### 坑 2：把整个 Python 世界搬进项目

这也是错的。

你要的是“可发布运行时”，不是“开发环境快照”。

### 坑 3：让运行时偷偷联网下载模型

这是错的。

既然目标是离线可移植，就必须：

- 模型预先准备好
- C# 显式指定本地模型仓
- 发布包内就包含所需模型

### 坑 4：一上来就做 GPU

这是错的。

第一阶段要的是“任何普通 Windows 电脑都能跑”，不是“极限速度”。

### 坑 5：把最终导出格式交给 Demucs

这会把现有 UI 的格式能力打散掉。

最终导出格式仍然应该统一交给 `FFmpeg`。

### 坑 6：直接把 Demucs 的输出目录结构展示给用户

这会让 UI 变得非常难看，而且后续改命名规则会非常痛苦。

### 坑 7：浮动依赖版本

不要写这种东西：

```text
torch>=...
torchaudio>=...
demucs>=...
```

一定要锁定版本，尤其是上游已经归档的前提下。

### 坑 8：没验证架构就一起发 x86 / ARM64

当前项目虽然总体有多架构发布配置，但你手上的媒体二进制现实上是先按 `x64` 路线验证的。

Demucs 也应该先遵守这个原则。

先做：

- `win-x64`

再决定要不要补：

- `win-x86`
- `win-arm64`

## 推荐的验收标准

后续谁接这个模块，至少要通过下面这些检查，才算走对了：

1. 一台全新的 Windows x64 机器，没有 Python、没有 PyTorch，也能运行拆音。
2. 导入一个带音轨的视频，能够正常拆出四轨。
3. 导入一个纯音频文件，也能正常拆出四轨。
4. 四轨名称明确是：
   - `vocals`
   - `drums`
   - `bass`
   - `other`
5. 输出格式可以继续走当前 UI 所支持的格式选择。
6. 仓库里没有出现海量 Python 散文件。
7. 发布目录没有出现“跑一台机器行，换一台机器就缺环境”的情况。
8. 运行时不依赖联网下载模型。

## 官方模型文件口径

如果后续要手工整理本地模型仓，`htdemucs_ft` 这条线至少要对上官方仓库里的这些文件：

```text
htdemucs_ft.yaml
f7e0c4bc-ba3fe64a.th
d12395a8-e57c48e6.th
92cfc3b6-ef3bcb9c.th
04573f0d-f3cf25b2.th
```

不要自己改文件名，不要随便删，不要用来路不明的替代文件。

## 最后一句交接建议

后续实现时，请把目标始终锁定为这句话：

“像 FFmpeg 一样把 Demucs 做成 Vidvix 的离线核心组件，而不是把一个 Python 项目硬塞进 WinUI 项目里。”

只要守住这条线，拆音模块就不会再走偏。

## 参考链接

- Demucs 归档上游仓库：
  - <https://github.com/facebookresearch/demucs>
- Demucs 作者维护的 fork：
  - <https://github.com/adefossez/demucs>
- Demucs PyPI 页面：
  - <https://pypi.org/project/demucs/>
- Demucs 官方 README 中关于模型与 CLI 的说明：
  - <https://github.com/facebookresearch/demucs>
- Demucs 官方本地模型仓文件列表：
  - <https://raw.githubusercontent.com/facebookresearch/demucs/main/demucs/remote/files.txt>
- `htdemucs_ft` 官方模型定义：
  - <https://raw.githubusercontent.com/facebookresearch/demucs/main/demucs/remote/htdemucs_ft.yaml>
- Python Windows embeddable package 文档：
  - <https://docs.python.org/3.10/using/windows.html>
