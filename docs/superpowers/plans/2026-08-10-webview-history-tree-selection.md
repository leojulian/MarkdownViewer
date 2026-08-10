# WebView History Tree Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make WebView2 mouse back/forward navigation update the workspace tree selection, title, status, path, favorite state, and current document as one validated state transition, while leaving all external UI state unchanged for invalid history targets.

**Architecture:** Keep WebView2 as the navigation-history owner and identify each rendered Markdown page with embedded document metadata. Move path/workspace eligibility checks into `DocumentSelectionService`, perform all validation and tree lookup before changing WPF state, then select the existing tree node without reloading the document.

**Tech Stack:** .NET 8, WPF, WebView2, Markdig, xUnit

---

## Execution constraints

- Work only in `D:\CodeHere\practise\MarkdownViewer` on Git branch `main`.
- Preserve the user's existing uncommitted changes in `src/MainWindow.xaml.cs`, `src/Services/DocumentMetadataService.cs`, and `tests/DocumentMetadataServiceTests.cs`.
- Before editing existing source files, invoke the `preserve-file-encoding` skill and preserve their current encoding and line endings.
- Do not commit, push, merge, rebase, switch branches, clean generated files, or deploy unless the user gives separate explicit authorization.
- Do not rebuild the document tree when a browser-history target cannot be located.
- Do not call `LoadMarkdownFile` while synchronizing a completed WebView2 history navigation.

## File map

- Modify `src/Services/DocumentMetadataService.cs`: safely parse document identity returned by WebView2 scripts.
- Modify `tests/DocumentMetadataServiceTests.cs`: cover missing, malformed, and invalid-path metadata.
- Modify `src/Services/DocumentSelectionService.cs`: decide whether a history document is eligible to synchronize into the current workspace state.
- Modify `tests/DocumentSelectionServiceTests.cs`: test workspace, standalone, outside-workspace, missing-file, and invalid-path eligibility.
- Modify `src/MainWindow.xaml.cs`: validate the history target, find the tree node without side effects, select it without loading, and then update the remaining UI state.
- No README or persistent data format change is required.

### Task 1: Harden rendered-page document metadata parsing

**Files:**
- Modify: `tests/DocumentMetadataServiceTests.cs`
- Modify: `src/Services/DocumentMetadataService.cs`

- [ ] **Step 1: Add failing metadata rejection tests**

Append these tests to `DocumentMetadataServiceTests`:

```csharp
[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("null")]
[InlineData("not-json")]
[InlineData("\"\"")]
public void ParseScriptResult_MissingOrMalformedValue_ReturnsNull(string? scriptResult)
{
    Assert.Null(DocumentMetadataService.ParseScriptResult(scriptResult));
}

[Fact]
public void ParseScriptResult_InvalidWindowsPath_ReturnsNull()
{
    var scriptResult = JsonSerializer.Serialize("invalid\0path.md");

    Assert.Null(DocumentMetadataService.ParseScriptResult(scriptResult));
}
```

- [ ] **Step 2: Run the focused test and confirm the invalid-path case fails**

Run:

```powershell
dotnet test tests\MarkdownViewer.Tests.csproj -c Release --filter FullyQualifiedName~DocumentMetadataServiceTests
```

Expected: `ParseScriptResult_InvalidWindowsPath_ReturnsNull` fails because `Path.GetFullPath` currently throws for the embedded null character; existing valid-path coverage remains passing.

- [ ] **Step 3: Make metadata parsing safely reject path conversion failures**

Replace `ParseScriptResult` with:

```csharp
public static string? ParseScriptResult(string? scriptResult)
{
    if (string.IsNullOrWhiteSpace(scriptResult))
        return null;

    try
    {
        var filePath = JsonSerializer.Deserialize<string>(scriptResult);
        return string.IsNullOrWhiteSpace(filePath) ? null : Path.GetFullPath(filePath);
    }
    catch (Exception exception) when (exception is JsonException or ArgumentException or
                                      NotSupportedException or PathTooLongException)
    {
        return null;
    }
}
```

