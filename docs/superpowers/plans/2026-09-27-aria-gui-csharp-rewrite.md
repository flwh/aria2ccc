# aria-gui C# WinForms 重构实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Go/Fyne 版 aria-gui 完整重构为 C# WinForms 程序，用系统自带 csc.exe 一条命令编译出单文件（内嵌 aria2c）的 aria-gui.exe。

**Architecture:** WinForms 纯代码界面（无设计器文件）+ 纯逻辑层（Config/History/Json/Parser/DownloadTask/Embed/Manager）。aria2c.exe 以 `/resource:` 内嵌，运行时经 sha256 校验释放到 `%TEMP%\aria-gui\`；任务调度采用"每任务一个 aria2c 子进程、暂停=杀进程保留 .aria2 控制文件"的语义。

**Tech Stack:** C# 5（.NET Framework 4.8.1 自带 csc，无 SDK、无 NuGet）、WinForms、PowerShell 构建脚本。

**Spec:** `docs/superpowers/specs/2026-09-27-aria-gui-csharp-rewrite-design.md`（执行者必读；本计划中的所有文案/行为以 spec 为准）

## Global Constraints

- 编译器：仅使用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（不存在时回退 `Framework32` 同路径）；禁止安装任何 SDK/包
- **C# 5 语法**：禁止字符串插值 `$"..."`、`?.`、`nameof`、表达式体成员（`=>` 方法/属性）、自动属性初始化器、`out var`、模式匹配（`is T x`）、元组。允许：`var`、lambda、匿名方法、对象/集合初始化器、自动属性（无初始化器）、泛型、LINQ
- 数字/日期格式化一律显式 `CultureInfo.InvariantCulture`
- 源文件一律 UTF-8（无 BOM）；所有 csc 调用带 `/codepage:65001`
- 内嵌资源名精确为 `aria2c.exe`（`/resource:<path>,aria2c.exe`）
- **项目不是 git 仓库**：执行者不得运行任何 git 命令；每个任务以"运行验证"步骤代替提交步骤
- 运行脚本统一用：`powershell -NoProfile -ExecutionPolicy Bypass -File <脚本路径>`
- 所有用户可见文案必须与 spec §6 逐字一致（含空格数量、✔/✖ 符号、全角括号）
- Go 源码在 Task 12 前**不得删除**（删除不可逆，由 Task 12 统一执行）；根目录 `aria2c.exe` 同理
- 与 Go 版**不要求对齐或兼容**（2026-09-27 用户确认）：行为与文案以 spec 为准；本计划中"与 Go 版一致/对齐/移植"等字样仅为来历说明，不构成约束；数据文件不与 Go 版互读

## 文件结构总览（终态）

| 文件 | 职责 |
|---|---|
| `build/build.ps1` | 定位 csc，全量编译 src（含 UI）→ 根目录 `aria-gui.exe`（内嵌资源） |
| `build/run-tests.ps1` | 编译"纯逻辑源文件 + tests" → 运行自测 exe，透传退出码 |
| `build/get_aria2c.ps1` | 修改：下载 aria2c 到 `assets/aria2c/` |
| `assets/aria2c/aria2c.exe` | 内嵌资源源文件（由 `internal/engine/aria2c/` 复制而来） |
| `src/Program.cs` | 入口（Task 1 先建桩，Task 11 替换为真实实现） |
| `src/Json.cs` | 迷你 JSON 读写（C# 版自有格式） |
| `src/Config.cs` | 配置持久化 + 校验 |
| `src/History.cs` | 历史持久化 |
| `src/Engine/Parser.cs` | 进度行解析 + FormatSize/FormatEta（无 UI/IO 依赖） |
| `src/Engine/DownloadTask.cs` | TaskStatus + DownloadTask + DisplayName |
| `src/Engine/Embed.cs` | 资源释放（sha256 复用） |
| `src/Engine/Manager.cs` | 调度器（并发/进程/事件/BuildArgs/QuoteArg） |
| `src/UI/Win32.cs` | EM_SETCUEBANNER 占位符辅助（spec 文件清单外的补充） |
| `src/UI/TaskRow.cs` | 单任务行控件 |
| `src/UI/TaskListPanel.cs` | 任务列表容器（订阅事件、增量更新） |
| `src/UI/AddDialogForm.cs` | 添加下载对话框 |
| `src/UI/SettingsView.cs` | 设置页 |
| `src/UI/HistoryView.cs` | 历史页 |
| `src/UI/MainForm.cs` | 主窗口（工具栏 + 三 Tab + 状态栏 + 1s 定时器） |
| `tests/ParserTests.cs` | 自测程序（console，Main 返回失败数）；各任务**增量追加**测试节 |

---

### Task 1: 脚手架（目录 / 构建脚本 / 资源 / .gitignore / Program 桩）

**Files:**
- Create: `build/build.ps1`、`build/run-tests.ps1`、`src/Program.cs`（桩）
- Create: `assets/aria2c/aria2c.exe`（复制自 `internal/engine/aria2c/aria2c.exe`）
- Modify: `build/get_aria2c.ps1`（目标目录）、`.gitignore`

**Interfaces:**
- Produces: 可用的 `build.ps1`（后续每个任务用它验证编译）；`Program.cs` 桩（Task 11 替换）
- 说明：本任务不写任何业务代码；验证"编译链路 + 资源内嵌"成立

- [ ] **Step 1: 复制 aria2c 资源**

```powershell
New-Item -ItemType Directory -Force -Path k:\vibecode\sys\aria-gui\assets\aria2c | Out-Null
Copy-Item k:\vibecode\sys\aria-gui\internal\engine\aria2c\aria2c.exe k:\vibecode\sys\aria-gui\assets\aria2c\aria2c.exe
(Get-FileHash k:\vibecode\sys\aria-gui\assets\aria2c\aria2c.exe).Hash
```
预期哈希：`BE2099C214F63A3CB4954B09A0BECD6E2E34660B886D4C898D260FEBFE9D70C2`

- [ ] **Step 2: 创建 `src/Program.cs`（桩）**

```csharp
using System;

namespace AriaGui
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 脚手架桩：Task 11 替换为真实入口（Config/History/Manager/MainForm）
        }
    }
}
```

- [ ] **Step 3: 创建 `build/build.ps1`**

```powershell
# 编译 aria-gui：定位系统自带 csc，产出根目录 aria-gui.exe（内嵌 aria2c.exe）。
# 用法: powershell -NoProfile -ExecutionPolicy Bypass -File build\build.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    Write-Error "未找到系统自带的 csc.exe（需要 .NET Framework 4.x）"
    exit 1
}

$res = Join-Path $root "assets\aria2c\aria2c.exe"
if (-not (Test-Path $res)) {
    Write-Error "缺少 $res，请先运行 build\get_aria2c.ps1"
    exit 1
}

# @() 强制数组：管道单文件输出会被解包为标量字符串，导致 @srcs splat 逐字符拆分
$srcs = @(Get-ChildItem (Join-Path $root "src") -Recurse -Filter *.cs -File | ForEach-Object { $_.FullName })

$out = Join-Path $root "aria-gui.exe"
& $csc /nologo /codepage:65001 /target:winexe /optimize+ "/out:$out" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    "/resource:$res,aria2c.exe" @srcs
if ($LASTEXITCODE -ne 0) {
    Write-Error "编译失败（csc 退出码 $LASTEXITCODE）"
    exit 1
}

$item = Get-Item $out
Write-Host ("生成 {0}（{1:N0} 字节）" -f $item.FullName, $item.Length)
```

- [ ] **Step 4: 创建 `build/run-tests.ps1`**

```powershell
# 编译并运行自测（纯逻辑模块 + tests/ParserTests.cs 的 Main）。
# 用法: powershell -NoProfile -ExecutionPolicy Bypass -File build\run-tests.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    Write-Error "未找到系统自带的 csc.exe"
    exit 1
}

# 注意：排除 Program.cs（Main 冲突）与 src\UI（WinForms 不可测）；Engine 单独一层递归。
$srcs = @()
$srcs += Get-ChildItem (Join-Path $root "src") -Filter *.cs -File |
    Where-Object { $_.Name -ne "Program.cs" } | ForEach-Object { $_.FullName }
if (Test-Path (Join-Path $root "src\Engine")) {
    $srcs += Get-ChildItem (Join-Path $root "src\Engine") -Filter *.cs -File | ForEach-Object { $_.FullName }
}
$srcs += Get-ChildItem (Join-Path $root "tests") -Filter *.cs -File | ForEach-Object { $_.FullName }

$out = Join-Path $root "build\aria-gui-tests.exe"
& $csc /nologo /codepage:65001 /target:exe "/out:$out" @srcs
if ($LASTEXITCODE -ne 0) {
    Write-Error "测试程序编译失败（csc 退出码 $LASTEXITCODE）"
    exit 1
}

& $out
exit $LASTEXITCODE
```

- [ ] **Step 5: 修改 `build/get_aria2c.ps1`**

两处修改（其余不动）：
- 第 1 行注释改为：`# 获取 aria2c.exe 到 assets/aria2c/ 供 csc /resource: 内嵌使用。`
- 第 9 行改为：`$dest = Join-Path $root "assets\aria2c"`

- [ ] **Step 6: 重写 `.gitignore`**

```
# 构建产物（仓库根目录与 build 目录的 exe）
/*.exe
build/*.exe

# 运行时产物
aria2.session
*.aria2
```

- [ ] **Step 7: 将构建脚本转为 UTF-8 带 BOM（PS 5.1 中文兼容）**

Windows PowerShell 5.1 读取无 BOM 的 UTF-8 脚本时按 ANSI(GBK) 解码，脚本内中文提示会乱码；`get_aria2c.ps1` 原文件即带 BOM。对新生成的 `build.ps1`、`run-tests.ps1` 各转换一次（内容不变，仅编码加 BOM）：

```powershell
foreach ($p in @("k:\vibecode\sys\aria-gui\build\build.ps1", "k:\vibecode\sys\aria-gui\build\run-tests.ps1")) {
    $t = [System.IO.File]::ReadAllText($p, (New-Object System.Text.UTF8Encoding $false))
    [System.IO.File]::WriteAllText($p, $t, (New-Object System.Text.UTF8Encoding $true))
}
```

备注：C# 源码保持 UTF-8 **无 BOM**（由 csc `/codepage:65001` 保证），不要对 `.cs` 做此转换。

