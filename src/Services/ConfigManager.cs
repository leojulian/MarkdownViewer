using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MarkdownViewer;

internal sealed class ConfigManager
{
    internal const double DefaultTocWidth = 280;
    internal const double MinTocWidth = 150;
    internal const double MaxTocWidth = 480;

    private readonly string _configFile;
    private readonly string _legacyConfigFile;

    public ConfigManager()
        : this(AppDomain.CurrentDomain.BaseDirectory)
    {
    }

    internal ConfigManager(string baseDirectory)
    {
        _configFile = Path.Combine(baseDirectory, "Data", "config.json");
        _legacyConfigFile = Path.Combine(baseDirectory, "History", "config.json");
    }

    public double ZoomFactor { get; set; } = 1.0;
    public bool IsDarkMode { get; set; }
    public bool IsTocVisible { get; set; }
    public bool IsToolbarVisible { get; set; } = true;
    public double TocWidth { get; set; } = DefaultTocWidth;

    public void Load()
    {
        try
        {
            var configFile = File.Exists(_configFile) ? _configFile : _legacyConfigFile;
            if (!File.Exists(configFile))
                return;

            var json = File.ReadAllText(configFile, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<ConfigManager>(json);
            if (data == null)
                return;

            ZoomFactor = data.ZoomFactor;
            IsDarkMode = data.IsDarkMode;
            IsTocVisible = data.IsTocVisible;
            IsToolbarVisible = data.IsToolbarVisible;
            TocWidth = NormalizeTocWidth(data.TocWidth);
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

    internal static double NormalizeTocWidth(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width))
            return DefaultTocWidth;

        return Math.Clamp(width, MinTocWidth, MaxTocWidth);
    }
}
