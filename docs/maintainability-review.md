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