- [ ] **Step 4: Run the focused metadata tests**

Run:

```powershell
dotnet test tests\MarkdownViewer.Tests.csproj -c Release --filter FullyQualifiedName~DocumentMetadataServiceTests
```

Expected: all `DocumentMetadataServiceTests` pass.

### Task 2: Define history-target synchronization eligibility

**Files:**
- Modify: `tests/DocumentSelectionServiceTests.cs`
- Modify: `src/Services/DocumentSelectionService.cs`

- [ ] **Step 1: Add failing eligibility tests**

Append these tests to `DocumentSelectionServiceTests`:

```csharp
[Fact]
public void ExistingHistoryDocumentInsideWorkspace_CanSynchronize()
{
    using var temp = new TemporaryDirectory();
    var workspacePath = Path.Combine(temp.Path, "workspace");
    Directory.CreateDirectory(workspacePath);
    var filePath = Path.Combine(workspacePath, "A.md");
    File.WriteAllText(filePath, "# A");

    Assert.True(DocumentSelectionService.CanSynchronizeHistoryDocument(
        OpenMode.Workspace, workspacePath, filePath));
}

[Fact]
public void StandaloneHistoryDocument_CannotSynchronizeWorkspaceState()
{
    using var temp = new TemporaryDirectory();
    var filePath = Path.Combine(temp.Path, "A.md");
    File.WriteAllText(filePath, "# A");

    Assert.False(DocumentSelectionService.CanSynchronizeHistoryDocument(
        OpenMode.Standalone, temp.Path, filePath));
}

[Fact]
public void HistoryDocumentOutsideWorkspace_CannotSynchronize()
{
    using var temp = new TemporaryDirectory();
    var workspacePath = Path.Combine(temp.Path, "workspace");
    Directory.CreateDirectory(workspacePath);
    var filePath = Path.Combine(temp.Path, "outside.md");
    File.WriteAllText(filePath, "# Outside");

    Assert.False(DocumentSelectionService.CanSynchronizeHistoryDocument(
        OpenMode.Workspace, workspacePath, filePath));
}

[Fact]
public void MissingHistoryDocument_CannotSynchronize()
{
    using var temp = new TemporaryDirectory();

    Assert.False(DocumentSelectionService.CanSynchronizeHistoryDocument(
        OpenMode.Workspace, temp.Path, Path.Combine(temp.Path, "missing.md")));
}

[Fact]
public void InvalidHistoryDocumentPath_CannotSynchronize()
{
    Assert.False(DocumentSelectionService.CanSynchronizeHistoryDocument(
        OpenMode.Workspace, @"C:\workspace", "invalid\0path.md", _ => true));
}
```

- [ ] **Step 2: Run the focused test and confirm compilation fails**

Run:

```powershell
dotnet test tests\MarkdownViewer.Tests.csproj -c Release --filter FullyQualifiedName~DocumentSelectionServiceTests
```

Expected: build fails because `CanSynchronizeHistoryDocument` does not exist.

- [ ] **Step 3: Add the minimal eligibility method**

Add this method to `DocumentSelectionService` below `FindAdjacentDocument`:

```csharp
public static bool CanSynchronizeHistoryDocument(OpenMode openMode, string? workspacePath,
    string? filePath, Func<string, bool>? exists = null)
{
    if (openMode != OpenMode.Workspace ||
        string.IsNullOrWhiteSpace(workspacePath) ||
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
```

- [ ] **Step 4: Run the focused document-selection tests**

Run:

```powershell
dotnet test tests\MarkdownViewer.Tests.csproj -c Release --filter FullyQualifiedName~DocumentSelectionServiceTests
```

Expected: all `DocumentSelectionServiceTests` pass, including the pre-existing adjacent-document tests.

### Task 3: Make tree lookup and selection independently callable

**Files:**
- Modify: `src/MainWindow.xaml.cs:1232-1287`

- [ ] **Step 1: Add a side-effect-free tree lookup helper**

