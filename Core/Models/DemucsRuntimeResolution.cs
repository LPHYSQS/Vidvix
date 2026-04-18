namespace Vidvix.Core.Models;

public sealed class DemucsRuntimeResolution
{
    public DemucsRuntimeVariant RuntimeVariant { get; init; } = DemucsRuntimeVariant.Cpu;

    public required string PythonExecutablePath { get; init; }

    public required string RuntimeRootPath { get; init; }

    public required string ModelRepositoryPath { get; init; }

    public bool WasExtracted { get; init; }
}
