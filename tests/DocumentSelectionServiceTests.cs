using System;
using System.IO;
using MarkdownViewer;

namespace MarkdownViewer.Tests;

public sealed class DocumentSelectionServiceTests
{
    [Fact]
    public void ExistingHistoryDocumentInsideWorkspace_CanSynchronize()
    {
        using var temp = new TemporaryDirectory();
        var workspacePath = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspacePath);
        var filePath = Path.Combine(workspacePath, "A.md");
        File.WriteAllText(filePath, "# A");

        Assert.True(DocumentSelectionService.CanSynchronizeHistoryDocument(
            OpenMode.Workspace, workspacePath, filePath));
    }

    [Fact]
    public void StandaloneHistoryDocument_CannotSynchronizeWorkspaceState()
    {
        using var temp = new TemporaryDirectory();
        var filePath = Path.Combine(temp.Path, "A.md");
        File.WriteAllText(filePath, "# A");

        Assert.False(DocumentSelectionService.CanSynchronizeHistoryDocument(
            OpenMode.Standalone, temp.Path, filePath));
    }

    [Fact]
    public void HistoryDocumentOutsideWorkspace_CannotSynchronize()
    {
        using var temp = new TemporaryDirectory();
        var workspacePath = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspacePath);
        var filePath = Path.Combine(temp.Path, "outside.md");
        File.WriteAllText(filePath, "# Outside");

        Assert.False(DocumentSelectionService.CanSynchronizeHistoryDocument(
            OpenMode.Workspace, workspacePath, filePath));
    }

    [Fact]
    public void MissingHistoryDocument_CannotSynchronize()
    {
        using var temp = new TemporaryDirectory();

        Assert.False(DocumentSelectionService.CanSynchronizeHistoryDocument(
            OpenMode.Workspace, temp.Path, Path.Combine(temp.Path, "missing.md")));
    }

    [Fact]
    public void InvalidHistoryDocumentPath_CannotSynchronize()
    {
        Assert.False(DocumentSelectionService.CanSynchronizeHistoryDocument(
            OpenMode.Workspace, @"C:\workspace", "invalid\0path.md", _ => true));
    }

    [Fact]
    public void DeletedDocument_SelectsNextExistingDocumentFirst()
    {
        var documents = new[] { "a.md", "deleted.md", "c.md", "d.md" };
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.md", "c.md", "d.md" };

        var result = DocumentSelectionService.FindAdjacentDocument(
            documents, 1, existing.Contains);

        Assert.Equal("c.md", result);
    }

    [Fact]
    public void DeletedLastDocument_FallsBackToPreviousDocument()
    {
        var documents = new[] { "a.md", "b.md", "deleted.md" };
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.md", "b.md" };

        var result = DocumentSelectionService.FindAdjacentDocument(
            documents, 2, existing.Contains);

        Assert.Equal("b.md", result);
    }

    [Fact]
    public void NoExistingDocument_ReturnsNull()
    {
        Assert.Null(DocumentSelectionService.FindAdjacentDocument(
            new[] { "deleted.md" }, 0, _ => false));
    }
}
