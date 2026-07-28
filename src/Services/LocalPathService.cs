using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MarkdownViewer;

internal static class LocalPathService
{
    public static bool TryResolveLocalInput(string input, out string localPath,
        out string? fragment, out string? error)
    {
        localPath = "";
        fragment = null;
        error = null;
        var value = input.Trim().Trim('"');

        try
        {
            if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsFile)
                {
                    error = "无效的 file URI。";
                    return false;
                }

                localPath = Path.GetFullPath(uri.LocalPath);
                fragment = string.IsNullOrEmpty(uri.Fragment)
                    ? null
                    : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
                return true;
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out var nonFileUri) &&
                !nonFileUri.IsFile && !string.IsNullOrEmpty(nonFileUri.Scheme))
            {
                error = $"不支持的 URI 协议: {nonFileUri.Scheme}";
                return false;
            }

            localPath = Path.GetFullPath(value);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string? FindBestWorkspaceForFile(string filePath, IEnumerable<string> workspacePaths)
    {
        string fullFilePath;
        try
        {
            fullFilePath = Path.GetFullPath(filePath);
        }
        catch
        {
            return null;
        }

        return workspacePaths
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Where(path => IsFileInsideFolder(fullFilePath, path))
            .OrderByDescending(path => path.Length)
            .FirstOrDefault();
    }

    public static bool IsFileInsideFolder(string filePath, string folderPath)
    {
        var relativePath = Path.GetRelativePath(folderPath, filePath);
        return !Path.IsPathRooted(relativePath) &&
               !relativePath.Equals("..", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    public static bool TryResolveDocumentLinkUri(string link, string? currentFilePath, out Uri uri)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out uri!))
            return true;

        if (string.IsNullOrEmpty(currentFilePath))
            return false;

        var baseUri = new UriBuilder
        {
            Scheme = Uri.UriSchemeFile,
            Host = "",
            Path = Path.GetFullPath(currentFilePath)
        }.Uri;
        return Uri.TryCreate(baseUri, link, out uri!);
    }

    public static bool TryResolveLocalImagePath(string source, string markdownDirectory,
        out string imagePath, out string suffix)
    {
        imagePath = "";
        suffix = "";
        if (string.IsNullOrWhiteSpace(source) ||
            source.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            if (source.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(source, UriKind.Absolute, out var fileUri) || !fileUri.IsFile)
                    return false;

                imagePath = Path.GetFullPath(fileUri.LocalPath);
                suffix = fileUri.Query + fileUri.Fragment;
                return true;
            }

            var pathPart = source;
            var suffixIndex = source.IndexOfAny(['?', '#']);
            if (suffixIndex >= 0)
            {
                pathPart = source[..suffixIndex];
                suffix = source[suffixIndex..];
            }

            pathPart = Uri.UnescapeDataString(pathPart).Replace('/', Path.DirectorySeparatorChar);
            imagePath = Path.GetFullPath(Path.IsPathRooted(pathPart)
                ? pathPart
                : Path.Combine(markdownDirectory, pathPart));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsSamePath(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return false;

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
