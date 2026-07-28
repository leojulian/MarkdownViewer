中文 | [English](README.en.md)

# <img src="src/Assets/app_icon.png" width="32" align="left" style="margin-right:8px"> MarkdownViewer

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
- 本地图片通过 WebView2 受控虚拟主机映射加载，支持 HTML/Markdown 图片、中文、空格、URL 编码和多级子目录
- 本地 Markdown 链接会按当前 workspace 边界在当前实例或新实例中打开
- 文件夹链接使用 Explorer 打开，其他普通文件交给系统默认程序

### 🖱️ 拖拽支持
- 拖拽 **文件夹** → 自动填充文档树
- 拖拽 **.md 文件** → 直接渲染
- 不支持的文件格式会弹出提示

### 🚀 系统打开方式
- 支持通过 Windows 右键菜单的 **打开方式** 直接打开 Markdown 文件
- 支持将 MarkdownViewer 设置为 Markdown 默认查看程序并双击打开文件
- 双击、打开方式或命令行文件会优先匹配曾经打开过的工作区，并在原目录树中定位文件
- 文件不属于任何历史工作区时进入单文件模式，不扫描父目录，也不会创建 `.markdownviewer`
- 支持普通 Windows 路径和 URL 编码的 `file:///` Markdown URI

### 🧭 工作区与单文件模式

| 模式 | 进入方式 | 目录树与收藏 | 文件监控 |
|------|----------|--------------|----------|
| 工作区 | 打开/拖拽文件夹，或外部文件匹配到历史工作区 | 显示完整目录树，使用 `{folder}/.markdownviewer/favorites.json` | 监控整个工作区 |
| 单文件 | Ctrl+O、拖拽文件，或外部文件未匹配工作区 | 隐藏目录树，禁用目录收藏 | 只监控当前文件 |

单文件被外部编辑器保存后会自动刷新并保持滚动位置；被删除或移动时显示文件不可用，不会自动打开同目录的其他文档。

### 🔄 历史记录
- 关闭时自动保存当前文件夹与文档路径
- 启动时自动恢复上次会话，定位到上次查看的文档
- 记录文件位于 `{exe}/History/history.json`，最多保留 20 条

### 🗑️ 文件实时监控
- 通过 `FileSystemWatcher` 监控已打开文件夹的文件变更

| 事件 | 行为 |
|------|------|
| 当前文件被删除 | 按目录树顺序切换至下一文档；若无下一文档则切换至上一文档 |
| 无剩余文档 | 显示空状态提示 |
| 文件内容变更 | 自动刷新渲染（保持滚动位置，600ms 文件事件合并） |
| 编辑器原子保存 | 合并短暂删除/重建事件，保持当前文档与滚动位置 |
| 文件新增/重命名 | 自动刷新文档树 |

### 🎨 视图功能
- **缩放**：`Ctrl+加号/Ctrl+减号` 或工具栏按钮（30%~300%）
- **深色模式**：`Ctrl+D` 切换，代码块/表格/引用块自适应配色
- **重新加载**：`F5` 刷新当前文档
- **工具栏开关**：视图菜单控制显示/隐藏，状态即时保存
- **状态栏**：只读文本框，支持 `Ctrl+C` 复制路径

### 📊 Mermaid 图表
- 支持流程图、时序图、类图等 Mermaid 图表（离线渲染）
- mermaid.js 嵌入资源，首次运行自动提取

### 🔍 文本搜索
- `Ctrl+F` 打开搜索栏，实时高亮并显示 **当前/总数**
- `Enter` 下一个 / `Shift+Enter` 上一个

### ⭐ 收藏夹
- 工具栏按钮或右键菜单添加/取消收藏
- 收藏列表独立显示在左侧面板，支持折叠
- 保存在 `{folder}/.markdownviewer/favorites.json`，目录带 Windows 隐藏属性，重启保留
- 添加或取消收藏时立即原子保存，并通过跨进程锁避免多个 MarkdownViewer 实例互相覆盖
- 单文件模式不绑定文件父目录的收藏，需打开文件夹后使用目录收藏

