using System;
using System.IO;
using System.Net;
using System.Text.Json;

namespace MarkdownViewer;

internal static class DocumentMetadataService
{
    private const string MetadataName = "markdownviewer-file";

    public static string BuildMetadataTag(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return string.Empty;

        var fullPath = Path.GetFullPath(filePath);
        return $"<meta name=\"{MetadataName}\" content=\"{WebUtility.HtmlEncode(fullPath)}\">";
    }

    public static string? ParseScriptResult(string? scriptResult)
    {
        if (string.IsNullOrWhiteSpace(scriptResult))
            return null;

        try
        {
            var filePath = JsonSerializer.Deserialize<string>(scriptResult);
            return string.IsNullOrWhiteSpace(filePath) ? null : Path.GetFullPath(filePath);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
    }
}
