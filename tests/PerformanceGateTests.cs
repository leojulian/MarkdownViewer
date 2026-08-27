using System.Collections;
using System.Reflection;

namespace MarkdownViewer.Tests;

public sealed class PerformanceGateTests
{
    private static readonly Assembly ProductionAssembly =
        typeof(global::MarkdownViewer.MainWindow).Assembly;

    [Fact]
    public void Summarize_ReportsP50P95AndMaximum()
    {
        dynamic summary = InvokeGate(
            "Summarize", "DocumentOpen", new[] { 10d, 20d, 30d, 40d, 50d });

        Assert.Equal(30, (double)summary.P50Ms);
        Assert.Equal(50, (double)summary.P95Ms);
        Assert.Equal(50, (double)summary.MaxMs);
    }

    [Fact]
    public void Evaluate_RejectsSameEnvironmentP95RegressionAboveTwentyFivePercent()
    {
        var baseline = CreateReport("machine-a", "DocumentOpen", 100);
        var current = CreateReport("machine-a", "DocumentOpen", 126);

        dynamic result = InvokeGate(
            "Evaluate", current, baseline, 0.25,
            new Dictionary<string, double> { ["DocumentOpen"] = 500 });

        var failures = ((IEnumerable)result.Failures).Cast<string>().ToArray();
        Assert.False((bool)result.Passed);
        Assert.Contains("25%", Assert.Single(failures));
    }

    [Fact]
    public void Evaluate_DoesNotCompareBaselineFromDifferentEnvironment()
    {
        var baseline = CreateReport("machine-a", "DocumentOpen", 100);
        var current = CreateReport("machine-b", "DocumentOpen", 126);

        dynamic result = InvokeGate(
            "Evaluate", current, baseline, 0.25,
            new Dictionary<string, double> { ["DocumentOpen"] = 500 });

        Assert.True((bool)result.Passed);
        Assert.Empty(((IEnumerable)result.Failures).Cast<string>());
    }

    [Fact]
    public void Evaluate_IgnoresSubFiveMillisecondBaselineNoise()
    {
        var baseline = CreateReport("machine-a", "DocumentOpen", 3);
        var current = CreateReport("machine-a", "DocumentOpen", 4.5);

        dynamic result = InvokeGate(
            "Evaluate", current, baseline, 0.25,
            new Dictionary<string, double> { ["DocumentOpen"] = 100 });

        Assert.True((bool)result.Passed);
        Assert.Empty(((IEnumerable)result.Failures).Cast<string>());
    }

    [Fact]
    public void Evaluate_RejectsAbsoluteBudgetViolation()
    {
        var current = CreateReport("machine-a", "HistoryBack", 301);

        dynamic result = InvokeGate(
            "Evaluate", current, null!, 0.25,
            new Dictionary<string, double> { ["HistoryBack"] = 300 });

        var failures = ((IEnumerable)result.Failures).Cast<string>().ToArray();
        Assert.False((bool)result.Passed);
        Assert.Contains("absolute budget", Assert.Single(failures), StringComparison.OrdinalIgnoreCase);
    }

    private static object CreateReport(string fingerprint, string metricName, double p95)
    {
        var summaryType = GetProductionType("PerformanceSummary");
        var summary = Activator.CreateInstance(
            summaryType, metricName, p95, p95, p95, 5)!;
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), summaryType);
        var metrics = Activator.CreateInstance(dictionaryType)!;
        dictionaryType.GetMethod("Add")!.Invoke(metrics, new[] { metricName, summary });

        return Activator.CreateInstance(
            GetProductionType("PerformanceReport"), fingerprint, metrics)!;
    }

    private static dynamic InvokeGate(string methodName, params object[] arguments)
    {
        var gateType = GetProductionType("PerformanceGate");
        var method = gateType.GetMethod(
            methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(gateType.FullName, methodName);

        return method.Invoke(null, arguments)
            ?? throw new InvalidOperationException($"{methodName} returned null.");
    }

    private static Type GetProductionType(string typeName)
    {
        return ProductionAssembly.GetType($"MarkdownViewer.{typeName}", throwOnError: true)!;
    }
}
