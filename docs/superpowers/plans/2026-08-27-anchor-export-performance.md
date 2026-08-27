# Anchor Navigation, HTML Export, and Performance Gates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add GitHub-compatible in-document navigation, current-document offline HTML export, repeatable render/navigation performance gates, and deploy the verified release.

**Architecture:** Parse each Markdown document once through a shared Markdig pipeline and expose both rendered HTML and heading metadata. Keep export composition and image embedding in a UI-independent service; keep WebView2 orchestration in `MainWindow`. Run deterministic core performance measurements in a console runner and explicit in-app WebView2 scenarios for navigation/back/forward timing.

**Tech Stack:** .NET 8, WPF, Markdig 0.37, WebView2, xUnit, JSON performance reports

**Spec:** `docs/superpowers/specs/2026-08-27-anchor-export-performance-design.md`

## Global Constraints

- Work only in `D:\CodeHere\practise\MarkdownViewer` on Git branch `main`.
- Preserve existing UTF-8 without BOM and LF line endings for all touched source and test files.
- Do not modify the reference Markdown document in `technicaldocs`.
- Do not commit, push, merge, rebase, switch branches, or clean unrelated files.
- Use TDD for production behavior: add a focused failing test, observe the expected failure, implement minimally, then rerun.
- Normal application startup and existing workspace/link routing must remain unchanged unless the explicit performance mode is enabled.
- Deploy only after functional tests, Release build, performance gates, and final diagnostics pass.

---

### Task 1: Shared Markdown rendering and GitHub heading IDs

**Files:**

- Create: `src/Models/MarkdownHeading.cs`
- Create: `src/Models/RenderedMarkdownDocument.cs`
- Create: `src/Services/MarkdownRenderingService.cs`
- Create: `tests/MarkdownRenderingServiceTests.cs`
- Modify: `src/MainWindow.xaml.cs`

**Interfaces:**

- Produces: `MarkdownRenderingService.Render(string markdown) -> RenderedMarkdownDocument`
- Produces: `RenderedMarkdownDocument.Html` and `RenderedMarkdownDocument.Headings`
- Produces: `MarkdownHeading(Level, Text, Id)`

- [x] Add failing tests proving `## 3. Pipeline 配置与规划产生的字段` produces ID `3-pipeline-配置与规划产生的字段`, duplicate headings receive distinct IDs, and rendered HTML IDs equal heading metadata IDs.
- [x] Run `dotnet test tests\MarkdownViewer.Tests.csproj -c Release --filter FullyQualifiedName~MarkdownRenderingServiceTests` and confirm failure because the service does not exist.
- [x] Implement the shared pipeline with `UseAutoIdentifiers(AutoIdentifierOptions.GitHub)` before `UseAdvancedExtensions`, parse once, render the AST, and read `HeadingBlock` HTML attributes.
- [x] Replace `MainWindow` pipeline construction and the separate `BuildToc(markdown)` parse with `MarkdownRenderingService.Render`; populate the TOC from returned heading metadata and remove `GenerateHeadingId`.
- [x] Rerun focused tests and `dotnet build src\MarkdownViewer.csproj -c Release`.

### Task 2: Offline single-file HTML export service

**Files:**

- Create: `src/Models/HtmlExportResult.cs`
- Create: `src/Services/HtmlExportService.cs`
- Create: `tests/HtmlExportServiceTests.cs`

**Interfaces:**

- Consumes: `MarkdownRenderingService`
- Produces: `HtmlExportService.CreateDocument(string markdown, string sourceFilePath, bool darkMode, string mermaidScript) -> HtmlExportResult`
- Produces: `HtmlExportService.WriteAtomically(string targetPath, string html)`
- Produces: warning and external-resource counts in `HtmlExportResult`

- [x] Add failing tests for local PNG embedding, Chinese/space paths, preserved data URI, preserved HTTP image with external-resource count, missing image warning, embedded Mermaid, theme colors, absence of WebView metadata, and atomic output.
- [x] Run the focused tests and confirm expected compile failures.
- [x] Implement HTML image rewriting using `LocalPathService.TryResolveLocalImagePath`, extension/signature MIME mapping, base64, an export-only HTML template, and atomic same-directory temporary write/replace.
- [x] Run focused tests, then all service tests.

### Task 3: File-menu export integration

**Files:**

- Modify: `src/MainWindow.xaml`
- Modify: `src/MainWindow.xaml.cs`
- Modify: `README.md`
- Modify: `README.en.md`
- Modify: `samples/sample.md`

**Interfaces:**

- Consumes: `HtmlExportService.CreateDocument` and `WriteAtomically`
- Adds: `ExportHtml_Click`