Insert this method immediately before `SelectFileInTree`:

```csharp
private TreeViewItem? FindFileNodeInTree(string filePath)
{
    foreach (var item in FileTreeView.Items)
    {
        if (item is TreeViewItem rootNode)
        {
            var targetNode = FindFileNodeByPath(rootNode, filePath);
            if (targetNode != null)
                return targetNode;
        }
    }

    return null;
}
```

- [ ] **Step 2: Add a selection helper that always restores event suppression**

Insert this method after `FindFileNodeInTree`:

```csharp
private void SelectFileNode(TreeViewItem targetNode, bool loadFile)
{
    ExpandAncestors(targetNode);

    _isRestoringFileSelection = true;
    try
    {
        targetNode.IsSelected = true;
        targetNode.BringIntoView();
    }
    finally
    {
        _isRestoringFileSelection = false;
    }

    if (loadFile && targetNode.Tag is string path && File.Exists(path))
        LoadMarkdownFile(path);
}
```

- [ ] **Step 3: Reduce `SelectFileInTree` to composition of lookup and selection**

Replace the current `SelectFileInTree` method with:

```csharp
private bool SelectFileInTree(string filePath, bool loadFile = true)
{
    var targetNode = FindFileNodeInTree(filePath);
    if (targetNode == null)
        return false;

    SelectFileNode(targetNode, loadFile);
    return true;
}
```

- [ ] **Step 4: Build the application project**

Run:

```powershell
dotnet build src\MarkdownViewer.csproj -c Release
```

Expected: build succeeds with no new compiler errors. This step confirms existing tree-selection call sites still compile without changing their behavior.

### Task 4: Apply validated, atomic history-state synchronization

**Files:**
- Modify: `src/MainWindow.xaml.cs:637-668`

- [ ] **Step 1: Replace the current history synchronization method**

Replace `SyncCurrentDocumentFromWebView` with:

```csharp
private async System.Threading.Tasks.Task SyncCurrentDocumentFromWebView()
{
    if (webView.CoreWebView2 == null)
        return;

    try
    {
        var scriptResult = await webView.CoreWebView2.ExecuteScriptAsync(
            "document.querySelector('meta[name=\\\"markdownviewer-file\\\"]')?.getAttribute('content') || ''");
        var filePath = DocumentMetadataService.ParseScriptResult(scriptResult);

        if (!DocumentSelectionService.CanSynchronizeHistoryDocument(
                _openMode, _currentFolderPath, filePath))
        {
            return;
        }

        var targetNode = FindFileNodeInTree(filePath!);
        if (targetNode == null)
            return;

        SelectFileNode(targetNode, loadFile: false);
        _currentFilePath = filePath;
        FilePathText.Text = filePath;
        StatusText.Text = $"已加载: {Path.GetFileName(filePath)}";
        Title = $"{Path.GetFileName(filePath)} - Markdown 查看器";
        UpdateFavButton();
    }
    catch
    {
        // 元数据读取或 UI 同步失败时保持导航前的外部状态。
    }
}
```

This intentionally performs eligibility checks and tree lookup before changing any external UI state. It also does not skip synchronization merely because `_currentFilePath` already equals the page metadata, allowing a stale tree selection to be repaired.

- [ ] **Step 2: Confirm no history synchronization path reloads Markdown**

Run:

```powershell
$source = Get-Content -Raw 'src\MainWindow.xaml.cs'
$method = [regex]::Match($source, '(?s)private async System\.Threading\.Tasks\.Task SyncCurrentDocumentFromWebView\(\).*?(?=        private void WebView_WebMessageReceived)').Value
$method
if ([string]::IsNullOrWhiteSpace($method) -or $method.Contains('LoadMarkdownFile')) {
    throw 'History synchronization is missing or reloads Markdown.'
}
```

Expected: the method is printed and the command exits successfully. `SelectFileNode(targetNode, loadFile: false)` is the only selection operation in the synchronization method.

