# Vidvix 本地化快速定位手册（供 AI Agent 使用）

## 目的

这份文档只回答两件事：

1. 新增功能或新增 UI 文案时，应该改哪几个文件。
2. 新增一种语言时，应该补哪几个位置。

默认前提：

- 不重构现有本地化架构。
- 所有用户可见文案都继续走 `Resources/Localization/`。
- 回退语言固定为 `zh-CN`。
- 新文案必须同时补 `zh-CN` 和 `en-US`。

## 先看这里

本地化主入口：

- 服务：`Services/LocalizationService.cs`
- 接口：`Core/Interfaces/ILocalizationService.cs`
- 组合根接线：`Utils/AppCompositionRoot.cs`
- 语言持久化：`Services/UserPreferencesService.cs`
- 用户偏好字段：`Core/Models/UserPreferences.cs`
- 资源清单：`Resources/Localization/manifest.json`

资源目录固定为：

```text
Resources/Localization/
  manifest.json
  zh-CN/
    common.json
    settings.json
    main-window.json
    trim.json
    split-audio.json
    merge.json
    terminal.json
    media-details.json
  en-US/
    common.json
    settings.json
    main-window.json
    trim.json
    split-audio.json
    merge.json
    terminal.json
    media-details.json
```

## 新增 UI 文案时怎么定位

### 全局 / 通用 / 集中式配置

- 资源文件：`common.json`
- 典型前缀：`common.*`
- 主要代码入口：
  - `Core/Models/ApplicationConfiguration.cs`
  - `ViewModels/MainViewModel.Localization.cs`
- 适用场景：
  - 工作区标题、描述
  - 处理模式名称
  - 输出格式名称
  - 跨模块复用按钮 / 开关 / 语言名称

### 设置页

- 资源文件：`settings.json`
- 典型前缀：`settings.*`
- 主要代码入口：
  - `Views/Controls/ApplicationSettingsPane.xaml`
  - `ViewModels/MainViewModel.Localization.cs`
- 适用场景：
  - 语言切换
  - 外观
  - 系统托盘
  - 桌面快捷方式
  - 处理完成行为
  - 转码方式 / GPU 加速

### 主窗口外壳 / 公共反馈

- 资源文件：`main-window.json`
- 典型前缀：`mainWindow.*`
- 主要代码入口：
  - `Views/MainWindow.xaml`
  - `ViewModels/MainViewModel.cs`
  - `ViewModels/MainViewModel.Localization.cs`
  - `ViewModels/MainViewModel.ProcessingMessages.cs`
  - `ViewModels/MainViewModel.Import.cs`
  - `ViewModels/MainViewModel.Progress.cs`
- 适用场景：
  - 工具栏按钮
  - 页头说明
  - 公共进度区
  - 队列状态
  - 输出目录反馈
  - 导入 / 取消 / 失败提示

### 裁剪模块

- 资源文件：`trim.json`
- 典型前缀：`trim.*`
- 主要代码入口：
  - `Views/Controls/VideoTrimWorkspaceView.xaml`
  - `ViewModels/VideoTrimWorkspaceViewModel.cs`
  - `ViewModels/VideoTrimWorkspaceViewModel.Preview.cs`
- 服务侧入口：
  - `Services/TrimWorkflowService.cs`
  - `Services/VideoTrimWorkflowService*.cs`
- 适用场景：
  - 占位文案
  - 预览控制
  - 裁剪区间说明
  - 导出进度 / 成功 / 失败
  - smart trim 回退提示

### 拆音模块

- 资源文件：`split-audio.json`
- 典型前缀：`splitAudio.*`
- 主要代码入口：
  - `Views/SplitAudioPage.xaml`
  - `ViewModels/SplitAudioWorkspaceViewModel.cs`
  - `ViewModels/SplitAudioWorkspaceViewModel.Preview.cs`
  - `ViewModels/SplitAudioInputState.cs`
  - `ViewModels/SplitAudioProgressState.cs`
- 服务侧入口：
  - `Services/AudioSeparationWorkflowService.cs`
  - `Services/Demucs/DemucsExecutionPlanner.cs`
  - `Services/Demucs/DemucsRuntimeService.cs`
- 适用场景：
  - 输入区
  - 拆音设置
  - 结果卡片
  - 运行进度
  - Demucs 运行时错误 / 回退说明

### 合并模块

- 资源文件：`merge.json`
- 典型前缀：
  - `merge.mode.*`
  - `merge.summary.*`
  - `merge.status.*`
  - `merge.progress.*`
  - `merge.page.*`
  - `merge.page.item.*`
  - `merge.dialog.*`
- 主要代码入口：
  - `Views/MergePage.xaml`
  - `ViewModels/MergeViewModel.cs`
  - `ViewModels/MergeViewModel.Localization.cs`
  - `ViewModels/MergeViewModel.UiText.cs`
  - `ViewModels/MergeViewModel.Progress.cs`
  - `ViewModels/MergeViewModel.MediaMetadata.cs`
  - `ViewModels/MergeViewModel.AudioVideoCompose.cs`
- 模型侧入口：
  - `Core/Models/MediaItem.cs`
  - `Core/Models/TrackItem.cs`
  - `Core/Models/MergeMediaMetadataParser.cs`