- [x] Add “导出 HTML 文档” under the File menu after Reload and before History.
- [x] In `ExportHtml_Click`, validate the current file, show `SaveFileDialog` with `.html`, read the current Markdown, export with the current theme, write atomically, and report warnings/external resources.
- [x] Document current-document export behavior and add a Mermaid block to the sample when absent so manual export verifies SVG rendering.
- [x] Run LSP diagnostics for touched UI/source files and build Release.

### Task 4: Performance report and baseline comparison core

**Files:**

- Create: `performance/MarkdownViewer.Performance.csproj`
- Create: `performance/Program.cs`
- Create: `performance/PerformanceModels.cs`
- Create: `performance/PerformanceGate.cs`
- Create: `tests/PerformanceGateTests.cs`
- Modify: `MarkdownViewer.sln`
- Modify: `.gitignore`

**Interfaces:**

- Produces: P50/P95/max metrics and environment fingerprint JSON.
- Produces: `PerformanceGate.Evaluate(current, baseline, regressionTolerance, absoluteBudgets)`.

- [x] Add failing tests for percentile calculation, 25% same-environment regression rejection, a 5 ms minimum detectable delta for sub-millisecond noise, different-environment baseline handling, and absolute-budget rejection.
- [x] Implement models and gate logic minimally; rerun focused tests.
- [x] Implement deterministic render/export workloads with warmup and sampled measurements using the shared services.
- [x] Add the performance project to the solution and ignore generated `.verify/performance-*.json` files.
- [x] Run the core performance runner once, inspect stable P95 values, then set rounded fixed absolute budgets above observed stable values.

### Task 5: Explicit WebView2 navigation performance scenario

**Files:**

- Create: `src/Models/PerformanceScenarioOptions.cs`
- Create: `src/Services/PerformanceScenarioArguments.cs`
- Create: `tests/PerformanceScenarioArgumentsTests.cs`
- Modify: `src/App.xaml.cs`
- Modify: `src/MainWindow.xaml.cs`
- Modify: `performance/Program.cs`

**Interfaces:**

- Explicit CLI: `--performance-test <workspace> <output-json>`
- Produces: timings for initial navigation, ten-document forward navigation, history back, and history forward; each sample includes final document identity validation.

- [x] Add failing argument-parser tests proving normal file startup is unchanged and malformed performance arguments are rejected.
- [x] Implement argument parsing and pass explicit options to `MainWindow` without changing normal startup.
- [x] Add an internal state-machine runner that waits for successful `NavigationCompleted`, document metadata synchronization, and two `requestAnimationFrame` callbacks before recording each action.
- [x] Generate ten linked documents in the performance console runner, launch the Release EXE in explicit performance mode, read its JSON, and merge metrics into the report.
- [x] Run the scenario to create a baseline, rerun against that baseline, and ensure final document identities and budgets pass.

### Task 6: Full regression, real-document validation, and review

**Files:**

- Verify all files touched by Tasks 1-5.

- [x] Run `lsp_diagnostics` on all touched C# and XAML files before builds.
- [x] Run `dotnet build MarkdownViewer.sln -c Release`.
- [x] Run `dotnet test MarkdownViewer.sln -c Release --no-build`.
- [x] Run the performance project in baseline-compare mode and preserve the JSON paths/results.
- [x] Open the reference Markdown in the Release app and verify links to sections 3-9.
- [x] Export the reference document and `samples/sample.md`, open both in Edge, and verify anchors, local images, Mermaid SVG, theme, and absence of external script/style dependencies.
- [x] Run `git diff --check`, inspect exact diffs, verify encoding/newlines, and run `lens_diagnostics` mode `all` for edited files.

### Task 7: Publish and deploy the verified release

**Files:**

- Deploy artifacts only to: `D:\Program Files\MarkdownViewer`

- [x] Publish `src\MarkdownViewer.csproj` for `win-x64` Release into a clean task-specific output directory without deleting user files.
- [x] Enumerate running `MarkdownViewer.exe` processes and terminate only processes whose executable path equals `D:\Program Files\MarkdownViewer\MarkdownViewer.exe`; record the count and wait for exit.
- [x] Copy the published EXE, PDB, Mermaid asset, and required runtime files to the formal deployment directory.
- [x] Compare SHA-256 hashes for the formal EXE and published EXE.
- [x] Launch from the formal path with the reference document, rerun the explicit WebView2 performance scenario against the formal EXE, and verify the process remains responsive.
- [x] Report modified source files, tests, build/test/performance results, deployment process count, files, hashes, and any residual risk. Do not commit.
