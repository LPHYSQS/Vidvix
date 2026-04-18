using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Vidvix.Views;

public sealed partial class SplitAudioPage : Page
{
    public SplitAudioPage()
    {
        InitializeComponent();
        UpdateSelectedOutputFormatDescription();
    }

    private void OnOutputFormatSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectedOutputFormatDescription();

    private async void OnBrowseOutputDirectoryClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            CommitButtonText = "选择输出目录",
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            ViewMode = PickerViewMode.List
        };

        picker.FileTypeFilter.Add("*");

        var windowHandle = GetActiveWindow();
        if (windowHandle == IntPtr.Zero)
        {
            windowHandle = GetForegroundWindow();
        }

        if (windowHandle != IntPtr.Zero)
        {
            InitializeWithWindow.Initialize(picker, windowHandle);
        }

        var selectedFolder = await picker.PickSingleFolderAsync();
        if (selectedFolder is null)
        {
            return;
        }

        SplitAudioOutputDirectoryTextBox.Text = selectedFolder.Path;
        ToolTipService.SetToolTip(SplitAudioOutputDirectoryTextBox, selectedFolder.Path);
    }

    private void OnClearOutputDirectoryClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SplitAudioOutputDirectoryTextBox.Text = string.Empty;
        ToolTipService.SetToolTip(SplitAudioOutputDirectoryTextBox, null);
    }

    private void UpdateSelectedOutputFormatDescription()
    {
        if (SplitAudioOutputFormatComboBox is null || SplitAudioOutputFormatDescriptionText is null)
        {
            return;
        }

        if (SplitAudioOutputFormatComboBox.SelectedItem is ComboBoxItem selectedItem &&
            selectedItem.Tag is string description)
        {
            SplitAudioOutputFormatDescriptionText.Text = description;
            return;
        }

        SplitAudioOutputFormatDescriptionText.Text = string.Empty;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
