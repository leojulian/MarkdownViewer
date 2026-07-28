using MarkdownViewer;

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
            IsToolbarVisible = false
        };
        saved.Save();

        var loaded = new ConfigManager(temp.Path);
        loaded.Load();

        Assert.Equal(1.75, loaded.ZoomFactor);
        Assert.True(loaded.IsDarkMode);
        Assert.True(loaded.IsTocVisible);
        Assert.False(loaded.IsToolbarVisible);
    }
}
