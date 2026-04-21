using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace Vidvix.ViewModels;

internal sealed class SplitAudioInputState
{
    private readonly Func<string> _defaultInputSummaryResolver;
    private Func<string>? _currentInputSummaryResolver;

    public SplitAudioInputState(Func<string> defaultInputSummaryResolver)
    {
        _defaultInputSummaryResolver = defaultInputSummaryResolver ?? throw new ArgumentNullException(nameof(defaultInputSummaryResolver));
        InputSummaryText = _defaultInputSummaryResolver();
    }

    public string InputPath { get; private set; } = string.Empty;

    public string InputFileName { get; private set; } = string.Empty;

    public string InputSummaryText { get; private set; }

    public bool HasInput => !string.IsNullOrWhiteSpace(InputPath);

    public Visibility PlaceholderVisibility => HasInput ? Visibility.Collapsed : Visibility.Visible;

    public Visibility InputCardVisibility => HasInput ? Visibility.Visible : Visibility.Collapsed;

    public void SetSelectedInput(string inputPath, Func<string>? inputSummaryResolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        InputPath = Path.GetFullPath(inputPath);
        InputFileName = Path.GetFileName(InputPath);
        _currentInputSummaryResolver = inputSummaryResolver;
        InputSummaryText = (_currentInputSummaryResolver ?? _defaultInputSummaryResolver).Invoke();
    }

    public void RefreshLocalization()
    {
        InputSummaryText = (_currentInputSummaryResolver ?? _defaultInputSummaryResolver).Invoke();
    }

    public void Clear()
    {
        InputPath = string.Empty;
        InputFileName = string.Empty;
        _currentInputSummaryResolver = null;
        InputSummaryText = _defaultInputSummaryResolver();
    }
}
