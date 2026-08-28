namespace MarkdownViewer.Tests;

public sealed class HistoryManagerTests
{
    [Fact]
    public void SaveAndLoad_PreservesLatestWorkspaceAndDocument()
    {
        using var temp = new TemporaryDirectory();
        var workspace = Path.Combine(temp.Path, "workspace");
        var document = Path.Combine(workspace, "guide.md");

        var writer = new HistoryManager(temp.Path);
        writer.AddEntry(workspace, document);
        writer.Save();

        Assert.True(File.Exists(Path.Combine(temp.Path, "Data", "history.json")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "History", "history.json")));

        var reader = new HistoryManager(temp.Path);
        reader.Load();

        var latest = Assert.Single(reader.GetAll());
        Assert.Equal(workspace, latest.FolderPath);
        Assert.Equal(document, latest.LastFilePath);
    }

    [Fact]
    public void AddEntry_MovesExistingWorkspaceToTheFront()
    {
        using var temp = new TemporaryDirectory();
        var history = new HistoryManager(temp.Path);

        history.AddEntry("first", "first.md");
        history.AddEntry("second", "second.md");
        history.AddEntry("first", "updated.md");

        var entries = history.GetAll();
        Assert.Equal(2, entries.Count);
        Assert.Equal("first", entries[0].FolderPath);
        Assert.Equal("updated.md", entries[0].LastFilePath);
    }

    [Fact]
    public void Load_WhenDataHistoryIsMissing_ReadsLegacyHistoryFile()
    {
        using var temp = new TemporaryDirectory();
        var historyDirectory = Path.Combine(temp.Path, "History");
        Directory.CreateDirectory(historyDirectory);
        File.WriteAllText(Path.Combine(historyDirectory, "history.json"),
            "[{\"FolderPath\":\"legacy\",\"LastFilePath\":\"legacy.md\"}]");

        var history = new HistoryManager(temp.Path);
        history.Load();

        var latest = Assert.Single(history.GetAll());
        Assert.Equal("legacy", latest.FolderPath);
        Assert.Equal("legacy.md", latest.LastFilePath);
    }

    [Fact]
    public void Load_WhenDataAndLegacyHistoryExist_PrefersDataHistory()
    {
        using var temp = new TemporaryDirectory();
        var dataDirectory = Path.Combine(temp.Path, "Data");
        var historyDirectory = Path.Combine(temp.Path, "History");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(historyDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "history.json"),
            "[{\"FolderPath\":\"current\",\"LastFilePath\":\"current.md\"}]");
        File.WriteAllText(Path.Combine(historyDirectory, "history.json"),
            "[{\"FolderPath\":\"legacy\",\"LastFilePath\":\"legacy.md\"}]");

        var history = new HistoryManager(temp.Path);
        history.Load();

        var latest = Assert.Single(history.GetAll());
        Assert.Equal("current", latest.FolderPath);
        Assert.Equal("current.md", latest.LastFilePath);
    }
}
