using System.Text.Json;

namespace MarkdownViewer.Tests;

public sealed class DocumentMetadataServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("\"\"")]
    [InlineData("\"bad\\u0000path\"")]
    public void ParseScriptResult_ReturnsNull_ForRejectedInputs(string? scriptResult)
    {
        Assert.Null(DocumentMetadataService.ParseScriptResult(scriptResult));
    }

    [Fact]
    public void BuildMetadataTagAndParseScriptResult_PreserveDocumentPath()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "MarkdownViewer", "章节 \"一\".md");

        var tag = DocumentMetadataService.BuildMetadataTag(filePath);
        var scriptResult = JsonSerializer.Serialize(Path.GetFullPath(filePath));

        Assert.Contains("name=\"markdownviewer-file\"", tag);
        Assert.Contains("&quot;", tag);
        Assert.Equal(Path.GetFullPath(filePath), DocumentMetadataService.ParseScriptResult(scriptResult));
    }
}
