using System;
using System.Collections.Generic;

namespace Vidvix.Core.Models;

public sealed class FilePickerRequest
{
    public FilePickerRequest(IReadOnlyList<string> allowedFileTypes, string commitButtonText = "选择")
    {
        ArgumentNullException.ThrowIfNull(allowedFileTypes);

        AllowedFileTypes = allowedFileTypes;
        CommitButtonText = commitButtonText;
    }

    public IReadOnlyList<string> AllowedFileTypes { get; }

    public string CommitButtonText { get; }
}
