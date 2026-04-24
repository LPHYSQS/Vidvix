using System;
using System.IO;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class AiMaterialItemViewModel : ObservableObject
{
    private bool _isActive;

    public AiMaterialItemViewModel(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        InputPath = Path.GetFullPath(inputPath);
        InputFileName = Path.GetFileName(InputPath);
        InputFileNameWithoutExtension = Path.GetFileNameWithoutExtension(InputPath);
        InputDirectory = Path.GetDirectoryName(InputPath) ?? string.Empty;
        FileExtensionText = Path.GetExtension(InputPath).TrimStart('.').ToUpperInvariant();
    }

    public string InputPath { get; }

    public string InputFileName { get; }

    public string InputFileNameWithoutExtension { get; }

    public string InputDirectory { get; }

    public string FileExtensionText { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