- [ ] **Step 8: 验证编译链路**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\build.ps1`
Expected：输出 `生成 ...aria-gui.exe（5,6xx,xxx 字节）`，退出码 0。**大小必须超过 5MB**（桩代码本身只有几 KB，体积来自内嵌 aria2c，证明 `/resource:` 生效）。

---

### Task 2: Parser（TDD：先测后写）

**Files:**
- Create: `tests/ParserTests.cs`（自测骨架 + 解析器用例）
- Create: `src/Engine/Parser.cs`
- Test: `tests/ParserTests.cs`（编译进 `build\aria-gui-tests.exe` 运行）

**Interfaces:**
- Consumes: 无
- Produces（后续任务依赖的精确签名）:
  - `namespace AriaGui.Engine`、`public struct ProgressUpdate { public string SeedId; public long Completed; public long Total; public double Percent; public long Speed; public int Eta; }`
  - `public static class Parser`：`bool ParseProgressLine(string line, out ProgressUpdate update)`、`string FormatSize(long b)`、`string FormatEta(int sec)`、`internal static bool TryParseSize(string s, out long bytes)`、`internal static bool TryParseEta(string s, out int seconds)`

- [ ] **Step 1: 创建 `tests/ParserTests.cs`（只含解析器测试节）**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using AriaGui.Engine;

namespace AriaGui.Tests
{
    /// 自测程序：Main 返回失败数（0 = 全部通过）。各任务在此增量追加测试节。
    internal static class ParserTests
    {
        private static int _passed;
        private static int _failed;

        private static void Check(string name, bool cond, string detail)
        {
            if (cond)
            {
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name + "  " + detail);
            }
        }

        private static int Main()
        {
            RunProgressLineCases();
            RunParseSizeCases();
            RunFormatSizeCases();
            RunFormatEtaCases();

            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} passed, {1} failed", _passed, _failed));
            return _failed == 0 ? 0 : 1;
        }

        private static void CheckProgress(string name, string line, bool wantOk,
            long completed, long total, double percent, long speed, int eta)
        {
            ProgressUpdate u;
            bool ok = Parser.ParseProgressLine(line, out u);
            if (ok != wantOk)
            {
                Check(name, false, "ok=" + ok + " want=" + wantOk);
                return;
            }
            if (!ok)
            {
                Check(name, true, "");
                return;
            }
            List<string> errs = new List<string>();
            if (u.Completed != completed) errs.Add("Completed=" + u.Completed + " want=" + completed);
            if (u.Total != total) errs.Add("Total=" + u.Total + " want=" + total);
            if (u.Percent != percent) errs.Add("Percent=" + u.Percent + " want=" + percent);
            if (u.Speed != speed) errs.Add("Speed=" + u.Speed + " want=" + speed);
            if (u.Eta != eta) errs.Add("Eta=" + u.Eta + " want=" + eta);
            Check(name, errs.Count == 0, string.Join("; ", errs.ToArray()));
        }

        private static void RunProgressLineCases()
        {
            CheckProgress("普通 HTTP 下载",
                "[#0a1b2c 1.2MiB/5.0MiB(24%) CN:2 DL:1.1MiB ETA:3s]",
                true, 1258291L, 5242880L, 24, 1153433L, 3);
            CheckProgress("起始 0%",
                "[#0a1b2c 0B/100MiB(0%) CN:1 DL:0B]",
                true, 0L, 104857600L, 0, 0L, -1);
            CheckProgress("BT 元数据未就绪（总大小未知）",
                "[#0a1b2c 976.0KiB/??(?) CN:3 DL:976.0KiB ETA:n/a]",
                true, 999424L, 0L, 0, 999424L, -1);
            CheckProgress("长 ETA 复合格式",
                "[#ff00 1.0GiB/50.0GiB(2%) CN:5 DL:2.5MiB ETA:1h2m3s]",
                true, 1073741824L, 53687091200L, 2, 2621440L, 3723);
            CheckProgress("非进度行-日志",
                "2026-09-27 12:00:00 NOTICE: Download completed:",
                false, 0L, 0L, 0, 0L, 0);
            CheckProgress("非进度行-空",
                "", false, 0L, 0L, 0, 0L, 0);
        }

        private static void CheckSize(string input, long want)
        {
            long got;
            bool ok = Parser.TryParseSize(input, out got);
            Check("parseSize(" + input + ")", ok && got == want,
                "got=" + got + " ok=" + ok + " want=" + want);
        }

        private static void RunParseSizeCases()
        {
            CheckSize("0B", 0L);
            CheckSize("512B", 512L);
            CheckSize("1KiB", 1024L);
            CheckSize("976.0KiB", 999424L);
            CheckSize("1.5MiB", 1572864L);
            CheckSize("2GiB", 2147483648L);
            CheckSize("1TiB", 1099511627776L);
            CheckSize("100KB", 100000L);
        }

        private static void CheckFormatSize(long input, string want)
        {
            string got = Parser.FormatSize(input);
            Check("FormatSize(" + input + ")", got == want, "got=" + got + " want=" + want);
        }

        private static void RunFormatSizeCases()
        {
            CheckFormatSize(0L, "0B");
            CheckFormatSize(512L, "512B");
            CheckFormatSize(1024L, "1.0KiB");
            CheckFormatSize(1572864L, "1.5MiB");
            CheckFormatSize(2147483648L, "2.0GiB");
        }

        private static void CheckFormatEta(int input, string want)
        {
            string got = Parser.FormatEta(input);
            Check("FormatEta(" + input + ")", got == want, "got=" + got + " want=" + want);
        }

        private static void RunFormatEtaCases()
        {
            CheckFormatEta(-1, "--");
            CheckFormatEta(0, "0s");
            CheckFormatEta(59, "59s");
            CheckFormatEta(60, "1m0s");
            CheckFormatEta(3661, "1h1m");
            CheckFormatEta(90000, "1d1h");
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：FAIL —— `测试程序编译失败`，csc 报 `CS0246/CS0103`（找不到 `Parser`/`ProgressUpdate`）。这正是红灯。

- [ ] **Step 3: 创建 `src/Engine/Parser.cs`**

```csharp
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AriaGui.Engine
{
    /// 一次进度解析结果（对齐 Go 版 ProgressUpdate）。
    public struct ProgressUpdate
    {
        public string SeedId;
        public long Completed;
        public long Total;
        public double Percent;
        public long Speed;
        public int Eta;
    }

    /// aria2c 进度行解析与人类可读格式化（无 UI/IO 依赖，可独立测试）。
    public static class Parser
    {
        private static readonly Regex ProgressLineRe = new Regex(@"^\[#([0-9a-fA-F]+)\s+(.*)\]\s*$");
        private static readonly Regex SizePairRe = new Regex(@"^([\d.]+[KMGT]?i?B)/(?:([\d.]+[KMGT]?i?B)|\?\?|--)");
        private static readonly Regex PercentRe = new Regex(@"\((\d+)%\)");
        private static readonly Regex SpeedRe = new Regex(@"(?:DL|SPD):([\d.]+[KMGT]?i?B)(?:/s)?");
        private static readonly Regex EtaRe = new Regex(@"ETA:(\S+)");
        private static readonly Regex EtaPartsRe = new Regex(@"(\d+)([dhms])");

        // 注意顺序（与 Go 版一致）：长单位在前，避免 "KiB" 被 "B" 抢先匹配。
        private static readonly string[] SizeSuffixes =
            { "TiB", "GiB", "MiB", "KiB", "TB", "GB", "MB", "KB", "B" };
        private static readonly long[] SizeMults =
            { 1099511627776L, 1073741824L, 1048576L, 1024L, 1000000000000L, 1000000000L, 1000000L, 1000L, 1L };

        private static readonly string[] SizeUnits = { "B", "KiB", "MiB", "GiB", "TiB" };

        /// 尝试把 aria2c 输出的一行解析为进度；非进度行返回 false（不视为错误）。
        public static bool ParseProgressLine(string line, out ProgressUpdate update)
        {
            update = new ProgressUpdate();
            line = (line == null ? "" : line).Trim();

            Match m = ProgressLineRe.Match(line);
            if (!m.Success) return false;
            update.SeedId = m.Groups[1].Value;
            string rest = m.Groups[2].Value;
            bool hasSizes = false;

            Match sm = SizePairRe.Match(rest);
            if (sm.Success)
            {
                hasSizes = true;
                long v;
                if (TryParseSize(sm.Groups[1].Value, out v)) update.Completed = v;
                string totalStr = sm.Groups[2].Value;
                if (totalStr != "" && totalStr != "--")
                {
                    if (TryParseSize(totalStr, out v)) update.Total = v;
                }
            }

            Match pm = PercentRe.Match(rest);
            if (pm.Success)
            {
                double d;
                if (double.TryParse(pm.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    update.Percent = d;
            }

            Match dm = SpeedRe.Match(rest);
            if (dm.Success)
            {
                long v;
                if (TryParseSize(dm.Groups[1].Value, out v)) update.Speed = v;
            }

            update.Eta = -1;
            Match em = EtaRe.Match(rest);
            if (em.Success)
            {
                int eta;
                if (TryParseEta(em.Groups[1].Value, out eta)) update.Eta = eta;
            }

            // 至少命中大小字段才算有效进度行（0B/0B(0%) 也是合法起始进度）。
            if (!hasSizes) return false;
            return true;
        }

        /// "1.2MiB" -> 1258291，"976.0KiB" -> 999424，"0B" -> 0
        internal static bool TryParseSize(string s, out long bytes)
        {
            bytes = 0;
            s = (s == null ? "" : s).Trim();
            for (int i = 0; i < SizeSuffixes.Length; i++)
            {
                if (s.EndsWith(SizeSuffixes[i], StringComparison.Ordinal))
                {
                    string num = s.Substring(0, s.Length - SizeSuffixes[i].Length);
                    double f;
                    if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                        return false;
                    bytes = (long)(f * SizeMults[i]);
                    return true;
                }
            }
            return false;
        }

        /// "3s"->3，"1h2m3s"->3723，"2d"->172800；"n/a"/"--:--:--" 等返回 false。
        internal static bool TryParseEta(string s, out int seconds)
        {
            seconds = -1;
            s = (s == null ? "" : s).Trim();
            if (s == "" || s == "n/a" || s.Contains("--")) return false;
            MatchCollection ms = EtaPartsRe.Matches(s);
            if (ms.Count == 0) return false;
            int total = 0;
            foreach (Match g in ms)
            {
                int v;
                if (!int.TryParse(g.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    return false;
                switch (g.Groups[2].Value)
                {
                    case "d": total += v * 86400; break;
                    case "h": total += v * 3600; break;
                    case "m": total += v * 60; break;
                    case "s": total += v; break;
                }
            }
            seconds = total;
            return true;
        }

        /// 字节数 → 人类可读（1024 进制；<1024 输出整数，其余保留一位小数）。
        public static string FormatSize(long b)
        {
            double f = b;
            int i = 0;
            while (f >= 1024 && i < SizeUnits.Length - 1)
            {
                f /= 1024;
                i++;
            }
            if (i == 0) return b.ToString(CultureInfo.InvariantCulture) + SizeUnits[i];
            return f.ToString("0.0", CultureInfo.InvariantCulture) + SizeUnits[i];
        }

        /// 秒数 → 剩余时间（"%dd%dh" / "%dh%dm" / "%dm%ds" / "%ds"；负值 "--"）。
        public static string FormatEta(int sec)
        {
            if (sec < 0) return "--";
            int d = sec / 86400;
            int h = (sec % 86400) / 3600;
            int m = (sec % 3600) / 60;
            int s = sec % 60;
            if (d > 0) return d.ToString(CultureInfo.InvariantCulture) + "d" + h.ToString(CultureInfo.InvariantCulture) + "h";
            if (h > 0) return h.ToString(CultureInfo.InvariantCulture) + "h" + m.ToString(CultureInfo.InvariantCulture) + "m";
            if (m > 0) return m.ToString(CultureInfo.InvariantCulture) + "m" + s.ToString(CultureInfo.InvariantCulture) + "s";
            return s.ToString(CultureInfo.InvariantCulture) + "s";
        }
    }
}
```

- [ ] **Step 4: 运行确认通过**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：`25 passed, 0 failed`，退出码 0（6 进度行 + 8 大小 + 5 FormatSize + 6 FormatEta）。

---

### Task 3: Json（迷你 JSON 读写，TDD）

**Files:**
- Create: `src/Json.cs`
- Modify: `tests/ParserTests.cs`（Main 追加调用 + 新增 `RunJsonCases`）

**Interfaces:**
- Consumes: 无
- Produces:
  - `namespace AriaGui`、`public sealed class JsonObject`：
    `void Set(string key, object value)`、`object Get(string key)`、`string GetString(string key, string def)`、`long GetLong(string key, long def)`、`string ToJson()`、`void WriteTo(StringBuilder sb, int indent)`、
    `static JsonObject ParseObject(string text)`、`static List<JsonObject> ParseObjectArray(string text)`
  - 值类型约定：字符串/`long`/`bool`/`null`/嵌套 `JsonObject`（数组仅解析不写出给业务用）

- [ ] **Step 1: 修改 `tests/ParserTests.cs`**

在 `Main()` 的 `RunFormatEtaCases();` 之后追加一行：

```csharp
            RunJsonCases();
```

并在类内追加方法：

```csharp
        private static void RunJsonCases()
        {
            JsonObject o = new JsonObject();
            o.Set("k", "a\"b\\c\nd");
            o.Set("n", 42L);
            o.Set("b", true);
            o.Set("m", null);
            string json = o.ToJson();
            Check("Json 转义输出",
                json == "{\n  \"k\": \"a\\\"b\\\\c\\nd\",\n  \"n\": 42,\n  \"b\": true,\n  \"m\": null\n}",
                "got=" + json.Replace("\n", "\\n"));

            JsonObject back = JsonObject.ParseObject(json);
            Check("Json 往返-字符串", back.GetString("k", "") == "a\"b\\c\nd", "got=" + back.GetString("k", ""));
            Check("Json 往返-数字", back.GetLong("n", 0) == 42L, "got=" + back.GetLong("n", 0));
            Check("Json 往返-null", back.Get("m") == null && back.GetString("m", "X") == "X", "");

            JsonObject cjk = new JsonObject();
            cjk.Set("dir", "C:\\下载\\目录");
            Check("Json CJK 原样", cjk.ToJson().Contains("C:\\下载\\目录"), "got=" + cjk.ToJson());

            JsonObject bom = JsonObject.ParseObject("\uFEFF{\"a\": 1}");
            Check("Json BOM 容忍", bom.GetLong("a", 0) == 1L, "got=" + bom.GetLong("a", 0));

            JsonObject skip = JsonObject.ParseObject("{\"arr\": [1, 2, {\"b\": true}], \"c\": \"x\"}");
            Check("Json 跳过嵌套值", skip.GetString("c", "") == "x", "got=" + skip.GetString("c", ""));

            List<JsonObject> arr = JsonObject.ParseObjectArray("[\n  {\"a\": 1},\n  {\"a\": 2}\n]");
            Check("Json 对象数组", arr.Count == 2 && arr[1].GetLong("a", 0) == 2L, "count=" + arr.Count);

            Check("Json 空数组", JsonObject.ParseObjectArray("[]").Count == 0, "");

            JsonObject esc = JsonObject.ParseObject("{\"s\": \"\\u4e2d\\u6587\\t\\\"q\\\"\"}");
            Check("Json 转义读取", esc.GetString("s", "") == "中文\t\"q\"", "got=" + esc.GetString("s", ""));
        }
```

- [ ] **Step 2: 运行确认失败**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：FAIL —— 编译报 `CS0246`（找不到 `JsonObject`）。

- [ ] **Step 3: 创建 `src/Json.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AriaGui
{
    /// 保插入序的迷你 JSON 对象（C# 版自有格式）：
    /// 2 空格缩进、CJK 原样（不转义为 \uXXXX）、无尾换行。
    public sealed class JsonObject
    {
        private readonly List<KeyValuePair<string, object>> _items = new List<KeyValuePair<string, object>>();

        public void Set(string key, object value)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Key == key)
                {
                    _items[i] = new KeyValuePair<string, object>(key, value);
                    return;
                }
            }
            _items.Add(new KeyValuePair<string, object>(key, value));
        }

        public object Get(string key)
        {
            foreach (KeyValuePair<string, object> kv in _items)
            {
                if (kv.Key == key) return kv.Value;
            }
            return null;
        }

        public string GetString(string key, string def)
        {
            object v = Get(key);
            if (v is string) return (string)v;
            return def;
        }

        public long GetLong(string key, long def)
        {
            object v = Get(key);
            if (v is long) return (long)v;
            if (v is double) return (long)(double)v;
            return def;
        }

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            WriteTo(sb, 0);
            return sb.ToString();
        }

        /// 供数组序列化复用；indent 为当前嵌套层级。
        public void WriteTo(StringBuilder sb, int indent)
        {
            if (_items.Count == 0) { sb.Append("{}"); return; }
            sb.Append("{\n");
            for (int i = 0; i < _items.Count; i++)
            {
                Indent(sb, indent + 1);
                WriteString(sb, _items[i].Key);
                sb.Append(": ");
                WriteValue(sb, _items[i].Value, indent + 1);
                if (i < _items.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            Indent(sb, indent);
            sb.Append('}');
        }

        public static JsonObject ParseObject(string text)
        {
            JsonReader r = new JsonReader(text == null ? "" : text);
            JsonObject o = r.ReadObject();
            r.SkipWs();
            if (!r.Eof) throw new FormatException("JSON 尾部有多余字符");
            return o;
        }

        public static List<JsonObject> ParseObjectArray(string text)
        {
            JsonReader r = new JsonReader(text == null ? "" : text);
            r.SkipWs();
            r.Expect('[');
            List<JsonObject> list = new List<JsonObject>();
            r.SkipWs();
            if (!r.Eof && r.Peek() == ']') { r.Next(); return list; }
            while (true)
            {
                r.SkipWs();
                if (r.Eof) throw new FormatException("JSON 数组未闭合");
                if (r.Peek() != '{') throw new FormatException("JSON 数组元素必须为对象");
                list.Add(r.ReadObject());
                r.SkipWs();
                if (r.Eof) throw new FormatException("JSON 数组未闭合");
                char c = r.Next();
                if (c == ',') continue;
                if (c == ']') break;
                throw new FormatException("JSON 数组分隔符错误");
            }
            r.SkipWs();
            if (!r.Eof) throw new FormatException("JSON 尾部有多余字符");
            return list;
        }

        private static void Indent(StringBuilder sb, int depth)
        {
            for (int i = 0; i < depth; i++) sb.Append("  ");
        }

        private static void WriteValue(StringBuilder sb, object v, int indent)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is long) { sb.Append(((long)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is int) { sb.Append(((int)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double) { sb.Append(((double)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (v is JsonObject) { ((JsonObject)v).WriteTo(sb, indent); return; }
            throw new InvalidOperationException("不支持的 JSON 值类型: " + v.GetType().FullName);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// 极简递归下降读取器；容忍 BOM 与前导空白。
        private sealed class JsonReader
        {
            private readonly string _s;
            private int _pos;

            public JsonReader(string s)
            {
                _s = s;
                _pos = 0;
            }

            public bool Eof { get { return _pos >= _s.Length; } }
            public char Peek() { return _s[_pos]; }
            public char Next() { return _s[_pos++]; }

            public void SkipWs()
            {
                while (!Eof)
                {
                    char c = Peek();
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\uFEFF') { _pos++; continue; }
                    break;
                }
            }

            public JsonObject ReadObject()
            {
                SkipWs();
                Expect('{');
                JsonObject o = new JsonObject();
                SkipWs();
                if (!Eof && Peek() == '}') { Next(); return o; }
                while (true)
                {
                    SkipWs();
                    string key = ReadString();
                    Expect(':');
                    o.Set(key, ReadValue());
                    SkipWs();
                    if (Eof) throw new FormatException("JSON 对象未闭合");
                    char c = Next();
                    if (c == ',') continue;
                    if (c == '}') break;
                    throw new FormatException("JSON 对象分隔符错误");
                }
                return o;
            }

            private object ReadValue()
            {
                SkipWs();
                if (Eof) throw new FormatException("JSON 意外结束");
                char c = Peek();
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't') { ExpectWord("true"); return true; }
                if (c == 'f') { ExpectWord("false"); return false; }
                if (c == 'n') { ExpectWord("null"); return null; }
                return ReadNumber();
            }

            private List<object> ReadArray()
            {
                Expect('[');
                List<object> list = new List<object>();
                SkipWs();
                if (!Eof && Peek() == ']') { Next(); return list; }
                while (true)
                {
                    list.Add(ReadValue());
                    SkipWs();
                    if (Eof) throw new FormatException("JSON 数组未闭合");
                    char c = Next();
                    if (c == ',') continue;
                    if (c == ']') break;
                    throw new FormatException("JSON 数组分隔符错误");
                }
                return list;
            }

            private string ReadString()
            {
                Expect('"');
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    if (Eof) throw new FormatException("JSON 字符串未闭合");
                    char c = Next();
                    if (c == '"') break;
                    if (c == '\\')
                    {
                        if (Eof) throw new FormatException("JSON 转义未完成");
                        char e = Next();
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_pos + 4 > _s.Length) throw new FormatException("JSON \\u 转义不完整");
                                sb.Append((char)Convert.ToInt32(_s.Substring(_pos, 4), 16));
                                _pos += 4;
                                break;
                            default: throw new FormatException("JSON 未知转义 \\" + e);
                        }
                        continue;
                    }
                    sb.Append(c);
                }
                return sb.ToString();
            }

            private object ReadNumber()
            {
                int start = _pos;
                bool isFloat = false;
                while (!Eof)
                {
                    char c = Peek();
                    if (c == '-' || c == '+' || (c >= '0' && c <= '9')) { _pos++; continue; }
                    if (c == '.' || c == 'e' || c == 'E') { isFloat = true; _pos++; continue; }
                    break;
                }
                string tok = _s.Substring(start, _pos - start);
                if (tok == "") throw new FormatException("JSON 数字格式错误");
                if (!isFloat)
                {
                    long l;
                    if (long.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
                }
                double d;
                if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                throw new FormatException("JSON 数字格式错误: " + tok);
            }

            public void Expect(char c)
            {
                SkipWs();
                if (Eof || Next() != c) throw new FormatException("JSON 期望字符 '" + c + "'");
            }

            private void ExpectWord(string w)
            {
                if (_pos + w.Length > _s.Length || _s.Substring(_pos, w.Length) != w)
                    throw new FormatException("JSON 期望 " + w);
                _pos += w.Length;
            }
        }
    }
}
```

- [ ] **Step 4: 运行确认通过**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：`36 passed, 0 failed`（25 + Json 11 项）。

---

### Task 4: Config（TDD）

**Files:**
- Create: `src/Config.cs`
- Modify: `tests/ParserTests.cs`（Main 追加调用 + `RunSpeedLimitCases`、`RunConfigCases`）

**Interfaces:**
- Consumes: `JsonObject`（Task 3）
- Produces:
  - `namespace AriaGui`、`public sealed class Config`：
    字段 `string SaveDir; int MaxConcurrent; string SpeedLimit; int Split;`
    静态 `bool ValidSpeedLimit(string s)`、`Config Default()`、`string ConfigPath()`、`Config Load()`、`void ApplyJson(Config cfg, JsonObject root)`、`void Clamp(Config cfg)`、`static readonly UTF8Encoding Utf8NoBom`
    实例 `string ToJson()`、`string Save()`（成功返回 null，失败返回错误消息）

- [ ] **Step 1: 修改 `tests/ParserTests.cs`**

在 `Main()` 的 `RunJsonCases();` 之后追加：

```csharp
            RunSpeedLimitCases();
            RunConfigCases();
```

类内追加：

```csharp
        private static void RunSpeedLimitCases()
        {
            string[] valid = { "", "512K", "2M", "1G", "1.5M", "512k", "1024" };
            foreach (string s in valid)
                Check("ValidSpeedLimit(" + s + ")", Config.ValidSpeedLimit(s), "");
            string[] invalid = { "abc", "512KB", "K512", "-1M", "1 M", "1.5x" };
            foreach (string s in invalid)
                Check("ValidSpeedLimit(" + s + ")", !Config.ValidSpeedLimit(s), "");
        }

        private static void RunConfigCases()
        {
            // Go 版写出的合法 JSON 样例 → 正确读取
            string sample = "{\n  \"save_dir\": \"D:\\\\dl\",\n  \"max_concurrent\": 5,\n  \"speed_limit\": \"2M\",\n  \"split\": 8\n}";
            Config cfg = Config.Default();
            Config.ApplyJson(cfg, JsonObject.ParseObject(sample));
            Check("Config 读取样例",
                cfg.SaveDir == "D:\\dl" && cfg.MaxConcurrent == 5 && cfg.SpeedLimit == "2M" && cfg.Split == 8,
                cfg.SaveDir + "|" + cfg.MaxConcurrent + "|" + cfg.SpeedLimit + "|" + cfg.Split);

            // 越界值被钳制（与 Go Load 一致）
            Config cfg2 = Config.Default();
            Config.ApplyJson(cfg2, JsonObject.ParseObject("{\"max_concurrent\": 99, \"split\": 0, \"speed_limit\": \"bad\"}"));
            Check("Config 钳制",
                cfg2.MaxConcurrent == 10 && cfg2.Split == 1 && cfg2.SpeedLimit == "",
                cfg2.MaxConcurrent + "|" + cfg2.Split + "|" + cfg2.SpeedLimit);

            // 写出键序与 Go 一致
            Config cfg3 = Config.Default();
            cfg3.SaveDir = "C:\\x";
            cfg3.MaxConcurrent = 3;
            cfg3.SpeedLimit = "";
            cfg3.Split = 4;
            Check("Config 写出格式",
                cfg3.ToJson() == "{\n  \"save_dir\": \"C:\\\\x\",\n  \"max_concurrent\": 3,\n  \"speed_limit\": \"\",\n  \"split\": 4\n}",
                "got=" + cfg3.ToJson().Replace("\n", "\\n"));
        }
```

- [ ] **Step 2: 运行确认失败**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：FAIL —— 编译报 `CS0103/CS0246`（找不到 `Config`）。

- [ ] **Step 3: 创建 `src/Config.cs`**

```csharp
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AriaGui
{
    /// 应用设置，持久化于 %AppData%\aria-gui\config.json（C# 版自有格式）。
    public sealed class Config
    {
        public string SaveDir = "";
        public int MaxConcurrent = 3;
        public string SpeedLimit = "";
        public int Split = 4;

        /// UTF-8 无 BOM 编码。
        public static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static readonly Regex SpeedLimitRe = new Regex(@"^\d+(\.\d+)?[KMGkmg]?$");

        /// 空串（不限速）或合法格式返回 true。
        public static bool ValidSpeedLimit(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            return SpeedLimitRe.IsMatch(s);
        }

        /// 默认配置：保存目录取用户 Downloads。
        public static Config Default()
        {
            Config c = new Config();
            c.SaveDir = DownloadsDir();
            c.MaxConcurrent = 3;
            c.SpeedLimit = "";
            c.Split = 4;
            return c;
        }

        private static string DownloadsDir()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home)) return Path.Combine(home, "Downloads");
            return ".";
        }

        /// 配置文件绝对路径。
        public static string ConfigPath()
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(baseDir)) return null;
            return Path.Combine(Path.Combine(baseDir, "aria-gui"), "config.json");
        }

        /// 读取配置；文件不存在或损坏时回退默认值。
        public static Config Load()
        {
            Config cfg = Default();
            string p = ConfigPath();
            if (p == null || !File.Exists(p)) return cfg;
            string text;
            try { text = File.ReadAllText(p); }
            catch { return cfg; }
            JsonObject root;
            try { root = JsonObject.ParseObject(text); }
            catch { return cfg; }
            ApplyJson(cfg, root);
            return cfg;
        }

        /// 从解析后的 JSON 应用字段；随后执行钳制（供 Load 与测试复用）。
        public static void ApplyJson(Config cfg, JsonObject root)
        {
            cfg.SaveDir = root.GetString("save_dir", cfg.SaveDir);
            cfg.MaxConcurrent = (int)root.GetLong("max_concurrent", cfg.MaxConcurrent);
            cfg.SpeedLimit = root.GetString("speed_limit", cfg.SpeedLimit);
            cfg.Split = (int)root.GetLong("split", cfg.Split);
            Clamp(cfg);
        }

        /// 兜底钳制（Load 与测试复用）。
        public static void Clamp(Config cfg)
        {
            if (cfg.MaxConcurrent < 1) cfg.MaxConcurrent = 1;
            if (cfg.MaxConcurrent > 10) cfg.MaxConcurrent = 10;
            if (cfg.Split < 1) cfg.Split = 1;
            if (!ValidSpeedLimit(cfg.SpeedLimit)) cfg.SpeedLimit = "";
        }

        /// 序列化为 JSON 文本（2 空格缩进）。
        public string ToJson()
        {
            JsonObject o = new JsonObject();
            o.Set("save_dir", SaveDir);
            o.Set("max_concurrent", (long)MaxConcurrent);
            o.Set("speed_limit", SpeedLimit);
            o.Set("split", (long)Split);
            return o.ToJson();
        }

        /// 写回配置；成功返回 null，失败返回错误消息（供 UI 展示）。
        public string Save()
        {
            string p = ConfigPath();
            if (p == null) return "无法确定配置目录";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, ToJson(), Utf8NoBom);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
```

- [ ] **Step 4: 运行确认通过**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：`52 passed, 0 failed`（36 + 限速 13 + Config 3）。

---

### Task 5: History（TDD）

**Files:**
- Create: `src/History.cs`
- Modify: `tests/ParserTests.cs`（Main 追加调用 + `RunHistoryCases`）

**Interfaces:**
- Consumes: `JsonObject`（Task 3）、`Config.Utf8NoBom`（Task 4）
- Produces:
  - `namespace AriaGui`、`public sealed class HistoryEntry`：字段 `string Url, Name, Dir, Status; long Size; DateTime At;`
  - `public sealed class HistoryStore`：
    静态 `HistoryStore Load()`、`string Serialize(List<HistoryEntry> entries)`、`List<HistoryEntry> Deserialize(string text)`
    实例 `void Add(HistoryEntry e)`、`List<HistoryEntry> All()`、`void Clear()`

- [ ] **Step 1: 修改 `tests/ParserTests.cs`**

在 `Main()` 的 `RunConfigCases();` 之后追加：

```csharp
            RunHistoryCases();
```

类内追加：

```csharp
        private static void RunHistoryCases()
        {
            // ISO 8601 日期样例（含带时区偏移与无偏移两种 at）
            string goSample = "[\n  {\n    \"url\": \"https://example.com/a.zip\",\n    \"name\": \"a.zip\",\n    \"dir\": \"D:\\\\dl\",\n    \"size\": 1048576,\n    \"status\": \"已完成\",\n    \"at\": \"2026-09-27T12:00:00.1234567+08:00\"\n  },\n  {\n    \"url\": \"https://example.com/b.zip\",\n    \"name\": \"b.zip\",\n    \"dir\": \"D:\\\\dl\",\n    \"size\": 0,\n    \"status\": \"失败\",\n    \"at\": \"2026-09-26T08:30:00\"\n  }\n]";
            List<HistoryEntry> sample = HistoryStore.Deserialize(goSample);
            Check("History 读取 Go 样例",
                sample.Count == 2 && sample[0].Name == "a.zip" && sample[0].Size == 1048576L && sample[0].Status == "已完成",
                "count=" + sample.Count);
            Check("History 时间解析-带偏移", sample[0].At != DateTime.MinValue, "got=" + sample[0].At.ToString("o"));
            Check("History 时间解析-无偏移", sample[1].At.Year == 2026 && sample[1].At.Hour == 8,
                "got=" + sample[1].At.ToString("o"));

            // 往返：Serialize → Deserialize 字段一致
            HistoryEntry e = new HistoryEntry();
            e.Url = "https://x/y f.zip?q=1";
            e.Name = "y f.zip";
            e.Dir = "C:\\下载";
            e.Size = 123456789L;
            e.Status = "已完成";
            e.At = new DateTime(2026, 9, 27, 10, 20, 30, DateTimeKind.Local);
            List<HistoryEntry> one = new List<HistoryEntry>();
            one.Add(e);
            List<HistoryEntry> back = HistoryStore.Deserialize(HistoryStore.Serialize(one));
            Check("History 往返",
                back.Count == 1 && back[0].Url == e.Url && back[0].Name == e.Name && back[0].Dir == e.Dir
                && back[0].Size == e.Size && back[0].Status == e.Status && back[0].At == e.At,
                "count=" + back.Count);

            // 空列表
            Check("History 空序列化", HistoryStore.Serialize(new List<HistoryEntry>()) == "[]", "");
        }
```

- [ ] **Step 2: 运行确认失败**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：FAIL —— 编译报 `CS0246`（找不到 `HistoryStore`/`HistoryEntry`）。

- [ ] **Step 3: 创建 `src/History.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AriaGui
{
    /// 一条历史记录。
    public sealed class HistoryEntry
    {
        public string Url = "";
        public string Name = "";
        public string Dir = "";
        public long Size;
        public string Status = ""; // 已完成 / 失败
        public DateTime At;
    }

    /// 线程安全的历史存储：%AppData%\aria-gui\history.json，新→旧，上限 200 条。
    public sealed class HistoryStore
    {
        private const int MaxEntries = 200;
        private readonly object _lock = new object();
        private string _path;
        private List<HistoryEntry> _entries = new List<HistoryEntry>();

        /// 从 %AppData%\aria-gui\history.json 加载；不存在/损坏时静默返回空存储。
        public static HistoryStore Load()
        {
            HistoryStore s = new HistoryStore();
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(baseDir)) return s;
            s._path = Path.Combine(Path.Combine(baseDir, "aria-gui"), "history.json");
            string text;
            try { text = File.ReadAllText(s._path); }
            catch { return s; }
            try { s._entries = Deserialize(text); }
            catch { s._entries = new List<HistoryEntry>(); }
            return s;
        }

        /// 追加一条记录并落盘（超出上限丢弃最旧）。
        public void Add(HistoryEntry e)
        {
            lock (_lock)
            {
                e.At = DateTime.Now;
                _entries.Insert(0, e);
                if (_entries.Count > MaxEntries)
                    _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
                SaveLocked();
            }
        }

        /// 全部记录（新→旧）。
        public List<HistoryEntry> All()
        {
            lock (_lock)
            {
                return new List<HistoryEntry>(_entries);
            }
        }

        /// 清空历史并落盘。
        public void Clear()
        {
            lock (_lock)
            {
                _entries = new List<HistoryEntry>();
                SaveLocked();
            }
        }

        private void SaveLocked()
        {
            if (string.IsNullOrEmpty(_path)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, Serialize(_entries), Config.Utf8NoBom);
            }
            catch
            {
                // 与 Go 版一致：落盘失败静默
            }
        }

        /// 序列化为 JSON 数组（2 空格缩进，与 Go json.MarshalIndent 一致）。
        public static string Serialize(List<HistoryEntry> entries)
        {
            if (entries.Count == 0) return "[]";
            StringBuilder sb = new StringBuilder();
            sb.Append("[\n");
            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append("  ");
                ToJsonObject(entries[i]).WriteTo(sb, 1);
                if (i < entries.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// 从 JSON 数组文本解析（字段缺失/多余均容错）。
        public static List<HistoryEntry> Deserialize(string text)
        {
            List<HistoryEntry> list = new List<HistoryEntry>();
            List<JsonObject> objs = JsonObject.ParseObjectArray(text);
            foreach (JsonObject o in objs)
            {
                HistoryEntry e = new HistoryEntry();
                e.Url = o.GetString("url", "");
                e.Name = o.GetString("name", "");
                e.Dir = o.GetString("dir", "");
                e.Size = o.GetLong("size", 0);
                e.Status = o.GetString("status", "");
                e.At = ParseTime(o.GetString("at", ""));
                list.Add(e);
            }
            return list;
        }

        private static JsonObject ToJsonObject(HistoryEntry e)
        {
            JsonObject o = new JsonObject();
            o.Set("url", e.Url);
            o.Set("name", e.Name);
            o.Set("dir", e.Dir);
            o.Set("size", e.Size);
            o.Set("status", e.Status);
            o.Set("at", e.At.ToString("o", CultureInfo.InvariantCulture));
            return o;
        }

        private static DateTime ParseTime(string s)
        {
            DateTime t;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t))
                return t;
            return DateTime.MinValue;
        }
    }
}
```

- [ ] **Step 4: 运行确认通过**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：`57 passed, 0 failed`（52 + History 5）。

---

### Task 6: DownloadTask（TDD）

**Files:**
- Create: `src/Engine/DownloadTask.cs`
- Modify: `tests/ParserTests.cs`（Main 追加调用 + `RunDisplayNameCases`）

**Interfaces:**
- Consumes: 无
- Produces:
  - `namespace AriaGui.Engine`、`public enum TaskStatus { Queued, Running, Paused, Completed, Failed, Removed }`
  - `public static class TaskStatusText`：`string ToText(TaskStatus s)`（排队中/下载中/已暂停/已完成/失败/已删除/未知）
  - `public sealed class DownloadTask`：字段 `string Id, Url, Dir, Output, Error; TaskStatus Status; long Completed, Total, Speed; int Eta（初始 -1）; double Percent; DateTime CreatedAt, FinishedAt;`
    内部字段 `internal Process Proc; internal bool PauseRequested, RemoveRequested;`
    方法 `bool IsMagnet()`、`string DisplayName()`

- [ ] **Step 1: 修改 `tests/ParserTests.cs`**

在 `Main()` 的 `RunHistoryCases();` 之后追加：

```csharp
            RunDisplayNameCases();
