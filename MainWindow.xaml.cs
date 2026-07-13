using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace MarkdownViewer
{
    public partial class MainWindow : Window
    {
        private string? _currentFilePath;
        private string? _currentFolderPath;
        private double _zoomFactor = 1.0;
        private bool _isDarkMode;
        private readonly MarkdownPipeline _pipeline;
        private readonly HistoryManager _historyManager;
        private static readonly string _mermaidJs = LoadMermaidJs();
        private FileSystemWatcher? _fileWatcher;
        private bool _isRestoringFileSelection;
        private System.Timers.Timer? _debounceTimer;
        private double _scrollRestoreY;
        private bool _isAutoReload;

        private static readonly HashSet<string> _supportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".mdwn", ".mdtxt", ".mdtext", ".rmd"
        };

        private static string LoadMermaidJs()
        {
            try
            {
                using var stream = typeof(MainWindow).Assembly
                    .GetManifestResourceStream("MarkdownViewer.mermaid.min.js");
                if (stream == null) return "";
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch
            {
                return "";
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UsePipeTables()
                .UseTaskLists()
                .UseEmojiAndSmiley()
                .Build();

            _historyManager = new HistoryManager();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            KeyDown += MainWindow_KeyDown;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            webView.CoreWebView2.NavigationCompleted += WebView_NavigationCompleted;

            // 还原最近一次打开的文件夹
            RestoreLastSession();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            StopFileWatcher();

            // 关闭时保存当前文件夹及当前文件到历史记录
            if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
            {
                _historyManager.AddEntry(_currentFolderPath, _currentFilePath);
                _historyManager.Save();
            }
        }

        #region 历史记录
        private void RestoreLastSession()
        {
            _historyManager.Load();
            var lastEntry = _historyManager.GetLatest();

            if (lastEntry != null && Directory.Exists(lastEntry.FolderPath))
            {
                _currentFolderPath = lastEntry.FolderPath;
                PopulateFileTree(lastEntry.FolderPath);
                StartFileWatcher(lastEntry.FolderPath);

                StatusText.Text = $"历史记录: {lastEntry.FolderPath}";
                Title = $"{Path.GetFileName(lastEntry.FolderPath)} - Markdown 查看器";

                // 恢复上次打开的文档
                if (!string.IsNullOrEmpty(lastEntry.LastFilePath) && File.Exists(lastEntry.LastFilePath))
                {
                    SelectFileInTree(lastEntry.LastFilePath);
                }
            }
            else
            {
                RenderEmpty();
            }
        }
        #endregion

        #region 拖拽支持
        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null || files.Length == 0)
                    return;

                var path = files[0];

                // 判断是文件夹还是文件
                if (Directory.Exists(path))
                {
                    OpenFolder(path);
                }
                else if (File.Exists(path))
                {
                    if (_supportedExtensions.Contains(Path.GetExtension(path)))
                    {
                        LoadMarkdownFile(path);
                    }
                    else
                    {
                        MessageBox.Show($"不支持的文件格式: {Path.GetExtension(path)}\n\n支持的格式: {string.Join(", ", _supportedExtensions)}",
                            "不支持的文件", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }
        #endregion

        #region 快捷键
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (e.Key == Key.O)
                    OpenFolder_Click(sender, e);
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.O:
                        OpenFile_Click(sender, e);
                        break;
                    case Key.OemPlus:
                    case Key.Add:
                        ZoomIn_Click(sender, e);
                        break;
                    case Key.OemMinus:
                    case Key.Subtract:
                        ZoomOut_Click(sender, e);
                        break;
                    case Key.D0:
                        ZoomReset_Click(sender, e);
                        break;
                    case Key.D:
                        ToggleDarkMode_Click(sender, e);
                        break;
                    case Key.F:
                        ShowSearchBar();
                        break;
                }
            }
            else if (e.Key == Key.F5)
            {
                Reload_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                if (SearchBar.Visibility == Visibility.Visible)
                    CloseSearchBar();
            }
        }
        #endregion

        #region 文件操作
        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Markdown 文件 (*.md;*.markdown;*.mdown;*.mkd;*.mkdn;*.mdwn;*.mdtxt;*.mdtext;*.rmd)|*.md;*.markdown;*.mdown;*.mkd;*.mkdn;*.mdwn;*.mdtxt;*.mdtext;*.rmd|所有文件 (*.*)|*.*",
                Title = "打开 Markdown 文件"
            };

            if (dialog.ShowDialog() == true)
            {
                LoadMarkdownFile(dialog.FileName);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择包含 Markdown 文件的文件夹"
            };

            if (dialog.ShowDialog() == true)
            {
                OpenFolder(dialog.FolderName);
            }
        }

        private void OpenFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                return;

            StopFileWatcher();

            _currentFolderPath = folderPath;
            _currentFilePath = null;
            PopulateFileTree(folderPath);
            StartFileWatcher(folderPath);

            StatusText.Text = $"文件夹: {folderPath}";
            Title = $"{Path.GetFileName(folderPath)} - Markdown 查看器";

            _historyManager.AddEntry(folderPath, null);
        }

        private void PopulateFileTree(string folderPath)
        {
            FileTreeView.Items.Clear();

            if (!Directory.Exists(folderPath))
                return;

            var rootNode = CreateDirectoryNode(folderPath);
            if (rootNode != null)
            {
                FileTreeView.Items.Add(rootNode);
            }

            UpdateFileCount();
        }

        private TreeViewItem? CreateDirectoryNode(string directoryPath)
        {
            var dirInfo = new DirectoryInfo(directoryPath);
            string label;

            // 根节点显示完整文件夹名, 子目录只显示目录名
            if (directoryPath == _currentFolderPath)
            {
                label = $"📂 {dirInfo.Name}";
            }
            else
            {
                label = $"📁 {dirInfo.Name}";
            }

            var dirNode = new TreeViewItem
            {
                Header = label,
                Tag = directoryPath,
                IsExpanded = true,
                FontWeight = directoryPath == _currentFolderPath ? FontWeights.SemiBold : FontWeights.Normal
            };

            int itemCount = 0;

            // 先添加子目录(递归)
            try
            {
                foreach (var subDir in dirInfo.EnumerateDirectories())
                {
                    var subNode = CreateDirectoryNode(subDir.FullName);
                    if (subNode != null)
                    {
                        dirNode.Items.Add(subNode);
                        itemCount++;
                    }
                }
            }
            catch (UnauthorizedAccessException) { /* 跳过无权限目录 */ }

            // 再添加 Markdown 文件
            try
            {
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    if (_supportedExtensions.Contains(file.Extension))
                    {
                        var fileNode = new TreeViewItem
                        {
                            Header = $"📄 {file.Name}",
                            Tag = file.FullName,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
                        };
                        dirNode.Items.Add(fileNode);
                        itemCount++;
                    }
                }
            }
            catch (UnauthorizedAccessException) { /* 跳过无权限目录 */ }

            // 如果目录及其子目录都没有 markdown 文件, 返回 null (不显示空目录)
            if (itemCount == 0 && directoryPath != _currentFolderPath)
                return null;

            return dirNode;
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
            {
                LoadMarkdownFile(_currentFilePath);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void AutoReloadCurrentFile()
        {
            if (_currentFilePath == null || webView.CoreWebView2 == null)
                return;

            // 保存当前滚动位置
            try
            {
                var scrollYScript = await webView.CoreWebView2.ExecuteScriptAsync(
                    "window.scrollY || document.documentElement.scrollTop || 0");
                _scrollRestoreY = double.TryParse(scrollYScript?.Trim('"'), out var y) ? y : 0;
            }
            catch
            {
                _scrollRestoreY = 0;
            }

            _isAutoReload = true;
            LoadMarkdownFile(_currentFilePath);
        }

        private void LoadMarkdownFile(string filePath)
        {
            try
            {
                var markdown = File.ReadAllText(filePath, Encoding.UTF8);
                var html = Markdig.Markdown.ToHtml(markdown, _pipeline);
                var fullHtml = WrapHtml(html, Path.GetDirectoryName(filePath));

                webView.NavigateToString(fullHtml);
                _currentFilePath = filePath;
                FilePathText.Text = filePath;
                StatusText.Text = $"已加载: {Path.GetFileName(filePath)}";
                Title = $"{Path.GetFileName(filePath)} - Markdown 查看器";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载文件失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!_isAutoReload || _scrollRestoreY <= 0)
                return;

            _isAutoReload = false;

            // 恢复滚动位置（延迟一帧确保 DOM 渲染完成）
            await System.Threading.Tasks.Task.Delay(50);
            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.scrollTo(0, {_scrollRestoreY});");
            }
            catch { /* 忽略 */ }
        }
        #endregion

        #region 文件监控 (检测删除)
        private void StartFileWatcher(string folderPath)
        {
            StopFileWatcher();

            try
            {
                _fileWatcher = new FileSystemWatcher(folderPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };

                _fileWatcher.Deleted += OnFileDeleted;
                _fileWatcher.Renamed += OnFileRenamed;
                _fileWatcher.Created += OnFileCreated;
                _fileWatcher.Changed += OnFileChanged;

                // 只监控支持的扩展名
                foreach (var ext in _supportedExtensions)
                {
                    _fileWatcher.Filters.Add($"*{ext}");
                }
            }
            catch
            {
                // 无权限监控时静默失败
            }
        }

        private void StopFileWatcher()
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            Dispatcher.Invoke(() => HandleFileDeleted(e.FullPath));
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // 重命名后刷新整个树
                if (!string.IsNullOrEmpty(_currentFolderPath))
                {
                    var savedFilePath = _currentFilePath;
                    PopulateFileTree(_currentFolderPath);

                    // 尝试重新选中当前文件（如果只是重命名了其他文件）
                    if (!string.IsNullOrEmpty(savedFilePath) && File.Exists(savedFilePath))
                    {
                        SelectFileInTree(savedFilePath);
                    }
                    // 如果改名的是当前文件（旧名消失、新名出现），尝试选中新名
                    else if (e.OldFullPath == _currentFilePath)
                    {
                        _currentFilePath = null;
                        HandleFileDeleted(e.OldFullPath);
                    }
                }
            });
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // 新增文件时刷新树
                if (!string.IsNullOrEmpty(_currentFolderPath))
                {
                    var savedFilePath = _currentFilePath;
                    PopulateFileTree(_currentFolderPath);

                    if (!string.IsNullOrEmpty(savedFilePath) && File.Exists(savedFilePath))
                    {
                        SelectFileInTree(savedFilePath);
                    }
                }
            });
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // 文件内容变更时自动刷新渲染（仅当前打开的文件，带防抖）
            if (e.FullPath != _currentFilePath || !File.Exists(_currentFilePath))
                return;

            // 防抖：300ms 内的多次变更合并为一次刷新
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
            _debounceTimer.Elapsed += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (e.FullPath == _currentFilePath && File.Exists(_currentFilePath))
                    {
                        AutoReloadCurrentFile();
                    }
                });
            };
            _debounceTimer.Start();
        }

        private void HandleFileDeleted(string deletedPath)
        {
            // 从树中移除已删除的节点
            RemoveFileNodeFromTree(deletedPath);
            UpdateFileCount();

            // 如果删除的是当前正在显示的文件，切换到下一个
            if (deletedPath == _currentFilePath)
            {
                _currentFilePath = null;
                var nextFile = GetFirstFileInTree();

                if (nextFile != null)
                {
                    // 下一个文件存在 → 选中并加载
                    _isRestoringFileSelection = true;
                    nextFile.IsSelected = true;
                    nextFile.BringIntoView();
                    _isRestoringFileSelection = false;

                    if (nextFile.Tag is string path && File.Exists(path))
                    {
                        LoadMarkdownFile(path);
                    }
                }
                else
                {
                    // 没有下一个文件 → 清空显示
                    RenderEmptyNoFiles();
                }
            }
        }

        private void RemoveFileNodeFromTree(string filePath)
        {
            foreach (var item in FileTreeView.Items)
            {
                if (item is TreeViewItem rootNode)
                {
                    RemoveFileNodeRecursive(rootNode, filePath);

                    // 如果目录节点空了, 移除目录节点
                    CleanupEmptyDirectoryNodes(rootNode);
                }
            }
        }

        private bool RemoveFileNodeRecursive(TreeViewItem node, string filePath)
        {
            for (int i = node.Items.Count - 1; i >= 0; i--)
            {
                if (node.Items[i] is TreeViewItem child)
                {
                    if (child.Tag is string childPath && childPath == filePath && File.Exists(childPath) == false)
                    {
                        node.Items.RemoveAt(i);
                        return true;
                    }

                    if (child.Items.Count > 0)
                    {
                        if (RemoveFileNodeRecursive(child, filePath))
                            return true;

                        CleanupEmptyDirectoryNodes(child);
                    }
                }
            }
            return false;
        }

        private void CleanupEmptyDirectoryNodes(TreeViewItem dirNode)
        {
            // 移除空的子目录节点
            for (int i = dirNode.Items.Count - 1; i >= 0; i--)
            {
                if (dirNode.Items[i] is TreeViewItem child)
                {
                    // 如果该目录节点已无子项（空目录），移除它
                    if (child.Tag is string childPath && !File.Exists(childPath) && child.Items.Count == 0)
                    {
                        dirNode.Items.RemoveAt(i);
                    }
                }
            }
        }

        private TreeViewItem? GetFirstFileInTree()
        {
            foreach (var item in FileTreeView.Items)
            {
                if (item is TreeViewItem rootNode)
                {
                    var fileNode = FindFirstFileNode(rootNode);
                    if (fileNode != null)
                        return fileNode;
                }
            }
            return null;
        }

        private TreeViewItem? FindFirstFileNode(TreeViewItem node)
        {
            foreach (var child in node.Items)
            {
                if (child is TreeViewItem childNode)
                {
                    if (childNode.Tag is string path && File.Exists(path))
                        return childNode;

                    var found = FindFirstFileNode(childNode);
                    if (found != null)
                        return found;
                }
            }
            return null;
        }

        private void SelectFileInTree(string filePath)
        {
            foreach (var item in FileTreeView.Items)
            {
                if (item is TreeViewItem rootNode)
                {
                    var targetNode = FindFileNodeByPath(rootNode, filePath);
                    if (targetNode != null)
                    {
                        // 确保所有父节点展开
                        ExpandAncestors(targetNode);

                        _isRestoringFileSelection = true;
                        targetNode.IsSelected = true;
                        targetNode.BringIntoView();
                        _isRestoringFileSelection = false;

                        if (targetNode.Tag is string path && File.Exists(path))
                        {
                            LoadMarkdownFile(path);
                        }
                        return;
                    }
                }
            }
        }

        private TreeViewItem? FindFileNodeByPath(TreeViewItem node, string filePath)
        {
            if (node.Tag is string path && path == filePath && File.Exists(path))
                return node;

            foreach (var child in node.Items)
            {
                if (child is TreeViewItem childNode)
                {
                    var found = FindFileNodeByPath(childNode, filePath);
                    if (found != null)
                        return found;
                }
            }
            return null;
        }

        private void ExpandAncestors(TreeViewItem node)
        {
            var parent = node.Parent as TreeViewItem;
            while (parent != null)
            {
                parent.IsExpanded = true;
                parent = parent.Parent as TreeViewItem;
            }
        }

        private void RenderEmptyNoFiles()
        {
            var hint = _isDarkMode ? "#888" : "#999";
            var html = $@"
<div style=""text-align:center;color:{hint};padding-top:150px;"">
    <h2>📭 无文档</h2>
    <p>当前文件夹中没有 Markdown 文档</p>
    <p style=""font-size:0.9em;opacity:0.7;"">拖拽 .md 文件或文件夹到窗口，或使用菜单打开</p>
</div>";
            webView.NavigateToString(WrapHtml(html, null));
            FilePathText.Text = "";
            StatusText.Text = "就绪";
        }
        #endregion

        #region 缩放
        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (_zoomFactor < 3.0)
            {
                _zoomFactor += 0.1;
                ApplyZoom();
            }
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (_zoomFactor > 0.3)
            {
                _zoomFactor -= 0.1;
                ApplyZoom();
            }
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            _zoomFactor = 1.0;
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            if (webView.CoreWebView2 != null)
            {
                webView.ZoomFactor = _zoomFactor;
                ZoomText.Text = $"{Math.Round(_zoomFactor * 100)}%";
            }
        }
        #endregion

        #region 深色模式
        private void ToggleDarkMode_Click(object sender, RoutedEventArgs e)
        {
            _isDarkMode = !_isDarkMode;
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                Reload_Click(sender, e);
            }
        }
        #endregion

        #region HTML 包装
        private string WrapHtml(string content, string? basePath)
        {
            var bgColor = _isDarkMode ? "#1e1e1e" : "#ffffff";
            var textColor = _isDarkMode ? "#d4d4d4" : "#333333";
            var linkColor = _isDarkMode ? "#569cd6" : "#0066cc";
            var codeBg = _isDarkMode ? "#2d2d2d" : "#f5f5f5";
            var borderColor = _isDarkMode ? "#404040" : "#e0e0e0";

            var baseTag = string.IsNullOrEmpty(basePath) ? "" :
                $"<base href=\"file:///{basePath.Replace('\\', '/')}/\">";

            return $@"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    {baseTag}
    <script>/* mermaid.js embedded */
{_mermaidJs}</script>
    <script>
        (function() {{
            function renderMermaid() {{
                var blocks = document.querySelectorAll('pre code.language-mermaid');
                if (blocks.length === 0) return;
                if (typeof mermaid === 'undefined') {{ setTimeout(renderMermaid, 100); return; }}
                mermaid.initialize({{ startOnLoad: false, theme: '{(_isDarkMode ? "dark" : "default")}', securityLevel: 'loose' }});
                blocks.forEach(function(block) {{
                    var pre = block.parentElement;
                    var container = document.createElement('div');
                    container.className = 'mermaid-container';
                    container.style.cssText = 'margin:1em 0;text-align:center;overflow-x:auto;';
                    pre.parentNode.replaceChild(container, pre);
                    container.textContent = block.textContent;
                }});
                mermaid.run({{ querySelector: '.mermaid-container' }});
            }}
            renderMermaid();
        }})();
    </script>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            line-height: 1.6;
            color: {textColor};
            background-color: {bgColor};
            max-width: 900px;
            margin: 0 auto;
            padding: 30px 40px;
        }}
        h1, h2, h3, h4, h5, h6 {{
            margin-top: 1.5em;
            margin-bottom: 0.5em;
            font-weight: 600;
            line-height: 1.3;
        }}
        h1 {{ font-size: 2em; border-bottom: 1px solid {borderColor}; padding-bottom: 0.3em; }}
        h2 {{ font-size: 1.5em; border-bottom: 1px solid {borderColor}; padding-bottom: 0.3em; }}
        h3 {{ font-size: 1.25em; }}
        p {{ margin: 1em 0; }}
        a {{ color: {linkColor}; text-decoration: none; }}
        a:hover {{ text-decoration: underline; }}
        code {{
            background: {codeBg};
            padding: 2px 6px;
            border-radius: 4px;
            font-family: Consolas, Monaco, 'Courier New', monospace;
            font-size: 0.9em;
        }}
        pre {{
            background: {codeBg};
            padding: 16px;
            border-radius: 6px;
            overflow-x: auto;
            border: 1px solid {borderColor};
        }}
        pre code {{
            background: none;
            padding: 0;
        }}
        blockquote {{
            margin: 1em 0;
            padding: 0.5em 1em;
            border-left: 4px solid {linkColor};
            background: {codeBg};
            color: {textColor};
            opacity: 0.9;
        }}
        table {{
            border-collapse: collapse;
            width: 100%;
            margin: 1em 0;
        }}
        th, td {{
            border: 1px solid {borderColor};
            padding: 8px 12px;
            text-align: left;
        }}
        th {{ background: {codeBg}; font-weight: 600; }}
        img {{ max-width: 100%; height: auto; border-radius: 4px; }}
        ul, ol {{ padding-left: 2em; }}
        li {{ margin: 0.3em 0; }}
        hr {{ border: none; border-top: 1px solid {borderColor}; margin: 2em 0; }}
        input[type=""checkbox""] {{ margin-right: 6px; }}
    </style>
