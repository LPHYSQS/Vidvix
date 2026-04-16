using System;

namespace Vidvix.Core.Models;

/// <summary>
/// 表示音视频合成导出前的源素材分析结果。
/// </summary>
public sealed record AudioVideoComposeSourceAnalysis(
    TimeSpan VideoDuration,
    TimeSpan AudioDuration,
    double VideoFrameRate,
    bool VideoHasAudioStream);
