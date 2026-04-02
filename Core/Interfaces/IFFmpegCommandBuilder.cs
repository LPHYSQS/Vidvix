using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IFFmpegCommandBuilder
{
    IFFmpegCommandBuilder Reset();

    IFFmpegCommandBuilder SetExecutablePath(string executablePath);

    IFFmpegCommandBuilder SetInput(string inputFilePath);

    IFFmpegCommandBuilder SetOutput(string outputFilePath);

    IFFmpegCommandBuilder AddGlobalParameter(string parameter);

    IFFmpegCommandBuilder AddGlobalParameter(string parameter, string value);

    IFFmpegCommandBuilder AddParameter(string parameter);

    IFFmpegCommandBuilder AddParameter(string parameter, string value);

    FFmpegCommand Build();
}