```

类内追加：

```csharp
        private static void RunDisplayNameCases()
        {
            DownloadTask t = new DownloadTask();
            t.Url = "https://example.com/a/b/file.zip?token=1#frag";
            Check("DisplayName URL 尾段", t.DisplayName() == "file.zip", "got=" + t.DisplayName());

            t.Url = "https://example.com/dir/";
            Check("DisplayName 去尾斜杠", t.DisplayName() == "dir", "got=" + t.DisplayName());

            t.Url = "https://example.com";
            Check("DisplayName 纯主机", t.DisplayName() == "example.com", "got=" + t.DisplayName());

            t.Url = "https://example.com/x.bin";
            t.Output = "x.bin";
            Check("DisplayName 优先 Output", t.DisplayName() == "x.bin", "got=" + t.DisplayName());

            DownloadTask m = new DownloadTask();
            m.Url = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=somefileisohavingname";
            string expectTail = m.Url.Substring(m.Url.Length - 40);
            Check("DisplayName 磁力链接", m.DisplayName() == "磁力链接 ..." + expectTail, "got=" + m.DisplayName());

            Check("IsMagnet", m.IsMagnet() && !t.IsMagnet(), "");
        }
```

- [ ] **Step 2: 运行确认失败**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：FAIL —— 编译报 `CS0246`（找不到 `DownloadTask`）。

- [ ] **Step 3: 创建 `src/Engine/DownloadTask.cs`**

```csharp
using System;
using System.Diagnostics;

