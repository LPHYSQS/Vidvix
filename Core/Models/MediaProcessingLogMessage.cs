// 功能：媒体处理流程日志消息模型（承载 Service 层返回的提示、警告与错误）
// 模块：视频转换模块 / 音频转换模块
// 说明：可复用，供不同 ViewModel 把服务结果映射为界面日志。
namespace Vidvix.Core.Models;

public readonly record struct MediaProcessingLogMessage(LogLevel Level, string Message);
