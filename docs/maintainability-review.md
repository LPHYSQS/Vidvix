# Vidvix 可维护性审查

## 本轮审查目标

- 不改变现有功能行为，不删减任何现有模块。
- 保持界面文案为简体中文，避免引入英文兜底和乱码风险。
- 在可控风险下优先处理“长期维护成本高、但又不值得冒功能风险做大改”的耦合点。

## 本轮已落实的结构优化

- 把 FFmpeg 命令拼装从 `MainViewModel` 抽离到 `Services/FFmpeg/MediaProcessingCommandFactory`。
  现在 ViewModel 只表达“当前要处理什么”，不再直接持有大量容器、编码器和参数细节。
- 新增 `Core/Models/MediaProcessingContext` 与 `MediaProcessingCommandRequest`。
  这些模型把“业务上下文”和“命令构建输入”区分开，后续新增 AI 流程、批处理策略或更多导出模式时更容易复用。
- 新增 `Core/Models/ProcessingWorkspaceProfile`。
  音频/视频工作区的中文文案、拖拽提示、导入按钮文案、支持输入格式等规则统一收口，减少散落在 ViewModel 中的条件分支。
- 把 `Services/MediaInfo/MediaInfoService` 拆为 `MediaInfoService.cs / Probe.cs / Snapshot.cs / Formatting.cs / Models.cs`。
  当前服务主文件只保留缓存与入口协调，后续调整 ffprobe 参数、诊断输出或详情面板字段时，不必再在同一文件里来回跳转。
- 把 `Views/MainWindow.xaml.cs` 拆为 `MainWindow.xaml.cs / Chrome.cs / Overlays.cs / DragDrop.cs / WindowPlacement.cs`。
  这样标题栏配色、浮层动画、拖拽导入、窗口位置恢复和 Win32 互操作各自收口，后续修改窗口行为时更不容易误碰别的路径。
- 把 `Utils/AppCompositionRoot` 的对象装配改为服务分组。
  入口继续保持手动组合根，不引入额外 DI 依赖，但已经把基础设施、媒体运行时和业务工作流分层整理，降低新增服务时的接线成本。
- 新增标准 `Vidvix.sln` 与三套离线发布 `pubxml`。
  这两部分主要解决 IDE 兼容性和自包含发布的可重复性，不涉及功能行为变更。

## 本轮额外识别出的发布风险

- 仓库当前随附的 `Tools/ffmpeg/ffmpeg.exe`、`ffprobe.exe`、`Tools/mpv/mpv-1.dll` 与 `d3dcompiler_43.dll` 全部是 `x64` 二进制。
  这意味着目前已经被完整验证并可直接对外宣称的离线路径应是 `win-x64`。`win-x86` 与 `win-arm64` 的自包含发布现在可以产生产物，但若要保证这些原生架构下的媒体预览和离线运行时也完全无缺口，后续还需要补充匹配架构的第三方离线库。

## 目前仍然存在但这轮没有强拆的风险点

- `ViewModels/MainViewModel.Execution.cs`
  仍然承担执行编排、预检、日志反馈、取消控制和异常翻译，职责偏重。下一轮如继续整理，建议优先抽出“预检服务”和“批处理执行编排器”。
- `Services/MediaInfo/MediaInfoService.cs`
  文件体量较大，既负责调用 `ffprobe`，也负责结果解析和展示字段整形。后续更适合拆成“探测执行器 + 结果映射器 + 展示字段格式化器”。
- `Views/MainWindow.xaml`
  页面已经稳定，但内容密度较高。后续如果继续增加 AI 面板、批量规则或高级设置，建议把主窗口内容继续拆成独立用户控件，避免单个 XAML 继续膨胀。

## 后续扩展建议

- 新增处理模式时，优先扩展命令工厂和模式上下文，不要把新分支直接塞回 `MainViewModel`。
- 新增 AI 功能时，优先新增独立服务，例如分析服务、任务规划服务、结果解释服务，不要把远程调用和提示词逻辑直接放进 View 或 ViewModel。
- 若后续要支持更多工作区，先新增 `ProcessingWorkspaceProfile`，再接入对应导入规则和模式策略，避免复制一整套音频/视频判断分支。
