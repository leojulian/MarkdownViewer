using MarkdownViewer;

namespace MarkdownViewer.Tests;

public sealed class FavoritesManagerTests
{
    [Fact]
    public void MultipleManagers_MergeAddsAndRemovalsWithoutOverwritingEachOther()
    {
        using var temp = new TemporaryDirectory();
        var firstFile = Path.Combine(temp.Path, "first.md");
        var secondFile = Path.Combine(temp.Path, "second.md");
        File.WriteAllText(firstFile, "# first");
        File.WriteAllText(secondFile, "# second");

        var first = CreateManager(temp.Path);
        var second = CreateManager(temp.Path);

        Assert.True(first.Add(firstFile, out var firstError), firstError);
        Assert.True(second.Add(secondFile, out var secondError), secondError);
        Assert.Contains(firstFile, first.GetAll(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(secondFile, first.GetAll(), StringComparer.OrdinalIgnoreCase);

        Assert.True(first.Remove(firstFile, out var removeError), removeError);
        Assert.DoesNotContain(firstFile, second.GetAll(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(secondFile, second.GetAll(), StringComparer.OrdinalIgnoreCase);
    }

    private static FavoritesManager CreateManager(string workspace)
    {
        var manager = new FavoritesManager();
        manager.SetFolderPath(workspace);
        manager.Load();
        return manager;
    }
}
