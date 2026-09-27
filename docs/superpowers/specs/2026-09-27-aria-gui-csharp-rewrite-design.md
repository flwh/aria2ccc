# aria-gui → C# WinForms (csc 可编译) 重构设计

- 日期：2026-09-27
- 状态：已与用户确认（方案 A：WinForms + 系统自带 csc）
- 前置决策：原地替换，直接删除全部 Go 源码（无备份、无 git 仓库，用户已明确确认）

## 1. 背景与目标

现有项目为 Go 1.27 + Fyne 的 Windows 下载器 GUI，以子进程方式驱动内嵌的 aria2c.exe。
本次将其完整重构为 **C# WinForms** 程序，要求：

- 使用系统自带的 `csc.exe`（.NET Framework 4.8.1）**单条命令即可编译**，不安装任何 SDK/依赖
- 保持**单文件 exe**：aria2c.exe 以 `/resource:` 内嵌，运行时释放到临时目录
- 与 Go 版**不要求对齐或兼容**（2026-09-27 用户确认）：功能完整、行为与文案以本 spec 为准；数据文件（`config.json`/`history.json`）为 C# 版自有格式
- 重新编译产生的 exe 功能完整（下载/暂停/继续/删除/历史/设置）

非目标：不新增功能；不做托盘图标、任务持久化恢复、JSON-RPC、多语言、主题。

## 2. 实测环境约束（已验证）

| 项目 | 结论 |
|---|---|
| 可用编译器 | 仅 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（4.8.9232.0）；无 dotnet SDK、无 VS |
| 语言级别 | **C# 5**：字符串插值、自动属性初始化器等 C# 6+ 语法报错 CS1056/CS1519（实测） |
| WinForms | `/r:System.Windows.Forms.dll /r:System.Drawing.dll` 编译通过（实测） |
| 资源内嵌 | `/resource:path,aria2c.exe` + `Assembly.GetManifestResourceStream` 可读，len=5649408（实测） |
| 中文源码 | 无 BOM 的 UTF-8 源码默认即可正确解析；`/codepage:65001` 亦受支持（实测）。build.ps1 统一加 `/codepage:65001` |
| 行分隔 | `OutputDataReceived` 将独立的 `\r` 与 `\n`、`\r\n` 均正确分行（实测 `[L1][L2][L3][L4]`），可直接解析 aria2c 的 `\r` 进度行 |

## 3. 方案决策记录

- **A. WinForms + 系统 csc（选定）**：零安装、全链路实测通过、代码量可控。
- B. WPF 纯代码：被否——无设计器搭建复杂易错，仍受 C# 5 限制，收益有限。
- C. 安装 SDK 用 Roslyn/现代语法：被否——违背"开箱可编译"初衷。

## 4. 目录结构与文件清单

重构后：

```
aria-gui/
├─ build/
│  ├─ build.ps1        # csc 编译 → 根目录 aria-gui.exe
│  ├─ run-tests.ps1    # 编译并运行解析器自测
│  └─ get_aria2c.ps1   # 修改：下载目标改为 assets/aria2c/
├─ assets/aria2c/aria2c.exe          # 内嵌资源（由 internal/engine/aria2c/ 移入）
├─ src/
│  ├─ Program.cs       # namespace AriaGui：入口
│  ├─ Json.cs          # namespace AriaGui：迷你 JSON 读写器
│  ├─ Config.cs        # namespace AriaGui：配置持久化
│  ├─ History.cs       # namespace AriaGui：历史持久化
│  ├─ Engine/
│  │  ├─ Parser.cs         # namespace AriaGui.Engine：进度解析 + 格式化（无任何 UI/IO 依赖）
│  │  ├─ DownloadTask.cs   # TaskStatus 枚举 + DownloadTask 类
│  │  ├─ Embed.cs          # 资源释放（sha256）
│  │  └─ Manager.cs        # 任务调度器
│  └─ UI/
│     ├─ MainForm.cs       # namespace AriaGui.UI：主窗口
│     ├─ TaskListPanel.cs  # 任务列表容器
│     ├─ TaskRow.cs        # 单任务行控件
│     ├─ AddDialogForm.cs  # 添加下载对话框
│     ├─ HistoryView.cs    # 历史页
│     └─ SettingsView.cs   # 设置页
├─ tests/ParserTests.cs    # namespace AriaGui.Tests：console 自测程序
├─ docs/superpowers/specs/2026-09-27-aria-gui-csharp-rewrite-design.md
├─ .gitignore              # 更新
└─ aria-gui.exe            # 构建产物（根目录，不提交）
```

