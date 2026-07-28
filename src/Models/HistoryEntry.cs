using System;

namespace MarkdownViewer;

internal sealed class HistoryEntry
{
    public string FolderPath { get; set; } = "";
    public string? LastFilePath { get; set; }
    public DateTime OpenedAt { get; set; }
}
