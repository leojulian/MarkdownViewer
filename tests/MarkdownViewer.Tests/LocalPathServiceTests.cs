using MarkdownViewer;

namespace MarkdownViewer.Tests;

public sealed class LocalPathServiceTests
{
    [Fact]
    public void ResolveFileUri_DecodesPathAndFragment()
    {
        var result = LocalPathService.TryResolveLocalInput(
            "file:///D:/Docs/%E4%B8%AD%E6%96%87%20Guide.md#%E9%AA%8C%E8%AF%81",
            out var path, out var fragment, out var error);

        Assert.True(result, error);
        Assert.Equal(Path.GetFullPath(@"D:\Docs\中文 Guide.md"), path);
        Assert.Equal("验证", fragment);
    }

    [Fact]
    public void ResolveLocalInput_RejectsNonFileUri()
    {
        var result = LocalPathService.TryResolveLocalInput(
            "https://example.com/readme.md", out _, out _, out var error);

        Assert.False(result);
        Assert.Contains("https", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsFileInsideFolder_DoesNotMatchSiblingWithSamePrefix()
    {
        Assert.True(LocalPathService.IsFileInsideFolder(@"D:\Docs\guide.md", @"D:\Docs"));
        Assert.False(LocalPathService.IsFileInsideFolder(@"D:\Docs2\guide.md", @"D:\Docs"));
    }

    [Fact]
    public void FindBestWorkspace_ChoosesDeepestExistingWorkspace()
    {
        using var temp = new TemporaryDirectory();
        var root = Directory.CreateDirectory(Path.Combine(temp.Path, "workspace")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(root, "docs")).FullName;
        var file = Path.Combine(nested, "guide.md");
        File.WriteAllText(file, "# test");

        var result = LocalPathService.FindBestWorkspaceForFile(file, new[] { root, nested });

        Assert.Equal(nested, result);
    }

    [Fact]
    public void ResolveDocumentLink_UsesCurrentMarkdownAsBase()
    {
        var current = Path.GetFullPath(@"D:\Docs\root\current.md");

        var result = LocalPathService.TryResolveDocumentLinkUri(
            "../other.md#section", current, out var uri);

        Assert.True(result);
        Assert.True(uri.IsFile);
        Assert.Equal(Path.GetFullPath(@"D:\Docs\other.md"), Path.GetFullPath(uri.LocalPath));
        Assert.Equal("#section", uri.Fragment);
    }

    [Fact]
    public void ResolveDocumentLink_PreservesUncShare()
    {
        var result = LocalPathService.TryResolveDocumentLinkUri(
            "../other.md#section", @"\\server\share\root\current.md", out var uri);

        Assert.True(result);
        Assert.Equal(@"\\server\share\other.md", uri.LocalPath);
        Assert.Equal("#section", uri.Fragment);
    }

    [Fact]
    public void ResolveLocalImage_DecodesRelativePathAndPreservesSuffix()
    {
        var markdownDirectory = Path.GetFullPath(@"D:\Docs\guide");

        var result = LocalPathService.TryResolveLocalImagePath(
            "images/%E4%B8%AD%E6%96%87%20image.png?size=2#preview",
            markdownDirectory, out var imagePath, out var suffix);

        Assert.True(result);
        Assert.Equal(Path.GetFullPath(@"D:\Docs\guide\images\中文 image.png"), imagePath);
        Assert.Equal("?size=2#preview", suffix);
    }

    [Theory]
    [InlineData("data:image/png;base64,AA==")]
    [InlineData("https://example.com/image.png")]
    [InlineData("//example.com/image.png")]
    public void ResolveLocalImage_IgnoresNonLocalSources(string source)
    {
        Assert.False(LocalPathService.TryResolveLocalImagePath(
            source, @"D:\Docs", out _, out _));
    }
}
