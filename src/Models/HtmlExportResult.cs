using System.Collections.Generic;

namespace MarkdownViewer;

public sealed record HtmlExportResult(
    string Html,
    IReadOnlyList<string> Warnings,
    int ExternalResourceCount)
{
    public int WarningCount => Warnings.Count;
}