namespace AriaGui.Engine
{
    /// 任务状态机（对齐 Go 版）：
    /// Queued → Running → Completed / Failed；Paused → Running（resume）；Removed 仅内部标记。
    public enum TaskStatus
    {
        Queued,
        Running,
        Paused,
        Completed,
        Failed,
        Removed
    }

    public static class TaskStatusText
    {
        public static string ToText(TaskStatus s)
        {
            switch (s)
            {
                case TaskStatus.Queued: return "排队中";
                case TaskStatus.Running: return "下载中";
                case TaskStatus.Paused: return "已暂停";
                case TaskStatus.Completed: return "已完成";
                case TaskStatus.Failed: return "失败";
                case TaskStatus.Removed: return "已删除";
            }
            return "未知";
        }
    }

    /// 一个下载任务，对应至多一个 aria2c 子进程。
    public sealed class DownloadTask
    {
        public string Id = "";
        public string Url = "";
        public string Dir = "";
        public string Output = "";
        public TaskStatus Status;
        public long Completed;
        public long Total;   // 0 = 未知（BT 元数据未就绪）
        public long Speed;   // bytes/s
        public int Eta = -1; // 秒，-1 = 未知
        public double Percent;
        public string Error = "";
        public DateTime CreatedAt;
        public DateTime FinishedAt;

        internal Process Proc;
        internal bool PauseRequested;
        internal bool RemoveRequested;

        /// 是否为磁力链接。
        public bool IsMagnet()
        {
            if (string.IsNullOrEmpty(Url)) return false;
            return Url.ToLowerInvariant().StartsWith("magnet:");
        }

        /// 列表展示名：Output > "磁力链接 "+尾40字符 > URL 路径尾段（异常回退尾50字符）。
        public string DisplayName()
        {
            if (!string.IsNullOrEmpty(Output)) return Output;
            if (IsMagnet()) return "磁力链接 " + TrimTail(Url, 40);

            string u = Url == null ? "" : Url;
            int i = u.IndexOfAny(new char[] { '?', '#' });
            if (i >= 0) u = u.Substring(0, i);
            u = u.TrimEnd('/');
            int slash = u.LastIndexOf('/');
            string baseName = slash >= 0 ? u.Substring(slash + 1) : u;
            if (baseName == "" || baseName == "/" || baseName == ".")
                return TrimTail(Url, 50);
            return baseName;
        }

        private static string TrimTail(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= n) return s;
            return "..." + s.Substring(s.Length - n);
        }
    }
}
```

- [ ] **Step 4: 运行确认通过**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：`63 passed, 0 failed`（57 + DisplayName 6）。

---

### Task 7: Embed（资源释放）

**Files:**
- Create: `src/Engine/Embed.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `namespace AriaGui.Engine`、`public static class Embed`：
    `string Extract()`（失败抛异常，消息格式：`"读取嵌入的 aria2c 失败: ..."` / `"创建临时目录失败: ..."` / `"写出 aria2c 失败: ..."`）、`string BinaryPath { get; }`

**测试说明**：不写单元测试——`Extract()` 依赖测试 exe 被 `/resource:` 内嵌（测试 exe 无资源），且会写 `%TEMP%`；其验证放在 Task 11 启动冒烟（断言 `%TEMP%\aria-gui\aria2c.exe` 的 sha256）。本任务验证 = 编译通过（run-tests.ps1 会把 Embed.cs 一并编译）。

- [ ] **Step 1: 创建 `src/Engine/Embed.cs`**

