using System.Collections.Generic;

namespace MarkdownViewer;

public sealed record RenderedMarkdownDocument(
    string Html,
    IReadOnlyList<MarkdownHeading> Headings);
