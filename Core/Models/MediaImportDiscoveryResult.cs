using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidvix.Core.Models;

public sealed class MediaImportDiscoveryResult
{
    public MediaImportDiscoveryResult(
        IEnumerable<string> supportedFiles,
        int unsupportedEntries,
        int missingEntries,
        int unavailableDirectories)
    {
        ArgumentNullException.ThrowIfNull(supportedFiles);

        SupportedFiles = supportedFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        UnsupportedEntries = unsupportedEntries;
        MissingEntries = missingEntries;
        UnavailableDirectories = unavailableDirectories;
    }

    public IReadOnlyList<string> SupportedFiles { get; }

    public int UnsupportedEntries { get; }

    public int MissingEntries { get; }

    public int UnavailableDirectories { get; }
}
