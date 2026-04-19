# Vidvix 后续架构重构交接指引

## 文档目的

这份文档写给下一位继续维护 Vidvix 的工程师。

当前项目已经做过一轮“增量式整理”，分层、职责拆分、命令工厂、模式配置、组合根分组等都比之前更好，但整体上仍然是“功能先落地，架构随后补偿”的状态。它还没有烂到必须推倒重来，但如果继续沿着现在的方式叠加新功能，后续耦合度会再次迅速升高。

这份文档的目标不是要求你重写整个项目，而是帮助你在不冒高风险的前提下，持续把项目往“高内聚、低耦合、可回归验证”的方向推进。

## 建议阅读顺序

1. `DOCS/architecture-overview.md`
2. `DOCS/maintainability-review.md`
3. 本文档

前两份文档回答的是“已经做了哪些整理”；本文回答的是“下一步还该怎么拆，拆到什么程度算有效”。

## 当前阶段的总体判断

### 结论

Vidvix 现在最大的问题，不是单个 bug，而是以下三类结构性耦合仍然较重：

- 工作区级 ViewModel 仍然承担了太多职责。
- 若干核心 Service 仍然同时承担“平台适配 + 流程编排 + 规则判断 + 状态管理”。
- UI 平台细节、偏好持久化、媒体处理规则、外部进程控制，在若干热点模块中仍然交织在一起。

### 现阶段的典型信号

下面这些文件体量本身不等于问题，但它们是很强的热点信号：

- `ViewModels/MergeViewModel.cs` 约 1993 行
- `ViewModels/MergeViewModel.AudioVideoCompose.cs` 约 1237 行
- `ViewModels/VideoTrimWorkspaceViewModel.cs` 约 1666 行
- `ViewModels/VideoTrimWorkspaceViewModel.Preview.cs` 约 708 行
- `ViewModels/SplitAudioWorkspaceViewModel.cs` 约 732 行
- `ViewModels/SplitAudioWorkspaceViewModel.Preview.cs` 约 654 行
- `Services/VideoPreview/MpvVideoPreviewService.cs` 约 1667 行
- `Services/VideoTrimWorkflowService.SmartTrim.cs` 约 804 行
- `Views/MergePage.xaml` 约 1875 行
- `Views/MainWindow.xaml` 约 1211 行

另外还有两个值得特别记住的信号：

- `ViewModel` 层里仍然直接使用了 `Microsoft.UI.Xaml.Visibility`、`Symbol` 等 WinUI 类型，这说明“界面表现细节”还在向状态层渗透。
- `tests` 下当前只有一个正式测试项目 `tests/SplitAudioOfflineSmoke`，说明自动化回归面仍然偏薄，后续任何重构都必须带着“验证面补齐”一起做。

## 这次之后不要再回退的基本共识

后续任何新增功能，都不应该再把下面这些债重新加回去：

- 不要再让 `View` 或 `code-behind` 直接调用 FFmpeg、ffprobe、Demucs、mpv 或文件系统细节。
- 不要再让重量级 `ViewModel` 同时承担“状态 + 业务规则 + 流程编排 + 偏好持久化 + 平台事件翻译”。
- 不要再把平台类型继续引入 `Core` 或业务协调层。
- 不要再给单个 Service 持续叠加“顺手处理一下”的逻辑，直到它变成新的总控类。
- 不要把 `partial` 当作长期终点。`partial` 只是过渡期的减压手段，不是最终边界。

## 建议的目标架构

不要推倒重建，也不建议现在强上复杂 DI 框架或完整 Clean Architecture 模板。对这个项目更合适的是“在现有目录基础上，逐步做职责下沉和边界收缩”。

建议把项目逐步收敛到下面四层：

### 1. Presentation 层

职责：

- 视图渲染
- 交互事件绑定
- 展示态拼装

包含：

- `Views`
- 精简后的 `ViewModels`
- Converter / UI-only mapper

要求：

- 不直接知道外部进程细节
- 不直接知道 FFmpeg 参数拼装
- 不直接依赖 Win32 / mpv / Demucs 的底层生命周期

### 2. Application 层

职责：

- 一个用例如何执行
- 多个 Service 如何被协调
- 取消、进度、结果、失败翻译

建议承载对象：

