using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MarkdownViewer;

public sealed record PerformanceWebViewResult(
    string WebViewVersion,
    IReadOnlyDictionary<string, PerformanceSummary> Metrics);

public static class PerformanceWebViewRunner
{
    public static async Task<PerformanceWebViewResult> RunAsync(string markdownPath)
    {
        var webView = new WebView2();
        var window = new Window
        {
            Title = "MarkdownViewer Performance",
            Width = 1024,
            Height = 768,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = webView
        };

        try
        {
            window.Show();
            await webView.EnsureCoreWebView2Async();
            var core = webView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 初始化失败。");
            var webViewVersion = core.Environment.BrowserVersionString;
            var sourceMarkdown = await File.ReadAllTextAsync(markdownPath);
            var documents = CreateDocuments(sourceMarkdown);

            await NavigateAndMeasureAsync(
                webView,
                () => webView.NavigateToString(BuildHtml("warmup", "<p>warmup</p>")),
                "warmup");

            var navigateSamples = new List<double>();
            foreach (var document in documents)
            {
                navigateSamples.Add(await NavigateAndMeasureAsync(
                    webView,
                    () => webView.NavigateToString(document.Html),
                    document.Identity));
            }

            var backSamples = new List<double>();
            for (var index = documents.Count - 2; index >= 0; index--)
            {
                if (!core.CanGoBack)
                    throw new InvalidOperationException("WebView2 历史栈缺少后退记录。");
                var expectedIdentity = documents[index].Identity;
                backSamples.Add(await NavigateAndMeasureAsync(
                    webView, core.GoBack, expectedIdentity));
            }

            var forwardSamples = new List<double>();
            for (var index = 1; index < documents.Count; index++)
            {
                if (!core.CanGoForward)
                    throw new InvalidOperationException("WebView2 历史栈缺少前进记录。");
                var expectedIdentity = documents[index].Identity;
                forwardSamples.Add(await NavigateAndMeasureAsync(
                    webView, core.GoForward, expectedIdentity));
            }

            return new PerformanceWebViewResult(
                webViewVersion,
                new Dictionary<string, PerformanceSummary>(StringComparer.Ordinal)
                {
                    ["WebViewNavigate"] = PerformanceGate.Summarize(
                        "WebViewNavigate", navigateSamples),
                    ["HistoryBack"] = PerformanceGate.Summarize(
                        "HistoryBack", backSamples),
                    ["HistoryForward"] = PerformanceGate.Summarize(
                        "HistoryForward", forwardSamples)
                });
        }
        finally
        {
            window.Close();
            webView.Dispose();
        }
    }

    private static IReadOnlyList<PerformanceDocument> CreateDocuments(string sourceMarkdown)
    {
        var documents = new List<PerformanceDocument>();
        for (var index = 0; index < 10; index++)
        {
            var identity = $"document-{index}.md";
            var markdown = $"# Performance Document {index}\n\n{sourceMarkdown}";
            var rendered = MarkdownRenderingService.Render(markdown);
            documents.Add(new PerformanceDocument(
                identity,
                BuildHtml(identity, rendered.Html)));
        }

        return documents;
    }

    private static string BuildHtml(string identity, string body)
    {
        return $$"""
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <meta name="performance-document" content="{{WebUtility.HtmlEncode(identity)}}">
    <style>
        body { font-family: 'Segoe UI', sans-serif; max-width: 900px; margin: 0 auto; padding: 30px 40px; line-height: 1.6; }
        table { border-collapse: collapse; width: 100%; }
        th, td { border: 1px solid #ddd; padding: 8px 12px; }
    </style>
</head>
<body>{{body}}</body>
</html>
""";
    }

    private static async Task<double> NavigateAndMeasureAsync(
        WebView2 webView,
        Action navigate,
        string expectedIdentity)
    {
        var core = webView.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 尚未初始化。");
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigationCompleted(
            object? sender,
            CoreWebView2NavigationCompletedEventArgs eventArgs)
        {
            completion.TrySetResult(eventArgs);
        }

        core.NavigationCompleted += OnNavigationCompleted;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            navigate();
            var completed = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (!completed.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"WebView2 导航失败: {completed.WebErrorStatus}");
            }

            await WaitForLayoutAsync(webView);
            var scriptResult = await core.ExecuteScriptAsync(
                "document.querySelector('meta[name=\"performance-document\"]')?.content || ''");
            var actualIdentity = JsonSerializer.Deserialize<string>(scriptResult);
            if (!string.Equals(actualIdentity, expectedIdentity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"页面身份不一致，期望 {expectedIdentity}，实际 {actualIdentity}。");
            }

            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }
        finally
        {
            core.NavigationCompleted -= OnNavigationCompleted;
        }
    }

    private static async Task WaitForLayoutAsync(WebView2 webView)
    {
        var core = webView.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 尚未初始化。");
        await core.ExecuteScriptAsync(
            "window.__markdownViewerLayoutReady=false;" +
            "requestAnimationFrame(function(){requestAnimationFrame(function(){" +
            "window.__markdownViewerLayoutReady=true;});});");

        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            var ready = await core.ExecuteScriptAsync(
                "window.__markdownViewerLayoutReady === true");
            if (string.Equals(ready, "true", StringComparison.OrdinalIgnoreCase))
                return;
            await Task.Delay(5);
        }

        throw new TimeoutException("等待 WebView2 完成两帧布局超时。");
    }

    private sealed record PerformanceDocument(string Identity, string Html);
}
