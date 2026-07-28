using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MarkdownViewer;

internal sealed class ConfigManager
{
    private readonly string _configFile;

    public ConfigManager()
        : this(AppDomain.CurrentDomain.BaseDirectory)
    {
    }

    internal ConfigManager(string baseDirectory)
    {
        var historyDirectory = Path.Combine(baseDirectory, "History");
        _configFile = Path.Combine(historyDirectory, "config.json");
    }

    public double ZoomFactor { get; set; } = 1.0;
    public bool IsDarkMode { get; set; }
    public bool IsTocVisible { get; set; }
    public bool IsToolbarVisible { get; set; } = true;

    public void Load()
    {
        try
        {
            if (!File.Exists(_configFile))
                return;

            var json = File.ReadAllText(_configFile, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<ConfigManager>(json);
            if (data == null)
                return;

            ZoomFactor = data.ZoomFactor;
            IsDarkMode = data.IsDarkMode;
            IsTocVisible = data.IsTocVisible;
            IsToolbarVisible = data.IsToolbarVisible;
        }
        catch
        {
            // 配置读取失败时使用默认值。
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configFile);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFile, json, Encoding.UTF8);
        }
        catch
        {
            // 配置保存失败不应影响查看器主流程。
        }
    }
}
