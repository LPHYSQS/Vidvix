using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Vidvix.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Vidvix.Views.Controls;

public sealed partial class TerminalCommandComposer : UserControl
{
    public TerminalCommandComposer()
    {
        InitializeComponent();
    }

    public TerminalWorkspaceViewModel ViewModel
    {
        get => (TerminalWorkspaceViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(TerminalWorkspaceViewModel),
        typeof(TerminalCommandComposer),
        new PropertyMetadata(new TerminalWorkspaceViewModel()));

    private void OnCommandInputDragEnter(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        e.Handled = true;
    }

    private void OnCommandInputDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        e.Handled = true;
    }

    private async void OnCommandInputDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var storageItems = await e.DataView.GetStorageItemsAsync();
            var droppedPaths = storageItems
                .Where(item => ViewModel.CanAcceptDroppedItem(
                    item.Path,
                    item.IsOfType(StorageItemTypes.Folder)))
                .Select(item => item.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();

            if (droppedPaths.Length == 0)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                e.Handled = true;
                return;
            }

            var insertionText = string.Join(" ", droppedPaths.Select(static path => $"\"{path}\""));
            InsertTextIntoCommandBox(insertionText);

            e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnCommandTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        if (ViewModel.ExecuteCommand.CanExecute(null))
        {
            ViewModel.ExecuteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void InsertTextIntoCommandBox(string insertionText)
    {
        var currentText = CommandTextBox.Text ?? string.Empty;
        var selectionStart = ResolveSelectionStart(currentText);
        var selectionLength = ResolveSelectionLength(currentText, selectionStart);
        var prefixText = currentText[..selectionStart];
        var suffixText = currentText[(selectionStart + selectionLength)..];
        var normalizedInsertionText = insertionText;

        if (prefixText.Length > 0 && !char.IsWhiteSpace(prefixText[^1]))
        {
            normalizedInsertionText = $" {normalizedInsertionText}";
        }

        if (suffixText.Length > 0 && !char.IsWhiteSpace(suffixText[0]))
        {
            normalizedInsertionText = $"{normalizedInsertionText} ";
        }

        var updatedText = string.Concat(prefixText, normalizedInsertionText, suffixText);
        var caretIndex = prefixText.Length + normalizedInsertionText.Length;

        ViewModel.CommandText = updatedText;
        CommandTextBox.Text = updatedText;
        CommandTextBox.Focus(FocusState.Programmatic);
        CommandTextBox.SelectionStart = caretIndex;
        CommandTextBox.SelectionLength = 0;
    }

    private int ResolveSelectionStart(string currentText)
    {
        if (CommandTextBox.FocusState == FocusState.Unfocused)
        {
            return currentText.Length;
        }

        return Math.Clamp(CommandTextBox.SelectionStart, 0, currentText.Length);
    }

    private int ResolveSelectionLength(string currentText, int selectionStart)
    {
        if (CommandTextBox.FocusState == FocusState.Unfocused)
        {
            return 0;
        }

        var maxLength = currentText.Length - selectionStart;
        return Math.Clamp(CommandTextBox.SelectionLength, 0, maxLength);
    }
}
