using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace MarkdownViewer
{
    public partial class MainWindow : Window
    {
        private string? _currentFilePath;
        private string? _currentFolderPath;
        private OpenMode _openMode;
        private GridLength _workspaceColumnWidth = new(280);
        private double _tocColumnWidth = ConfigManager.DefaultTocWidth;
        private double _zoomFactor = 1.0;
        private bool _isDarkMode;
        private readonly MarkdownPipeline _pipeline;
        private readonly HistoryManager _historyManager;
        private readonly FavoritesManager _favoritesManager;
        private readonly ConfigManager _configManager;
        private readonly string? _startupFilePath;
        private static readonly string _mermaidJsContent;

        static MainWindow()
        {
            _mermaidJsContent = ExtractMermaidJs();
        }
        private FileSystemWatcher? _fileWatcher;
        private bool _isRestoringFileSelection;
        private System.Timers.Timer? _debounceTimer;
        private double _scrollRestoreY;
        private bool _isAutoReload;
        private string? _pendingFragment;
        private readonly List<string> _activeResourceHosts = new();
        private const int FileEventDebounceMilliseconds = 600;

        private static readonly HashSet<string> _supportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".mdwn", ".mdtxt", ".mdtext", ".rmd"
        };

        private static readonly HashSet<string> _blockedShellExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
            ".wsf", ".wsh", ".scr", ".msi", ".msp", ".lnk", ".url"
        };

        private static readonly Regex _imageSourceRegex = new(
            @"(?<prefix><img\b[^>]*\bsrc\s*=\s*)(?<quote>[""'])(?<source>.*?)(\k<quote>)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string ExtractMermaidJs()
        {
            try
            {
                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                var filePath = Path.Combine(exeDir, "mermaid.min.js");
                if (File.Exists(filePath) && new FileInfo(filePath).Length > 100000)
                    return File.ReadAllText(filePath);

                using var stream = typeof(MainWindow).Assembly
                    .GetManifestResourceStream("MarkdownViewer.Assets.mermaid.min.js");
                if (stream == null) return "";
                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                stream.CopyTo(fs);
                return File.ReadAllText(filePath);
            }
            catch
            {
                return "";
            }
        }

        public MainWindow(string? startupFilePath = null)
        {
            InitializeComponent();
            _startupFilePath = startupFilePath;
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UsePipeTables()
                .UseTaskLists()
                .UseEmojiAndSmiley()
                .Build();

            _historyManager = new HistoryManager();
            _favoritesManager = new FavoritesManager();
            _configManager = new ConfigManager();
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
            webView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;
            webView.CoreWebView2.NewWindowRequested += WebView_NewWindowRequested;

            // 注入 mermaid.js（避免 NavigateToString 大小限制）
            if (!string.IsNullOrEmpty(_mermaidJsContent))
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(_mermaidJsContent);

            // 加载并应用 UI 配置
            _configManager.Load();
            ApplyConfig();
            _historyManager.Load();

            if (!OpenStartupFile())
            {
                // 没有命令行文件参数时，还原最近一次打开的文件夹
                RestoreLastSession();
            }
        }

        private bool OpenStartupFile()
        {
            if (string.IsNullOrWhiteSpace(_startupFilePath))
                return false;

            if (!LocalPathService.TryResolveLocalInput(_startupFilePath, out var filePath, out var fragment, out var error))
            {
                RenderEmpty();
                MessageBox.Show($"无法打开指定文件: {error}", "打开文件失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }

            if (!File.Exists(filePath))
            {
                RenderEmpty();
                MessageBox.Show($"文件不存在: {filePath}", "打开文件失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }

            if (!_supportedExtensions.Contains(Path.GetExtension(filePath)))
            {
                RenderEmpty();
                MessageBox.Show($"不支持的文件格式: {Path.GetExtension(filePath)}\n\n支持的格式: {string.Join(", ", _supportedExtensions)}",
                    "不支持的文件", MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }

            _pendingFragment = fragment;
            OpenExternalFile(filePath);

            return true;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            StopFileWatcher();

            // 关闭时保存当前 workspace 及当前文件到历史记录
            if (_openMode == OpenMode.Workspace &&
                !string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
            {
                _historyManager.AddEntry(_currentFolderPath, _currentFilePath);
                _historyManager.Save();
            }

            _configManager.ZoomFactor = _zoomFactor;
            _configManager.IsDarkMode = _isDarkMode;
            _configManager.IsTocVisible = TocPanel.Visibility == Visibility.Visible;
            _configManager.IsToolbarVisible = MainToolBar.Visibility == Visibility.Visible;
            CaptureTocWidth();
            _configManager.TocWidth = _tocColumnWidth;
            _configManager.Save();
        }

        #region 历史记录
        private void RestoreLastSession()
        {
            var lastEntry = _historyManager.GetLatest();

            if (lastEntry != null && Directory.Exists(lastEntry.FolderPath))
            {
                OpenWorkspace(lastEntry.FolderPath, lastEntry.LastFilePath);
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
            if (e.Data.GetDataPresent(DataFormats.FileDrop) ||
                e.Data.GetDataPresent(DataFormats.UnicodeText) ||
                e.Data.GetDataPresent(DataFormats.Text))
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
            string? input = null;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null || files.Length == 0)
                    return;

                input = files[0];
            }
            else if (e.Data.GetDataPresent(DataFormats.UnicodeText))
                input = e.Data.GetData(DataFormats.UnicodeText) as string;
            else if (e.Data.GetDataPresent(DataFormats.Text))
                input = e.Data.GetData(DataFormats.Text) as string;

            if (string.IsNullOrWhiteSpace(input))
                return;

            if (!LocalPathService.TryResolveLocalInput(input, out var path, out _, out var error))
            {
                MessageBox.Show(error ?? "无法识别拖入内容。", "无法打开",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 拖拽文件夹仍等同于“打开文件夹”，进入 workspace。
            if (Directory.Exists(path))
                OpenWorkspace(path);
            else if (File.Exists(path) && _supportedExtensions.Contains(Path.GetExtension(path)))
                OpenStandaloneFile(path);
            else if (File.Exists(path))
                MessageBox.Show($"不支持的文件格式: {Path.GetExtension(path)}\n\n支持的格式: {string.Join(", ", _supportedExtensions)}",
                    "不支持的文件", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    case Key.T:
                        ToggleToc_Click(sender, e);
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
                OpenStandaloneFile(dialog.FileName);
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
                OpenWorkspace(dialog.FolderName);
            }
        }

        private void OpenExternalFile(string filePath)
        {
            var workspacePath = FindBestWorkspaceForFile(filePath);
            if (!string.IsNullOrEmpty(workspacePath))
                OpenWorkspace(workspacePath, filePath);
            else
                OpenStandaloneFile(filePath);
        }

        private string? FindBestWorkspaceForFile(string filePath)
        {
            return LocalPathService.FindBestWorkspaceForFile(filePath,
                _historyManager.GetAll().Select(entry => entry.FolderPath));
        }

        private void OpenStandaloneFile(string filePath)
        {
            if (!File.Exists(filePath) || !_supportedExtensions.Contains(Path.GetExtension(filePath)))
                return;

            StopFileWatcher();
            _openMode = OpenMode.Standalone;
            _currentFolderPath = null;
            _currentFilePath = null;
            _favoritesManager.SetFolderPath(null);
            RefreshFavoritesList();
            SetWorkspacePanelVisible(false);

            LoadMarkdownFile(filePath);
            StartStandaloneFileWatcher(filePath);
            StatusText.Text = $"单文件: {filePath}";
            UpdateFavButton();
        }

        private void OpenWorkspace(string folderPath, string? targetFilePath = null)
        {
            if (!Directory.Exists(folderPath))
                return;

            StopFileWatcher();

            folderPath = Path.GetFullPath(folderPath);
            _openMode = OpenMode.Workspace;
            _currentFolderPath = folderPath;
            _currentFilePath = null;
            SetWorkspacePanelVisible(true);
            _favoritesManager.SetFolderPath(folderPath);
            _favoritesManager.Load();
            RefreshFavoritesList();
            PopulateFileTree(folderPath);
            StartFileWatcher(folderPath);

            StatusText.Text = $"文件夹: {folderPath}";
            Title = $"{Path.GetFileName(folderPath)} - Markdown 查看器";

            if (!string.IsNullOrEmpty(targetFilePath) &&
                File.Exists(targetFilePath) && LocalPathService.IsFileInsideFolder(targetFilePath, folderPath))
            {
                if (!SelectFileInTree(targetFilePath))
                    LoadMarkdownFile(targetFilePath);
            }

            _historyManager.AddEntry(folderPath, _currentFilePath);
            _historyManager.Save();
            UpdateFavButton();
        }

        private void SetWorkspacePanelVisible(bool visible)
        {
            if (visible)
            {
                WorkspaceColumn.MinWidth = 180;
                WorkspaceColumn.Width = _workspaceColumnWidth.Value > 0 ? _workspaceColumnWidth : new GridLength(280);
                WorkspaceSplitterColumn.Width = new GridLength(5);
                WorkspacePanel.Visibility = Visibility.Visible;
                WorkspaceSplitter.Visibility = Visibility.Visible;
                FavButton.IsEnabled = true;
                FavButton.ToolTip = "收藏当前文档";
            }
            else
            {
                if (WorkspaceColumn.Width.Value > 0)
                    _workspaceColumnWidth = WorkspaceColumn.Width;
                WorkspaceColumn.MinWidth = 0;
                WorkspaceColumn.Width = new GridLength(0);
                WorkspaceSplitterColumn.Width = new GridLength(0);
                WorkspacePanel.Visibility = Visibility.Collapsed;
                WorkspaceSplitter.Visibility = Visibility.Collapsed;
                FavButton.IsEnabled = false;
                FavButton.ToolTip = "单文件模式不支持目录收藏，请先打开文件夹";
            }
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

        private void HistoryMenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            HistoryMenu.Items.Clear();
            var entries = _historyManager.GetAll();
            if (entries.Count == 0)
            {
                HistoryMenu.Items.Add(new MenuItem { Header = "(无历史记录)", IsEnabled = false });
                return;
            }
            foreach (var entry in entries)
            {
                var item = new MenuItem
                {
                    Header = entry.FolderPath,
                    ToolTip = $"上次打开: {entry.OpenedAt:yyyy-MM-dd HH:mm}"
                };
                var path = entry.FolderPath;
                item.Click += (_, _) =>
                {
                    if (Directory.Exists(path))
                        OpenWorkspace(path);
                };
                HistoryMenu.Items.Add(item);
            }
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
                html = RewriteLocalImageSources(html, filePath);
                BuildToc(markdown);
                var fullHtml = WrapHtml(html, filePath);

                webView.NavigateToString(fullHtml);
                _currentFilePath = filePath;
                FilePathText.Text = filePath;
                StatusText.Text = $"已加载: {Path.GetFileName(filePath)}";
                Title = $"{Path.GetFileName(filePath)} - Markdown 查看器";
                UpdateFavButton();

                if (_openMode == OpenMode.Workspace && !_isAutoReload && !string.IsNullOrEmpty(_currentFolderPath))
                {
                    _historyManager.AddEntry(_currentFolderPath, _currentFilePath);
                    _historyManager.Save();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载文件失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_isAutoReload)
            {
                _isAutoReload = false;
                if (_scrollRestoreY > 0)
                {
                    // 恢复滚动位置（延迟一帧确保 DOM 渲染完成）
                    await System.Threading.Tasks.Task.Delay(50);
                    try
                    {
                        await webView.CoreWebView2.ExecuteScriptAsync(
                            $"window.scrollTo(0, {_scrollRestoreY});");
                    }
                    catch { /* 忽略 */ }
                }
            }

            if (!string.IsNullOrEmpty(_pendingFragment))
            {
                var fragment = _pendingFragment;
                _pendingFragment = null;
                await ScrollToFragment(fragment);
            }
        }

        private void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var uri = e.TryGetWebMessageAsString();
                if (!string.IsNullOrWhiteSpace(uri))
                    HandleDocumentLink(uri);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开链接: {ex.Message}", "链接错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void WebView_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            try
            {
                HandleDocumentLink(e.Uri);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开链接: {ex.Message}", "链接错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void HandleDocumentLink(string link)
        {
            if (link.StartsWith("#", StringComparison.Ordinal))
            {
                var localFragment = Uri.UnescapeDataString(link[1..]);
                if (!string.IsNullOrEmpty(localFragment))
                    _ = ScrollToFragment(localFragment);
                return;
            }

            if (!LocalPathService.TryResolveDocumentLinkUri(link, _currentFilePath, out var uri))
            {
                MessageBox.Show($"无法识别链接: {link}", "链接错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (uri.Scheme is "http" or "https" or "mailto")
            {
                OpenWithSystem(uri.AbsoluteUri);
                return;
            }

            if (!uri.IsFile)
            {
                MessageBox.Show($"不支持的链接协议: {uri.Scheme}", "链接错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var localPath = Path.GetFullPath(uri.LocalPath);
            var fragment = string.IsNullOrEmpty(uri.Fragment)
                ? null
                : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

            if (Directory.Exists(localPath))
            {
                OpenWithSystem(localPath);
                return;
            }

            if (!File.Exists(localPath))
            {
                MessageBox.Show($"目标不存在: {localPath}", "链接目标不存在",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_supportedExtensions.Contains(Path.GetExtension(localPath)))
            {
                OpenLinkedMarkdown(localPath, fragment);
                return;
            }

            var extension = Path.GetExtension(localPath);
            if (_blockedShellExtensions.Contains(extension))
            {
                MessageBox.Show($"出于安全考虑，不允许从 Markdown 文档直接启动此类文件: {extension}",
                    "已阻止文件启动", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OpenWithSystem(localPath);
        }

        private void OpenLinkedMarkdown(string filePath, string? fragment)
        {
            filePath = Path.GetFullPath(filePath);

            if (LocalPathService.IsSamePath(filePath, _currentFilePath))
            {
                if (!string.IsNullOrEmpty(fragment))
                    _ = ScrollToFragment(fragment);
                return;
            }

            if (_openMode == OpenMode.Workspace && !string.IsNullOrEmpty(_currentFolderPath) &&
                LocalPathService.IsFileInsideFolder(filePath, _currentFolderPath))
            {
                _pendingFragment = fragment;
                if (!SelectFileInTree(filePath))
                {
                    PopulateFileTree(_currentFolderPath);
                    if (!SelectFileInTree(filePath))
                    {
                        _pendingFragment = null;
                        MessageBox.Show($"文件存在，但无法在当前 workspace 文档树中定位: {filePath}",
                            "无法定位文档", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                return;
            }

            StartNewViewerInstance(filePath, fragment);
        }

        private static void StartNewViewerInstance(string filePath, string? fragment)
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executablePath))
                throw new InvalidOperationException("无法确定 MarkdownViewer 可执行文件路径。");

            var argument = filePath;
            if (!string.IsNullOrEmpty(fragment))
            {
                var builder = new UriBuilder(new Uri(Path.GetFullPath(filePath))) { Fragment = fragment };
                argument = builder.Uri.AbsoluteUri;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(argument);
            Process.Start(startInfo);
        }

        private static void OpenWithSystem(string target)
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }

        private async System.Threading.Tasks.Task ScrollToFragment(string fragment)
        {
            if (webView.CoreWebView2 == null || string.IsNullOrEmpty(fragment))
                return;

            await System.Threading.Tasks.Task.Delay(50);
            try
            {
                var script = BuildFragmentScrollScript(fragment);
                await webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch { /* 忽略无效锚点 */ }
        }

        private static string BuildFragmentScrollScript(string fragment)
        {
            var fragmentJson = JsonSerializer.Serialize(fragment);
            return "(function(){var wanted=" + fragmentJson + ";var h=document.getElementById(wanted);" +
                   "if(!h){var named=document.getElementsByName(wanted);if(named.length>0)h=named[0];}" +
                   "if(!h){var hs=document.querySelectorAll('h1,h2,h3,h4,h5,h6');" +
                   "for(var i=0;i<hs.length;i++){if(hs[i].textContent.trim()===wanted){h=hs[i];break;}}}" +
                   "if(!h)return false;h.scrollIntoView({behavior:'auto',block:'start'});return true;})();";
        }

        private string RewriteLocalImageSources(string html, string markdownFilePath)
        {
            ClearLocalResourceMappings();
            if (webView.CoreWebView2 == null)
                return html;

            var markdownDirectory = Path.GetDirectoryName(Path.GetFullPath(markdownFilePath));
            if (string.IsNullOrEmpty(markdownDirectory))
                return html;

            var directoryHosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return _imageSourceRegex.Replace(html, match =>
            {
                var source = System.Net.WebUtility.HtmlDecode(match.Groups["source"].Value);
                if (!LocalPathService.TryResolveLocalImagePath(source, markdownDirectory, out var imagePath, out var suffix))
                    return match.Value;

                var imageDirectory = Path.GetDirectoryName(imagePath);
                if (string.IsNullOrEmpty(imageDirectory) || !Directory.Exists(imageDirectory))
                    return match.Value;

                if (!directoryHosts.TryGetValue(imageDirectory, out var hostName))
                {
                    hostName = $"resource-{directoryHosts.Count}.markdownviewer.local";
                    webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        hostName, imageDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
                    directoryHosts[imageDirectory] = hostName;
                    _activeResourceHosts.Add(hostName);
                }

                var fileName = Path.GetFileName(imagePath);
                var resourceUrl = $"https://{hostName}/{Uri.EscapeDataString(fileName)}{suffix}";
                return match.Groups["prefix"].Value + match.Groups["quote"].Value +
                       System.Net.WebUtility.HtmlEncode(resourceUrl) + match.Groups["quote"].Value;
            });
        }

        private void ClearLocalResourceMappings()
        {
            if (webView.CoreWebView2 == null)
                return;

            foreach (var hostName in _activeResourceHosts)
            {
                try
                {
                    webView.CoreWebView2.ClearVirtualHostNameToFolderMapping(hostName);
                }
                catch { /* 映射不存在时忽略 */ }
            }
            _activeResourceHosts.Clear();
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

        private void StartStandaloneFileWatcher(string filePath)
        {
            StopFileWatcher();

            var folderPath = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(folderPath) || string.IsNullOrEmpty(fileName))
                return;

            try
            {
                _fileWatcher = new FileSystemWatcher(folderPath)
                {
                    Filter = fileName,
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                _fileWatcher.Deleted += OnFileDeleted;
                _fileWatcher.Renamed += OnFileRenamed;
                _fileWatcher.Created += OnFileCreated;
                _fileWatcher.Changed += OnFileChanged;
            }
            catch
            {
                // 无权限监控时静默失败
            }
        }

        private void StopFileWatcher()
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            _debounceTimer = null;

            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_openMode == OpenMode.Standalone)
                {
                    if (LocalPathService.IsSamePath(e.FullPath, _currentFilePath))
                        RenderStandaloneMissing(e.FullPath);
                    return;
                }

                if (_openMode == OpenMode.Workspace)
                {
                    if (LocalPathService.IsSamePath(e.FullPath, _currentFilePath))
                        ScheduleCurrentFileRecheck(e.FullPath);
                    else
                        HandleFileDeleted(e.FullPath);
                }
            });
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_openMode == OpenMode.Standalone)
                {
                    if (LocalPathService.IsSamePath(e.OldFullPath, _currentFilePath))
                    {
                        _currentFilePath = e.FullPath;
                        FilePathText.Text = e.FullPath;
                        StartStandaloneFileWatcher(e.FullPath);
                        LoadMarkdownFile(e.FullPath);
                    }
                    return;
                }

                // Workspace 中当前文件的原子替换通常表现为“临时文件重命名为当前文件”。
                if (_openMode == OpenMode.Workspace && LocalPathService.IsSamePath(e.FullPath, _currentFilePath))
                {
                    ScheduleCurrentFileRecheck(e.FullPath);
                    return;
                }

                // 当前文件被真正重命名时，保持当前文档并恢复滚动位置。
                if (_openMode == OpenMode.Workspace && LocalPathService.IsSamePath(e.OldFullPath, _currentFilePath) &&
                    File.Exists(e.FullPath) && _supportedExtensions.Contains(Path.GetExtension(e.FullPath)))
                {
                    _currentFilePath = e.FullPath;
                    if (!string.IsNullOrEmpty(_currentFolderPath))
                    {
                        PopulateFileTree(_currentFolderPath);
                        SelectFileInTree(e.FullPath, loadFile: false);
                    }
                    ScheduleCurrentFileRecheck(e.FullPath);
                    return;
                }

                // 其他文件重命名后刷新树，但不重复加载当前文档。
                if (_openMode == OpenMode.Workspace && !string.IsNullOrEmpty(_currentFolderPath))
                {
                    var savedFilePath = _currentFilePath;
                    PopulateFileTree(_currentFolderPath);

                    // 尝试重新选中当前文件（如果只是重命名了其他文件）
                    if (!string.IsNullOrEmpty(savedFilePath) && File.Exists(savedFilePath))
                    {
                        SelectFileInTree(savedFilePath, loadFile: false);
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
                if (_openMode == OpenMode.Standalone)
                {
                    if (LocalPathService.IsSamePath(e.FullPath, _currentFilePath) && File.Exists(e.FullPath))
                        AutoReloadCurrentFile();
                    return;
                }

                if (_openMode == OpenMode.Workspace && LocalPathService.IsSamePath(e.FullPath, _currentFilePath))
                {
                    ScheduleCurrentFileRecheck(e.FullPath);
                    return;
                }

                // 新增其他文件时刷新树，但不重复加载当前文档。
                if (_openMode == OpenMode.Workspace && !string.IsNullOrEmpty(_currentFolderPath))
                {
                    var savedFilePath = _currentFilePath;
                    PopulateFileTree(_currentFolderPath);

                    if (!string.IsNullOrEmpty(savedFilePath) && File.Exists(savedFilePath))
                    {
                        SelectFileInTree(savedFilePath, loadFile: false);
                    }
                }
            });
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // 文件内容变更时自动刷新渲染（仅当前打开的文件，带防抖）
            if (_openMode != OpenMode.Standalone && _openMode != OpenMode.Workspace)
                return;

            if (!LocalPathService.IsSamePath(e.FullPath, _currentFilePath) || string.IsNullOrEmpty(_currentFilePath) || !File.Exists(_currentFilePath))
                return;

            ScheduleCurrentFileRecheck(e.FullPath);
        }

        private void ScheduleCurrentFileRecheck(string filePath)
        {
            // 合并编辑器原子保存产生的 Deleted/Created/Renamed/Changed 事件。
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Timers.Timer(FileEventDebounceMilliseconds) { AutoReset = false };
            _debounceTimer.Elapsed += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (!LocalPathService.IsSamePath(filePath, _currentFilePath))
                        return;

                    if (File.Exists(filePath))
                        AutoReloadCurrentFile();
                    else if (_openMode == OpenMode.Workspace)
                        HandleFileDeleted(filePath);
                    else if (_openMode == OpenMode.Standalone)
                        RenderStandaloneMissing(filePath);
                });
            };
            _debounceTimer.Start();
        }

        private void HandleFileDeleted(string deletedPath)
        {
            // 必须在移除节点前保留原目录树顺序，才能从被删除文档的位置查找相邻文档。
            var documentPaths = GetDocumentPathsInTree(deletedPath);
            var deletedIndex = documentPaths.FindIndex(path => LocalPathService.IsSamePath(path, deletedPath));

            // 从树中移除已删除的节点
            RemoveFileNodeFromTree(deletedPath);
            UpdateFileCount();

            // 如果删除的是当前正在显示的文件，优先切换到目录树中的下一文档；
            // 下一文档不存在时，再切换到上一文档。
            if (LocalPathService.IsSamePath(deletedPath, _currentFilePath))
            {
                _currentFilePath = null;
                var adjacentPath = DocumentSelectionService.FindAdjacentDocument(documentPaths, deletedIndex);

                if (!string.IsNullOrEmpty(adjacentPath))
                {
                    SelectFileInTree(adjacentPath);
                }
                else
                {
                    // 前后都没有可用文档 → 清空显示
                    RenderEmptyNoFiles();
                }
            }
        }

        private List<string> GetDocumentPathsInTree(string includeMissingPath)
        {
            var paths = new List<string>();
            foreach (var item in FileTreeView.Items)
            {
                if (item is TreeViewItem rootNode)
                    CollectDocumentPaths(rootNode, includeMissingPath, paths);
            }
            return paths;
        }

        private void CollectDocumentPaths(TreeViewItem node, string includeMissingPath, List<string> paths)
        {
            if (node.Tag is string path &&
                _supportedExtensions.Contains(Path.GetExtension(path)) &&
                (File.Exists(path) || LocalPathService.IsSamePath(path, includeMissingPath)))
            {
                paths.Add(path);
                return;
            }

            foreach (var child in node.Items)
            {
                if (child is TreeViewItem childNode)
                    CollectDocumentPaths(childNode, includeMissingPath, paths);
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

        private bool SelectFileInTree(string filePath, bool loadFile = true)
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

                        if (loadFile && targetNode.Tag is string path && File.Exists(path))
                        {
                            LoadMarkdownFile(path);
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        private TreeViewItem? FindFileNodeByPath(TreeViewItem node, string filePath)
        {
            if (node.Tag is string path &&
                string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(path))
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
                _configManager.ZoomFactor = _zoomFactor;
                _configManager.Save();
                ApplyZoom();
            }
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (_zoomFactor > 0.3)
            {
                _zoomFactor -= 0.1;
                _configManager.ZoomFactor = _zoomFactor;
                _configManager.Save();
                ApplyZoom();
            }
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            _zoomFactor = 1.0;
            _configManager.ZoomFactor = _zoomFactor;
            _configManager.Save();
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
            _configManager.IsDarkMode = _isDarkMode;
            _configManager.Save();
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                Reload_Click(sender, e);
            }
        }
        #endregion

        #region HTML 包装
        private string WrapHtml(string content, string? baseFilePath)
        {
            var bgColor = _isDarkMode ? "#1e1e1e" : "#ffffff";
            var textColor = _isDarkMode ? "#d4d4d4" : "#333333";
            var linkColor = _isDarkMode ? "#569cd6" : "#0066cc";
            var codeBg = _isDarkMode ? "#2d2d2d" : "#f5f5f5";
            var borderColor = _isDarkMode ? "#404040" : "#e0e0e0";

            var baseTag = "";
            if (!string.IsNullOrEmpty(baseFilePath))
            {
                var baseUri = new Uri(Path.GetFullPath(baseFilePath)).AbsoluteUri;
                baseTag = $"<base href=\"{System.Net.WebUtility.HtmlEncode(baseUri)}\">";
            }

            return $@"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    {baseTag}
    <script>
        (function() {{
            document.addEventListener('click', function(event) {{
                var anchor = event.target.closest('a[href]');
                if (!anchor) return;
                event.preventDefault();
                event.stopPropagation();
                window.chrome.webview.postMessage(anchor.getAttribute('href') || anchor.href);
            }}, true);

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

        private void RenderStandaloneMissing(string filePath)
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            var hint = _isDarkMode ? "#888" : "#999";
            var html = $@"
<div style=""text-align:center;color:{hint};padding-top:150px;"">
    <h2>📄 文件已删除或移动</h2>
    <p>{System.Net.WebUtility.HtmlEncode(filePath)}</p>
    <p style=""font-size:0.9em;opacity:0.7;"">文件恢复后可按 F5 重新加载</p>
</div>";
            webView.NavigateToString(WrapHtml(html, null));
            StatusText.Text = $"文件已删除或移动: {filePath}";
            Title = "文件不可用 - Markdown 查看器";
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
        private int _searchIndex;

        private void ShowSearchBar()
        {
            SearchBar.Visibility = Visibility.Visible;
            SearchBox.Focus();
            SearchBox.SelectAll();
            _searchIndex = 0;
        }

        private void CloseSearchBar()
        {
            SearchBar.Visibility = Visibility.Collapsed;
            SearchBox.Text = "";
            ClearSearchHighlight();
            _searchIndex = 0;
        }

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchIndex = 0;
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
                var escaped = EscapeJs(text);
                var backwards = (!forward).ToString().ToLower();
                var findScript = $"window.find('{escaped}', false, {backwards}, true, false, true, false);";
                var found = await webView.CoreWebView2.ExecuteScriptAsync(findScript);
                var wasFound = found?.Trim('"') == "true";

                if (!wasFound)
                {
                    var reset = forward
                        ? $"window.getSelection().removeAllRanges(); window.find('{escaped}', false, false, true, false, true, false);"
                        : $"window.getSelection().removeAllRanges(); window.find('{escaped}', false, true, true, false, true, false);";
                    await webView.CoreWebView2.ExecuteScriptAsync(reset);
                }

                // 统计总数
                var countScript = $"(function(){{ try {{ var m = document.body.textContent.match(new RegExp('{escaped}','gi')); return (m?m.length:0).toString(); }} catch(e) {{ return '0'; }} }})();";
                var countResult = await webView.CoreWebView2.ExecuteScriptAsync(countScript);
                int.TryParse(countResult?.Trim('"'), out var total);

                if (total > 0)
                {
                    // 维护当前索引
                    if (forward)
                    {
                        _searchIndex++;
                        if (_searchIndex > total) _searchIndex = 1;
                    }
                    else
                    {
                        _searchIndex--;
                        if (_searchIndex < 1) _searchIndex = total;
                    }
                    if (!wasFound) _searchIndex = 1; // 回绕到开头
                    SearchCountText.Text = $"{_searchIndex}/{total}";
                }
                else
                {
                    SearchCountText.Text = "0/0";
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
            return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "");
        }
        #endregion

        #region 收藏
        private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFilePath) || !File.Exists(_currentFilePath))
                return;

            if (_favoritesManager.IsFavorite(_currentFilePath))
                RemoveFavorite(_currentFilePath);
            else
                AddFavorite(_currentFilePath);
        }

        private void AddFavorite(string filePath)
        {
            if (!_favoritesManager.Add(filePath, out var error))
            {
                MessageBox.Show($"保存收藏失败: {error}", "收藏失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            RefreshFavoritesList();
            UpdateFavButton();
        }

        private void RemoveFavorite(string filePath)
        {
            if (!_favoritesManager.Remove(filePath, out var error))
            {
                MessageBox.Show($"取消收藏失败: {error}", "收藏失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            RefreshFavoritesList();
            UpdateFavButton();
        }

        private void FileTreeView_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 找到被右击的 TreeViewItem
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep is not TreeViewItem)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is TreeViewItem item)
            {
                item.IsSelected = true;
                e.Handled = true;

                if (item.Tag is string path && File.Exists(path))
                {
                    var menu = new ContextMenu();
                    if (_favoritesManager.IsFavorite(path))
                    {
                        var mi = new MenuItem { Header = "取消收藏", FontSize = 12 };
                        mi.Click += (s, args) => RemoveFavorite(path);
                        menu.Items.Add(mi);
                    }
                    else
                    {
                        var mi = new MenuItem { Header = "⭐ 添加到收藏", FontSize = 12 };
                        mi.Click += (s, args) => AddFavorite(path);
                        menu.Items.Add(mi);
                    }
                    menu.IsOpen = true;
                }
            }
        }

        private void FavItem_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.Content is FavItem fav)
            {
                item.IsSelected = true;
                e.Handled = true;

                var menu = new ContextMenu();
                var removeItem = new MenuItem { Header = "取消收藏", FontSize = 12 };
                removeItem.Click += (s, args) => RemoveFavorite(fav.FilePath);
                menu.Items.Add(removeItem);
                menu.IsOpen = true;
            }
        }


        private void FavoritesHeader_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            FavoritesList.Visibility = FavoritesList.Visibility == Visibility.Collapsed
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void FavoritesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FavoritesList.SelectedItem is FavItem fav && File.Exists(fav.FilePath))
                LoadMarkdownFile(fav.FilePath);
        }

        private void RefreshFavoritesList()
        {
            var favs = _favoritesManager.GetAll().Where(f => File.Exists(f)).ToList();
            FavoritesList.Items.Clear();

            if (favs.Count == 0)
            {
                FavoritesHeader.Visibility = Visibility.Collapsed;
                FavoritesList.Visibility = Visibility.Collapsed;
                FavButton.Content = "⭐ 收藏";
                return;
            }

            FavoritesHeader.Visibility = Visibility.Visible;
            FavoritesList.Visibility = Visibility.Visible;
            FavCountText.Text = $"{favs.Count} 个";

            foreach (var f in favs)
            {
                FavoritesList.Items.Add(new FavItem { FilePath = f, DisplayName = Path.GetFileName(f) });
            }

            UpdateFavButton();
        }

        private void UpdateFavButton()
        {
            if (!string.IsNullOrEmpty(_currentFilePath) && _favoritesManager.IsFavorite(_currentFilePath))
                FavButton.Content = "⭐ 已收藏";
            else
                FavButton.Content = "⭐ 收藏";
        }
        #endregion

        #region 目录
        private void ToggleToc_Click(object sender, RoutedEventArgs e)
        {
            if (TocPanel.Visibility == Visibility.Visible)
                HideToc();
            else
                ShowToc();
        }

        private void ToggleToolbar_Click(object sender, RoutedEventArgs e)
        {
            var visible = MainToolBar.Visibility != Visibility.Visible;
            MainToolBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _configManager.IsToolbarVisible = visible;
            _configManager.Save();
        }

        private void TocClose_Click(object sender, RoutedEventArgs e)
        {
            HideToc();
        }

        private void ShowToc()
        {
            _tocColumnWidth = ConfigManager.NormalizeTocWidth(_tocColumnWidth);
            TocColumn.MinWidth = ConfigManager.MinTocWidth;
            TocColumn.Width = new GridLength(_tocColumnWidth);
            TocSplitterColumn.Width = new GridLength(5);
            TocPanel.Visibility = Visibility.Visible;
            TocSplitter.Visibility = Visibility.Visible;
            _configManager.IsTocVisible = true;
            _configManager.TocWidth = _tocColumnWidth;
            _configManager.Save();
        }

        private void HideToc()
        {
            CaptureTocWidth();
            TocPanel.Visibility = Visibility.Collapsed;
            TocSplitter.Visibility = Visibility.Collapsed;
            TocColumn.MinWidth = 0;
            TocColumn.Width = new GridLength(0);
            TocSplitterColumn.Width = new GridLength(0);
            _configManager.IsTocVisible = false;
            _configManager.TocWidth = _tocColumnWidth;
            _configManager.Save();
        }

        private void CaptureTocWidth()
        {
            if (TocPanel.Visibility != Visibility.Visible)
                return;

            var width = TocColumn.ActualWidth > 0 ? TocColumn.ActualWidth : TocColumn.Width.Value;
            _tocColumnWidth = ConfigManager.NormalizeTocWidth(width);
        }

        private void TocSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            CaptureTocWidth();
            TocColumn.Width = new GridLength(_tocColumnWidth);
            _configManager.TocWidth = _tocColumnWidth;
            _configManager.Save();
        }

        private void BuildToc(string markdown)
        {
            TocTreeView.Items.Clear();
            try
            {
                var doc = Markdig.Markdown.Parse(markdown, _pipeline);
                var headings = doc.Descendants<Markdig.Syntax.HeadingBlock>().ToList();
                if (headings.Count == 0) return;

                // 用栈构建层级
                var stack = new Stack<(TreeViewItem node, int level)>();

                foreach (var h in headings)
                {
                    var text = h.Inline?.FirstChild?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    var id = GenerateHeadingId(text);
                    var header = new TextBlock
                    {
                        Text = text,
                        TextWrapping = TextWrapping.NoWrap,
                        ToolTip = text
                    };
                    var item = new TreeViewItem
                    {
                        Header = header,
                        Tag = id,
                        FontSize = 14 - h.Level  // h1=13, h2=12, h3=11...
                    };

                    // 找到合适的父节点
                    while (stack.Count > 0 && stack.Peek().level >= h.Level)
                        stack.Pop();

                    if (stack.Count == 0)
                    {
                        TocTreeView.Items.Add(item);
                    }
                    else
                    {
                        stack.Peek().node.Items.Add(item);
                    }
                    stack.Push((item, h.Level));
                }
            }
            catch { }
        }

        private static string GenerateHeadingId(string text)
        {
            var sb = new StringBuilder();
            foreach (var c in text)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                    sb.Append(char.ToLowerInvariant(c));
                else if (c == ' ')
                    sb.Append('-');
            }
            var id = sb.ToString();
            if (id.Length > 0 && char.IsDigit(id[0]))
                id = "section-" + id;
            return id;
        }

        private async void TocTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is string id && webView.CoreWebView2 != null)
            {
                try
                {
                    var text = item.Header switch
                    {
                        TextBlock header => header.Text,
                        string value => value,
                        _ => ""
                    };
                    var escaped = EscapeJs(text);
                    // 先尝试按 Markdig 生成的 id 找，失败则按文本内容找
                    var script = $"(function(){{ var h = document.getElementById('{id}'); if(!h){{ var hs = document.querySelectorAll('h1,h2,h3,h4,h5,h6'); for(var i=0;i<hs.length;i++){{ if(hs[i].textContent.trim()==='{escaped}'){{ h=hs[i]; break; }} }} }} if(h){{ h.scrollIntoView({{behavior:'auto',block:'start'}}); }} }})();";
                    await webView.CoreWebView2.ExecuteScriptAsync(script);
                }
                catch { }
            }
        }
        #endregion

        private void ApplyConfig()
        {
            var c = _configManager;
            _zoomFactor = c.ZoomFactor;
            ApplyZoom();
            _isDarkMode = c.IsDarkMode;
            _tocColumnWidth = ConfigManager.NormalizeTocWidth(c.TocWidth);
            if (_isDarkMode && !string.IsNullOrEmpty(_currentFilePath))
                Reload_Click(this, new RoutedEventArgs());
            if (c.IsTocVisible)
            {
                ShowToc();
            }
            else
                HideToc();
            MainToolBar.Visibility = c.IsToolbarVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        #region 关于
        private void About_Click(object sender, RoutedEventArgs e)
        {
            const string projectUrl = "https://github.com/leojulian/MarkdownViewer";

            var content = new StackPanel { Margin = new Thickness(24) };
            content.Children.Add(new TextBlock
            {
                Text = $"MarkdownViewer v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            });
            content.Children.Add(new TextBlock
            {
                Text = "基于 WPF + Markdig + WebView2 构建\n支持 Markdown 扩展语法与本地文档浏览",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });
            content.Children.Add(new TextBlock
            {
                Text = "主要功能:\n" +
                       "• 文件夹浏览与文档树\n" +
                       "• 拖拽文件/文件夹打开\n" +
                       "• 历史记录与上次会话恢复\n" +
                       "• 文件实时监控与自动刷新\n" +
                       "• 缩放、深色模式与工具栏配置\n" +
                       "• Mermaid 图表与文本搜索\n" +
                       "• 收藏夹与文档目录导航",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var projectLink = new Hyperlink(new Run(projectUrl))
            {
                NavigateUri = new Uri(projectUrl)
            };
            projectLink.RequestNavigate += (_, args) =>
            {
                Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true });
                args.Handled = true;
            };

            var linkText = new TextBlock { Margin = new Thickness(0, 0, 0, 20) };
            linkText.Inlines.Add(new Run("项目地址: "));
            linkText.Inlines.Add(projectLink);
            content.Children.Add(linkText);

            var closeButton = new Button
            {
                Content = "确定",
                IsDefault = true,
                MinWidth = 80,
                Padding = new Thickness(12, 4, 12, 4),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            content.Children.Add(closeButton);

            var aboutWindow = new Window
            {
                Title = "关于",
                Owner = this,
                Content = content,
                Width = 520,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
            closeButton.Click += (_, _) => aboutWindow.Close();
            aboutWindow.ShowDialog();
        }
        #endregion
    }

}
