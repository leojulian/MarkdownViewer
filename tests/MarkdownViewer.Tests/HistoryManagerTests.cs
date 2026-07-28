using MarkdownViewer;

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
}
