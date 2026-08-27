using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MarkdownViewer;

public static class HtmlExportService
{
    private static readonly Regex ImageSourceRegex = new(
        @"(?<prefix><img\b[^>]*\bsrc\s*=\s*)(?<quote>[""'])(?<source>.*?)(\k<quote>)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static HtmlExportResult CreateDocument(
        string markdown,
        string sourceFilePath,
        bool darkMode,
        string mermaidScript)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentNullException.ThrowIfNull(mermaidScript);

        var rendered = MarkdownRenderingService.Render(markdown);
        var warnings = new List<string>();
        var externalResourceCount = 0;
        var markdownDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceFilePath))
            ?? throw new ArgumentException("无法确定 Markdown 文件目录。", nameof(sourceFilePath));

        var body = ImageSourceRegex.Replace(rendered.Html, match =>
        {
            var source = WebUtility.HtmlDecode(match.Groups["source"].Value);
            if (IsEmbeddedSource(source))
                return match.Value;

            if (IsExternalSource(source))
            {
                externalResourceCount++;
                return match.Value;
            }

            if (!LocalPathService.TryResolveLocalImagePath(
                    source, markdownDirectory, out var imagePath, out _))
            {
                warnings.Add($"无法解析本地图片: {source}");
                return match.Value;
            }

            try
            {
                var bytes = File.ReadAllBytes(imagePath);
                var dataUri = $"data:{GetMimeType(imagePath, bytes)};base64,{Convert.ToBase64String(bytes)}";
                return match.Groups["prefix"].Value + match.Groups["quote"].Value +
                       WebUtility.HtmlEncode(dataUri) + match.Groups["quote"].Value;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"无法读取本地图片 {imagePath}: {exception.Message}");
                return match.Value;
            }
        });

        return new HtmlExportResult(
            BuildHtmlDocument(body, sourceFilePath, darkMode, mermaidScript),
            warnings,
            externalResourceCount);
    }

    public static void WriteAtomically(string targetPath, string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(html);

        var fullTargetPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTargetPath)
            ?? throw new ArgumentException("无法确定导出目录。", nameof(targetPath));
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, fullTargetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static bool IsEmbeddedSource(string source)
    {
        return source.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
               source.StartsWith("blob:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExternalSource(string source)
    {
        if (source.StartsWith("//", StringComparison.Ordinal))
            return true;

        return Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
               uri.Scheme is "http" or "https";
    }

    private static string GetMimeType(string filePath, byte[] bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47)
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
            return "image/jpeg";

        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream"
        };
    }

    private static string BuildHtmlDocument(
        string body,
        string sourceFilePath,
        bool darkMode,
        string mermaidScript)
    {
        var backgroundColor = darkMode ? "#1e1e1e" : "#ffffff";
        var textColor = darkMode ? "#d4d4d4" : "#333333";
        var linkColor = darkMode ? "#569cd6" : "#0066cc";
        var codeBackground = darkMode ? "#2d2d2d" : "#f5f5f5";
        var borderColor = darkMode ? "#404040" : "#e0e0e0";
        var title = WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(sourceFilePath));
        var safeMermaidScript = mermaidScript.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
        var mermaidTheme = darkMode ? "dark" : "default";

        return $$"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>{{title}}</title>
    <style>
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            line-height: 1.6;
            color: {{textColor}};
            background-color: {{backgroundColor}};
            max-width: 900px;
            margin: 0 auto;
            padding: 30px 40px;
        }
        h1, h2, h3, h4, h5, h6 { margin-top: 1.5em; margin-bottom: 0.5em; font-weight: 600; line-height: 1.3; }
        h1 { font-size: 2em; border-bottom: 1px solid {{borderColor}}; padding-bottom: 0.3em; }
        h2 { font-size: 1.5em; border-bottom: 1px solid {{borderColor}}; padding-bottom: 0.3em; }
        h3 { font-size: 1.25em; }
        a { color: {{linkColor}}; text-decoration: none; }
        a:hover { text-decoration: underline; }
        code { background: {{codeBackground}}; padding: 2px 6px; border-radius: 4px; font-family: Consolas, Monaco, 'Courier New', monospace; font-size: 0.9em; }
        pre { background: {{codeBackground}}; padding: 16px; border-radius: 6px; overflow-x: auto; border: 1px solid {{borderColor}}; }
        pre code { background: none; padding: 0; }
        blockquote { margin: 1em 0; padding: 0.5em 1em; border-left: 4px solid {{linkColor}}; background: {{codeBackground}}; color: {{textColor}}; opacity: 0.9; }
        table { border-collapse: collapse; width: 100%; margin: 1em 0; }
        th, td { border: 1px solid {{borderColor}}; padding: 8px 12px; text-align: left; }
        th { background: {{codeBackground}}; font-weight: 600; }
        img { max-width: 100%; height: auto; border-radius: 4px; }
        ul, ol { padding-left: 2em; }
        li { margin: 0.3em 0; }
        hr { border: none; border-top: 1px solid {{borderColor}}; margin: 2em 0; }
        input[type="checkbox"] { margin-right: 6px; }
        .mermaid-container { margin: 1em 0; text-align: center; overflow-x: auto; }
    </style>
    <script>{{safeMermaidScript}}</script>
    <script>
        document.addEventListener('DOMContentLoaded', function () {
            var blocks = document.querySelectorAll('pre code.language-mermaid');
            if (typeof mermaid === 'undefined') return;
            mermaid.initialize({ startOnLoad: false, theme: '{{mermaidTheme}}', securityLevel: 'loose' });
            blocks.forEach(function (block) {
                var pre = block.parentElement;
                var container = document.createElement('div');
                container.className = 'mermaid-container';
                pre.parentNode.replaceChild(container, pre);
                container.textContent = block.textContent;
            });
            mermaid.run({ querySelector: '.mermaid, .mermaid-container' });
        });
    </script>
</head>
<body>
{{body}}
</body>
</html>
""";
    }
}