</head>
<body>
    {content}
</body>
</html>";
        }

        #region 文件树选择
        private void FileTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_isRestoringFileSelection)
                return;

            if (e.NewValue is TreeViewItem item && item.Tag is string path && File.Exists(path))
            {
                LoadMarkdownFile(path);
            }
        }
        #endregion

        private void RenderEmpty()
        {
            var hint = _isDarkMode ? "#888" : "#999";
            var html = $@"
<div style=""text-align:center;color:{hint};padding-top:150px;"">
    <h2>📄 Markdown 查看器</h2>
    <p>点击「打开文件夹」或按 Ctrl+Shift+O 选择文件夹，或点击「打开文件」选择单个 .md 文件</p>
    <p style=""margin-top:10px;"">也可以直接拖拽 .md 文件或文件夹到窗口</p>
    <p style=""font-size:0.9em;opacity:0.7;"">支持表格、任务列表、代码高亮等扩展语法</p>
</div>";
            webView.NavigateToString(WrapHtml(html, null));
            FileCountText.Text = "";
        }

        private void UpdateFileCount()
        {
            int count = CountMdFiles(FileTreeView.Items);
            FileCountText.Text = count > 0 ? $"{count} 个文档" : "";
        }

        private int CountMdFiles(ItemCollection items)
        {
            int count = 0;
            foreach (var item in items)
            {
                if (item is TreeViewItem node)
                {
                    if (node.Tag is string path && File.Exists(path))
                        count++;
                    count += CountMdFiles(node.Items);
                }
            }
            return count;
        }
        #endregion

        #region 搜索
        private void ShowSearchBar()
        {
            SearchBar.Visibility = Visibility.Visible;
            SearchBox.Focus();
            SearchBox.SelectAll();
        }

        private void CloseSearchBar()
        {
            SearchBar.Visibility = Visibility.Collapsed;
            SearchBox.Text = "";
            ClearSearchHighlight();
        }

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            await ExecuteFind(true);
        }

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ExecuteFind(!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseSearchBar();
            }
        }

        private async void SearchNext_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteFind(true);
            SearchBox.Focus();
        }

        private async void SearchPrev_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteFind(false);
            SearchBox.Focus();
        }

        private void SearchClose_Click(object sender, RoutedEventArgs e)
        {
            CloseSearchBar();
        }

        private async System.Threading.Tasks.Task ExecuteFind(bool forward)
        {
            if (webView.CoreWebView2 == null) return;
            var text = SearchBox.Text;
            if (string.IsNullOrEmpty(text))
            {
                ClearSearchHighlight();
                SearchCountText.Text = "";
                return;
            }

            try
            {
                // window.find(text, caseSensitive, backwards, wrapAround, wholeWord, searchInFrames, showDialog)
                var script = $"window.find('{EscapeJs(text)}', false, {(!forward).ToString().ToLower()}, true, false, true, false);";
                var result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                var found = result?.Trim('"') == "true";
                if (!found)
                {
                    // 从头/尾重新搜索
                    var resetScript = forward
                        ? "window.getSelection().removeAllRanges(); window.find('" + EscapeJs(text) + "', false, false, true, false, true, false);"
                        : "window.getSelection().removeAllRanges(); window.find('" + EscapeJs(text) + "', false, true, true, false, true, false);";
                    await webView.CoreWebView2.ExecuteScriptAsync(resetScript);
                }
            }
            catch
            {
                SearchCountText.Text = "搜索出错";
            }
        }

        private async void ClearSearchHighlight()
        {
            if (webView.CoreWebView2 == null) return;
            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync("window.getSelection().removeAllRanges();");
            }
            catch { }
        }

        private static string EscapeJs(string s)
        {
            return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
        #endregion

        #region 关于
        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Markdown 查看器 v1.3\n\n" +
                "基于 WPF + Markdig + WebView2 构建\n" +
                "支持标准 Markdown 及扩展语法\n\n" +
                "功能:\n" +
                "• 文件夹浏览与文档树\n" +
                "• 拖拽文件/文件夹打开\n" +
                "• 打开历史记录恢复\n" +
                "• 文件变更自动刷新(保持滚动位置)\n" +
                "• 删除文件自动切换",
                "关于",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        #endregion
    }

    #region 历史记录管理
    internal class HistoryEntry
    {
        public string FolderPath { get; set; } = "";
        public string? LastFilePath { get; set; }
        public DateTime OpenedAt { get; set; }
    }

    internal class HistoryManager
    {
        private readonly string _historyDir;
        private readonly string _historyFile;
        private List<HistoryEntry> _entries;
        private const int MaxHistoryEntries = 20;

        public HistoryManager()
        {
            // History 文件夹放在可执行文件目录中
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            _historyDir = Path.Combine(exeDir, "History");
            _historyFile = Path.Combine(_historyDir, "history.json");
            _entries = new List<HistoryEntry>();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_historyFile))
                {
                    var json = File.ReadAllText(_historyFile, Encoding.UTF8);
                    _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? new List<HistoryEntry>();
                }
            }
            catch
            {
                _entries = new List<HistoryEntry>();
            }
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(_historyDir))
                    Directory.CreateDirectory(_historyDir);

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_entries, options);
                File.WriteAllText(_historyFile, json, Encoding.UTF8);
            }
            catch
            {
                // 静默失败, 不影响主流程
            }
        }

        public void AddEntry(string folderPath, string? lastFilePath)
        {
            // 移除已有的相同路径
            _entries.RemoveAll(e => string.Equals(e.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase));

            // 添加到列表头部
            _entries.Insert(0, new HistoryEntry
            {
                FolderPath = folderPath,
                LastFilePath = lastFilePath,
                OpenedAt = DateTime.Now
            });

            // 限制历史记录数量
            while (_entries.Count > MaxHistoryEntries)
            {
                _entries.RemoveAt(_entries.Count - 1);
            }
        }

        public HistoryEntry? GetLatest()
        {
            return _entries.FirstOrDefault();
        }
    }
    #endregion
}
