# Markdown 锚点、HTML 导出与性能门禁设计

**项目**：`D:\CodeHere\practise\MarkdownViewer`
**分支**：Git `main`
**日期**：2026-08-27
**状态**：已批准

## 1. 目标

1. 支持 GitHub 风格 Markdown 文档内锚点，覆盖数字前缀、英文、中文和重复标题。
2. 右侧目录与正文链接使用 Markdig 生成的同一标题 ID。
3. 文件菜单能够把当前 Markdown 导出为跟随当前主题的离线单文件 HTML。
4. 建立文档打开及连续多文档跳转、后退、前进渲染的性能基线和门禁。
5. 完成构建、测试后部署到 `D:\Program Files\MarkdownViewer\MarkdownViewer.exe`。

参考验收文件：

`D:\CodeHere\technicaldocs\pipeline\03-Pipeline现状与参数转换\Pipeline配置中心P0-2Pipeline动态去重字段来源分类.md`

## 2. 非目标

- 不修改参考 Markdown 文档中的链接定义。
- 不导出整个 workspace 或生成静态站点。
- 不下载 HTTP/HTTPS 图片。
- 不改变 Markdown 跨 workspace 打开规则和 WebView2 历史所有权。
- 不提交、推送或清理用户已有工作区改动。

## 3. 统一渲染与锚点

建立独立渲染服务，按以下顺序配置 Markdig：

1. `UseAutoIdentifiers(AutoIdentifierOptions.GitHub)`；
2. `UseAdvancedExtensions()`；
3. 现有 Pipe Tables、Task Lists、Emoji 扩展。

服务一次解析 Markdown，并从同一 AST 产出 HTML 和标题列表。标题 ID 直接读取 Markdig `HtmlAttributes.Id`，不再使用手写 ID 规则。

参考标题：

```markdown
## 3. Pipeline 配置与规划产生的字段
```

必须生成：

```html
<h2 id="3-pipeline-配置与规划产生的字段">
```

重复标题必须使用 Markdig 的去重结果，例如第二个标题带 `-1` 后缀。纯 `#fragment`、同文档链接和 `other.md#fragment` 继续走现有路由。

## 4. 当前文档 HTML 导出

文件菜单增加“导出 HTML 文档”。只有当前 Markdown 文件存在时可执行。

导出行为：

- 默认文件名为当前 Markdown 文件名，扩展名改为 `.html`；
- 主题跟随当前预览主题；
- 使用统一渲染服务生成正文和 GitHub 风格标题 ID；
- 本地图片读取真实文件字节并转为带 MIME 的 base64 data URI；
- 已有 data URI 保持不变；
- HTTP/HTTPS 图片不下载，保留地址并计入外部资源提示；
- Mermaid 脚本、样式和渲染启动代码内嵌；
- 不包含 MarkdownViewer 页面元数据、虚拟主机映射或 `window.chrome.webview`；
- 页内锚点交给普通浏览器原生处理；
- 先写同目录临时文件，再原子替换目标文件；失败时删除临时文件，不留下半成品。

本地图片缺失或不可读时保留原引用、继续导出，并在完成提示中报告警告数量。

## 5. 性能门禁

新增独立 Release 性能运行器，不污染正常应用启动路径。固定数据集包括：

- 与参考文档同量级的表格和中英文标题文档；
- 可重复生成的大文档；
- 10 篇互相链接的文档序列。

测量指标：

- `DocumentOpen`：读取、Markdown 解析、HTML 生成和目录提取；
- `WebViewNavigate`：提交 HTML 到 `NavigationCompleted` 且 DOM 完成两帧布局；
- `HistoryBack` / `HistoryForward`：连续多文档导航后的 WebView2 后退和前进；
- 最终页面元数据必须与预期文档一致，性能通过不能替代正确性。

运行器预热后采样，输出 P50、P95、最大值和环境指纹 JSON。环境指纹至少包含 CPU、OS、.NET、WebView2 和应用版本。

判定规则：

- 同环境存在基线时，各关键指标 P95 超过基线 25%，且差值至少达到 `5 ms` 最小可判别阈值时失败；
- `5 ms` 噪声阈值只作用于相对基线比较，固定绝对预算始终独立生效；
- 同时受代码中固定绝对预算约束；
- 初次运行生成 `.verify/performance-baseline.json`，后续运行不得自动放宽阈值；
- HTML 导出耗时记录在报告中，但不纳入文档打开预算。

绝对预算在首次实测后取高于稳定 P95 的整数值并固定到运行器配置。

## 6. 错误处理

- 当前文件不存在：禁用导出或显示“当前文档不可用”。
- 保存路径无权限或写入失败：显示错误，不报告成功。
- 无效锚点：保持现有静默不跳转行为，不启动新实例。
- WebView2 性能场景初始化或导航失败：性能运行器以非零退出码结束并保留报告中的失败原因。
- 部署时只结束可执行路径严格等于正式部署路径的 MarkdownViewer 进程。

## 7. 验证

自动化测试覆盖：

- 数字、英文、中文标题的 GitHub ID；
- 重复标题 ID；
- HTML 与目录 ID 一致；
- 本地中文/空格图片、data URI、外部图片和缺失图片；
- Mermaid 及应用专用脚本排除；
- 原子写入成功与失败清理；
- 性能报告统计与基线 25% 判定。

集成验证覆盖：

1. 打开参考文档，点击第 1 章分类链接，分别跳到第 3 至第 9 章。
2. 导出参考文档和 `samples/sample.md`，使用 Edge 打开并检查锚点、主题、图片和 Mermaid。
3. 运行性能运行器，建立基线后立即按基线复跑门禁。
4. Release 构建和全量测试。
5. 发布后校验正式 EXE 与发布产物哈希一致，从正式路径启动并复验。

## 8. 需求追踪

| 需求 | 设计位置 | 代码位置 | 验证 |
| --- | --- | --- | --- |
| ANC-001 GitHub 风格页内锚点 | 3 | `src/Services/MarkdownRenderingService.cs` | 渲染测试、参考文档实测 |
| ANC-002 目录与正文 ID 一致 | 3 | 渲染服务、`MainWindow.BuildToc` | 重复标题测试、目录点击实测 |
| EXP-001 当前文档离线单文件导出 | 4 | `src/Services/HtmlExportService.cs`、文件菜单 | 导出测试、Edge 实测 |
| EXP-002 跟随当前主题 | 4 | HTML 导出模板 | 深浅主题导出测试 |
| PERF-001 文档打开性能门禁 | 5 | `performance/` | 基线生成和复跑 |
| PERF-002 多文档跳转回退渲染性能 | 5 | `performance/` | WebView2 导航报告 |
| DEP-001 正式路径部署 | 7 | 发布产物 | 哈希和启动验证 |
