using System.Reflection;

namespace MarkdownViewer.Tests;

public sealed class HtmlExportServiceTests
{
    [Fact]
    public void CreateDocument_EmbedsLocalImageWithChineseAndSpacePath()
    {
        using var temp = new TemporaryDirectory();
        var sourcePath = Path.Combine(temp.Path, "说明.md");
        var imagePath = Path.Combine(temp.Path, "中文 图片.png");
        var imageBytes = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2, 3 };
        File.WriteAllBytes(imagePath, imageBytes);

        var result = CreateDocument(
            "![示例](<中文 图片.png>)", sourcePath, darkMode: false, "window.mermaid = {};" );

        Assert.Contains("data:image/png;base64," + Convert.ToBase64String(imageBytes), (string)result.Html);
        Assert.Equal(0, (int)result.WarningCount);
        Assert.Equal(0, (int)result.ExternalResourceCount);
    }

    [Fact]
    public void CreateDocument_PreservesDataAndExternalImages()
    {
        using var temp = new TemporaryDirectory();
        var sourcePath = Path.Combine(temp.Path, "说明.md");
        const string dataUri = "data:image/png;base64,AA==";
        const string externalUri = "https://example.com/image.png";
        var markdown = $"![data]({dataUri})\n\n![external]({externalUri})";

        var result = CreateDocument(
            markdown, sourcePath, darkMode: false, "window.mermaid = {};" );

        Assert.Contains(dataUri, (string)result.Html);
        Assert.Contains(externalUri, (string)result.Html);
        Assert.Equal(1, (int)result.ExternalResourceCount);
        Assert.Equal(0, (int)result.WarningCount);
    }

    [Fact]
    public void CreateDocument_ReportsMissingLocalImageWithoutFailingExport()
    {
        using var temp = new TemporaryDirectory();
        var sourcePath = Path.Combine(temp.Path, "说明.md");

        var result = CreateDocument(
            "![missing](missing.png)", sourcePath, darkMode: false, "window.mermaid = {};" );

        Assert.Equal(1, (int)result.WarningCount);
        Assert.Contains("missing.png", (string)result.Html);
    }

    [Fact]
    public void CreateDocument_EmbedsMermaidAndUsesRequestedThemeWithoutAppBridge()
    {
        using var temp = new TemporaryDirectory();
        var sourcePath = Path.Combine(temp.Path, "说明.md");
        const string mermaidScript = "window.__offlineMermaid = true;";

        var result = CreateDocument(
            "```mermaid\ngraph TD\n  A-->B\n```", sourcePath, darkMode: true, mermaidScript);
        var html = (string)result.Html;

        Assert.Contains(mermaidScript, html);
        Assert.Contains("theme: 'dark'", html);
        Assert.Contains("querySelector: '.mermaid, .mermaid-container'", html);
        Assert.Contains("#1e1e1e", html);
        Assert.DoesNotContain("markdownviewer-file", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("window.chrome.webview", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDocument_UsesGitHubHeadingIdentifiers()
    {
        using var temp = new TemporaryDirectory();
        var sourcePath = Path.Combine(temp.Path, "说明.md");

        var result = CreateDocument(
            "## 3. Pipeline 配置与规划产生的字段", sourcePath, darkMode: false, "window.mermaid = {};" );

        Assert.Contains("id=\"3-pipeline-配置与规划产生的字段\"", (string)result.Html);
    }

    [Fact]
    public void WriteAtomically_ReplacesTargetAndRemovesTemporaryFile()
    {
        using var temp = new TemporaryDirectory();
        var targetPath = Path.Combine(temp.Path, "export.html");
        File.WriteAllText(targetPath, "old");

        InvokeExportMethod("WriteAtomically", targetPath, "<html>new</html>");

        Assert.Equal("<html>new</html>", File.ReadAllText(targetPath));
        Assert.Empty(Directory.GetFiles(temp.Path, ".export.html.*.tmp"));
    }

    private static dynamic CreateDocument(
        string markdown,
        string sourceFilePath,
        bool darkMode,
        string mermaidScript)
    {
        return InvokeExportMethod(
            "CreateDocument", markdown, sourceFilePath, darkMode, mermaidScript);
    }

    private static dynamic InvokeExportMethod(string methodName, params object[] arguments)
    {
        var serviceType = typeof(global::MarkdownViewer.MainWindow).Assembly.GetType(
            "MarkdownViewer.HtmlExportService", throwOnError: true)!;
        var method = serviceType.GetMethod(
            methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(serviceType.FullName, methodName);

        var result = method.Invoke(null, arguments);
        if (method.ReturnType == typeof(void))
            return new object();

        return result
            ?? throw new InvalidOperationException($"{methodName} returned null.");
    }
}
