[中文](README.md) | English

# <img src="src/Assets/app_icon.png" width="32" align="left" style="margin-right:8px"> MarkdownViewer

A Windows Markdown viewer built with WPF, Markdig, and WebView2. It supports workspace navigation, standalone files, drag and drop, session restore, local resources, and live file monitoring.

## Why MarkdownViewer

More Markdown documents are now written by AI agents, leaving developers to spend more time reading, checking, and reviewing them. Opening a general-purpose editor such as VS Code or Notepad++ just to inspect one `.md` file adds unnecessary weight to that workflow.

MarkdownViewer is built for this review loop. It opens documents directly while keeping the workspace tree, table of contents, local images, and link navigation that matter during review. Developers can return to their usual editor when changes are needed without loading a full development environment for routine reading.

## Features

### Workspace and document tree

- Open a folder as a workspace and browse Markdown files in their original directory hierarchy.
- Recursively display nested folders while hiding empty folders.
- Track the document count and resize the navigation pane with a splitter.
- When the current document is deleted, select the next document in tree order, or the previous one if no next document exists.

### Workspace and standalone modes

| Mode | Entry points | Tree and favorites | Monitoring |
|------|--------------|--------------------|------------|
| Workspace | Open or drop a folder, or open an external file that matches a known workspace | Full tree and `{folder}/.markdownviewer/favorites.json` | Entire workspace |
| Standalone | `Ctrl+O`, drop a Markdown file, or open a file outside known workspaces | Tree hidden and workspace favorites disabled | Current file only |

Standalone files reload after external edits while preserving scroll position. If a standalone file is deleted or moved, MarkdownViewer shows an unavailable state instead of opening another file from the same directory.

### Markdown rendering

- Markdig with advanced extensions, pipe tables, task lists, emoji, and smileys.
- WebView2 rendering with light and dark themes.
- Offline Mermaid rendering.
- Local Markdown and HTML images through controlled WebView2 virtual-host mappings.
- Local paths with Chinese characters, spaces, URL encoding, and nested directories.
- In-page anchors matched by DOM `id`, `name`, or heading text.

### Local links and file URIs

- Open regular Windows paths and URL-encoded `file:///` Markdown URIs.
- Open Markdown files from the same workspace in the current instance and select their tree nodes.
- Open Markdown files outside the current workspace in a new MarkdownViewer instance.
- Open folder links in Windows Explorer.
- Open ordinary files with their system default applications.
- Block executable and script extensions from being launched directly by Markdown documents.

### History, favorites, and configuration

- Restore the latest workspace and selected document.
- Store up to 20 workspace history entries in `{exe}/History/history.json`.
- Save favorites atomically and coordinate concurrent instances with a named mutex.
- Persist zoom, theme, table-of-contents visibility, and toolbar visibility in `{exe}/History/config.json`.

### Search and navigation

- `Ctrl+F` search with current and total match counts.
- `Enter` for the next match and `Shift+Enter` for the previous match.
- `Ctrl+T` toggles the heading tree generated from the Markdig AST.

## Keyboard shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+O` | Open file |
| `Ctrl+Shift+O` | Open folder |
| `Ctrl++` / `Ctrl+-` | Zoom in or out |
| `Ctrl+0` | Reset zoom |
| `Ctrl+D` | Toggle dark mode |
| `Ctrl+F` | Search |
| `Ctrl+T` | Toggle table of contents |
| `F5` | Reload current document |

## Supported extensions

`.md` `.markdown` `.mdown` `.mkd` `.mkdn` `.mdwn` `.mdtxt` `.mdtext` `.rmd`

## Repository layout

```text
MarkdownViewer/
├── MarkdownViewer.sln
├── src/
│   ├── App.xaml
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs           # WPF and WebView2 orchestration
│   ├── Models/                      # Open mode, history, and favorites models
│   ├── Services/                    # Paths, document selection, history, favorites, config
│   ├── Properties/
│   ├── Assets/
│   └── MarkdownViewer.csproj
├── tests/
│   ├── MarkdownViewer.Tests.csproj
│   └── *Tests.cs                    # Automated rule and persistence tests
├── samples/
│   ├── sample.md                    # Manual integration verification
│   └── sample-assets/
├── README.md
└── README.en.md
```

`MainWindow` owns window state and WPF/WebView2 event orchestration. UI-independent path resolution, workspace matching, local image resolution, adjacent-document selection, and persistence live under `Services` and can be tested independently.

## Technology

| Technology | Purpose |
|------------|---------|
| .NET 8 WPF | Desktop application framework |
| [Markdig](https://www.nuget.org/packages/Markdig) 0.37 | Markdown parser |
| [WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) 1.0 | Chromium-based rendering |
| `System.Text.Json` | History, favorites, and UI configuration |
| `FileSystemWatcher` | Workspace and standalone file monitoring |
| xUnit | Automated rule and persistence tests |

## Run

```powershell
dotnet run --project src\MarkdownViewer.csproj
```

## Build and test

```powershell
dotnet build MarkdownViewer.sln -c Release
dotnet test MarkdownViewer.sln -c Release
```

## Manual verification

Open [`samples/sample.md`](samples/sample.md) in workspace mode to verify local images, Markdown navigation, folder and ordinary file links, blocked scripts, external links, and in-page anchors.

## Version highlights

| Version | Highlights |
|---------|------------|
| v1.8 | Markdig AST table of contents and `Ctrl+T` |
| v1.9 | Persistent zoom, theme, TOC, and toolbar settings |
| v1.10 | Correct command-line and Windows Open With startup behavior |
| v1.11 (local test build) | Workspace/standalone modes, recursive tree, watcher debounce, deletion fallback, concurrent favorites |
| v1.12 (local implementation) | `file:///` URI support, local images, workspace-aware Markdown links, Explorer folder links, system file opening |
| Next version (in progress) | `src/tests/samples` layout, extracted testable services, automated tests, and bilingual documentation |
