using System.Collections;

namespace MarkdownViewer.Tests;

public sealed class MarkdownRenderingServiceTests
{
    [Fact]
    public void Render_UsesGitHubIdentifierForNumberedMixedLanguageHeading()
    {
        const string markdown = "## 3. Pipeline 配置与规划产生的字段";

        dynamic document = Render(markdown);
        var headings = ((IEnumerable)document.Headings).Cast<object>().ToArray();
        dynamic heading = Assert.Single(headings);

        Assert.Equal(2, (int)heading.Level);
        Assert.Equal("3. Pipeline 配置与规划产生的字段", (string)heading.Text);
        Assert.Equal("3-pipeline-配置与规划产生的字段", (string)heading.Id);
        Assert.Contains("id=\"3-pipeline-配置与规划产生的字段\"", (string)document.Html);
    }

    [Fact]
    public void Render_AssignsDistinctIdentifiersToDuplicateHeadings()
    {
        const string markdown = "## 重复标题\n\n## 重复标题";

        dynamic document = Render(markdown);
        var ids = ((IEnumerable)document.Headings)
            .Cast<object>()
            .Select(heading => (string)heading.GetType().GetProperty("Id")!.GetValue(heading)!)
            .ToArray();

        Assert.Equal(new[] { "重复标题", "重复标题-1" }, ids);
    }

    [Fact]
    public void Render_CombinesAllInlineContentInHeadingMetadata()
    {
        const string markdown = "#### 3.3.2 Core-owned `PrepareParam`字段";

        dynamic document = Render(markdown);
        var headings = ((IEnumerable)document.Headings).Cast<object>().ToArray();
        dynamic heading = Assert.Single(headings);

        Assert.Equal("3.3.2 Core-owned PrepareParam字段", (string)heading.Text);
        Assert.Contains(">3.3.2 Core-owned <code>PrepareParam</code>字段</h4>", (string)document.Html);
    }

    [Fact]
    public void Render_HeadingMetadataMatchesRenderedHtmlIdentifiers()
    {
        const string markdown = "# Overview\n\n## 3. Pipeline 配置与规划产生的字段\n\n## 重复标题\n\n## 重复标题";

        dynamic document = Render(markdown);
        var htmlIds = System.Text.RegularExpressions.Regex.Matches(
                (string)document.Html, "<h[1-6] id=\"(?<id>[^\"]+)\"")
            .Select(match => match.Groups["id"].Value)
            .ToArray();
        var headingIds = ((IEnumerable)document.Headings)
            .Cast<object>()
            .Select(heading => (string)heading.GetType().GetProperty("Id")!.GetValue(heading)!)
            .ToArray();

        Assert.Equal(headingIds, htmlIds);
    }

    private static dynamic Render(string markdown)
    {
        var serviceType = typeof(global::MarkdownViewer.MainWindow).Assembly.GetType(
            "MarkdownViewer.MarkdownRenderingService", throwOnError: true)!;
        var method = serviceType.GetMethod("Render")
            ?? throw new MissingMethodException(serviceType.FullName, "Render");
        return method.Invoke(null, new object[] { markdown })
            ?? throw new InvalidOperationException("Render returned null.");
    }
}
