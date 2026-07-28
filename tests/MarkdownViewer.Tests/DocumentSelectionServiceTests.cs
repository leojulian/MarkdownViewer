using MarkdownViewer;

namespace MarkdownViewer.Tests;

public sealed class DocumentSelectionServiceTests
{
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
