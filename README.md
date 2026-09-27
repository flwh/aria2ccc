# aria-gui

基于 [aria2c](https://aria2.github.io/) 的 Windows 桌面下载器：C# WinForms 单文件应用（零第三方依赖）+ 浏览器嗅探扩展（Manifest V3）。

## 功能

- 内嵌 aria2c，多协议下载：HTTP / HTTPS / FTP / BitTorrent / 磁力链接
- m3u8（HLS）视频下载：解析清单 → 分片批量下载 → 自动合并为单文件
- 浏览器扩展嗅探：抓取页面媒体直链，单链接预填「添加下载」、多链接批量确认
- 任务管理：开始 / 暂停 / 继续 / 删除，并发数与分片数控制、全局限速
- 下载历史记录、Trackers 列表维护（内置常用 tracker）
- RPC 服务开关（供 AriaNg 等外部工具连接，仅监听本机）
- 网络代理（http / https / socks5）
- 无边框浅色主题界面，自带应用图标

## 构建

无需安装 .NET SDK，使用系统自带 `csc.exe` 编译（.NET Framework 4.x）：

```
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

产物：根目录 `aria-gui.exe`（aria2c 已作为嵌入资源打包，单文件分发）。

运行测试（156 项，含本地 HTTP 服务器 / 代理服务器的 E2E 用例）：

```
powershell -ExecutionPolicy Bypass -File build\run-tests.ps1
```

打包浏览器扩展分发 zip（从 manifest 读取版本号，输出根目录 `aria-gui-extension-v<版本>.zip`，可上传商店或解压后加载）：

```
powershell -ExecutionPolicy Bypass -File build\pack-extension.ps1
```

GitHub Actions：推送到 GitHub 后在 Linux 环境自动编译并上传 `aria-gui.exe` 为 artifact（工作流 `.github/workflows/linux-build.yml`；基于 net481 参考程序集，无需 Windows 即可交叉编译）。

## 使用

1. 运行 `aria-gui.exe`（便携式：配置保存在 exe 同目录 `config\config.json`）
2. 点击「添加下载」粘贴链接开始下载；m3u8 链接自动按流媒体处理
3. 浏览器扩展：打开 `chrome://extensions` → 开启开发者模式 → 「加载已解压的扩展程序」→ 选择 `browser-extension` 目录

## 目录结构

```
src/                C# 源码
  Engine/           下载引擎（Manager / Parser / M3u8 / Sniffer / Embed / DownloadTask）
  UI/               WinForms 界面（MainForm / TaskList / Dialogs / SettingsView / ...）
browser-extension/  MV3 浏览器嗅探扩展
tests/              测试（MiniHttpServer / MiniProxyServer / ParserTests）
build/              构建与测试脚本、E2E 验证截图
assets/             应用图标与嵌入的 aria2c.exe
docs/               设计与实施文档
```

## 说明

- 编译目标为 .NET Framework 4.x（C# 5 语法），界面与引擎均零第三方依赖
- aria2c 版本 1.37.0，作为嵌入资源随 exe 打包，运行时释放到临时目录调用
- 浏览器扩展为纯 JavaScript（无构建步骤），通过 `http://127.0.0.1:6866` 与下载器通信（需先启动 aria-gui）

## 许可证

- 本项目自身代码：[MIT](LICENSE)
- 捆绑的第三方组件 `aria2c.exe`（aria2，GPL-2.0-or-later）：见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
