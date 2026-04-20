using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface ILocalizationService
{
    event EventHandler? LanguageChanged;

    string CurrentLanguage { get; }

    string FallbackLanguage { get; }

    IReadOnlyList<LocalizationLanguageOption> AvailableLanguages { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default);

    bool TryGetString(string key, out string value);

    string GetString(string key, string? fallback = null);

    string Format(string key, IReadOnlyDictionary<string, object?>? arguments = null, string? fallback = null);
}