- 适用场景：
  - 模式切换
  - 轨道空态
  - 主界面按钮 / 标签 / 占位符
  - 素材卡 / 轨道卡摘要
  - 运行态完成 / 失败 / 转码提示

### 终端模块

- 资源文件：`terminal.json`
- 典型前缀：`terminal.*`
- 主要代码入口：
  - `ViewModels/TerminalWorkspaceViewModel.cs`
  - `ViewModels/TerminalOutputEntryViewModel.cs`
- 服务侧入口：
  - `Services/FFmpegTerminalService.cs`
- 适用场景：
  - 命令输入区
  - 输出区
  - 状态标签
  - 拒绝 / 取消 / 失败说明

### 媒体详情

- 资源文件：`media-details.json`
- 典型前缀：`mediaDetails.*`
- 主要代码入口：
  - `ViewModels/MediaDetailPanelViewModel.cs`
  - `ViewModels/MainViewModel.Details.cs`
- 服务侧入口：
  - `Services/MediaInfo/MediaInfoService*.cs`
- 模型侧入口：
  - `Core/Models/MediaDetailField.cs`
- 适用场景：
  - 面板标题
  - 节标题
  - 字段标签
  - 复制反馈
  - ffprobe 失败 / 诊断信息

## 新增文案的标准步骤

1. 先决定文案属于哪个资源文件，不要跨模块乱放。
2. 在 `Resources/Localization/zh-CN/*.json` 和 `Resources/Localization/en-US/*.json` 同时补 key。
3. key 命名继续使用 `docs/localization-key-registry.md` 里的前缀规范。
4. 代码里统一使用 `GetString` / `Format` 或对应 ViewModel 的 `GetLocalizedText` / `FormatLocalizedText`。
5. 如果文案会缓存到属性或条目对象里，必须补刷新入口。

常见刷新入口：

- 主窗口：`MainViewModel.ApplyLocalizationState()`
- 裁剪：`VideoTrimWorkspaceViewModel.RefreshLocalization()`
- 拆音：`SplitAudioWorkspaceViewModel.RefreshLocalization()`
- 合并：`MergeViewModel.RefreshLocalization()`
- 终端：`TerminalWorkspaceViewModel.RefreshLocalization()`
- 详情：`MediaDetailPanelViewModel.RefreshLocalization()`

## 动态文案规则

- 统一使用占位符，不拼接整句。
- 参数名在双语里保持一致。
- 典型写法：

```text
mainWindow.message.outputDirectorySelected = 已将输出目录设置为：{path}
merge.status.processingLocked.moduleOperation = 当前{module}任务处理中，若需{operation}，请先取消当前任务。
```

## 如果新增的是“媒体详情字段”

这一类不要只补显示标签，还要补稳定字段 key。

必须同时看：

- `Core/Models/MediaDetailField.cs`
- `Services/MediaInfo/MediaInfoService.Snapshot.cs`
- `Core/Models/MergeMediaMetadataParser.cs`
- `ViewModels/VideoTrimWorkspaceViewModel.cs`
- `Resources/Localization/*/media-details.json`

原则：

- 解析逻辑优先按 `MediaDetailField.Key`，不要再按中文标签匹配。
- 新字段标签应放到 `mediaDetails.field.*`。
- 如果该字段会被合并模块 / 裁剪模块二次解析，必须同步补 parser 分支。

## 如果新增的是“集中式配置项”

先看：

- `Core/Models/ApplicationConfiguration.cs`
- `Resources/Localization/*/common.json`

适用对象：

- 新工作区
- 新处理模式
- 新输出格式
- 新合并模式
- 新拆音加速模式

不要把最终显示文案硬编码回配置模型；配置模型里保留稳定 key 或 key 前缀。

## 新增一种语言时怎么做

1. 在 `Resources/Localization/` 下新增语言目录，例如 `ja-JP/`。
2. 复制现有 8 个 JSON 文件结构，保持 key 全量一致。
3. 更新 `manifest.json`，把新语言登记进去。
4. 在 `common.json` 中补语言显示名 key，例如 `common.language.option.ja-jp`。
5. 切换语言后跑一次往返验证，至少覆盖：
   - 设置页
   - 主窗口
   - 裁剪
   - 拆音
   - 合并
   - 终端
   - 媒体详情

默认不需要新增代码分支；现有架构应只依赖资源和 manifest。

## 改完后最低验证清单

- `dotnet build .\Vidvix.sln -c Debug -v minimal`
- 资源 key 对齐校验：新增语言与现有语言不能缺 key
- `zh-CN -> en-US -> zh-CN` 或目标语言往返热切换烟测
- `powershell -ExecutionPolicy Bypass -File .\scripts\test-split-audio-offline.ps1 -RepoRoot .`
- 启动 `bin\x64\Debug\net8.0-windows10.0.19041.0\Vidvix.exe`，确认不黑屏、不白屏、不闪退

## 不要这样做

- 不要把新 UI 文案直接写回 XAML 常量或服务异常消息里。
- 不要只补 `zh-CN` 不补 `en-US`。
- 不要新增依赖中文标签解析的逻辑。
- 不要为单个页面再造一套新的本地化服务或刷新总线。
- 不要把 R12 之后的维护变成“大面积回炉重构”。
