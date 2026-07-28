using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MarkdownViewer;

internal sealed class HistoryManager
{
    private const int MaxHistoryEntries = 20;
    private readonly string _historyDirectory;
    private readonly string _historyFile;
    private List<HistoryEntry> _entries = new();

    public HistoryManager(string? baseDirectory = null)
    {
        _historyDirectory = Path.Combine(baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, "History");
        _historyFile = Path.Combine(_historyDirectory, "history.json");
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_historyFile))
            {
                var json = File.ReadAllText(_historyFile, Encoding.UTF8);
                _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? new List<HistoryEntry>();
            }
        }
        catch
        {
            _entries = new List<HistoryEntry>();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_historyDirectory);

            List<HistoryEntry> existing = new();
            if (File.Exists(_historyFile))
            {
                var json = File.ReadAllText(_historyFile, Encoding.UTF8);
                existing = JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? new List<HistoryEntry>();
            }

            foreach (var entry in _entries)
            {
                existing.RemoveAll(candidate => string.Equals(candidate.FolderPath, entry.FolderPath,
                    StringComparison.OrdinalIgnoreCase));
                existing.Insert(0, entry);
            }

            if (existing.Count > MaxHistoryEntries)
                existing.RemoveRange(MaxHistoryEntries, existing.Count - MaxHistoryEntries);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_historyFile, JsonSerializer.Serialize(existing, options), Encoding.UTF8);
        }
        catch
        {
            // 历史记录失败不应影响查看器主流程。
        }
    }

    public List<HistoryEntry> GetAll() => _entries.ToList();

    public HistoryEntry? GetLatest() => _entries.FirstOrDefault();

    public void AddEntry(string folderPath, string? lastFilePath)
    {
        _entries.RemoveAll(entry => string.Equals(entry.FolderPath, folderPath,
            StringComparison.OrdinalIgnoreCase));
        _entries.Insert(0, new HistoryEntry
        {
            FolderPath = folderPath,
            LastFilePath = lastFilePath,
            OpenedAt = DateTime.Now
        });

        if (_entries.Count > MaxHistoryEntries)
            _entries.RemoveRange(MaxHistoryEntries, _entries.Count - MaxHistoryEntries);
    }
}