```csharp
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AriaGui.Engine
{
    /// 把内嵌的 aria2c.exe 释放到 %TEMP%\aria-gui\，sha256 相同则复用。
    public static class Embed
    {
        private const string ResourceName = "aria2c.exe";
        private static string _binaryPath;

        /// 返回释放后的可执行文件绝对路径（懒加载，失败抛异常）。
        public static string Extract()
        {
            if (!string.IsNullOrEmpty(_binaryPath)) return _binaryPath;

            byte[] data = ReadResource();
            string dir = Path.Combine(Path.GetTempPath(), "aria-gui");
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                throw new Exception("创建临时目录失败: " + ex.Message);
            }

            string target = Path.Combine(dir, "aria2c.exe");
            string wantHash = Hash(data);
            try
            {
                if (File.Exists(target) && Hash(File.ReadAllBytes(target)) == wantHash)
                {
                    _binaryPath = target;
                    return target;
                }
                File.WriteAllBytes(target, data);
            }
            catch (Exception ex)
            {
                throw new Exception("写出 aria2c 失败: " + ex.Message);
            }
            _binaryPath = target;
            return target;
        }

        /// 已释放的 aria2c 路径（供外部查询，未释放时为 null）。
        public static string BinaryPath
        {
            get { return _binaryPath; }
        }

        private static byte[] ReadResource()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            Stream s = null;
            foreach (string n in asm.GetManifestResourceNames())
            {
                if (n == ResourceName)
                {
                    s = asm.GetManifestResourceStream(n);
                    break;
                }
            }
            if (s == null)
                throw new Exception("读取嵌入的 aria2c 失败: 资源 " + ResourceName + " 不存在");
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[81920];
                    int read;
                    while ((read = s.Read(buf, 0, buf.Length)) > 0)
                        ms.Write(buf, 0, read);
                    return ms.ToArray();
                }
            }
            finally
            {
                s.Dispose();
            }
        }

        private static string Hash(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data);
                StringBuilder sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
```

- [ ] **Step 2: 验证编译与既有测试不受影响**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：`63 passed, 0 failed`（Embed.cs 作为纯逻辑文件被一并编译，无新增用例）。

再 Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\build.ps1`
Expected：编译成功，`aria-gui.exe` 大小仍 > 5MB。

---

### Task 8: Manager（调度器，TDD 覆盖 BuildArgs/QuoteArg）

**Files:**
- Create: `src/Engine/Manager.cs`
- Modify: `tests/ParserTests.cs`（Main 追加调用 + `RunManagerArgsCases`、`RunQuoteArgCases`）

**Interfaces:**
- Consumes: `Config`、`HistoryStore`（Task 4/5）、`Embed`（Task 7）、`Parser`/`DownloadTask`（Task 2/6）
- Produces:
  - `namespace AriaGui.Engine`、`public sealed class TaskEvent { public string TaskId; public TaskStatus Status; }`
  - `public sealed class Manager`：
    构造 `Manager(Config cfg, HistoryStore hist)`（内部 `Embed.Extract()`，失败抛异常）
    `void Subscribe(Action<TaskEvent> handler)`、`void Unsubscribe(Action<TaskEvent> handler)`
    `DownloadTask AddTask(string url, string dir, string output)`（非法 URL 抛 `InvalidOperationException`，消息见 spec）
    `void Start(string id)`、`void Pause(string id)`、`void Remove(string id)`
    `List<DownloadTask> Snapshot()`、`long TotalSpeed()`
    `internal static List<string> BuildArgs(DownloadTask t, Config cfg)`、`internal static string JoinArgs(List<string> args)`、`internal static string QuoteArg(string s)`

**线程规则（必须遵守）**：`_mu` 保护任务表；事件在**后台线程**同步派发给订阅者（订阅者必须立即返回，UI 侧只做 `BeginInvoke`）。`BuildArgs/JoinArgs/QuoteArg` 为纯静态函数，测试直接调用（不实例化 Manager，无需资源）。

- [ ] **Step 1: 修改 `tests/ParserTests.cs`**

在 `Main()` 的 `RunDisplayNameCases();` 之后追加：

```csharp
            RunManagerArgsCases();
            RunQuoteArgCases();
```

类内追加：

```csharp
        private static void RunManagerArgsCases()
        {
            Config cfg = Config.Default();
            cfg.Split = 4;

            DownloadTask http = new DownloadTask();
            http.Url = "https://example.com/f.zip";
            http.Dir = "D:\\dl";
            List<string> a = Manager.BuildArgs(http, cfg);
            Check("BuildArgs 首参为 URL", a[0] == "https://example.com/f.zip", "got=" + a[0]);
            Check("BuildArgs 含 dir", a.Contains("--dir=D:\\dl"), "");
            Check("BuildArgs 含分片三参数",
                a.Contains("--split=4") && a.Contains("--max-connection-per-server=4") && a.Contains("--max-concurrent-downloads=4"), "");
            Check("BuildArgs 无 BT 参数",
                !a.Contains("--follow-torrent=mem") && !a.Contains("--save-session=aria2.session"), "");

            DownloadTask withOut = new DownloadTask();
            withOut.Url = "https://example.com/f.zip";
            withOut.Dir = "D:\\dl";
            withOut.Output = "renamed.zip";
            Check("BuildArgs 含 out", Manager.BuildArgs(withOut, cfg).Contains("--out=renamed.zip"), "");

            cfg.SpeedLimit = "2M";
            Check("BuildArgs 含限速", Manager.BuildArgs(http, cfg).Contains("--max-overall-download-limit=2M"), "");

            DownloadTask mag = new DownloadTask();
            mag.Url = "magnet:?xt=urn:btih:abc";
            mag.Dir = "D:\\dl";
            List<string> b = Manager.BuildArgs(mag, cfg);
            Check("BuildArgs BT 参数",
                b.Contains("--save-session=aria2.session") && b.Contains("--bt-metadata-only=false")
                && b.Contains("--seed-ratio=0") && b.Contains("--bt-stop-time-limit=0")
                && b.Contains("--follow-torrent=mem"), "");

            DownloadTask tor = new DownloadTask();
            tor.Url = "https://example.com/a.TORRENT";
            tor.Dir = "D:\\dl";
            Check("BuildArgs .torrent 分支", Manager.BuildArgs(tor, cfg).Contains("--follow-torrent=mem"), "");
        }

        private static void RunQuoteArgCases()
        {
            Check("QuoteArg 简单", Manager.QuoteArg("abc") == "abc", "got=" + Manager.QuoteArg("abc"));
            Check("QuoteArg 空格", Manager.QuoteArg("a b") == "\"a b\"", "got=" + Manager.QuoteArg("a b"));
            Check("QuoteArg 引号", Manager.QuoteArg("a\"b") == "\"a\\\"b\"", "got=" + Manager.QuoteArg("a\"b"));
            Check("QuoteArg 路径", Manager.QuoteArg("C:\\Program Files\\x") == "\"C:\\Program Files\\x\"",
                "got=" + Manager.QuoteArg("C:\\Program Files\\x"));
            Check("QuoteArg 尾部反斜杠", Manager.QuoteArg("a b\\") == "\"a b\\\\\"", "got=" + Manager.QuoteArg("a b\\"));
        }