### 📑 文档目录
- `Ctrl+T` 切换右侧目录面板，Markdig AST 解析标题层级
- 点击标题跳转到对应位置，面板宽度可拖拽

### ⚙️ UI 配置持久化
- 缩放、深色模式、目录、工具栏状态自动保存
- 配置位于 `{exe}/History/config.json`，每次变更即时写入

## 快捷键

| 快捷键 | 功能 |
|--------|------|
| `Ctrl+O` | 打开文件 |
| `Ctrl+Shift+O` | 打开文件夹 |
| `Ctrl++/Ctrl+-` | 放大/缩小 |
| `Ctrl+0` | 重置缩放 |
| `Ctrl+D` | 切换深色模式 |
| `Ctrl+F` | 搜索 |
| `Ctrl+T` | 显示/隐藏目录 |
| `F5` | 重新加载当前文档 |

## 项目结构

```
MarkdownViewer/
├── MarkdownViewer.sln
├── src/
│   ├── App.xaml
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs           # WPF/WebView2 界面编排
│   ├── Models/                      # 打开模式、历史、收藏模型
│   ├── Services/                    # 路径规则、文档选择、历史、收藏、配置
│   ├── Properties/
│   ├── Assets/
│   └── MarkdownViewer.csproj
├── tests/
│   └── MarkdownViewer.Tests/        # 路径、链接、图片、删除回退、收藏并发测试
├── samples/
│   ├── sample.md                    # 综合人工验证入口
│   └── sample-assets/
├── README.md
└── README.en.md
```

`MainWindow` 保留窗口状态和 WPF/WebView2 事件编排；不依赖 UI 的路径解析、workspace 匹配、图片路径、相邻文档选择和持久化逻辑位于 `Services`，可以独立测试。

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

```powershell
dotnet run --project src\MarkdownViewer.csproj
```

## 构建与测试

```powershell
dotnet build MarkdownViewer.sln -c Release
dotnet test MarkdownViewer.sln -c Release
```

## 版本历史

| 版本 | 更新内容 |
|------|----------|
| v1.0 | 基础 Markdown 渲染、缩放、深色模式 |
| v1.1 | 文件夹浏览、文档树、拖拽支持、历史记录 |
| v1.2 | 历史恢复上次文档、FileSystemWatcher 实时监控、删除自动切换 |
| v1.3 | 文件变更自动刷新保持滚动位置、300ms 防抖合并 |
| v1.4 | Mermaid 图表渲染（离线嵌入）、Ctrl+F 文本搜索 |
| v1.5 | 收藏夹功能、右键菜单、收藏持久化 |
| v1.6 | 多分辨率 ICO 图标、Assets 目录整理 |
| v1.7 | 搜索显示当前/总数、收藏存于 .markdownviewer、历史子菜单、并发安全 |
| v1.8 | 文档目录面板 (Markdig AST)、Ctrl+T 切换、目录点击跳转 |
| v1.9 | UI 配置持久化 (缩放/深色/目录/工具栏)、即时保存 |
| v1.10 | 修复 Windows 打开方式和命令行启动未打开指定 Markdown 文件的问题 |
| v1.11（本地测试，未发布） | 工作区/单文件模式、历史工作区定位、递归目录树、独立文件监控、原子保存防抖、当前文档删除后按目录树顺序切换下一篇/上一篇、收藏夹隐藏目录原子保存与跨进程并发保护 |
| v1.12（本地实现，未发布） | 支持 `file:///` URI、本地图片标准 URI、Markdown 链接路由；同 workspace 文档在当前实例定位选中，跨 workspace 文档新开实例，文件夹用 Explorer 打开，普通文件使用系统默认程序 |
| 下一版本（开发中） | 迁移到 `src/tests/samples` 目录结构，抽离可测试的模型和服务，增加自动化测试，并提供中英文 README |

[`samples/sample.md`](samples/sample.md) 提供本地图片、Markdown 跳转、文件夹、普通文件、安全拦截和外部链接的综合测试入口。
