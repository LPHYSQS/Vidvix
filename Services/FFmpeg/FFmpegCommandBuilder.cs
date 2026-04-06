using System;
using System.Collections.Generic;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.FFmpeg;

public sealed class FFmpegCommandBuilder : IFFmpegCommandBuilder
{
    private readonly string _executablePath;
    private readonly string? _inputFilePath;
    private readonly string? _outputFilePath;
    private readonly IReadOnlyList<string> _globalParameters;
    private readonly IReadOnlyList<string> _parameters;

    public FFmpegCommandBuilder(string executablePath)
        : this(executablePath, null, null, Array.Empty<string>(), Array.Empty<string>())
    {
    }

    private FFmpegCommandBuilder(
        string executablePath,
        string? inputFilePath,
        string? outputFilePath,
        IReadOnlyList<string> globalParameters,
        IReadOnlyList<string> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        _executablePath = executablePath;
        _inputFilePath = inputFilePath;
        _outputFilePath = outputFilePath;
        _globalParameters = globalParameters;
        _parameters = parameters;
    }

    public IFFmpegCommandBuilder Reset() => new FFmpegCommandBuilder(_executablePath);

    public IFFmpegCommandBuilder SetExecutablePath(string executablePath) =>
        new FFmpegCommandBuilder(executablePath, _inputFilePath, _outputFilePath, _globalParameters, _parameters);

    public IFFmpegCommandBuilder SetInput(string inputFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFilePath);
        return new FFmpegCommandBuilder(_executablePath, inputFilePath, _outputFilePath, _globalParameters, _parameters);
    }

    public IFFmpegCommandBuilder SetOutput(string outputFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFilePath);
        return new FFmpegCommandBuilder(_executablePath, _inputFilePath, outputFilePath, _globalParameters, _parameters);
    }

    public IFFmpegCommandBuilder AddGlobalParameter(string parameter) =>
        AddTokens(parameter, null, appendToGlobalParameters: true);

    public IFFmpegCommandBuilder AddGlobalParameter(string parameter, string value) =>
        AddTokens(parameter, value, appendToGlobalParameters: true);

    public IFFmpegCommandBuilder AddParameter(string parameter) =>
        AddTokens(parameter, null, appendToGlobalParameters: false);

    public IFFmpegCommandBuilder AddParameter(string parameter, string value) =>
        AddTokens(parameter, value, appendToGlobalParameters: false);

    public FFmpegCommand Build()
    {
        if (string.IsNullOrWhiteSpace(_inputFilePath))
        {
            throw new InvalidOperationException("生成 FFmpeg 命令前必须先提供输入文件。");
        }

        if (string.IsNullOrWhiteSpace(_outputFilePath))
        {
            throw new InvalidOperationException("生成 FFmpeg 命令前必须先提供输出文件。");
        }

        var arguments = new List<string>(_globalParameters.Count + _parameters.Count + 3);
        arguments.AddRange(_globalParameters);
        arguments.Add("-i");
        arguments.Add(_inputFilePath);
        arguments.AddRange(_parameters);
        arguments.Add(_outputFilePath);

        return new FFmpegCommand(_executablePath, arguments);
    }

    private IFFmpegCommandBuilder AddTokens(string parameter, string? value, bool appendToGlobalParameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);

        var nextGlobalParameters = new List<string>(_globalParameters);
        var nextParameters = new List<string>(_parameters);
        var targetCollection = appendToGlobalParameters ? nextGlobalParameters : nextParameters;

        targetCollection.Add(parameter);

        if (!string.IsNullOrWhiteSpace(value))
        {
            targetCollection.Add(value);
        }

        return new FFmpegCommandBuilder(_executablePath, _inputFilePath, _outputFilePath, nextGlobalParameters, nextParameters);
    }
}
