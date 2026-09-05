using System;
using System.Linq;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace MarkdownViewer;

public static class MarkdownRenderingService
{
    private static readonly MarkdownPipeline Pipeline = CreatePipeline();

    public static RenderedMarkdownDocument Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var document = Markdown.Parse(markdown, Pipeline);
        var html = Markdown.ToHtml(document, Pipeline);
        var headings = document.Descendants<HeadingBlock>()
            .Select(heading => new MarkdownHeading(
                heading.Level,
                GetHeadingText(markdown, heading),
                heading.GetAttributes().Id ?? string.Empty))
            .ToArray();

        return new RenderedMarkdownDocument(html, headings);
    }

    private static string GetHeadingText(string markdown, HeadingBlock heading)
    {
        var source = markdown.Substring(heading.Span.Start, heading.Span.Length);
        return Markdown.ToPlainText(source, Pipeline).TrimEnd('\r', '\n');
    }

    private static MarkdownPipeline CreatePipeline()
    {
        return new MarkdownPipelineBuilder()
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .UseAdvancedExtensions()
            .UsePipeTables()
            .UseTaskLists()
            .UseEmojiAndSmiley()
            .Build();
    }
}
