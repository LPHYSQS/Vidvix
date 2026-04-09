using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services;

public sealed class UserPreferencesService : IUserPreferencesService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly string _preferencesFilePath;
    private readonly ILogger _logger;
    private readonly object _syncRoot = new();

    public UserPreferencesService(ApplicationConfiguration configuration, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _preferencesFilePath = Path.Combine(localAppDataPath, configuration.LocalDataDirectoryName, configuration.UserPreferencesFileName);
    }

    public UserPreferences Load()
    {
        lock (_syncRoot)
        {
            return LoadCore();
        }
    }

    public void Save(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        lock (_syncRoot)
        {
            SaveCore(preferences);
        }
    }

    public void Update(Func<UserPreferences, UserPreferences> updatePreferences)
    {
        ArgumentNullException.ThrowIfNull(updatePreferences);

        lock (_syncRoot)
        {
            var updatedPreferences = updatePreferences(LoadCore())
                ?? throw new InvalidOperationException("更新后的用户设置不能为空。");

            SaveCore(updatedPreferences);
        }
    }

    private UserPreferences LoadCore()
    {
        try
        {
            if (!File.Exists(_preferencesFilePath))
            {
                return new UserPreferences();
            }

            var content = File.ReadAllText(_preferencesFilePath);
            return JsonSerializer.Deserialize<UserPreferences>(content, SerializerOptions) ?? new UserPreferences();
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "读取用户设置失败，已回退为默认设置。", exception);
            return new UserPreferences();
        }
    }

    private void SaveCore(UserPreferences preferences)
    {
        var tempFilePath = $"{_preferencesFilePath}.tmp";

        try
        {
            var directoryPath = Path.GetDirectoryName(_preferencesFilePath)
                ?? throw new InvalidOperationException("用户设置目录不可用。");

            Directory.CreateDirectory(directoryPath);

            var content = JsonSerializer.Serialize(preferences, SerializerOptions);
            File.WriteAllText(tempFilePath, content);
            File.Move(tempFilePath, _preferencesFilePath, overwrite: true);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "保存用户设置失败。", exception);

            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }
}