- `*Coordinator`
- `*UseCase`
- `*WorkflowOrchestrator`
- `*SessionState`

这一层是后续解耦的主战场。

### 3. Domain / Policy 层

职责：

- 模式规则
- 输出策略
- 轨道接纳规则
- 分辨率 / 参数决策
- 输入合法性判断

要求：

- 尽量纯粹
- 尽量不依赖 UI 和平台
- 优先接受模型输入，返回模型结果

### 4. Infrastructure 层

职责：

- FFmpeg / ffprobe / Demucs / mpv / 文件系统 / 偏好存储 / 窗口平台适配

要求：

- 只暴露稳定接口
- 不反向吸收 UI 状态
- 外部进程生命周期、取消、超时、清理必须统一收口

## 下一任最应该优先处理的耦合点

### 一类：工作区级 ViewModel 过重

这类文件的问题不是“代码多”，而是职责混杂：

- 导入状态
- 输出设置
- 偏好持久化
- 执行状态
- 预览生命周期
- 进度反馈
- 错误文案
- UI 控件可见性

这些职责一旦都留在一个类里，后续任何需求都会变成“顺手再加一个字段和一个命令”，最终继续膨胀。

优先处理顺序建议如下：

1. `MergeViewModel`
2. `VideoTrimWorkspaceViewModel`
3. `SplitAudioWorkspaceViewModel`
4. `MainViewModel`

#### `MergeViewModel` 推荐拆法

先不要直接重写整个合并模块，而是先拆成以下对象：

- `MergeSessionState`
  负责当前模式、选中素材、输出目录、输出格式、处理状态等纯状态。
- `MergeTrackCollectionCoordinator`
  负责轨道集合增删、模式切换后的轨道投影、非法素材剔除。
- `MergeOutputSettingsState`
  负责文件名、输出目录、输出格式、偏好回写。
- `MergeExecutionCoordinator`
  负责启动合并、取消、进度同步、结果收口。
- `MergeModePolicy`
  负责不同模式下的轨道接纳规则、显示规则、默认值规则。

拆完后的目标不是“文件数量变多”，而是让 `MergeViewModel` 变成一个薄壳：只负责绑定命令和协调几个子对象。

#### `VideoTrimWorkspaceViewModel` 推荐拆法

优先拆成：

- `TrimInputSessionState`
- `TrimSelectionState`
- `TrimExportCoordinator`
- `TrimPreviewFacade`
- `TrimMediaDetailPresenter`

尤其要避免“时间轴选择逻辑、预览事件桥接、导出编排、输出格式偏好”继续留在同一类里。

#### `SplitAudioWorkspaceViewModel` 推荐拆法

优先拆成：

- `SplitAudioInputState`
- `SplitAudioExecutionCoordinator`
- `SplitAudioResultCollectionState`
- `SplitAudioPreviewFacade`
- `SplitAudioPreferencesState`

这块很适合作为“统一工作区拆法”的模板，因为它比合并模块简单，但又比普通导出更完整。

当前仓库已经先落了一步保守切片：

- `SplitAudioInputState`
- `SplitAudioWorkspacePreferencesState`
- `SplitAudioProgressState`
- `SplitAudioResultCollectionState`

也就是说，拆音工作区已经开始把“输入 / 偏好 / 进度 / 结果集合”从主 ViewModel 中抽离。后续继续拆时，优先顺着这个方向补 `SplitAudioExecutionCoordinator` 与 `SplitAudioPreviewFacade`，不要再把新的状态字段塞回 `SplitAudioWorkspaceViewModel`。

#### `MainViewModel` 推荐拆法

`MainViewModel` 后续应该尽量收缩成“主壳层”：

- 当前工作区切换
- 全局设置入口
- 全局媒体详情入口
- 跨工作区共享反馈

不要再把新的工作区流程往 `MainViewModel.Execution.cs` 里塞。

更具体地说，后续可以考虑把它进一步收敛为：

- `MainShellViewModel`
- `SettingsPaneViewModel`
- `MediaDetailsPaneViewModel`
- `WorkspaceNavigationState`

### 二类：预览服务过重

`Services/VideoPreview/MpvVideoPreviewService.cs` 已经接近一个“小型子系统”，它现在同时承担：

