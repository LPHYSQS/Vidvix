namespace Vidvix.Core.Models;

/// <summary>
/// 描述一次媒体处理在业务层面的上下文。
/// 界面层负责确定上下文，底层命令工厂根据该上下文生成具体命令。
/// </summary>
public readonly record struct MediaProcessingContext(
    ProcessingWorkspaceKind WorkspaceKind,
    ProcessingMode ProcessingMode,
    OutputFormatOption OutputFormat,
    TranscodingMode TranscodingMode,
    bool IsGpuAccelerationRequested,
    VideoAccelerationKind VideoAccelerationKind);
