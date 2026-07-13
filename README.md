# MarkdownViewer

基于 WPF + Markdig + WebView2 的 Markdown 文档查看器，支持文件夹浏览、文档树导航、拖拽打开、历史恢复与文件实时监控。

## 功能概览

### 📂 文件夹浏览与文档树
- 打开文件夹后，以树形结构展示所有子目录及 Markdown 文档
- 保留完整的目录层级关系，子文件夹嵌套显示
- 空目录自动隐藏，文档总数实时统计
- 左右面板可通过分割线自由拖拽调整宽度

### 📄 Markdown 渲染
- 基于 [Markdig](https://github.com/xoofx/markdig) 解析，WebView2 渲染
- 支持扩展语法：表格、任务列表、Emoji、Pipe Tables 等
- 自定义 CSS 样式，深色/浅色模式一键切换
- 图片相对路径自动解析（`<base>` 标签注入）

### 🖱️ 拖拽支持
- 拖拽 **文件夹** → 自动填充文档树
- 拖拽 **.md 文件** → 直接渲染
- 不支持的文件格式会弹出提示

### 🔄 历史记录
- 关闭时自动保存当前文件夹与文档路径
- 启动时自动恢复上次会话，定位到上次查看的文档
- 记录文件位于 `{exe}/History/history.json`，最多保留 20 条

### 🗑️ 文件实时监控
- 通过 `FileSystemWatcher` 监控已打开文件夹的文件变更

| 事件 | 行为 |
|------|------|
| 当前文件被删除 | 自动切换至下一个文档 |
| 无剩余文档 | 显示空状态提示 |
| 文件内容变更 | 自动刷新渲染（保持滚动位置，300ms防抖） |
| 文件新增/重命名 | 自动刷新文档树 |

### 🎨 视图功能
- **缩放**：`Ctrl+加号/Ctrl+减号` 或工具栏按钮（30%~300%）
- **深色模式**：`Ctrl+D` 切换，代码块/表格/引用块自适应配色
- **重新加载**：`F5` 刷新当前文档

## 快捷键

| 快捷键 | 功能 |
|--------|------|
| `Ctrl+O` | 打开文件 |
| `Ctrl+Shift+O` | 打开文件夹 |
| `Ctrl++/Ctrl+-` | 放大/缩小 |
| `Ctrl+0` | 重置缩放 |
| `Ctrl+D` | 切换深色模式 |
| `F5` | 重新加载当前文档 |

## 项目结构

```
MarkdownViewer/
├── App.xaml                 # 应用程序入口 XAML
├── App.xaml.cs              # 应用程序入口代码
├── MainWindow.xaml          # 主窗口布局（菜单、工具栏、TreeView、WebView2）
├── MainWindow.xaml.cs       # 主窗口逻辑
│   ├── MainWindow           # 窗口类
│   │   ├── 快捷键处理        # KeyDown 事件 → 菜单/工具栏功能
│   │   ├── 文件操作          # 打开文件/文件夹、加载渲染
│   │   ├── 文档树            # PopulateFileTree / CreateDirectoryNode
│   │   ├── 拖拽支持          # DragEnter / Drop（自动识别文件/文件夹）
│   │   ├── 文件监控          # FileSystemWatcher（删除/新增/重命名/变更）
│   │   ├── 历史恢复          # RestoreLastSession / SelectFileInTree
│   │   ├── 缩放 & 深色模式   # ZoomFactor / ToggleDarkMode
│   │   └── HTML 渲染包装     # WrapHtml（CSS 注入 + base 路径）
│   ├── HistoryManager       # 历史记录管理器（JSON 读写、增删查）
│   └── HistoryEntry         # 历史记录数据模型
├── MarkdownViewer.csproj    # .NET 8 WPF 项目文件
└── README.md                # 项目说明
```

## 技术栈

| 技术 | 用途 |
|------|------|
| .NET 8 WPF | 桌面应用框架 |
| [Markdig](https://www.nuget.org/packages/Markdig) 0.37 | Markdown 解析引擎 |
| [WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) 1.0 | Chromium 内核渲染 |
| `System.Text.Json` | 历史记录 JSON 序列化 |
| `FileSystemWatcher` | 文件系统实时监控 |

## 支持的文件格式

`.md` `.markdown` `.mdown` `.mkd` `.mkdn` `.mdwn` `.mdtxt` `.mdtext` `.rmd`

## 运行方式

```bash
cd MarkdownViewer
dotnet run
```

## 版本历史

| 版本 | 更新内容 |
|------|----------|
| v1.0 | 基础 Markdown 渲染、缩放、深色模式 |
| v1.1 | 文件夹浏览、文档树、拖拽支持、历史记录 |
| v1.3 | 文件变更自动刷新保持滚动位置、300ms 防抖合并 |
