using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services;

public sealed class LocalizationService : ILocalizationService
{
    private static readonly string[] DefaultResourceFiles =
    {
        "common",
        "settings",
        "main-window",
        "ai",
        "trim",
        "split-audio",
        "merge",
        "terminal",
        "media-details"
    };

    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex PlaceholderPattern = new(
        "\\{(?<name>[A-Za-z0-9_]+)\\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ApplicationConfiguration _configuration;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly ILogger _logger;
    private readonly string _resourceRootPath;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly HashSet<string> _missingKeyLogCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _languageCache = new(StringComparer.OrdinalIgnoreCase);

    private LocalizationManifest _manifest;
    private IReadOnlyDictionary<string, string> _fallbackResources;
    private IReadOnlyDictionary<string, string> _currentResources;
    private IReadOnlyList<LocalizationLanguageOption> _availableLanguages;
    private bool _isInitialized;

    public LocalizationService(
        ApplicationConfiguration configuration,
        IUserPreferencesService userPreferencesService,
        ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _userPreferencesService = userPreferencesService ?? throw new ArgumentNullException(nameof(userPreferencesService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _resourceRootPath = Path.Combine(AppContext.BaseDirectory, _configuration.LocalizationResourceRelativePath);
        _manifest = CreateDefaultManifest();
        _fallbackResources = new Dictionary<string, string>(StringComparer.Ordinal);
        _currentResources = new Dictionary<string, string>(StringComparer.Ordinal);
        _availableLanguages = BuildAvailableLanguages(_manifest);
        CurrentLanguage = _configuration.FallbackUiLanguage;
        FallbackLanguage = _configuration.FallbackUiLanguage;
    }

    public event EventHandler? LanguageChanged;

    public string CurrentLanguage { get; private set; }

    public string FallbackLanguage { get; private set; }

    public IReadOnlyList<LocalizationLanguageOption> AvailableLanguages => _availableLanguages;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _syncGate.WaitAsync(cancellationToken);

        try
        {
            await InitializeCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "本地化服务初始化失败，已回退到安全默认值。", exception);
            ResetToSafeDefaults();
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public async Task SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        var shouldRaiseLanguageChanged = false;

        await _syncGate.WaitAsync(cancellationToken);

        try
        {
            await InitializeCoreAsync(cancellationToken);

            var targetLanguage = ResolveSupportedLanguageOrFallback(languageCode);
            var targetResources = await GetOrLoadLanguageResourcesAsync(targetLanguage, cancellationToken);
            shouldRaiseLanguageChanged = !string.Equals(CurrentLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase);

            CurrentLanguage = targetLanguage;
            _currentResources = targetResources;
            PersistLanguagePreference(targetLanguage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "切换界面语言失败，已保留当前语言。", exception);
        }
        finally
        {
            _syncGate.Release();
        }

        if (shouldRaiseLanguageChanged)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool TryGetString(string key, out string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = string.Empty;
            return false;
        }

        if (_currentResources.TryGetValue(key, out var currentValue) &&
            currentValue is not null)
        {
            value = currentValue;
            return true;
        }

        if (!string.Equals(CurrentLanguage, FallbackLanguage, StringComparison.OrdinalIgnoreCase) &&
            _fallbackResources.TryGetValue(key, out var fallbackValue) &&
            fallbackValue is not null)
        {
            value = fallbackValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public string GetString(string key, string? fallback = null)
    {
        if (TryGetString(key, out var value))
        {
            return value;
        }

        var safeValue = string.IsNullOrWhiteSpace(fallback)
            ? $"[{key}]"
            : fallback;

        LogMissingKeyOnce(CurrentLanguage, key, safeValue);
        return safeValue;
    }

    public string Format(
        string key,
        IReadOnlyDictionary<string, object?>? arguments = null,
        string? fallback = null)
    {
        var template = GetString(key, fallback);
        if (arguments is null || arguments.Count == 0)
        {
            return template;
        }

        return PlaceholderPattern.Replace(
            template,
            match =>
            {
                var placeholderName = match.Groups["name"].Value;
                if (!arguments.TryGetValue(placeholderName, out var rawValue))
                {
                    return match.Value;
                }

                return Convert.ToString(rawValue, CultureInfo.CurrentCulture) ?? string.Empty;
            });
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        _manifest = await LoadManifestAsync(cancellationToken);
        _availableLanguages = BuildAvailableLanguages(_manifest);
        FallbackLanguage = NormalizeLanguageCode(_manifest.FallbackLanguage) ?? _configuration.FallbackUiLanguage;
        _fallbackResources = await GetOrLoadLanguageResourcesAsync(FallbackLanguage, cancellationToken);

        var preferredLanguage = _userPreferencesService.Load().CurrentUiLanguage;
        var targetLanguage = ResolveSupportedLanguageOrFallback(preferredLanguage);
        _currentResources = await GetOrLoadLanguageResourcesAsync(targetLanguage, cancellationToken);
        CurrentLanguage = targetLanguage;
        _isInitialized = true;
    }

    private void ResetToSafeDefaults()
    {
        _manifest = CreateDefaultManifest();
        _availableLanguages = BuildAvailableLanguages(_manifest);
        FallbackLanguage = _configuration.FallbackUiLanguage;
        CurrentLanguage = _configuration.FallbackUiLanguage;
        _fallbackResources = new Dictionary<string, string>(StringComparer.Ordinal);
        _currentResources = _fallbackResources;
        _isInitialized = true;
    }

    private async Task<LocalizationManifest> LoadManifestAsync(CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(_resourceRootPath, _configuration.LocalizationManifestFileName);
        if (!File.Exists(manifestPath))
        {
            _logger.Log(LogLevel.Warning, $"未找到本地化清单文件，已使用默认清单：{manifestPath}");
            return CreateDefaultManifest();
        }

        try
        {
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<LocalizationManifest>(
                stream,
                ManifestSerializerOptions,
                cancellationToken);

            return SanitizeManifest(manifest);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Log(LogLevel.Warning, $"读取本地化清单失败，已回退到默认清单：{manifestPath}", exception);
            return CreateDefaultManifest();
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> GetOrLoadLanguageResourcesAsync(
        string languageCode,
        CancellationToken cancellationToken)
    {
        if (_languageCache.TryGetValue(languageCode, out var cachedResources))
        {
            return cachedResources;
        }

        var resources = await LoadLanguageResourcesAsync(languageCode, cancellationToken);
        _languageCache[languageCode] = resources;
        return resources;
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadLanguageResourcesAsync(
        string languageCode,
        CancellationToken cancellationToken)
    {
        var resources = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var resourceFile in _manifest.ResourceFiles)
        {
            if (string.IsNullOrWhiteSpace(resourceFile))
            {
                continue;
            }

            var resourcePath = Path.Combine(_resourceRootPath, languageCode, $"{resourceFile}.json");
            if (!File.Exists(resourcePath))
            {
                _logger.Log(LogLevel.Warning, $"本地化资源文件缺失，已跳过：{resourcePath}");
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(resourcePath);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    _logger.Log(LogLevel.Warning, $"本地化资源文件根节点不是 JSON 对象，已跳过：{resourcePath}");
                    continue;
                }

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        _logger.Log(LogLevel.Warning, $"本地化资源项不是字符串，已忽略：{resourcePath} -> {property.Name}");
                        continue;
                    }

                    var key = property.Name.Trim();
                    if (key.Length == 0)
                    {
                        continue;
                    }

                    var value = property.Value.GetString() ?? string.Empty;
                    if (!resources.TryAdd(key, value))
                    {
                        _logger.Log(LogLevel.Warning, $"检测到重复本地化 key，后写入值已覆盖先前值：{languageCode} -> {key}");
                        resources[key] = value;
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.Log(LogLevel.Warning, $"读取本地化资源文件失败，已跳过：{resourcePath}", exception);
            }
        }

        return resources;
    }

    private string ResolveSupportedLanguageOrFallback(string? languageCode)
    {
        var normalizedLanguageCode = NormalizeLanguageCode(languageCode);
        if (normalizedLanguageCode is not null &&
            _availableLanguages.Any(option => string.Equals(option.Code, normalizedLanguageCode, StringComparison.OrdinalIgnoreCase)))
        {
            return normalizedLanguageCode;
        }

        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            _logger.Log(LogLevel.Warning, $"不支持的界面语言：{languageCode}，已自动回退到 {FallbackLanguage}。");
        }

        return FallbackLanguage;
    }

    private void PersistLanguagePreference(string languageCode)
    {
        _userPreferencesService.Update(existingPreferences => existingPreferences with
        {
            CurrentUiLanguage = languageCode
        });
    }

    private void LogMissingKeyOnce(string languageCode, string key, string safeValue)
    {
        var cacheKey = $"{languageCode}:{key}";
        lock (_missingKeyLogCache)
        {
            if (!_missingKeyLogCache.Add(cacheKey))
            {
                return;
            }
        }

        _logger.Log(LogLevel.Warning, $"本地化 key 缺失：语言={languageCode}，key={key}，已回退为安全值 {safeValue}。");
    }

    private IReadOnlyList<LocalizationLanguageOption> BuildAvailableLanguages(LocalizationManifest manifest) =>
        manifest.SupportedLanguages
            .Where(language => !string.IsNullOrWhiteSpace(language.Code))
            .Select(language => new LocalizationLanguageOption(
                NormalizeLanguageCode(language.Code) ?? _configuration.FallbackUiLanguage,
                string.IsNullOrWhiteSpace(language.DisplayName) ? language.Code! : language.DisplayName,
                string.IsNullOrWhiteSpace(language.NativeDisplayName) ? language.DisplayName ?? language.Code! : language.NativeDisplayName))
            .GroupBy(language => language.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

    private LocalizationManifest SanitizeManifest(LocalizationManifest? manifest)
    {
        var defaultManifest = CreateDefaultManifest();
        var fallbackLanguage = NormalizeLanguageCode(manifest?.FallbackLanguage) ?? defaultManifest.FallbackLanguage;
        var resourceFiles = manifest?.ResourceFiles?
            .Where(resourceFile => !string.IsNullOrWhiteSpace(resourceFile))
            .Select(resourceFile => resourceFile.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (resourceFiles is null || resourceFiles.Length == 0)
        {
            resourceFiles = defaultManifest.ResourceFiles.ToArray();
        }

        var supportedLanguages = manifest?.SupportedLanguages?
            .Where(language => !string.IsNullOrWhiteSpace(language.Code))
            .Select(language => new LocalizationManifestLanguage
            {
                Code = NormalizeLanguageCode(language.Code),
                DisplayName = string.IsNullOrWhiteSpace(language.DisplayName) ? language.Code : language.DisplayName.Trim(),
                NativeDisplayName = string.IsNullOrWhiteSpace(language.NativeDisplayName)
                    ? (string.IsNullOrWhiteSpace(language.DisplayName) ? language.Code : language.DisplayName.Trim())
                    : language.NativeDisplayName.Trim()
            })
            .Where(language => !string.IsNullOrWhiteSpace(language.Code))
            .GroupBy(language => language.Code!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (supportedLanguages is null || supportedLanguages.Count == 0)
        {
            supportedLanguages = defaultManifest.SupportedLanguages.ToList();
        }

        if (!supportedLanguages.Any(language => string.Equals(language.Code, fallbackLanguage, StringComparison.OrdinalIgnoreCase)))
        {
            supportedLanguages.Insert(0, new LocalizationManifestLanguage
            {
                Code = fallbackLanguage,
                DisplayName = fallbackLanguage,
                NativeDisplayName = fallbackLanguage
            });
        }

        return new LocalizationManifest
        {
            Version = manifest?.Version ?? defaultManifest.Version,
            FallbackLanguage = fallbackLanguage,
            ResourceFiles = resourceFiles,
            SupportedLanguages = supportedLanguages
        };
    }

    private LocalizationManifest CreateDefaultManifest() =>
        new()
        {
            Version = 1,
            FallbackLanguage = _configuration.FallbackUiLanguage,
            ResourceFiles = DefaultResourceFiles,
            SupportedLanguages =
            {
                new LocalizationManifestLanguage
                {
                    Code = _configuration.DefaultUiLanguage,
                    DisplayName = "Simplified Chinese",
                    NativeDisplayName = "简体中文"
                },
                new LocalizationManifestLanguage
                {
                    Code = _configuration.SecondaryUiLanguage,
                    DisplayName = "English (United States)",
                    NativeDisplayName = "English (United States)"
                }
            }
        };

    private static string? NormalizeLanguageCode(string? languageCode) =>
        string.IsNullOrWhiteSpace(languageCode)
            ? null
            : languageCode.Trim();

    private sealed class LocalizationManifest
    {
        public int Version { get; set; } = 1;

        public string FallbackLanguage { get; set; } = "zh-CN";

        public IReadOnlyList<string> ResourceFiles { get; set; } = Array.Empty<string>();

        public List<LocalizationManifestLanguage> SupportedLanguages { get; set; } = new();
    }

    private sealed class LocalizationManifestLanguage
    {
        public string? Code { get; set; }

        public string? DisplayName { get; set; }

        public string? NativeDisplayName { get; set; }
    }
}