- mpv Native 生命周期
- Win32 Host Window
- 事件循环
- 命令串行化
- 播放状态
- 加载/卸载同步
- Seek 行为控制
- Placement 更新

这类类一旦继续增长，后续任何播放器层问题都会变得难查。

建议把它逐步拆成：

- `MpvRuntimeHost`
- `MpvCommandClient`
- `MpvEventPump`
- `MpvHostWindowAdapter`
- `PreviewSessionState`

拆分原则：

- Win32 / HWND 逻辑和 mpv 命令逻辑不要继续放在同一类里。
- 状态字段和 native 调用不要继续交织。
- `IVideoPreviewService` 保持稳定外观，内部实现再分层。

### 三类：Workflow Service 仍然偏宽

像下面这些服务虽然已经比之前清晰，但仍有继续解耦空间：

- `MediaProcessingWorkflowService`
- `AudioSeparationWorkflowService`
- `VideoTrimWorkflowService`
- `VideoJoinWorkflowService`
- `AudioVideoComposeWorkflowService`

它们当前仍容易同时承担：

- 运行时准备
- 输入预检
- 规则决策
- 命令构建
- 外部进程执行
- 结果映射
- 日志翻译

后续建议按照下面方式拆：

- `*PreflightService`
- `*CommandFactory`
- `*ExecutionCoordinator`
- `*ResultTranslator`
- `*PolicyResolver`

一句话原则：

一个 workflow 最终应更像“编排器”，而不是“从规则到执行到文案全都包”的总控类。

### 四类：ViewModel 对 WinUI 类型的直接依赖

这是一个容易被忽略、但长期会持续增加耦合的点。

目前多个 ViewModel 仍直接依赖：

- `Visibility`
- `Symbol`
- 部分 `Microsoft.UI.Xaml` 类型

后续的方向应该是：

- `ViewModel` 只暴露 `bool`、简单枚举、纯文本和模型
- `Visibility` 交给 Converter 或 View 层转换
- 图标类型尽量改为语义化枚举或资源键，而不是直接暴露 WinUI 控件语义

短期内不必一次性清零，但从现在开始，新的 ViewModel 代码不要再继续引入更多 WinUI 类型。

### 五类：`ApplicationConfiguration` 和组合根的中心化压力

`Utils/AppCompositionRoot.cs` 现在已经比线性 `new` 链好很多，但它仍然是一个明显的接线中心。

`ApplicationConfiguration` 也承担了很多“全局能力表 + 模式配置 + 文案配置 + 文件格式配置”的职责。

后续建议：

- 把 `ApplicationConfiguration` 拆成多个 feature options
- 把组合根按模块注册函数继续细化

例如：

- `ProcessingOptions`
- `TrimOptions`
- `MergeOptions`
- `SplitAudioOptions`
- `PreviewOptions`

以及：

- `CreatePreviewServices()`
- `CreateMergeModule()`
- `CreateTrimModule()`
- `CreateSplitAudioModule()`

目标不是为了形式，而是降低“新增一个小能力时，需要同时改动配置对象、组合根和多个构造器”的改动面。

## 推荐的实施顺序

不要并行大拆多个工作区。建议按下面顺序推进。

### 阶段 0：先冻结加债方式

从现在开始，在重构真正完成前，先约束新增代码：

- 不在 `MergeViewModel` / `MainViewModel.Execution` / `MpvVideoPreviewService` 中继续追加新业务分支
- 不在 `View` 层新增业务规则
- 不在 `ViewModel` 中新增外部进程调用细节

如果必须加功能，优先把新功能放进新的协调器或策略对象。

### 阶段 1：先补通用基础件

这一阶段不要先拆 UI，而是先补“能支撑后续拆分的公共基础件”：

- 通用外部进程执行与取消收口
- 通用 Operation Result / Progress / Error 模型
- 通用输出路径 / 命名策略
- 通用输入合法性 / 文件发现策略
- feature options 切片

这是为了避免后面每个模块都各自再写一套。

### 阶段 2：优先拆 `MergeViewModel`

原因：

- 体量最大
- 业务分支最多
- 对后续合并/AI/时间轴扩展最敏感
- 但相对仍有独立工作区边界，适合作为解耦样板

这一阶段完成后，应该能沉淀出一套可复制模式：

- 状态对象怎么拆
- 协调器怎么落
- ViewModel 怎么变薄
- 偏好如何从 UI 状态中抽离

