using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;
using Vidvix.Services;

namespace Vidvix.Services.MediaInfo;

public sealed partial class MediaInfoService
{
    private static string ExtractFailureReason(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return ParseFailedMessage;
        }

        var lines = standardError.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.LastOrDefault() ?? ParseFailedMessage;
    }

    private static string CreateFfprobeDiagnosticDetails(
        string? ffprobePath,
        string inputPath,
        FfprobeExecutionResult? executionResult,
        string? additionalMessage = null)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(additionalMessage))
        {
            builder.AppendLine(additionalMessage);
        }

        builder.AppendLine($"输入文件：{inputPath}");

        if (!string.IsNullOrWhiteSpace(ffprobePath))
        {
            builder.AppendLine($"命令：{BuildFfprobeCommandLine(ffprobePath, inputPath)}");
        }

        if (executionResult is { } result)
        {
            builder.AppendLine($"退出码：{result.ExitCode}");
            AppendDiagnosticSection(builder, "标准错误", result.StandardError);
            AppendDiagnosticSection(builder, "标准输出", result.StandardOutput);
        }

        return builder.ToString().Trim();
    }

    private static void AppendDiagnosticSection(StringBuilder builder, string title, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        builder.AppendLine(title + "：");
        builder.AppendLine(TrimDiagnosticContent(content));
    }

    private static string TrimDiagnosticContent(string content)
    {
        var lines = content
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(12)
            .ToArray();

        return lines.Length == 0 ? content.Trim() : string.Join(Environment.NewLine, lines);
    }

    private static string BuildFfprobeCommandLine(string ffprobePath, string inputPath) =>
        string.Join(
            " ",
            new[]
            {
                QuoteArgument(ffprobePath),
                "-v",
                "quiet",
                "-print_format",
                "json",
                "-show_entries",
                LightweightProbeEntries,
                QuoteArgument(inputPath)
            });

    private static string QuoteArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static FfprobeResponse ParseProbeResult(string json)
    {
        return JsonSerializer.Deserialize<FfprobeResponse>(json, JsonOptions)
            ?? throw new JsonException("ffprobe 输出为空。");
    }

    private async Task<string?> ProbeMappedStreamBitrateAsync(
        string ffmpegPath,
        string inputPath,
        string mapSelector,
        string mediaLabel,
        double? durationSeconds,
        CancellationToken cancellationToken = default)
    {
        if (durationSeconds is not > 0)
        {
            return null;
        }

        using var process = new Process
        {
            StartInfo = CreateBitrateProbeStartInfo(ffmpegPath, inputPath, mapSelector),
            EnableRaisingEvents = true
        };
        var processId = 0;

        if (!process.Start())
        {
            return null;
        }

        processId = process.Id;
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        using var cancellationRegistration = ExternalProcessTermination.RegisterTermination(process, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ExternalProcessTermination.WaitForTerminationAsync(process, processId).ConfigureAwait(false);
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            throw;
        }

        await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);

        var standardError = standardErrorTask.Result;
        if (!TryExtractMappedStreamSizeBytes(standardError, mediaLabel, out var sizeBytes) || sizeBytes <= 0)
        {
            return null;
        }

        var bitsPerSecond = (sizeBytes * 8d) / durationSeconds.Value;
        return bitsPerSecond > 0
            ? bitsPerSecond.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    private static ProcessStartInfo CreateBitrateProbeStartInfo(string ffmpegPath, string inputPath, string mapSelector)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-nostats");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add(mapSelector);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("copy");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("null");
        startInfo.ArgumentList.Add("-");
        return startInfo;
    }

    private static bool TryExtractMappedStreamSizeBytes(string standardError, string mediaLabel, out long sizeBytes)
    {
        sizeBytes = 0;
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return false;
        }

        var lines = standardError.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index];
            if (!line.Contains("video:", StringComparison.OrdinalIgnoreCase) || !line.Contains("audio:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var token = ExtractLabeledSummaryToken(line, mediaLabel);
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            return TryParseSizeBytes(token, out sizeBytes);
        }

        return false;
    }

    private static string? ExtractLabeledSummaryToken(string line, string label)
    {
        var marker = label + ":";
        var startIndex = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return null;
        }

        startIndex += marker.Length;
        var endIndex = line.IndexOf(' ', startIndex);
        if (endIndex < 0)
        {
            endIndex = line.Length;
        }

        var token = line[startIndex..endIndex].Trim();
        return string.Equals(token, "N/A", StringComparison.OrdinalIgnoreCase) ? null : token;
    }

    private static bool TryParseSizeBytes(string sizeText, out long sizeBytes)
    {
        sizeBytes = 0;
        if (string.IsNullOrWhiteSpace(sizeText))
        {
            return false;
        }

        var units = new[] { "PiB", "TiB", "GiB", "MiB", "KiB", "B" };
        foreach (var unit in units)
        {
            if (!sizeText.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var numericPortion = sizeText[..^unit.Length];
            if (!double.TryParse(numericPortion, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
            {
                return false;
            }

            var multiplier = unit.ToUpperInvariant() switch
            {
                "PIB" => 1024d * 1024d * 1024d * 1024d * 1024d,
                "TIB" => 1024d * 1024d * 1024d * 1024d,
                "GIB" => 1024d * 1024d * 1024d,
                "MIB" => 1024d * 1024d,
                "KIB" => 1024d,
                _ => 1d
            };

            sizeBytes = (long)Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
            return sizeBytes > 0;
        }

        return false;
    }
}
