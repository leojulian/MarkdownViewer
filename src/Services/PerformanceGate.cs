using System;
using System.Collections.Generic;
using System.Linq;

namespace MarkdownViewer;

public static class PerformanceGate
{
    private const double MinimumRegressionDeltaMs = 5.0;

    public static PerformanceSummary Summarize(string name, IEnumerable<double> samples)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(samples);

        var ordered = samples.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
            throw new ArgumentException("至少需要一个性能样本。", nameof(samples));

        return new PerformanceSummary(
            name,
            Percentile(ordered, 0.50),
            Percentile(ordered, 0.95),
            ordered[^1],
            ordered.Length);
    }

    public static PerformanceGateResult Evaluate(
        PerformanceReport current,
        PerformanceReport? baseline,
        double regressionTolerance,
        IReadOnlyDictionary<string, double> absoluteBudgets)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(absoluteBudgets);
        if (regressionTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(regressionTolerance));

        var failures = new List<string>();
        foreach (var (name, metric) in current.Metrics)
        {
            if (absoluteBudgets.TryGetValue(name, out var budget) && metric.P95Ms > budget)
            {
                failures.Add(
                    $"{name} P95 {metric.P95Ms:F2} ms exceeds absolute budget {budget:F2} ms.");
            }
        }

        if (baseline != null && string.Equals(
                current.EnvironmentFingerprint,
                baseline.EnvironmentFingerprint,
                StringComparison.Ordinal))
        {
            foreach (var (name, metric) in current.Metrics)
            {
                if (!baseline.Metrics.TryGetValue(name, out var baselineMetric))
                    continue;

                var limit = Math.Max(
                    baselineMetric.P95Ms * (1 + regressionTolerance),
                    baselineMetric.P95Ms + MinimumRegressionDeltaMs);
                if (metric.P95Ms > limit)
                {
                    failures.Add(
                        $"{name} P95 {metric.P95Ms:F2} ms exceeds baseline {baselineMetric.P95Ms:F2} ms beyond the {regressionTolerance:P0} gate (minimum detectable delta {MinimumRegressionDeltaMs:F2} ms).");
                }
            }
        }

        return new PerformanceGateResult(failures.Count == 0, failures);
    }

    private static double Percentile(IReadOnlyList<double> ordered, double percentile)
    {
        var index = Math.Max(0, (int)Math.Ceiling(percentile * ordered.Count) - 1);
        return ordered[index];
    }
}
