# Vidvix 架构概览

## 当前分层

- `Core`
  放应用级模型和接口定义，不依赖具体视图或平台实现。
- `Services`
  放文件选择、用户偏好、FFmpeg 运行时、媒体信息、文件发现等基础能力实现。
- `ViewModels`
  放界面状态编排，不直接依赖具体 XAML 控件，负责把服务能力组织成可绑定的界面行为。
- `Views`
  放 WinUI 窗口与控件，只处理视觉交互、窗口外观和必要的平台事件。
- `Utils`
  放命令、可观察对象、路径解析、简单组合根等通用基础设施。

## 当前依赖边界

- `Views -> ViewModels -> Core.Interfaces`
- `Services -> Core.Interfaces/Core.Models`
- `Utils` 只承载基础设施，不承载业务规则
- `Core` 不反向依赖 `Views` 或 `Services`

## 本次整理后的重点

- `MainViewModel` 按职责拆成 `Import`、`Details`、`Processing`、`Preferences` 四个 partial 文件。
- 用户偏好改为“基于现有对象更新”的持久化方式，避免每次新增设置项时在多个调用点手动复制所有字段。
- 新增 `.editorconfig` 统一中文源码采用 `UTF-8 BOM`，降低 Windows 默认工具、脚本和 AI Agent 误判乱码的概率。
- 新增全局“转码方式”偏好：默认保持现有快速换封装行为，可切换到真正转码，并在视频可适用时接入 GPU 可用性检测与自动回退。

## 仍需长期坚持的约束

- 视图层不要直接调用 FFmpeg、媒体探测或文件系统细节，统一经由 ViewModel 和 Service。
- Service 不要依赖具体窗口或控件类型，避免平台事件向下层渗透。
- 新增设置项时优先走 `IUserPreferencesService.Update(...)`，不要重新手写整对象复制。
- 新增处理模式时优先扩展独立流程或策略，不要继续把 `MainViewModel` 变回“大而全”的总控类。

## 面向后续“视频 / 音频双模式”扩展的建议

- 把“模式定义”视为一等概念，未来可抽象为模式描述对象或策略集合。
- 每个模式至少拆成三块：
  - 导入约束
  - 输出格式与预检规则
  - 处理命令构建
- 如果未来音频模块继续增长，建议把 `MainViewModel` 再向下拆成模式级编排器，例如：
  - `IVideoProcessingWorkflow`
  - `IAudioProcessingWorkflow`
- AI 功能接入时，优先新增独立服务，例如 `IAiMediaAnalysisService`，不要把远程调用、提示词和 UI 状态直接揉进窗口或现有基础服务。

## 可移植性相关现状

- 项目已配置为 WinUI 自包含发布：
  - `WindowsPackageType=None`
  - `WindowsAppSDKSelfContained=true`
- 发布时会把离线运行目录复制到 `artifacts/publish-offline/`。
- 若应用目录不可写，FFmpeg 运行时会回退到 `%LOCALAPPDATA%\\Vidvix\\Tools\\MediaEngine` 下准备运行环境。
- 若要保证“离线电脑也可直接运行”，发布前应确保 `Tools\\ffmpeg\\ffmpeg.exe` 与 `ffprobe.exe` 已随产物一起输出。