**删除清单**（实施时逐一删除）：
`main.go`、`internal/config/config.go`、`internal/engine/{embed,manager,parser,parser_test,task,proc_windows}.go`、
`internal/history/store.go`、`ui/{window,task_list,toolbar,add_dialog,history,settings}.go`、
`go.mod`、`go.sum`、`internal/`（空目录）、`ui/`（空目录）、根目录 `aria2c.exe`（与 assets 副本同哈希 BE2099C2…）。

**保留并修改**：`build/get_aria2c.ps1`（`$dest` 指向 `assets\aria2c`）；`.gitignore`（去掉 Go 条目，加 `build/*.exe`）。

## 5. 编码规范

- **C# 5 语法**：禁止字符串插值 `$"..."`、`?.`、`nameof`、表达式体成员、自动属性初始化、`out var`、模式匹配、元组
- 数字/时间格式化一律显式 `CultureInfo.InvariantCulture`
- 类名/文件名 PascalCase；私有字段 `_camelCase`；中文注释
- 单文件 100~250 行；`Parser.cs` 不得引用 WinForms（供自测独立编译）

## 6. 模块设计

### 6.1 Json.cs（迷你 JSON）

- `JsonObject`：内部 `List<KeyValuePair<string, object>>` 保插入序
- 读：`JsonObject ParseObject(string)`、`List<JsonObject> ParseArrayOfObjects(string)`；支持 BOM 容忍、`\" \\ \/ \b \f \n \r \t \uXXXX` 转义、整数/浮点/bool/null；未知键与类型错误由调用方容错
- 写：对象 2 空格缩进；字符串仅转义 `"`、`\` 与 <0x20 控制字符（`\uXXXX`），**CJK 原样输出**；`long` 无文化格式输出；UTF-8 **无 BOM**、无尾换行（自有稳定格式，不与 Go 版互读）
- 由本模块自含实现，不引入 `JavaScriptSerializer`/`DataContractJsonSerializer`：输出可控（CJK 原样）且无额外程序集依赖

### 6.2 Config.cs

- 路径：`%AppData%\aria-gui\config.json`（`SpecialFolder.ApplicationData`）
- 默认值：`save_dir` = `%UserProfile%\Downloads`（取不到时 `"."`）；`max_concurrent`=3；`speed_limit`=""; `split`=4
- 键名/键序：`save_dir`, `max_concurrent`, `speed_limit`, `split`
- `Load()`：文件缺失/损坏全回退默认；`max_concurrent` 钳制 [1,10]；`split` <1 则置 1；`speed_limit` 非法则置 ""
- `ValidSpeedLimit`：空串或 `^\d+(\.\d+)?[KMGkmg]?$`
- `Save()`：目录不存在先创建；失败向上抛

### 6.3 History.cs

- 路径：`%AppData%\aria-gui\history.json`
- `HistoryEntry`：`url`, `name`, `dir`, `size`(long), `status`("已完成"/"失败"), `at`
- 上限 200，`Add` 头插、超限截尾、立即落盘；`All()` 返回副本；`Clear()` 置空落盘；内部加锁
- `at` 写出用 `DateTime.ToString("o")`（RFC3339，带本地时区偏移）；读入 `DateTime.Parse` + `RoundtripKind`（C# 版自有格式）

### 6.4 Engine/Parser.cs

解析 aria2c 进度输出行（正则与规则）：

- 正则（C# `Regex` 等价）：
  - 进度行 `^\[#([0-9a-fA-F]+)\s+(.*)\]\s*$`
  - 大小对 `^([\d.]+[KMGT]?i?B)/(?:([\d.]+[KMGT]?i?B)|\?\?|--)`
  - 百分比 `\((\d+)%\)`；速度 `(?:DL|SPD):([\d.]+[KMGT]?i?B)(?:/s)?`；ETA `ETA:(\S+)`
- `ParseProgressLine` 返回 `bool`+`ProgressUpdate{SeedID,Completed,Total,Percent,Speed,ETA}`；必须命中大小对才 `ok=true`；ETA 缺省 -1
- `ParseSize` 单位匹配顺序（不可乱）：TiB, GiB, MiB, KiB, TB, GB, MB, KB, B
- `ParseEta`：`(\d+)([dhms])` 全部匹配累加；`""`/`n/a`/含 `--` → 未知
- `FormatSize`：1024 进制；<1024 输出 `{n}B`；否则 `"0.0"` 一位小数 + KiB/MiB/GiB/TiB
- `FormatEta`：<0 → `"--"`；`%dd%dh` / `%dh%dm` / `%dm%ds` / `%ds`

### 6.5 Engine/DownloadTask.cs

- `TaskStatus`：Queued/Running/Paused/Completed/Failed/Removed，`ToText()` 文案：排队中/下载中/已暂停/已完成/失败/已删除/未知
- `DownloadTask` 字段：`Id`(格式 `t0001`)、`Url`、`Dir`、`Output`、`Status`、`Completed`、`Total`、`Speed`、`Eta`、`Percent`、`Error`、`CreatedAt`、`FinishedAt`；内部调度字段 `_proc`、`PauseRequested`、`RemoveRequested`
- `DisplayName`：`Output` > 磁力（`"磁力链接 "` + 尾 40 字符加 `"..."`）> URL 去 `?`/`#` 后取路径尾段（异常回退尾 50 字符）

### 6.6 Engine/Embed.cs

- `Extract()`：读取资源 `"aria2c.exe"`（按 `GetManifestResourceNames()` 精确匹配；失败报 `"读取嵌入的 aria2c 失败: ..."`）→ 释放到 `%TEMP%\aria-gui\aria2c.exe`（建目录失败报 `"创建临时目录失败: ..."`）→ 已存在且 sha256 相同则复用，否则写盘（失败报 `"写出 aria2c 失败: ..."`）→ 静态缓存路径
- 错误文案由启动路径 MessageBox 展示

### 6.7 Engine/Manager.cs（调度器）

线程模型：单把 `lock` 保护任务表（`Dictionary` + `order List` + `Queue`），每任务 `Task.Run` 一个工作线程，状态与进度变更后向订阅者发事件（**在后台线程触发**，订阅方自行封送 UI 线程）。

- `AddTask(url, dir, output)`：Trim；空 URL → 错 `"URL 不能为空"`；无 `://` 且非 `magnet:` 前缀、非 `.torrent` 后缀 → 错 `"无法识别的下载地址: {url}"`；`dir` 空则用 `cfg.SaveDir`；建任务（Queued、Eta=-1、CreatedAt=now）、入 `order`+队列 → Pump
- `Start(id)`：仅 Queued/Running 忽略；否则置 Queued、清 Pause/Remove 标志、入队、Pump
- `Pause(id)`：Queued → 出队置 Paused 发事件；Running → `PauseRequested=true` + Kill（保留 `.aria2` 控制文件）
- `Remove(id)`：Running → `RemoveRequested=true` + Kill；Queued → 出队置 Removed 发事件；其他 → 置 Removed 发事件。**注意：不清理 `.aria2` 文件**
- `Snapshot()`：按 `order` 顺序跳过 Removed，返回副本
- `TotalSpeed()`：Running 任务 Speed 求和
- `Pump()`（锁内）：`running < max(1, cfg.MaxConcurrent)` 且队列非空 → 出队有效 Queued → `running++`、后台启动
- `buildArgs` 与 Go 完全一致：
  `{url}`、`--dir=`、`--continue=true`、`--summary-interval=0`、`--auto-save-interval=10`、`--console-log-level=warn`、`--split={Split}`、`--max-connection-per-server={Split}`、`--max-concurrent-downloads={Split}`、`--check-integrity=true`；`Output` 非空加 `--out=`；限速非空加 `--max-overall-download-limit=`；磁力/`.torrent` 追加 `--save-session=aria2.session`、`--bt-metadata-only=false`、`--seed-ratio=0`、`--bt-stop-time-limit=0`、`--follow-torrent=mem`
- 进程启动：`UseShellExecute=false`、`CreateNoWindow=true`（aria2c 为 console 程序，不弹窗）；参数拼接需手写 `QuoteArg`（含空格/制表/引号才加引号，内部按 `\"` 规则转义；4.8 无 `ArgumentList`）
- 输出处理：`OutputDataReceived` + `ErrorDataReceived`（`BeginOutputReadLine`/`BeginErrorReadLine`）；进度行 → `ApplyProgress`；非进度且命中 `looksLikeError`（关键词 error/failed/exception/无法/拒绝/unreachable/timeout，小写包含判断）且非空 → 收集错误尾（最多保留**最后** 5 行）
- 退出判定：`removeRequested` → Removed；`pauseRequested` → Paused；`ExitCode != 0` → `Error` 为错误尾拼接（`"\n"`）或 `"exit code {N}"` → Failed + 归档"失败"；否则 `Total>0` 时 `Completed=Total, Percent=100` → Completed + 归档"已完成"
- `ApplyProgress`：更新 Completed；`Total>0` 才更新；`Percent>0` 用之，否则按已/总计算；更新 Speed/Eta；发事件
- 归档：`hist.Add(url, DisplayName, dir, Completed, 状态文案)`
- 关闭行为：主窗口关闭只退订事件后退出进程，**不强杀 aria2c 子进程**（父进程退出后子进程继续）

### 6.8 Program.cs

- `[STAThread] Main`：`EnableVisualStyles` + `SetCompatibleTextRenderingDefault(false)`
- `Config.Load()` → `History.Load()` → `new Manager(...)`；Manager 构造失败 → `MessageBox.Show(错误消息)` → 退出码 1
- 成功 → `Application.Run(new MainForm(mgr, cfg, hist))`

### 6.9 UI 层

**MainForm**
- 标题 `"Aria 下载器"`；客户区 820×560；启动居中
- 顶部 `ToolStrip`：`"添加下载"`、`"打开目录"`（`Process.Start("explorer.exe", SaveDir)`）；无图标（文字按钮）
- `TabControl` 三页：`"下载"`/`"历史"`/`"设置"`；切到历史页时刷新
- 底部 `StatusStrip` + Label，初值 `"就绪"`；`System.Windows.Forms.Timer` 每秒：若 `now - notifyAt < 5s` 跳过；速度>0 → `"下载中 {count} 个任务   合计 {size}/s"`（注意 3 空格）否则 `"就绪"`
- `Notify(text)`：记 `notifyAt`，设状态栏文本
- 窗体关闭：退订 Manager 事件后退出（不强杀子进程，见 §6.7）

**TaskListPanel + TaskRow**
- 容器：自定义双缓冲滚动 Panel，纵向手动布局行控件（行宽=容器可视宽）；订阅 Manager 事件，事件线程 `BeginInvoke` 到 UI 线程
- 增量更新：事件到达 → 按任务 ID 查行（`Dictionary<id, TaskRow>`），无则新建追加；`Update(task)` 刷新文本/进度/按钮；Removed 任务删行并重排 Y 坐标
- 行内容：名称（省略号）、状态（右对齐）；`ProgressBar` 0–100 = `(int)Percent`；
  信息行：`Total>0` → `"{已完成} / {总}   {速度}/s   剩余 {ETA}"`；否则 `"已下载 {已完成}   {速度}/s（总大小未知，BT 元数据获取中…）"`；Failed 且 Error 非空 → `"错误: " + 首行`
- 按钮规则：
  - Running：开始隐藏，暂停显示
  - Queued：开始=`"排队中"`禁用，暂停显示
  - Completed：开始、暂停均隐藏
  - Paused/Failed：暂停隐藏，开始显示并启用，文本=`"继续"`（非 Paused/Failed 时为 `"开始"`）
  - 删除按钮始终显示
- 行高固定约 80px，纵向排列：名称+右侧状态 / 进度条 / 信息+右侧按钮
- 通知跃迁：进入 Completed → `Notify("✔ {名称} 已完成")`；进入 Failed → `Notify("✖ {名称} 失败")`（用 `prev` 字典检测状态跃迁）

**AddDialogForm**（模态）
- 标题 `"添加下载"`；字段 `"下载链接"`（占位 `"HTTP/FTP 链接、磁力链接(magnet:...) 或 .torrent 地址"`）、`"保存目录"`（默认 `cfg.SaveDir` + `"浏览…"` 按钮，`FolderBrowserDialog`）、`"文件名"`（占位 `"可选，默认自动命名"`）
- 按钮 `"开始下载"`/`"取消"`；确认 → `AddTask`；异常 `MessageBox` 展示错误消息；成功 → `Notify("已添加任务: {名称}")`

**SettingsView**
- 字段：`"默认保存目录"`(+浏览)、`"并发下载数(1-10)"`、`"全局限速"`（占位 `"留空不限速，如 512K / 2M"`）、`"每任务分片数"`；按钮 `"保存设置"`
- 校验（失败 MessageBox 原文）：并发非 1–10 → `"并发下载数需为 1-10 的整数"`；分片非 1–16 → `"分片数需为 1-16 的整数"`；限速非法 → `"限速格式如 512K / 2M / 1G，或留空"`
- 成功：写入 cfg 并 `Save()`（失败 MessageBox）→ `Notify("设置已保存（限速与分片数对新任务生效）")`

**HistoryView**
- 列表行：`"[{状态}] {名称}"`；次行 `"{大小}   {时间}   {目录}"`，时间格式 `yyyy-MM-dd HH:mm:ss`
- 按钮 `"清空历史"` → 确认框 `"清空历史"`/`"确定删除全部历史记录？"` → `Clear()` + 刷新

## 7. 构建与测试脚本

**build/build.ps1**
1. 定位 csc：Framework64 → Framework32 → 均无则报错退出
2. 检查 `assets\aria2c\aria2c.exe` 存在，否则提示先运行 `get_aria2c.ps1`
3. `Get-ChildItem src -Recurse -Filter *.cs` 收集全部源文件
4. 编译：
   `csc /nologo /codepage:65001 /target:winexe /optimize+ /out:"<root>\aria-gui.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /resource:"<root>\assets\aria2c\aria2c.exe",aria2c.exe <所有 .cs>`
5. 成功打印产物路径+大小；失败退出码 1

**build/run-tests.ps1**
- `csc /nologo /codepage:65001 /target:exe /out:build\parser_tests.exe src\Engine\Parser.cs tests\ParserTests.cs` → 运行 → 透传退出码

**tests/ParserTests.cs**
- 25 个用例：进度行 6、ParseSize 8、FormatSize 5、FormatEta 6
- 逐用例打印 PASS/FAIL（失败含期望/实际），汇总 `{N} passed, {M} failed`，`M>0` 时退出码 1

## 8. 验收清单（Definition of Done）

1. `build/build.ps1` 编译成功，根目录产出 `aria-gui.exe`（约 5.5MB，含内嵌 aria2c）
2. `build/run-tests.ps1` 25/25 通过，退出码 0
3. 启动 exe：窗口出现（820×560 居中、标题"Aria 下载器"），无控制台窗口
4. 真实 URL 冒烟：添加 → 行出现且进度/速度/ETA 更新 → 暂停（"已暂停"）→ 继续（进度续涨）→ 删除（行消失）
5. 完成/失败任务进历史页，显示 `[已完成]/[失败]`；清空历史生效
6. 设置修改后重启保留；`config.json` 为合法 JSON（键名 snake_case、2 空格缩进、UTF-8 无 BOM）
7. `history.json` 读写往返正常（重启后历史保留、清空生效）
8. 全部 Go 源码、`go.mod`、`go.sum`、根目录 `aria2c.exe` 已删除；`.gitignore` 已更新
9. 已删除文件清单见 §4；无残留 `.go` 文件（`Get-ChildItem -Recurse *.go` 为空）

## 9. 风险与开放项

- C# 5 语法误用 → 编译期直接暴露，实施时高频编译即可
- 中文源码编码、`\r` 分行、资源内嵌 → **均已实测验证**（§2）
- `QuoteArg` 为手写参数引用 → 实施时以含空格的保存目录、带 `&`/`%` 的 URL 验证
- Go 源码删除不可逆 → 用户已明确确认"直接删除"
- 磁力/种子分支默认不做真实下载冒烟（网络依赖），以构建参数断言 + 人工酌情验证为准
