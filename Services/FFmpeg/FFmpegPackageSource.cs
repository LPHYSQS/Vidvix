using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.FFmpeg;

public sealed class FFmpegPackageSource : IFFmpegPackageSource, IDisposable
{
    private readonly ApplicationConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public FFmpegPackageSource(
        ApplicationConfiguration configuration,
        ILogger logger,
        HttpClient? httpClient = null)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient ?? CreateHttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public async Task<FFmpegPackageManifest> GetLatestPackageAsync(CancellationToken cancellationToken = default)
    {
        var expectedSha256 = await TryGetExpectedSha256Async(cancellationToken).ConfigureAwait(false);

        return new FFmpegPackageManifest
        {
            ArchiveUri = _configuration.FFmpegArchiveUri,
            ChecksumUri = _configuration.FFmpegChecksumUri,
            ArchiveFileName = _configuration.FFmpegArchiveFileName,
            ExpectedSha256 = expectedSha256
        };
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<string?> TryGetExpectedSha256Async(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(_configuration.FFmpegChecksumUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var checksum = ParseChecksum(content, _configuration.FFmpegArchiveFileName);

            if (string.IsNullOrWhiteSpace(checksum))
            {
                _logger.Log(LogLevel.Warning, "未能从校验清单中找到目标组件的哈希值，将跳过哈希校验。");
            }

            return checksum;
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "获取组件校验信息失败，将继续下载流程。", exception);
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Vidvix", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        return client;
    }

    private static string? ParseChecksum(string checksumFileContent, string archiveFileName)
    {
        using var reader = new StringReader(checksumFileContent);

        while (reader.ReadLine() is { } line)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine) ||
                !trimmedLine.EndsWith(archiveFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var segments = trimmedLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            return segments[0];
        }

        return null;
    }
}