```

- [ ] **Step 2: 运行确认失败**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：FAIL —— 编译报 `CS0246`（找不到 `Manager`）。

- [ ] **Step 3: 创建 `src/Engine/Manager.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace AriaGui.Engine
{
    /// 任务变更事件（在后台线程同步派发）。
    public sealed class TaskEvent
    {
        public string TaskId;
        public TaskStatus Status;
    }

    /// 任务调度器：每个任务对应一个 aria2c 子进程；暂停=杀进程保留 .aria2 控制文件，继续=--continue 重新拉起。
    public sealed class Manager
    {
        private readonly string _bin;
        private readonly Config _cfg;
        private readonly HistoryStore _hist;

        private readonly object _mu = new object();
        private readonly Dictionary<string, DownloadTask> _tasks = new Dictionary<string, DownloadTask>();
        private readonly List<string> _order = new List<string>();
        private readonly Queue<string> _queue = new Queue<string>();
        private int _seq;
        private int _running;

        private readonly object _subMu = new object();
        private readonly List<Action<TaskEvent>> _subs = new List<Action<TaskEvent>>();

        /// 释放内嵌 aria2c 并构造调度器；释放失败抛异常（由启动路径 MessageBox 展示）。
        public Manager(Config cfg, HistoryStore hist)
        {
            _bin = Embed.Extract();
            _cfg = cfg;
            _hist = hist;
        }

        /// 注册事件接收器（后台线程同步调用，处理器必须立即返回）。
        public void Subscribe(Action<TaskEvent> handler)
        {
            lock (_subMu)
            {
                _subs.Add(handler);
            }
        }

        /// 注销事件接收器。
        public void Unsubscribe(Action<TaskEvent> handler)
        {
            lock (_subMu)
            {
                _subs.Remove(handler);
            }
        }

        /// 新建任务并入队。dir 为空时使用配置的默认目录。
        public DownloadTask AddTask(string url, string dir, string output)
        {
            url = (url == null ? "" : url).Trim();
            if (url == "") throw new InvalidOperationException("URL 不能为空");
            string lower = url.ToLowerInvariant();
            if (url.IndexOf("://", StringComparison.Ordinal) < 0 &&
                !lower.StartsWith("magnet:") &&
                !lower.EndsWith(".torrent"))
                throw new InvalidOperationException("无法识别的下载地址: " + url);
            if (string.IsNullOrEmpty(dir)) dir = _cfg.SaveDir;

            lock (_mu)
            {
                _seq++;
                DownloadTask t = new DownloadTask();
                t.Id = string.Format(CultureInfo.InvariantCulture, "t{0:0000}", _seq);
                t.Url = url;
                t.Dir = dir;
                t.Output = (output == null ? "" : output).Trim();
                t.Status = TaskStatus.Queued;
                t.Eta = -1;
                t.CreatedAt = DateTime.Now;
                _tasks[t.Id] = t;
                _order.Add(t.Id);
                _queue.Enqueue(t.Id);
                PumpLocked();
                return t;
            }
        }

        /// 启动/恢复一个排队、暂停或失败的任务。
        public void Start(string id)
        {
            lock (_mu)
            {
                DownloadTask t;
                if (!_tasks.TryGetValue(id, out t)) return;
                if (t.Status == TaskStatus.Queued || t.Status == TaskStatus.Running) return;
                t.Status = TaskStatus.Queued;
                t.PauseRequested = false;
                t.RemoveRequested = false;
                _queue.Enqueue(id);
                PumpLocked();
            }
        }

        /// 暂停：杀进程但保留 .aria2 控制文件，之后可续传。
        public void Pause(string id)
        {
            lock (_mu)
            {
                DownloadTask t;
                if (!_tasks.TryGetValue(id, out t)) return;
                if (t.Status == TaskStatus.Queued)
                {
                    DequeueLocked(id);
                    t.Status = TaskStatus.Paused;
                    EmitLocked(t.Id, t.Status);
                }
                else if (t.Status == TaskStatus.Running)
                {
                    t.PauseRequested = true;
                    Kill(t);
                }
            }
        }

        /// 删除任务；运行中的先杀进程（对齐 Go 实际行为：不清理 .aria2 文件）。
        public void Remove(string id)
        {
            lock (_mu)
            {
                DownloadTask t;
                if (!_tasks.TryGetValue(id, out t)) return;
                if (t.Status == TaskStatus.Running)
                {
                    t.RemoveRequested = true;
                    Kill(t);
                }
                else if (t.Status == TaskStatus.Queued)
                {
                    DequeueLocked(id);
                    t.Status = TaskStatus.Removed;
                    EmitLocked(t.Id, TaskStatus.Removed);
                }
                else
                {
                    t.Status = TaskStatus.Removed;
                    EmitLocked(t.Id, TaskStatus.Removed);
                }
            }
        }

        /// 任务副本（按创建顺序，剔除已删除），供 UI 渲染。
        public List<DownloadTask> Snapshot()
        {
            lock (_mu)
            {
                List<DownloadTask> outp = new List<DownloadTask>(_order.Count);
                foreach (string id in _order)
                {
                    DownloadTask t;
                    if (!_tasks.TryGetValue(id, out t)) continue;
                    if (t.Status == TaskStatus.Removed) continue;
                    outp.Add(Clone(t));
                }
                return outp;
            }
        }

        /// 当前所有运行中任务的合计速度（状态栏展示）。
        public long TotalSpeed()
        {
            lock (_mu)
            {
                long sum = 0;
                foreach (DownloadTask t in _tasks.Values)
                {
                    if (t.Status == TaskStatus.Running) sum += t.Speed;
                }
                return sum;
            }
        }

        // ---- 内部实现 ----

        private static DownloadTask Clone(DownloadTask t)
        {
            DownloadTask c = new DownloadTask();
            c.Id = t.Id;
            c.Url = t.Url;
            c.Dir = t.Dir;
            c.Output = t.Output;
            c.Status = t.Status;
            c.Completed = t.Completed;
            c.Total = t.Total;
            c.Speed = t.Speed;
            c.Eta = t.Eta;
            c.Percent = t.Percent;
            c.Error = t.Error;
            c.CreatedAt = t.CreatedAt;
            c.FinishedAt = t.FinishedAt;
            return c;
        }

        /// 在锁内调用：尽可能启动排队任务，直到达到并发上限。
        private void PumpLocked()
        {
            int max = _cfg.MaxConcurrent;
            if (max < 1) max = 1;
            while (_running < max && _queue.Count > 0)
            {
                string id = _queue.Dequeue();
                DownloadTask t;
                if (!_tasks.TryGetValue(id, out t)) continue;
                if (t.Status != TaskStatus.Queued) continue;
                _running++;
                Task.Run(delegate { RunTask(t); });
            }
        }

        private void DequeueLocked(string id)
        {
            if (!_queue.Contains(id)) return;
            Queue<string> keep = new Queue<string>();
            while (_queue.Count > 0)
            {
                string x = _queue.Dequeue();
                if (x != id) keep.Enqueue(x);
            }
            while (keep.Count > 0) _queue.Enqueue(keep.Dequeue());
        }

        private void EmitLocked(string taskId, TaskStatus status)
        {
            TaskEvent ev = new TaskEvent();
            ev.TaskId = taskId;
            ev.Status = status;
            Action<TaskEvent>[] handlers;
            lock (_subMu)
            {
                handlers = _subs.ToArray();
            }
            foreach (Action<TaskEvent> h in handlers)
            {
                try { h(ev); }
                catch { /* 订阅者异常不影响调度 */ }
            }
        }

        private void SetStatus(DownloadTask t, TaskStatus s)
        {
            lock (_mu)
            {
                t.Status = s;
                EmitLocked(t.Id, s);
            }
        }

        private static void Kill(DownloadTask t)
        {
            try
            {
                if (t.Proc != null && !t.Proc.HasExited) t.Proc.Kill();
            }
            catch
            {
                // 进程已退出或句柄失效：同 Go 版忽略
            }
        }

        /// 启动 aria2c 并监控输出直至进程退出（工作线程中运行）。
        private void RunTask(DownloadTask t)
        {
            try
            {
                Process p = new Process();
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = _bin;
                psi.Arguments = JoinArgs(BuildArgs(t, _cfg));
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                p.StartInfo = psi;

                List<string> errTail = new List<string>();
                object tailLock = new object();
                DataReceivedEventHandler handler = delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    HandleLine(t, e.Data, errTail, tailLock);
                };
                p.OutputDataReceived += handler;
                p.ErrorDataReceived += handler;

                try
                {
                    p.Start();
                }
                catch (Exception ex)
                {
                    Fail(t, ex.Message);
                    return;
                }

                lock (_mu) { t.Proc = p; }
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                // .NET 4.0+ 的无参 WaitForExit 会等待异步输出读取完成
                p.WaitForExit();

                bool removeReq;
                bool pauseReq;
                lock (_mu)
                {
                    t.Proc = null;
                    removeReq = t.RemoveRequested;
                    pauseReq = t.PauseRequested;
                }

                if (removeReq)
                {
                    SetStatus(t, TaskStatus.Removed);
                }
                else if (pauseReq)
                {
                    SetStatus(t, TaskStatus.Paused);
                }
                else if (p.ExitCode != 0)
                {
                    string joined;
                    lock (tailLock)
                    {
                        joined = string.Join("\n", errTail.ToArray());
                    }
                    if (joined == "") joined = "exit code " + p.ExitCode.ToString(CultureInfo.InvariantCulture);
                    t.Error = joined;
                    Finish(t, TaskStatus.Failed);
                }
                else
                {
                    lock (_mu)
                    {
                        // 成功结束时 aria2c 最后一行进度即 100%
                        if (t.Total > 0)
                        {
                            t.Completed = t.Total;
                            t.Percent = 100;
                        }
                    }
                    Finish(t, TaskStatus.Completed);
                }
            }
            finally
            {
                lock (_mu)
                {
                    _running--;
                    PumpLocked();
                }
            }
        }

        private void HandleLine(DownloadTask t, string line, List<string> errTail, object tailLock)
        {
            ProgressUpdate u;
            if (Parser.ParseProgressLine(line, out u))
            {
                ApplyProgress(t, u);
                return;
            }
            string s = (line == null ? "" : line).Trim();
            if (s != "" && LooksLikeError(s))
            {
                lock (tailLock)
                {
                    errTail.Add(s);
                    if (errTail.Count > 5) errTail.RemoveAt(0);
                }
            }
        }

        private void ApplyProgress(DownloadTask t, ProgressUpdate u)
        {
            lock (_mu)
            {
                t.Completed = u.Completed;
                if (u.Total > 0) t.Total = u.Total;
                if (u.Percent > 0) t.Percent = u.Percent;
                else if (t.Total > 0 && t.Completed > 0) t.Percent = (double)t.Completed / (double)t.Total * 100.0;
                t.Speed = u.Speed;
                t.Eta = u.Eta;
                EmitLocked(t.Id, t.Status);
            }
        }

        private void Fail(DownloadTask t, string err)
        {
            t.Error = err;
            SetStatus(t, TaskStatus.Failed);
            Archive(t, "失败");
        }

        private void Finish(DownloadTask t, TaskStatus s)
        {
            t.FinishedAt = DateTime.Now;
            SetStatus(t, s);
            Archive(t, s == TaskStatus.Completed ? "已完成" : "失败");
        }

        private void Archive(DownloadTask t, string status)
        {
            if (_hist == null) return;
            HistoryEntry e = new HistoryEntry();
            e.Url = t.Url;
            e.Name = t.DisplayName();
            e.Dir = t.Dir;
            e.Size = t.Completed;
            e.Status = status;
            _hist.Add(e);
        }

        private static readonly string[] ErrorKeywords =
            { "error", "failed", "exception", "无法", "拒绝", "unreachable", "timeout" };

        private static bool LooksLikeError(string s)
        {
            string lower = s.ToLowerInvariant();
            foreach (string kw in ErrorKeywords)
            {
                if (lower.IndexOf(kw, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// 组装 aria2c 命令行参数（与 Go 版逐条一致）。
        internal static List<string> BuildArgs(DownloadTask t, Config cfg)
        {
            List<string> args = new List<string>();
            args.Add(t.Url);
            args.Add("--dir=" + t.Dir);
            args.Add("--continue=true");
            args.Add("--summary-interval=0");
            args.Add("--auto-save-interval=10");
            args.Add("--console-log-level=warn");
            args.Add("--split=" + cfg.Split.ToString(CultureInfo.InvariantCulture));
            args.Add("--max-connection-per-server=" + cfg.Split.ToString(CultureInfo.InvariantCulture));
            args.Add("--max-concurrent-downloads=" + cfg.Split.ToString(CultureInfo.InvariantCulture));
            args.Add("--check-integrity=true");
            if (!string.IsNullOrEmpty(t.Output)) args.Add("--out=" + t.Output);
            if (!string.IsNullOrEmpty(cfg.SpeedLimit)) args.Add("--max-overall-download-limit=" + cfg.SpeedLimit);
            string lower = (t.Url == null ? "" : t.Url).ToLowerInvariant();
            if (t.IsMagnet() || lower.EndsWith(".torrent"))
            {
                // BT/磁力：会话记录与种子参数
                args.Add("--save-session=aria2.session");
                args.Add("--bt-metadata-only=false");
                args.Add("--seed-ratio=0");
                args.Add("--bt-stop-time-limit=0");
                args.Add("--follow-torrent=mem");
            }
            return args;
        }

        /// 引用并拼接为命令行字符串（.NET 4.8 无 ArgumentList，需手写 MSVCRT 规则）。
        internal static string JoinArgs(List<string> args)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(QuoteArg(args[i]));
            }
            return sb.ToString();
        }

        /// 无特殊字符原样返回；否则按 MSVCRT 规则包裹双引号（含引号/尾部反斜杠转义）。
        internal static string QuoteArg(string s)
        {
            s = s == null ? "" : s;
            if (s != "" && s.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) return s;
            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            int backslashes = 0;
            foreach (char c in s)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                    continue;
                }
                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }
                sb.Append(c);
            }
            if (backslashes > 0) sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 4: 运行确认通过**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：`76 passed, 0 failed`（63 + BuildArgs 8 + QuoteArg 5）。

---

### Task 9: UI 控件（TaskRow + TaskListPanel + Win32 占位符辅助）

**Files:**
- Create: `src/UI/Win32.cs`、`src/UI/TaskRow.cs`、`src/UI/TaskListPanel.cs`

**Interfaces:**
- Consumes: `Manager`/`DownloadTask`/`TaskStatus`/`TaskStatusText`/`Parser`（Task 6/8）、`TaskEvent`（Task 8）
- Produces:
  - `namespace AriaGui.UI`、`internal static class Win32`：`void SetPlaceholder(TextBox box, string text)`
  - `public sealed class TaskRow : UserControl`：`const int RowHeight = 80`
    构造 `TaskRow(string taskId, Action<string> onStart, Action<string> onPause, Action<string> onRemove)`
    `void Bind(DownloadTask task)`
  - `public sealed class TaskListPanel : Panel`：构造 `TaskListPanel(Manager mgr, Action<string> notify)`、`void Detach()`

**测试说明**：WinForms 控件无自动化测试框架（spec 已声明）；验证 = `build.ps1` 编译通过 + Task 11 整合冒烟。

- [ ] **Step 1: 创建 `src/UI/Win32.cs`**

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AriaGui.UI
{
    /// .NET 4.8 的 TextBox 无原生占位符，用 EM_SETCUEBANNER 实现（对应 Fyne 的 SetPlaceHolder）。
    internal static class Win32
    {
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void SetPlaceholder(TextBox box, string text)
        {
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }
    }
}
```

- [ ] **Step 2: 创建 `src/UI/TaskRow.cs`**

```csharp
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AriaGui.Engine;

namespace AriaGui.UI
{
    /// 单任务行：名称/状态/进度条/信息行与 开始/暂停/删除 按钮（可见性规则对齐 Go 版 task_list.go）。
    public sealed class TaskRow : UserControl
    {
        public const int RowHeight = 80;

        private readonly Label _title;
        private readonly Label _status;
        private readonly ProgressBar _bar;
        private readonly Label _info;
        private readonly Button _startBtn;
        private readonly Button _pauseBtn;
        private readonly Button _removeBtn;

        public string TaskId;

        public TaskRow(string taskId, Action<string> onStart, Action<string> onPause, Action<string> onRemove)
        {
            TaskId = taskId;
            BorderStyle = BorderStyle.FixedSingle;

            _title = new Label();
            _title.AutoEllipsis = true;

            _status = new Label();
            _status.TextAlign = ContentAlignment.MiddleRight;

            _bar = new ProgressBar();
            _bar.Minimum = 0;
            _bar.Maximum = 100;

            _info = new Label();
            _info.AutoEllipsis = true;

            _startBtn = new Button();
            _startBtn.Text = "开始";
            _startBtn.Click += delegate { onStart(TaskId); };

            _pauseBtn = new Button();
            _pauseBtn.Text = "暂停";
            _pauseBtn.Click += delegate { onPause(TaskId); };

            _removeBtn = new Button();
            _removeBtn.Text = "删除";
            _removeBtn.Click += delegate { onRemove(TaskId); };

            Controls.Add(_title);
            Controls.Add(_status);
            Controls.Add(_bar);
            Controls.Add(_info);
            Controls.Add(_startBtn);
            Controls.Add(_pauseBtn);
            Controls.Add(_removeBtn);
            Height = RowHeight;
            LayoutInner();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInner();
        }

        private void LayoutInner()
        {
            int w = ClientSize.Width;
            _title.SetBounds(6, 6, w - 120, 18);
            _status.SetBounds(w - 110, 6, 104, 18);
            _bar.SetBounds(6, 28, w - 12, 16);
            _info.SetBounds(6, 48, w - 250, 18);
            _startBtn.SetBounds(w - 224, 46, 68, 26);
            _pauseBtn.SetBounds(w - 152, 46, 68, 26);
            _removeBtn.SetBounds(w - 80, 46, 68, 26);
        }

        /// 把任务数据绑定到行控件（对应 Go 的 bindRow）。
        public void Bind(DownloadTask task)
        {
            _title.Text = task.DisplayName();
            _status.Text = TaskStatusText.ToText(task.Status);

            if (task.Total > 0)
            {
                _bar.Value = ClampPercent((int)task.Percent);
                _info.Text = string.Format(CultureInfo.InvariantCulture,
                    "{0} / {1}   {2}/s   剩余 {3}",
                    Parser.FormatSize(task.Completed), Parser.FormatSize(task.Total),
                    Parser.FormatSize(task.Speed), Parser.FormatEta(task.Eta));
            }
            else
            {
                _bar.Value = 0;
                _info.Text = string.Format(CultureInfo.InvariantCulture,
                    "已下载 {0}   {1}/s（总大小未知，BT 元数据获取中…）",
                    Parser.FormatSize(task.Completed), Parser.FormatSize(task.Speed));
            }
            if (task.Status == TaskStatus.Failed && !string.IsNullOrEmpty(task.Error))
                _info.Text = "错误: " + FirstLine(task.Error);

            if (task.Status == TaskStatus.Running)
            {
                _startBtn.Visible = false;
                _pauseBtn.Visible = true;
            }
            else if (task.Status == TaskStatus.Queued)
            {
                _startBtn.Visible = true;
                _startBtn.Text = "排队中";
                _startBtn.Enabled = false;
                _pauseBtn.Visible = true;
            }
            else
            {
                _pauseBtn.Visible = false;
                if (task.Status == TaskStatus.Completed)
                {
                    _startBtn.Visible = false;
                }
                else
                {
                    _startBtn.Visible = true;
                    _startBtn.Enabled = true;
                    if (task.Status == TaskStatus.Failed || task.Status == TaskStatus.Paused) _startBtn.Text = "继续";
                    else _startBtn.Text = "开始";
                }
            }
        }

        private static int ClampPercent(int p)
        {
            if (p < 0) return 0;
            if (p > 100) return 100;
            return p;
        }

        private static string FirstLine(string s)
        {
            int i = s.IndexOf('\n');
            if (i < 0) return s;
            return s.Substring(0, i);
        }
    }
}
```

- [ ] **Step 3: 创建 `src/UI/TaskListPanel.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using AriaGui.Engine;

namespace AriaGui.UI
{
    /// 任务列表：订阅 Manager 事件做增量更新；行宽随容器宽度、纵向手动布局。
    public sealed class TaskListPanel : Panel
    {
        private readonly Manager _mgr;
        private readonly Action<string> _notify;
        private readonly Dictionary<string, TaskRow> _rows = new Dictionary<string, TaskRow>();
        private readonly Dictionary<string, TaskStatus> _prev = new Dictionary<string, TaskStatus>();
        private readonly Action<TaskEvent> _handler;

        public TaskListPanel(Manager mgr, Action<string> notify)
        {
            _mgr = mgr;
            _notify = notify;
            AutoScroll = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            _handler = HandleEvent;
            _mgr.Subscribe(_handler);
        }

        /// 退订事件（主窗口关闭前调用）。
        public void Detach()
        {
            _mgr.Unsubscribe(_handler);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Sync();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RelayoutRows();
        }

        private void HandleEvent(TaskEvent ev)
        {
            // 事件在后台线程触发：封送到 UI 线程整体同步；句柄未建时忽略（OnHandleCreated 兜底）。
            if (!IsHandleCreated) return;
            try
            {
                BeginInvoke(new MethodInvoker(delegate { Sync(); }));
            }
            catch (InvalidOperationException)
            {
                // 句柄销毁竞态，忽略
            }
        }

        private void Sync()
        {
            List<DownloadTask> items = _mgr.Snapshot();
            HashSet<string> alive = new HashSet<string>();
            foreach (DownloadTask t in items)
            {
                alive.Add(t.Id);
                TaskRow row;
                if (!_rows.TryGetValue(t.Id, out row))
                {
                    row = new TaskRow(t.Id, _mgr.Start, _mgr.Pause, _mgr.Remove);
                    _rows[t.Id] = row;
                    Controls.Add(row);
                }
                row.Bind(t);
                DetectNotify(t);
            }

            List<string> dead = new List<string>();
            foreach (string id in _rows.Keys)
            {
                if (!alive.Contains(id)) dead.Add(id);
            }
            foreach (string id in dead)
            {
                TaskRow r = _rows[id];
                _rows.Remove(id);
                Controls.Remove(r);
                r.Dispose();
                _prev.Remove(id);
            }

            RelayoutRows();
        }

        /// 任务完成/失败跃迁时向状态栏发应用内通知。
        private void DetectNotify(DownloadTask t)
        {
            TaskStatus old;
            bool had = _prev.TryGetValue(t.Id, out old);
            _prev[t.Id] = t.Status;
            if (!had || old == t.Status) return;
            if (t.Status == TaskStatus.Completed) _notify("✔ " + t.DisplayName() + " 已完成");
            else if (t.Status == TaskStatus.Failed) _notify("✖ " + t.DisplayName() + " 失败");
        }

        private void RelayoutRows()
        {
            int width = ClientSize.Width - 2;
            if (VerticalScroll.Visible) width -= SystemInformation.VerticalScrollBarWidth;
            if (width < 120) width = 120;
            int y = 0;
            foreach (Control c in Controls)
            {
                c.SetBounds(0, y, width, TaskRow.RowHeight);
                y += TaskRow.RowHeight + 4;
            }
        }
    }
}
```

- [ ] **Step 4: 验证编译**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\build.ps1`
Expected：编译成功（Program 桩 + UI 控件一并编译），`aria-gui.exe` 生成。

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：`76 passed, 0 failed`（UI 文件不在测试编译集内，数量不变）。

---

### Task 10: UI 表单页（AddDialogForm + SettingsView + HistoryView）

**Files:**
- Create: `src/UI/AddDialogForm.cs`、`src/UI/SettingsView.cs`、`src/UI/HistoryView.cs`

**Interfaces:**
- Consumes: `Manager.AddTask`（Task 8）、`Config`/`HistoryStore`/`HistoryEntry`（Task 4/5）、`Parser.FormatSize`（Task 2）、`Win32.SetPlaceholder`（Task 9）
- Produces:
  - `namespace AriaGui.UI`、`public sealed class AddDialogForm : Form`：构造 `AddDialogForm(Engine.Manager mgr, Action<string> notify, string defaultDir)`，由主窗口以 `ShowDialog(owner)` 模态调用
  - `public sealed class SettingsView : Panel`：构造 `SettingsView(Config cfg, Action<string> notify)`
  - `public sealed class HistoryView : Panel`：构造 `HistoryView(HistoryStore store)`、`void Reload()`

**测试说明**：WinForms 控件无自动化测试框架（spec 已声明）；验证 = `build.ps1` 编译通过 + Task 11 整合冒烟。三个文件自带完整逻辑，不接受"占位先编译"。

- [ ] **Step 1: 创建 `src/UI/AddDialogForm.cs`**

```csharp
using System;
using System.Drawing;
using System.Windows.Forms;
using AriaGui.Engine;

namespace AriaGui.UI
{
    /// 添加下载对话框（对应 Go 版 add_dialog.go：确认后创建任务并自动入队）。
    public sealed class AddDialogForm : Form
    {
        private readonly Manager _mgr;
        private readonly Action<string> _notify;

        private readonly TextBox _urlBox;
        private readonly TextBox _dirBox;
        private readonly TextBox _nameBox;

        public AddDialogForm(Manager mgr, Action<string> notify, string defaultDir)
        {
            _mgr = mgr;
            _notify = notify;

            Text = "添加下载";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 200);

            Label urlLabel = new Label();
            urlLabel.Text = "下载链接";
            urlLabel.SetBounds(14, 22, 72, 18);

            _urlBox = new TextBox();
            _urlBox.SetBounds(90, 19, 408, 23);
            Win32.SetPlaceholder(_urlBox, "HTTP/FTP 链接、磁力链接(magnet:...) 或 .torrent 地址");

            Label dirLabel = new Label();
            dirLabel.Text = "保存目录";
            dirLabel.SetBounds(14, 62, 72, 18);

            _dirBox = new TextBox();
            _dirBox.SetBounds(90, 59, 318, 23);
            _dirBox.Text = defaultDir;

            Button browse = new Button();
            browse.Text = "浏览…";
            browse.SetBounds(414, 57, 84, 26);
            browse.Click += delegate { BrowseDir(); };

            Label nameLabel = new Label();
            nameLabel.Text = "文件名";
            nameLabel.SetBounds(14, 102, 72, 18);

            _nameBox = new TextBox();
            _nameBox.SetBounds(90, 99, 408, 23);
            Win32.SetPlaceholder(_nameBox, "可选，默认自动命名");

            Button ok = new Button();
            ok.Text = "开始下载";
            ok.SetBounds(314, 150, 92, 30);
            ok.Click += delegate { Confirm(); };

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(414, 150, 84, 30);
            cancel.DialogResult = DialogResult.Cancel;

            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(urlLabel);
            Controls.Add(_urlBox);
            Controls.Add(dirLabel);
            Controls.Add(_dirBox);
            Controls.Add(browse);
            Controls.Add(nameLabel);
            Controls.Add(_nameBox);
            Controls.Add(ok);
            Controls.Add(cancel);
        }

        private void BrowseDir()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _dirBox.Text = dlg.SelectedPath;
            }
        }

        /// 校验并创建任务：失败弹错并保持对话框打开（对应 Go 的 ShowError + return）。
        private void Confirm()
        {
            try
            {
                DownloadTask task = _mgr.AddTask(_urlBox.Text, _dirBox.Text, _nameBox.Text);
                _notify("已添加任务: " + task.DisplayName());
                DialogResult = DialogResult.OK; // 关闭对话框
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "添加下载", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
```

- [ ] **Step 2: 创建 `src/UI/SettingsView.cs`**

```csharp
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace AriaGui.UI
{
    /// 设置页：默认目录、并发数、限速、分片数（对应 Go 版 settings.go，校验文案逐字一致）。
    public sealed class SettingsView : Panel
    {
        private readonly Config _cfg;
        private readonly Action<string> _notify;

        private readonly TextBox _dirBox;
        private readonly TextBox _concBox;
        private readonly TextBox _speedBox;
        private readonly TextBox _splitBox;

        public SettingsView(Config cfg, Action<string> notify)
        {
            _cfg = cfg;
            _notify = notify;

            Label dirLabel = new Label();
            dirLabel.Text = "默认保存目录";
            dirLabel.SetBounds(14, 22, 92, 18);

            _dirBox = new TextBox();
            _dirBox.SetBounds(110, 19, 298, 23);
            _dirBox.Text = cfg.SaveDir;

            Button browse = new Button();
            browse.Text = "浏览…";
            browse.SetBounds(414, 17, 84, 26);
            browse.Click += delegate { BrowseDir(); };

            Label concLabel = new Label();
            concLabel.Text = "并发下载数(1-10)";
            concLabel.SetBounds(14, 62, 92, 18);

            _concBox = new TextBox();
            _concBox.SetBounds(110, 59, 80, 23);
            _concBox.Text = cfg.MaxConcurrent.ToString(CultureInfo.InvariantCulture);

            Label speedLabel = new Label();
            speedLabel.Text = "全局限速";
            speedLabel.SetBounds(14, 102, 92, 18);

            _speedBox = new TextBox();
            _speedBox.SetBounds(110, 99, 160, 23);
            _speedBox.Text = cfg.SpeedLimit;
            Win32.SetPlaceholder(_speedBox, "留空不限速，如 512K / 2M");

            Label splitLabel = new Label();
            splitLabel.Text = "每任务分片数";
            splitLabel.SetBounds(14, 142, 92, 18);

            _splitBox = new TextBox();
            _splitBox.SetBounds(110, 139, 80, 23);
            _splitBox.Text = cfg.Split.ToString(CultureInfo.InvariantCulture);

            Button save = new Button();
            save.Text = "保存设置";
            save.SetBounds(14, 185, 92, 30);
            save.Click += delegate { SaveSettings(); };

            Controls.Add(dirLabel);
            Controls.Add(_dirBox);
            Controls.Add(browse);
            Controls.Add(concLabel);
            Controls.Add(_concBox);
            Controls.Add(speedLabel);
            Controls.Add(_speedBox);
            Controls.Add(splitLabel);
            Controls.Add(_splitBox);
            Controls.Add(save);
        }

        private void BrowseDir()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _dirBox.Text = dlg.SelectedPath;
            }
        }

        /// 校验并保存：数字解析与 Go 的 strconv.Atoi 语义一致（允许前导符号、不允许空白）。
        private void SaveSettings()
        {
            int conc;
            if (!int.TryParse(_concBox.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out conc)
                || conc < 1 || conc > 10)
            {
                MessageBox.Show(this, "并发下载数需为 1-10 的整数", "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int split;
            if (!int.TryParse(_splitBox.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out split)
                || split < 1 || split > 16)
            {
                MessageBox.Show(this, "分片数需为 1-16 的整数", "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Config.ValidSpeedLimit(_speedBox.Text))
            {
                MessageBox.Show(this, "限速格式如 512K / 2M / 1G，或留空", "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _cfg.SaveDir = _dirBox.Text;
            _cfg.MaxConcurrent = conc;
            _cfg.SpeedLimit = _speedBox.Text;
            _cfg.Split = split;
            string err = _cfg.Save(); // 成功返回 null
            if (err != null)
            {
                MessageBox.Show(this, err, "设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _notify("设置已保存（限速与分片数对新任务生效）");
        }
    }
}
```

- [ ] **Step 3: 创建 `src/UI/HistoryView.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AriaGui.Engine;

namespace AriaGui.UI
{
    /// 历史记录页：两行条目（标题 + 详情斜体）与"清空历史"按钮（对应 Go 版 history.go）。
    public sealed class HistoryView : Panel
    {
        private readonly HistoryStore _store;
        private readonly ListBox _list;
        private Font _subFont;

        public HistoryView(HistoryStore store)
        {
            _store = store;

            _list = new ListBox();
            _list.Dock = DockStyle.Fill;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = 38; // 两行：标题 18px + 详情 16px
            _list.IntegralHeight = false;
            _list.DrawItem += DrawItem;

            _subFont = new Font(Font, FontStyle.Italic);

            Button clear = new Button();
            clear.Text = "清空历史";
            clear.SetBounds(8, 6, 92, 28);
            clear.Click += delegate { OnClearClicked(); };

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 40;
            bottom.Controls.Add(clear);

            // Dock 顺序：Fill 先加，Bottom 后加（WinForms 反序布局规则）。
            Controls.Add(_list);
            Controls.Add(bottom);
            Reload();
        }

        /// 重新读取历史并刷新（主窗口切到本页时调用）。
        public void Reload()
        {
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                List<HistoryEntry> items = _store.All();
                for (int i = 0; i < items.Count; i++)
                    _list.Items.Add(items[i]);
            }
            finally
            {
                _list.EndUpdate();
            }
        }

        private void OnClearClicked()
        {
            DialogResult r = MessageBox.Show(this, "确定删除全部历史记录？", "清空历史",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;
            _store.Clear();
            Reload();
        }

        /// 两行绘制：`[状态] 名称`，次行 `大小   时间   目录`。
        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _list.Items.Count) return;
            HistoryEntry it = (HistoryEntry)_list.Items[e.Index];

            e.DrawBackground();
            Rectangle b = e.Bounds;
            string title = "[" + it.Status + "] " + it.Name;
            string sub = string.Format(CultureInfo.InvariantCulture, "{0}   {1}   {2}",
                Parser.FormatSize(it.Size),
                it.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                it.Dir);

            TextRenderer.DrawText(e.Graphics, title, Font,
                new Rectangle(b.X + 4, b.Y + 2, b.Width - 8, 18), e.ForeColor,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, sub, _subFont,
                new Rectangle(b.X + 4, b.Y + 20, b.Width - 8, 16), SystemColors.GrayText,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (_subFont != null) _subFont.Dispose();
            _subFont = new Font(Font, FontStyle.Italic);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _subFont != null) _subFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
```

- [ ] **Step 4: 验证编译**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\build.ps1`
Expected：编译成功（Program 桩 + 全部 UI 文件一并编译），`aria-gui.exe` 生成。

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：`76 passed, 0 failed`（UI 文件不在测试编译集内，数量不变）。

---

### Task 11: 主窗口与入口（MainForm + 真实 Program.cs + 启动冒烟）

**Files:**
- Create: `src/UI/MainForm.cs`
- Modify: `src/Program.cs`（桩 → 真实入口）

**Interfaces:**
- Consumes: `Manager`（Task 8）、`TaskListPanel`/`HistoryView`/`SettingsView`/`AddDialogForm`（Task 9/10）、`Config`/`HistoryStore`（Task 4/5）
- Produces:
  - `public sealed class MainForm : Form`：构造 `MainForm(Manager mgr, Config cfg, HistoryStore hist)`
  - 真实 `Program.Main()`：`Config.Load()` → `HistoryStore.Load()` → `new Manager(...)`（失败 MessageBox + 退出码 1）→ `Application.Run(new MainForm(...))`

- [ ] **Step 1: 创建 `src/UI/MainForm.cs`**

```csharp
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AriaGui.Engine;

namespace AriaGui.UI
{
    /// 主窗口：工具栏 + 下载/历史/设置 三 Tab + 状态栏（对应 Go 版 window.go / toolbar.go）。
    public sealed class MainForm : Form
    {
        private readonly Manager _mgr;
        private readonly Config _cfg;
        private readonly TaskListPanel _taskList;
        private readonly HistoryView _historyV;
        private readonly ToolStripStatusLabel _statusLabel;
        private readonly Timer _timer;

        private DateTime _notifyAt = DateTime.MinValue;

        public MainForm(Manager mgr, Config cfg, HistoryStore hist)
        {
            _mgr = mgr;
            _cfg = cfg;

            Text = "Aria 下载器";
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(820, 560);
            StartPosition = FormStartPosition.CenterScreen;

            _taskList = new TaskListPanel(mgr, Notify);
            _taskList.Dock = DockStyle.Fill;

            _historyV = new HistoryView(hist);
            _historyV.Dock = DockStyle.Fill;

            SettingsView settings = new SettingsView(cfg, Notify);
            settings.Dock = DockStyle.Fill;

            TabPage pageDownload = new TabPage("下载");
            pageDownload.Controls.Add(_taskList);
            TabPage pageHistory = new TabPage("历史");
            pageHistory.Controls.Add(_historyV);
            TabPage pageSettings = new TabPage("设置");
            pageSettings.Controls.Add(settings);

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(pageDownload);
            tabs.TabPages.Add(pageHistory);
            tabs.TabPages.Add(pageSettings);
            tabs.SelectedIndexChanged += delegate
            {
                if (tabs.SelectedTab == pageHistory) _historyV.Reload();
            };

            ToolStrip toolStrip = new ToolStrip();
            toolStrip.GripStyle = ToolStripGripStyle.Hidden;
            ToolStripButton addBtn = new ToolStripButton("添加下载");
            addBtn.DisplayStyle = ToolStripItemDisplayStyle.Text;
            addBtn.Click += delegate { ShowAddDialog(); };
            ToolStripButton openBtn = new ToolStripButton("打开目录");
            openBtn.DisplayStyle = ToolStripItemDisplayStyle.Text;
            openBtn.Click += delegate { OpenSaveDir(); };
            toolStrip.Items.Add(addBtn);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(openBtn);

            StatusStrip statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel();
            _statusLabel.Text = "就绪";
            _statusLabel.Spring = true;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusStrip.Items.Add(_statusLabel);

            // Dock 顺序：Fill 先加，Top/Bottom 后加（WinForms 反序布局规则，否则 Fill 会遮住条带）。
            Controls.Add(tabs);
            Controls.Add(statusStrip);
            Controls.Add(toolStrip);

            // 状态栏每秒刷新（WinForms Timer 在 UI 线程触发）
            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { OnTick(); };
            _timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            // 退订 Manager 事件（对齐 Go 的 close intercept；不强杀 aria2c 子进程）
            _taskList.Detach();
            base.OnFormClosed(e);
        }

        /// 在状态栏显示一条应用内通知（5 秒内不被定时刷新覆盖）。
        private void Notify(string text)
        {
            _notifyAt = DateTime.Now;
            _statusLabel.Text = text;
        }

        /// 每秒刷新合计速度与运行中任务数（文案含 3 个空格，与 Go 版逐字一致）。
        private void OnTick()
        {
            if (DateTime.Now - _notifyAt < TimeSpan.FromSeconds(5)) return;
            long speed = _mgr.TotalSpeed();
            if (speed > 0)
            {
                int count = 0;
                foreach (DownloadTask t in _mgr.Snapshot())
                {
                    if (t.Status == TaskStatus.Running) count++;
                }
                _statusLabel.Text = string.Format(CultureInfo.InvariantCulture,
                    "下载中 {0} 个任务   合计 {1}/s", count, Parser.FormatSize(speed));
            }
            else
            {
                _statusLabel.Text = "就绪";
            }
        }

        private void ShowAddDialog()
        {
            using (AddDialogForm dlg = new AddDialogForm(_mgr, Notify, _cfg.SaveDir))
            {
                dlg.ShowDialog(this);
            }
        }

        private void OpenSaveDir()
        {
            try
            {
                Process.Start("explorer.exe", "\"" + _cfg.SaveDir + "\"");
            }
            catch
            {
                // 对齐 Go 版：启动 explorer 失败静默忽略
            }
        }
    }
}
```

- [ ] **Step 2: 替换 `src/Program.cs`（桩 → 真实入口）**

```csharp
using System;
using System.Windows.Forms;
using AriaGui.Engine;
using AriaGui.UI;

namespace AriaGui
{
    /// 入口：加载配置/历史 → 构造 Manager（含资源释放）→ 运行主窗口。
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Config cfg = Config.Load();
            HistoryStore hist = HistoryStore.Load();

            Manager mgr;
            try
            {
                mgr = new Manager(cfg, hist); // 构造内含 Embed.Extract，失败抛异常
            }
            catch (Exception ex)
            {
                // 对齐 Go 版：报错窗 + 退出码 1（如内存中资源缺失/临时目录不可写）
                MessageBox.Show(ex.Message, "Aria 下载器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
                return;
            }

            Application.Run(new MainForm(mgr, cfg, hist));
        }
    }
}
```

- [ ] **Step 3: 编译产物**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\build.ps1`
Expected：输出 `生成 ...aria-gui.exe（5,6xx,xxx 字节）`，退出码 0。

