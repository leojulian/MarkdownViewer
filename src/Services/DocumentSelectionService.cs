using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MarkdownViewer;

internal static class DocumentSelectionService
{
    public static bool CanSynchronizeHistoryDocument(OpenMode openMode, string? workspacePath,
        string? filePath, Func<string, bool>? exists = null)
    {
        if (openMode != OpenMode.Workspace || string.IsNullOrWhiteSpace(workspacePath) ||
            string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        exists ??= File.Exists;

        try
        {
            return exists(filePath) && LocalPathService.IsFileInsideFolder(filePath, workspacePath);
        }
        catch (Exception exception) when (exception is ArgumentException or
                                          NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static string? FindAdjacentDocument(IReadOnlyList<string> documentPaths, int deletedIndex,
        Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;

        if (deletedIndex < 0)
            return documentPaths.FirstOrDefault(path => exists(path));

        for (var index = deletedIndex + 1; index < documentPaths.Count; index++)
        {
            if (exists(documentPaths[index]))
                return documentPaths[index];
        }

        for (var index = deletedIndex - 1; index >= 0; index--)
        {
            if (exists(documentPaths[index]))
                return documentPaths[index];
        }

        return null;
    }
}
