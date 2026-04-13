// 功能：FFmpeg 视频编码策略工具（统一封装 GPU/CPU H.264 编码参数与适用格式判断）
// 模块：裁剪模块 / 视频转换模块
// 说明：可复用，供多个命令工厂与工作流服务共享相同的编码策略。
using System;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.FFmpeg;

internal static class FFmpegVideoEncodingPolicy
{
    public static IFFmpegCommandBuilder ApplyH264Encoding(
        IFFmpegCommandBuilder builder,
        VideoAccelerationKind videoAccelerationKind) =>
        videoAccelerationKind switch
        {
            VideoAccelerationKind.NvidiaNvenc => builder
                .AddParameter("-c:v", "h264_nvenc")
                .AddParameter("-preset", "p5")
                .AddParameter("-cq", "23")
                .AddParameter("-pix_fmt", "yuv420p"),
            VideoAccelerationKind.IntelQuickSync => builder
                .AddParameter("-c:v", "h264_qsv")
                .AddParameter("-global_quality", "23")
                .AddParameter("-look_ahead", "0")
                .AddParameter("-pix_fmt", "nv12"),
            VideoAccelerationKind.AmdAmf => builder
                .AddParameter("-c:v", "h264_amf")
                .AddParameter("-quality", "quality")
                .AddParameter("-rc", "cqp")
                .AddParameter("-qp_i", "23")
                .AddParameter("-qp_p", "23")
                .AddParameter("-pix_fmt", "nv12"),
            _ => builder
                .AddParameter("-c:v", "libx264")
                .AddParameter("-crf", "23")
                .AddParameter("-preset", "medium")
                .AddParameter("-pix_fmt", "yuv420p")
        };

    public static bool SupportsHardwareVideoEncoding(OutputFormatOption outputFormat)
    {
        ArgumentNullException.ThrowIfNull(outputFormat);

        var extension = outputFormat.Extension.ToLowerInvariant();
        return extension is ".mp4" or ".mkv" or ".mov" or ".m4v" or ".ts" or ".m2ts";
    }
}