- [ ] **Step 3: Run focused service tests and build**

Run:

```powershell
dotnet test tests\MarkdownViewer.Tests.csproj -c Release --filter "FullyQualifiedName~DocumentMetadataServiceTests|FullyQualifiedName~DocumentSelectionServiceTests"
dotnet build src\MarkdownViewer.csproj -c Release
```

Expected: all focused tests pass and the application project builds successfully.

### Task 5: Run full regression verification

**Files:**
- Verify only; no product-file changes expected.

- [ ] **Step 1: Run the full Release build**

Run:

```powershell
dotnet build MarkdownViewer.sln -c Release
```

Expected: build succeeds with zero errors.

- [ ] **Step 2: Run the full Release test suite**

Run:

```powershell
dotnet test MarkdownViewer.sln -c Release --no-build
```

Expected: all tests pass with zero failures.

- [ ] **Step 3: Perform real WebView2 mouse-navigation verification**

Create an isolated copy of the existing `samples` workspace and place its path on the clipboard:

```powershell
$historyWorkspace = Join-Path $env:TEMP ("MarkdownViewer-HistoryNavigation-" + [guid]::NewGuid().ToString("N"))
Copy-Item -LiteralPath 'samples' -Destination $historyWorkspace -Recurse
Set-Clipboard -Value $historyWorkspace
Write-Output $historyWorkspace
dotnet run --project src\MarkdownViewer.csproj
```

In MarkdownViewer, press `Ctrl+Shift+O`, paste the clipboard path, open the copied workspace, select `sample.md`, and click `打开嵌套测试文档` to navigate to `sample-assets\嵌套 目录\nested-sample.md`.

Verify in order:

1. Confirm the nested document is rendered and selected in the tree.
2. Press the mouse Back button; `sample.md` is rendered, selected, expanded into view, and shown in `Title`, `StatusText`, `FilePathText`, and favorite state.
3. Press the mouse Forward button; all state returns to the nested document.
4. Rapidly alternate Back and Forward; final external state matches the final rendered page.
5. Return to the nested document. In a second PowerShell window, run the command below, then navigate Back. Confirm tree selection, title, status, path, favorite state, and current-file behavior remain on the nested document:

```powershell
$historyWorkspace = Get-Clipboard
Rename-Item -LiteralPath (Join-Path $historyWorkspace 'sample.md') -NewName 'sample.removed'
```

6. Navigate Forward again; no duplicate history entry or repeated page load appears.

Expected: all six checks pass. If physical mouse side buttons are unavailable, record this verification as not executed rather than substituting keyboard navigation and claiming full coverage.

### Task 6: Review the final working tree without committing

**Files:**
- Review all files changed by Tasks 1-4.

- [ ] **Step 1: Inspect repository status and exact diffs**

Run:

```powershell
git status --short
git diff -- src/MainWindow.xaml.cs src/Services/DocumentMetadataService.cs src/Services/DocumentSelectionService.cs tests/DocumentMetadataServiceTests.cs tests/DocumentSelectionServiceTests.cs
```

Expected: only the intended incremental changes appear in these files; pre-existing unrelated untracked directories and files remain untouched.

- [ ] **Step 2: Check for accidental encoding, newline, or whitespace noise**

Run:

```powershell
git diff --check
git diff --numstat -- src/MainWindow.xaml.cs src/Services/DocumentMetadataService.cs src/Services/DocumentSelectionService.cs tests/DocumentMetadataServiceTests.cs tests/DocumentSelectionServiceTests.cs
```

Expected: `git diff --check` reports no whitespace errors, and `--numstat` reflects localized edits rather than whole-file rewrites.

- [ ] **Step 3: Report without committing**

Report:

- the exact files modified;
- focused and full build/test commands with results;
- physical mouse Back/Forward verification result;
- any unverified behavior or environment limitation;
- confirmation that no commit, push, deployment, or unrelated cleanup was performed.

Do not add a commit step unless the user separately requests and approves the workspace's full commit-review procedure.
