# MarkdownViewer v1.13 综合验证文档

本文件用于快速验证本地图片、Markdown 跳转、文件夹、普通文件和安全拦截。每项下方均写明预期结果。

## 1. 图片显示

### 1.1 README 同款 HTML 相对图片

预期：显示 MarkdownViewer 图标。

<img src="../src/Assets/app_icon.png" width="96" alt="README 同款 HTML 图片">

### 1.2 Markdown 普通相对图片

预期：显示 MarkdownViewer 图标。

![普通相对图片](sample-assets/普通图片.png)

### 1.3 中文和空格路径

预期：显示 MarkdownViewer 图标。

![中文空格图片](<sample-assets/中文 图片.png>)

### 1.4 URL 编码路径

预期：显示 MarkdownViewer 图标，路径中的中文和空格已经百分号编码。

![URL 编码图片](sample-assets/%E4%B8%AD%E6%96%87%20%E5%9B%BE%E7%89%87.png)

### 1.5 嵌套目录图片

预期：显示 MarkdownViewer 图标。

![嵌套目录图片](<sample-assets/嵌套 目录/子目录图片.png>)

### 1.6 不存在图片

预期：只显示破损图片，不影响本文档其他内容。

![不存在图片](sample-assets/not-found.png)

### 1.7 Data URI

预期：显示一个小型绿色 SVG，应用不应改写 data URI。

<img alt="Data URI" src="data:image/svg+xml,%3Csvg%20xmlns='http://www.w3.org/2000/svg'%20width='160'%20height='50'%3E%3Crect%20width='160'%20height='50'%20fill='%232e8b57'/%3E%3Ctext%20x='12'%20y='31'%20fill='white'%3EData%20URI%20OK%3C/text%3E%3C/svg%3E">

## 2. Markdown 文档链接

### 2.1 当前文档页内锚点

[跳转到“验证完成位置”](#验证完成位置)

预期：当前窗口滚动到本文末尾，不启动新实例。

### 2.2 同 workspace 文档

[打开 README.md](../README.md)

预期：如果当前 workspace 是本项目目录，在当前实例打开仓库根目录的 README，并选中左侧目录树节点。

### 2.3 同 workspace 嵌套文档

[打开嵌套测试文档](<sample-assets/嵌套 目录/nested-sample.md>)

预期：当前实例打开嵌套文档，展开目录并选中对应节点。嵌套文档中的 `../` 图片也应显示。

### 2.4 URL 编码 file URI

[打开 GraphRuntimeViewer 审核模式指南](file:///D:/CodeHere/pipeline/Doc/07-testing/guides/GraphRuntimeViewer%E5%AE%A1%E6%A0%B8%E6%A8%A1%E5%BC%8F%E7%AE%97%E6%B3%95%E4%BA%BA%E5%91%98%E4%BD%BF%E7%94%A8%E6%8C%87%E5%8D%97.md)

预期：如果该文件不属于当前 workspace，保留当前窗口并新开 MarkdownViewer 实例。

## 3. 文件夹和普通文件

### 3.1 文件夹链接

[使用 Explorer 打开 sample-assets 文件夹](sample-assets/)

预期：Windows Explorer 打开对应文件夹，当前 MarkdownViewer 不切换 workspace。

### 3.2 普通文本文件

[使用系统默认程序打开测试文本](sample-assets/sample.txt)

预期：由 `.txt` 的系统默认程序打开。

### 3.3 被阻止的脚本文件

[尝试打开测试脚本](sample-assets/blocked-test.cmd)

预期：MarkdownViewer 显示安全阻止提示，不执行脚本。

### 3.4 不存在目标

[打开不存在文件](sample-assets/not-found.pdf)

预期：显示“链接目标不存在”提示。

## 4. 外部链接

[打开 MarkdownViewer GitHub 项目](https://github.com/leojulian/MarkdownViewer)

预期：使用系统默认浏览器打开。

## 验证完成位置

如果页内锚点工作正常，“跳转到验证完成位置”会滚动到这里。
