using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace MarkdownViewer.Performance;

internal static class Program
{
    private const double RegressionTolerance = 0.25;

    private static readonly IReadOnlyDictionary<string, double> AbsoluteBudgets =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["DocumentOpen"] = 100,
            ["HtmlExport"] = 250,
            ["WebViewNavigate"] = 500,
            ["HistoryBack"] = 350,
            ["HistoryForward"] = 350
        };

    [STAThread]
    private static int Main(string[] args)
    {
        PerformanceOptions options;
        try
        {
            options = PerformanceOptions.Parse(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Startup += async (_, _) =>
        {
            var exitCode = 1;
            try
            {
                exitCode = await RunAsync(options);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }
            finally
            {
                application.Shutdown(exitCode);
            }
        };

        return application.Run();
    }

    private static async Task<int> RunAsync(PerformanceOptions options)
    {
        var markdownPath = options.DocumentPath ?? CreatePerformanceDocument();
        var ownsDocument = options.DocumentPath == null;
        try
        {
            var metrics = MeasureCoreMetrics(markdownPath);
            var webViewResult = await RunWebViewPerformanceAsync(markdownPath);
            foreach (var (name, summary) in webViewResult.Metrics)
                metrics[name] = summary;

            var environment = CreateEnvironment(webViewResult.WebViewVersion);
            var fingerprint = CreateFingerprint(environment);
            PerformanceOutput? baselineOutput = null;
            if (!options.RecordBaseline && File.Exists(options.BaselinePath))
            {
                baselineOutput = JsonSerializer.Deserialize<PerformanceOutput>(
                    await File.ReadAllTextAsync(options.BaselinePath), JsonOptions());
            }

            var currentReport = new PerformanceReport(fingerprint, metrics);
            var baselineReport = baselineOutput == null
                ? null
                : new PerformanceReport(
                    baselineOutput.EnvironmentFingerprint,
                    baselineOutput.Metrics);
            var gate = PerformanceGate.Evaluate(
                currentReport, baselineReport, RegressionTolerance, AbsoluteBudgets);
            var output = new PerformanceOutput(
                DateTimeOffset.Now,
                fingerprint,
                environment,
                metrics,
                gate.Passed,
                gate.Failures);

            await WriteJsonAsync(options.OutputPath, output);
            if (options.RecordBaseline && gate.Passed)
                await WriteJsonAsync(options.BaselinePath, output);

            PrintSummary(output, options.OutputPath, options.RecordBaseline ? options.BaselinePath : null);
            return gate.Passed ? 0 : 1;
        }
        finally
        {
            if (ownsDocument && File.Exists(markdownPath))
                File.Delete(markdownPath);
        }
    }

    private static Dictionary<string, PerformanceSummary> MeasureCoreMetrics(string markdownPath)
    {
        for (var index = 0; index < 3; index++)
        {
            var warmupMarkdown = File.ReadAllText(markdownPath);
            _ = MarkdownRenderingService.Render(warmupMarkdown);
        }

        var openSamples = new List<double>();
        for (var index = 0; index < 30; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            var markdown = File.ReadAllText(markdownPath);
            _ = MarkdownRenderingService.Render(markdown);
            stopwatch.Stop();
            openSamples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var sourceMarkdown = File.ReadAllText(markdownPath);
        var exportSamples = new List<double>();
        for (var index = 0; index < 15; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            _ = HtmlExportService.CreateDocument(
                sourceMarkdown, markdownPath, darkMode: false, "window.mermaid = {};" );
            stopwatch.Stop();
            exportSamples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        return new Dictionary<string, PerformanceSummary>(StringComparer.Ordinal)
        {
            ["DocumentOpen"] = PerformanceGate.Summarize("DocumentOpen", openSamples),
            ["HtmlExport"] = PerformanceGate.Summarize("HtmlExport", exportSamples)
        };
    }

    private static string CreatePerformanceDocument()
    {
        var path = Path.Combine(
            Path.GetTempPath(), $"MarkdownViewer-Performance-{Guid.NewGuid():N}.md");
        var builder = new StringBuilder();
        builder.AppendLine("# 性能测试文档");
        builder.AppendLine();
        builder.AppendLine("| 序号 | 字段 | 来源 | 说明 |");
        builder.AppendLine("| ---: | --- | --- | --- |");
        for (var index = 0; index < 1200; index++)
        {
            if (index % 60 == 0)
                builder.AppendLine($"\n## {index / 60 + 1}. Pipeline 配置与规划产生的字段 {index}\n");
            builder.AppendLine($"| {index} | `Field{index}` | Pipeline 动态 | 参数说明 {index}，包含中文与 English | ");
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        return path;
    }

    private static async Task<(
        string WebViewVersion,
        IReadOnlyDictionary<string, PerformanceSummary> Metrics)> RunWebViewPerformanceAsync(
        string markdownPath)
    {
        var runnerType = typeof(MainWindow).Assembly.GetType(
            "MarkdownViewer.PerformanceWebViewRunner", throwOnError: true)!;
        var method = runnerType.GetMethod(
            "RunAsync", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(runnerType.FullName, "RunAsync");
        var taskObject = method.Invoke(null, new object[] { markdownPath })
            ?? throw new InvalidOperationException("PerformanceWebViewRunner.RunAsync returned null.");
        await (Task)taskObject;
        var result = taskObject.GetType().GetProperty("Result")?.GetValue(taskObject)
            ?? throw new InvalidOperationException("PerformanceWebViewRunner did not return a result.");
        var resultType = result.GetType();
        var webViewVersion = (string?)resultType.GetProperty("WebViewVersion")?.GetValue(result)
            ?? throw new InvalidOperationException("WebView2 version is missing.");
        var metrics = (IReadOnlyDictionary<string, PerformanceSummary>?)
            resultType.GetProperty("Metrics")?.GetValue(result)
            ?? throw new InvalidOperationException("WebView2 performance metrics are missing.");
        return (webViewVersion, metrics);
    }

    private static PerformanceEnvironment CreateEnvironment(string webViewVersion)
    {
        var cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
            ?? $"{Environment.ProcessorCount} logical processors";
        var applicationVersion = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(MainWindow).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        return new PerformanceEnvironment(
            cpu,
            Environment.OSVersion.VersionString,
            Environment.Version.ToString(),
            webViewVersion,
            applicationVersion);
    }

    private static string CreateFingerprint(PerformanceEnvironment environment)
    {
        var value = JsonSerializer.Serialize(environment, JsonOptions());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static async Task WriteJsonAsync(string path, PerformanceOutput output)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(output, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions { WriteIndented = true };
    }

    private static void PrintSummary(
        PerformanceOutput output,
        string outputPath,
        string? baselinePath)
    {
        foreach (var metric in output.Metrics.Values.OrderBy(value => value.Name))
        {
            Console.WriteLine(
                $"{metric.Name}: P50={metric.P50Ms:F2} ms, P95={metric.P95Ms:F2} ms, Max={metric.MaxMs:F2} ms, N={metric.SampleCount}");
        }

        Console.WriteLine($"Report: {outputPath}");
        if (!string.IsNullOrEmpty(baselinePath))
            Console.WriteLine($"Baseline: {baselinePath}");
        foreach (var failure in output.Failures)
            Console.Error.WriteLine(failure);
    }
}

internal sealed record PerformanceEnvironment(
    string Cpu,
    string OperatingSystem,
    string DotNet,
    string WebView2,
    string ApplicationVersion);

internal sealed record PerformanceOutput(
    DateTimeOffset RecordedAt,
    string EnvironmentFingerprint,
    PerformanceEnvironment Environment,
    IReadOnlyDictionary<string, PerformanceSummary> Metrics,
    bool Passed,
    IReadOnlyList<string> Failures);

internal sealed record PerformanceOptions(
    string OutputPath,
    string BaselinePath,
    bool RecordBaseline,
    string? DocumentPath)
{
    public static PerformanceOptions Parse(string[] args)
    {
        string? outputPath = null;
        string? baselinePath = null;
        string? documentPath = null;
        var recordBaseline = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--output":
                    outputPath = ReadValue(args, ref index, "--output");
                    break;
                case "--baseline":
                    baselinePath = ReadValue(args, ref index, "--baseline");
                    break;
                case "--document":
                    documentPath = ReadValue(args, ref index, "--document");
                    break;
                case "--record-baseline":
                    recordBaseline = true;
                    break;
                default:
                    throw new ArgumentException($"未知参数: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("缺少 --output <path>。");
        if (string.IsNullOrWhiteSpace(baselinePath))
            throw new ArgumentException("缺少 --baseline <path>。");
        if (!string.IsNullOrEmpty(documentPath) && !File.Exists(documentPath))
            throw new FileNotFoundException("性能测试文档不存在。", documentPath);

        return new PerformanceOptions(
            Path.GetFullPath(outputPath),
            Path.GetFullPath(baselinePath),
            recordBaseline,
            documentPath is null ? null : Path.GetFullPath(documentPath));
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"{option} 缺少值。");
        return args[index];
    }
}