- [ ] **Step 4: 回归自测**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：`76 passed, 0 failed`。

- [ ] **Step 5: 启动冒烟（窗口存活 + 资源释放哈希）**

```powershell
$root = "k:\vibecode\sys\aria-gui"
$cache = Join-Path $env:TEMP "aria-gui"
if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }   # 清旧缓存，验证首次释放路径
$p = Start-Process -FilePath (Join-Path $root "aria-gui.exe") -PassThru
$deadline = (Get-Date).AddSeconds(10)
while ((Get-Date) -lt $deadline -and $p.MainWindowTitle -eq "") { Start-Sleep -Milliseconds 300; $p.Refresh() }
"Alive: " + (-not $p.HasExited)
"Title: " + $p.MainWindowTitle
"Hash:  " + (Get-FileHash (Join-Path $cache "aria2c.exe")).Hash
Stop-Process -Id $p.Id
```

Expected：
- `Alive: True`
- `Title: Aria 下载器`
- `Hash:  BE2099C214F63A3CB4954B09A0BECD6E2E34660B886D4C898D260FEBFE9D70C2`

若 `Title` 为空但进程存活 → 弹出了启动错误 MessageBox（多半资源释放失败）：检查 `%TEMP%\aria-gui\` 是否可建目录、`assets\aria2c\aria2c.exe` 是否存在。

---

### Task 12: 删除 Go 源码 + DoD 全量验收

**Files:**
- Delete: 全部 Go 源码、`go.mod`/`go.sum`、根目录 `aria2c.exe`、`internal\` 与 `ui\` 目录树
- Verify: 终态目录结构 + spec §8 验收清单

**Interfaces:**
- Consumes: Task 1~11 全部成果（此时 Go 源码已无任何引用方，删除安全）
- Produces: 纯 C# 终态工程

- [ ] **Step 1: 逐个删除 Go 文件（每个文件一次 DeleteFile 调用；本任务之前不得执行）**

```
k:\vibecode\sys\aria-gui\main.go
k:\vibecode\sys\aria-gui\internal\config\config.go
k:\vibecode\sys\aria-gui\internal\engine\embed.go
k:\vibecode\sys\aria-gui\internal\engine\manager.go
k:\vibecode\sys\aria-gui\internal\engine\parser.go
k:\vibecode\sys\aria-gui\internal\engine\parser_test.go
k:\vibecode\sys\aria-gui\internal\engine\task.go
k:\vibecode\sys\aria-gui\internal\engine\proc_windows.go
k:\vibecode\sys\aria-gui\internal\engine\aria2c\aria2c.exe   # 已复制为 assets\aria2c\aria2c.exe，删副本
k:\vibecode\sys\aria-gui\internal\history\store.go
k:\vibecode\sys\aria-gui\ui\window.go
k:\vibecode\sys\aria-gui\ui\task_list.go
k:\vibecode\sys\aria-gui\ui\toolbar.go
k:\vibecode\sys\aria-gui\ui\add_dialog.go
k:\vibecode\sys\aria-gui\ui\history.go
k:\vibecode\sys\aria-gui\ui\settings.go
k:\vibecode\sys\aria-gui\go.mod
k:\vibecode\sys\aria-gui\go.sum
k:\vibecode\sys\aria-gui\aria2c.exe                          # 根目录副本（哈希与 assets 相同）
```

- [ ] **Step 2: 删除空目录**

先确认两棵目录树已无文件：

```powershell
(Get-ChildItem k:\vibecode\sys\aria-gui\internal, k:\vibecode\sys\aria-gui\ui -Recurse -File | Measure-Object).Count
```
Expected：`0`

再删除（此时目录全空，安全）：

```powershell
Remove-Item -Recurse k:\vibecode\sys\aria-gui\internal
Remove-Item -Recurse k:\vibecode\sys\aria-gui\ui
```

- [ ] **Step 3: 残留检查**

```powershell
(Get-ChildItem k:\vibecode\sys\aria-gui -Recurse -Filter *.go -File | Measure-Object).Count   # 期望 0
Test-Path k:\vibecode\sys\aria-gui\go.mod        # 期望 False
Test-Path k:\vibecode\sys\aria-gui\go.sum        # 期望 False
Test-Path k:\vibecode\sys\aria-gui\aria2c.exe    # 期望 False（根目录；assets 内的保留）
Test-Path k:\vibecode\sys\aria-gui\internal      # 期望 False
Test-Path k:\vibecode\sys\aria-gui\ui            # 期望 False
Test-Path k:\vibecode\sys\aria-gui\assets\aria2c\aria2c.exe   # 期望 True
```

- [ ] **Step 4: 删除后重新构建与自测（证明无隐藏依赖）**

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\build.ps1`
Expected：编译成功，`aria-gui.exe` 生成（>5MB）。

