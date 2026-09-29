using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LMTodo.Models;

namespace LMTodo.Repositories;

public sealed class SettingsService
{
    private readonly string _settingsPath;

    private readonly JsonSerializerOptions _options =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

    public string FilePath => _settingsPath;

    public SettingsService()
    {
        var appData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        var directory = Path.Combine(appData, "LMTodo");

        Directory.CreateDirectory(directory);

        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);

            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(json, _options)
                   ?? new AppSettings();
        }
        catch
        {
            // 设置读坏了不该阻止应用启动，直接回默认值
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, _options);

            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // 设置写失败不致命，忽略
        }
    }
}
