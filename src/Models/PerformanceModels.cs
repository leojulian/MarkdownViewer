using System.Collections.Generic;

namespace MarkdownViewer;

public sealed record PerformanceSummary(
    string Name,
    double P50Ms,
    double P95Ms,
    double MaxMs,
    int SampleCount);

public sealed record PerformanceReport(
    string EnvironmentFingerprint,
    IReadOnlyDictionary<string, PerformanceSummary> Metrics);

public sealed record PerformanceGateResult(
    bool Passed,
    IReadOnlyList<string> Failures);