### 阶段 3：按同一模式拆 `VideoTrim` 和 `SplitAudio`

这两块的重构目标不是彼此统一 UI，而是统一结构方法：

- 输入状态对象
- 执行协调器
- 预览门面
- 结果展示状态
- 偏好状态对象

如果这一阶段做好，后续新增新工作区时就不必再回到“大 ViewModel 先写起来”的老路。

### 阶段 4：拆 `MpvVideoPreviewService`

这一步适合在工作区结构已经稳定后再做。

因为播放器预览通常会牵涉：

- 平台窗口
- 线程
- native 事件
- 生命周期同步

如果在工作区本身还没稳定前就先动播放器层，调试成本会比较高。

### 阶段 5：收缩 `MainViewModel` 与大 XAML

当各工作区都已有自己的协调层后，再回头收缩：

- `MainViewModel`
- `MainWindow.xaml`
- `MergePage.xaml`

建议把大页面继续拆成更稳定的用户控件，例如：

- 输入区
- 输出设置区
- 轨道编辑区
- 进度反馈区
- 详情区

这样后续继续改 UI 时，文件冲突和误伤面都会下降。

### 阶段 6：补回归验证面

当前自动化验证面还不够强。后续每拆完一个热点模块，都要顺手补验证，而不是等全部拆完再补。

优先建议补这些：

- Trim 工作区 smoke test
- Merge 工作区 smoke test
- Terminal / 外部进程取消 smoke test
- MediaInfo / ffprobe 共享请求 smoke test
- Preview 生命周期 smoke test

目标不是一开始就追求完整单测覆盖率，而是至少让关键流程“能被重复验证”。

## 推荐的目录演进方式

不要急着做大规模迁目录，但可以逐步演进到下面这种组织方式：

```text
Core/
  Interfaces/
  Models/
  Policies/
  Options/
  Results/

Services/
  FFmpeg/
  Demucs/
  MediaInfo/
  VideoPreview/
  Workflows/
  Coordinators/
  State/

ViewModels/
  Main/
  Merge/
  Trim/
  SplitAudio/
  Terminal/

Views/
  Controls/
  Merge/
  Trim/
  SplitAudio/
```

注意：这不是要求立即搬家，而是提醒后续新增文件时，不要继续全部堆回根目录。

## 每轮重构的完成标准

每做完一个切片，至少检查以下几件事：

1. 行为是否保持不变。
2. 原来的热点类是否真的变薄，而不是只是把代码挪到更多 `partial`。
3. 新拆出来的对象是否边界清晰，有单一职责。
4. 是否减少了对 WinUI 类型的直接依赖。
5. 是否减少了组合根、配置对象、ViewModel 的同步改动面。
6. 是否补了最小必要的 smoke test 或验证器。
7. 是否同步更新了 `DOCS`。

如果一个重构做完后，只是“文件更多了”，但依赖关系和改动面没有变小，那它就不算真正完成。

## 明确不建议做的事情

下面这些事情短期内不要做：

- 不要为了“架构更先进”直接引入一整套复杂 DI / Mediator / EventBus 框架。
- 不要做一次性全项目重写。
- 不要为了追求目录好看而先大量移动文件。
- 不要把文案、偏好、业务规则、平台逻辑继续糊在一个新类里，只是换个名字。
- 不要把“类太大”机械地理解成“切成多个 partial 就结束了”。

## 如果你只能先做一件事

优先拆 `MergeViewModel`，并把它拆成“状态对象 + 轨道协调器 + 输出设置状态 + 执行协调器 + 模式策略”。

原因很简单：

- 这里是当前耦合最重的工作区。
- 它最能代表项目后续的复杂度走向。
- 只要这块拆出一个稳定范式，Trim、SplitAudio、MainShell 都会更容易跟进。

## 最后的判断标准

后续重构是否成功，不看“是不是用了更流行的架构名词”，只看三件事：

- 新增一个功能时，需要改动的文件数量是否下降。
- 修改一个工作区时，误伤另一个工作区的概率是否下降。
- 遇到取消、进度、预览、外部进程、偏好持久化这类横切问题时，是否已经有明确收口点，而不是继续全项目搜索。

如果这三件事在持续变好，那么项目就在朝着可维护方向发展。