Run：`powershell -NoProfile -ExecutionPolicy Bypass -File k:\vibecode\sys\aria-gui\build\run-tests.ps1`
Expected：`76 passed, 0 failed`。

- [ ] **Step 5: 终态目录对照**

```
aria-gui/
├─ assets/aria2c/aria2c.exe          # 内嵌资源
├─ build/{build.ps1, run-tests.ps1, get_aria2c.ps1}
├─ docs/superpowers/{specs/, plans/}
├─ src/{Program.cs, Json.cs, Config.cs, History.cs}
├─ src/Engine/{Parser.cs, DownloadTask.cs, Embed.cs, Manager.cs}
├─ src/UI/{Win32.cs, TaskRow.cs, TaskListPanel.cs, AddDialogForm.cs, SettingsView.cs, HistoryView.cs, MainForm.cs}
├─ tests/ParserTests.cs
├─ .gitignore
└─ aria-gui.exe                      # 构建产物（不提交）
```

- [ ] **Step 6: DoD 人工验收清单（GUI 无法自动化，交给用户确认）**

自动化部分（DoD 1/2/3/8/9）已在 Step 3/4 与 Task 11 Step 5 验证。以下需人工操作 `aria-gui.exe`：

1. 双击启动：窗口 820×560 居中、标题"Aria 下载器"、无控制台窗口
2. 工具栏"添加下载"：填入真实 HTTP 直链（小文件即可）→"开始下载"→ 行出现且进度/速度/ETA 刷新
3. 该行"暂停"→ 状态"已暂停"；"继续"→ 进度续涨；"删除"→ 行消失
4. 完成后切"历史"页：出现 `[已完成]` 条目（含大小/时间/目录）；"清空历史"→ 确认→ 列表清空
5. 设置页改并发数/保存目录 →"保存设置"→ 状态栏提示"设置已保存（限速与分片数对新任务生效）"→ 重启 exe 值保留
6. 打开 `%AppData%\aria-gui\config.json`：键名 `save_dir`/`max_concurrent`/`speed_limit`/`split`，2 空格缩进，UTF-8 无 BOM
7. 打开 `%AppData%\aria-gui\history.json`：数组格式与 Go 版字段一致（`url`/`name`/`dir`/`size`/`status`/`at`）

全部通过 → 重构完成。
