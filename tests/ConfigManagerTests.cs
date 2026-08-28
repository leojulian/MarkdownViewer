namespace MarkdownViewer.Tests;

public sealed class ConfigManagerTests
{
    [Fact]
    public void SaveAndLoad_RestoresAllUiSettings()
    {
        using var temp = new TemporaryDirectory();
        var saved = new ConfigManager(temp.Path)
        {
            ZoomFactor = 1.75,
            IsDarkMode = true,
            IsTocVisible = true,
            IsToolbarVisible = false,
            TocWidth = 360
        };
        saved.Save();

        Assert.True(File.Exists(Path.Combine(temp.Path, "Data", "config.json")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "History", "config.json")));

        var loaded = new ConfigManager(temp.Path);
        loaded.Load();

        Assert.Equal(1.75, loaded.ZoomFactor);
        Assert.True(loaded.IsDarkMode);
        Assert.True(loaded.IsTocVisible);
        Assert.False(loaded.IsToolbarVisible);
        Assert.Equal(360, loaded.TocWidth);
    }

    [Theory]
    [InlineData(80, ConfigManager.MinTocWidth)]
    [InlineData(150, 150)]
    [InlineData(320, 320)]
    [InlineData(480, 480)]
    [InlineData(900, ConfigManager.MaxTocWidth)]
    public void Load_ClampsTocWidth(double savedWidth, double expectedWidth)
    {
        using var temp = new TemporaryDirectory();
        var saved = new ConfigManager(temp.Path) { TocWidth = savedWidth };
        saved.Save();

        var loaded = new ConfigManager(temp.Path);
        loaded.Load();

        Assert.Equal(expectedWidth, loaded.TocWidth);
    }

    [Fact]
    public void Load_LegacyConfigWithoutTocWidth_UsesDefaultWidth()
    {
        using var temp = new TemporaryDirectory();
        var historyDirectory = Path.Combine(temp.Path, "History");
        Directory.CreateDirectory(historyDirectory);
        File.WriteAllText(Path.Combine(historyDirectory, "config.json"), "{\"ZoomFactor\":1.0}");

        var loaded = new ConfigManager(temp.Path);
        loaded.Load();

        Assert.Equal(ConfigManager.DefaultTocWidth, loaded.TocWidth);
    }

    [Fact]
    public void Load_WhenDataConfigIsMissing_ReadsLegacyHistoryConfig()
    {
        using var temp = new TemporaryDirectory();
        var historyDirectory = Path.Combine(temp.Path, "History");
        Directory.CreateDirectory(historyDirectory);
        File.WriteAllText(Path.Combine(historyDirectory, "config.json"), "{\"ZoomFactor\":1.5}");

        var loaded = new ConfigManager(temp.Path);
        loaded.Load();

        Assert.Equal(1.5, loaded.ZoomFactor);
    }

    [Fact]
    public void Load_WhenDataAndLegacyConfigsExist_PrefersDataConfig()
    {
        using var temp = new TemporaryDirectory();
        var dataDirectory = Path.Combine(temp.Path, "Data");
        var historyDirectory = Path.Combine(temp.Path, "History");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(historyDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "config.json"), "{\"ZoomFactor\":1.75}");
        File.WriteAllText(Path.Combine(historyDirectory, "config.json"), "{\"ZoomFactor\":1.25}");

        var loaded = new ConfigManager(temp.Path);
        loaded.Load();

        Assert.Equal(1.75, loaded.ZoomFactor);
    }
}
