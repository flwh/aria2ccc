using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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
            RunJsonCases();
            RunSpeedLimitCases();
            RunConfigCases();
            RunTrackerCases();
            RunHistoryCases();
            RunDisplayNameCases();
            RunManagerArgsCases();
            RunQuoteArgCases();
            RunSnifferCases();
            RunM3u8Cases();
            RunManagerPlaylistE2E();
            RunManagerProxyE2E();
            RunAria2VersionCases();

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
            Check("Json 往返-布尔", back.GetBool("b", false) && !back.GetBool("n", false) && back.GetBool("none", true), "");

            JsonObject cjk = new JsonObject();
            cjk.Set("dir", "C:\\下载\\目录");
            Check("Json CJK 原样", cjk.ToJson() == "{\n  \"dir\": \"C:\\\\下载\\\\目录\"\n}", "got=" + cjk.ToJson());

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
            // 合法 JSON 样例 → 正确读取
            string raw = "{\n  \"save_dir\": \"D:\\\\dl\",\n  \"max_concurrent\": 5,\n  \"speed_limit\": \"2M\",\n  \"split\": 8,\n  \"rpc_enabled\": true,\n  \"rpc_port\": 7200,\n  \"rpc_secret\": \"tok\",\n  \"sniff_enabled\": false,\n  \"proxy\": \"http://127.0.0.1:7890\"\n}";
            Config cfg = Config.Default();
            Config.ApplyJson(cfg, JsonObject.ParseObject(raw));
            Check("Config 读取样例",
                cfg.SaveDir == "D:\\dl" && cfg.MaxConcurrent == 5 && cfg.SpeedLimit == "2M" && cfg.Split == 8,
                cfg.SaveDir + "|" + cfg.MaxConcurrent + "|" + cfg.SpeedLimit + "|" + cfg.Split);
            Check("Config 缺失 trackers 键保留默认", cfg.Trackers == Config.BuiltinTrackers(), "got=" + cfg.Trackers);
            Check("Config 读取 RPC", cfg.RpcEnabled && cfg.RpcPort == 7200 && cfg.RpcSecret == "tok",
                cfg.RpcEnabled + "|" + cfg.RpcPort + "|" + cfg.RpcSecret);
            Check("Config 读取嗅探开关", !cfg.SniffEnabled, "got=" + cfg.SniffEnabled);
            Check("Config 读取代理", cfg.Proxy == "http://127.0.0.1:7890", "got=" + cfg.Proxy);

            // 缺失 RPC 键 → 保留默认（关闭 / 6800 / 空密钥）
            Config defRpc = Config.Default();
            Config.ApplyJson(defRpc, JsonObject.ParseObject("{\"split\": 2}"));
            Check("Config 缺失 RPC 键保留默认",
                !defRpc.RpcEnabled && defRpc.RpcPort == 6800 && defRpc.RpcSecret == "",
                defRpc.RpcEnabled + "|" + defRpc.RpcPort + "|" + defRpc.RpcSecret);
            Check("Config 缺失 sniff_enabled 键保留默认 true", defRpc.SniffEnabled, "got=" + defRpc.SniffEnabled);

            // 越界值被钳制
            Config cfg2 = Config.Default();
            Config.ApplyJson(cfg2, JsonObject.ParseObject("{\"max_concurrent\": 99, \"split\": 0, \"speed_limit\": \"bad\", \"rpc_port\": 99}"));
            Check("Config 钳制",
                cfg2.MaxConcurrent == 10 && cfg2.Split == 1 && cfg2.SpeedLimit == "" && cfg2.RpcPort == 1024,
                cfg2.MaxConcurrent + "|" + cfg2.Split + "|" + cfg2.SpeedLimit + "|" + cfg2.RpcPort);

            Config cfg2b = Config.Default();
            Config.ApplyJson(cfg2b, JsonObject.ParseObject("{\"rpc_port\": 70000}"));
            Check("Config rpc_port 上限钳制", cfg2b.RpcPort == 65535, "got=" + cfg2b.RpcPort);

            // 代理地址校验
            Check("ValidProxy 空串直连", Config.ValidProxy(""), "");
            Check("ValidProxy http", Config.ValidProxy("http://127.0.0.1:7890"), "");
            Check("ValidProxy socks5", Config.ValidProxy("socks5://127.0.0.1:1080"), "");
            Check("ValidProxy 无 scheme 拒绝", !Config.ValidProxy("127.0.0.1:7890"), "");
            Check("ValidProxy ftp 拒绝", !Config.ValidProxy("ftp://127.0.0.1"), "");

            // 非法代理值经 ApplyJson 的钳制被清空
            Config cfg2c = Config.Default();
            Config.ApplyJson(cfg2c, JsonObject.ParseObject("{\"proxy\": \"not a url\"}"));
            Check("Config 非法代理清空", cfg2c.Proxy == "", "got=" + cfg2c.Proxy);

            // RPC JSON 往返（密钥含引号转义）
            Config rpcRt = Config.Default();
            rpcRt.RpcEnabled = true;
            rpcRt.RpcPort = 7000;
            rpcRt.RpcSecret = "s\"x";
            rpcRt.SniffEnabled = false;
            rpcRt.Proxy = "socks5://127.0.0.1:1080";
            Config rpcBack = Config.Default();
            Config.ApplyJson(rpcBack, JsonObject.ParseObject(rpcRt.ToJson()));
            Check("Config RPC JSON 往返",
                rpcBack.RpcEnabled && rpcBack.RpcPort == 7000 && rpcBack.RpcSecret == "s\"x",
                rpcBack.RpcEnabled + "|" + rpcBack.RpcPort + "|" + rpcBack.RpcSecret);
            Check("Config 嗅探 JSON 往返", !rpcBack.SniffEnabled, "got=" + rpcBack.SniffEnabled);
            Check("Config 代理 JSON 往返", rpcBack.Proxy == "socks5://127.0.0.1:1080", "got=" + rpcBack.Proxy);

            // 写出键序稳定
            Config cfg3 = Config.Default();
            cfg3.SaveDir = "C:\\x";
            cfg3.MaxConcurrent = 3;
            cfg3.SpeedLimit = "";
            cfg3.Split = 4;
            cfg3.Trackers = "";
            cfg3.RpcEnabled = false;
            cfg3.RpcPort = 6800;
            cfg3.RpcSecret = "";
            cfg3.SniffEnabled = true;
            cfg3.Proxy = "";
            Check("Config 写出格式",
                cfg3.ToJson() == "{\n  \"save_dir\": \"C:\\\\x\",\n  \"max_concurrent\": 3,\n  \"speed_limit\": \"\",\n  \"split\": 4,\n  \"trackers\": \"\",\n  \"rpc_enabled\": false,\n  \"rpc_port\": 6800,\n  \"rpc_secret\": \"\",\n  \"sniff_enabled\": true,\n  \"proxy\": \"\"\n}",
                "got=" + cfg3.ToJson().Replace("\n", "\\n"));

            // 数据目录：程序所在目录下 config 子目录（便携式，不写用户目录）
            string dataDir = Config.DataDir();
            string cfgPath = Config.ConfigPath();
            Check("Config 数据目录位于程序目录下方",
                dataDir != null && cfgPath != null
                && dataDir.StartsWith(AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase)
                && dataDir.Replace('\\', '/').EndsWith("/config")
                && cfgPath == Path.Combine(dataDir, "config.json"),
                "dir=" + dataDir + " cfg=" + cfgPath);
        }

        private static void RunTrackerCases()
        {
            Check("Trackers 内置默认非空", Config.Default().Trackers.IndexOf("udp://", StringComparison.Ordinal) >= 0, "");

            string norm = Config.NormalizeTrackers("  a \r\n\r\nb\n a \n\t\n c ");
            Check("Trackers 规范化", norm == "a\nb\nc", "got=" + norm.Replace("\n", "|"));
            Check("Trackers 规范化-空输入", Config.NormalizeTrackers(null) == "" && Config.NormalizeTrackers(" \n \r\n") == "", "");

            Config cfg = Config.Default();
            cfg.Trackers = "udp://x:1/announce\nudp://y:2/announce";
            Check("TrackerArg 逗号连接", cfg.TrackerArg() == "udp://x:1/announce,udp://y:2/announce", "got=" + cfg.TrackerArg());
            cfg.Trackers = "";
            Check("TrackerArg 空列表", cfg.TrackerArg() == "", "got=" + cfg.TrackerArg());

            // JSON 往返：写出 → 读回一致
            cfg.Trackers = "udp://p:3/announce\nudp://q:4/announce";
            Config back = Config.Default();
            Config.ApplyJson(back, JsonObject.ParseObject(cfg.ToJson()));
            Check("Trackers JSON 往返", back.Trackers == "udp://p:3/announce\nudp://q:4/announce",
                "got=" + back.Trackers.Replace("\n", "|"));

            // 显式空串尊重用户清空意图
            Config empty = Config.Default();
            Config.ApplyJson(empty, JsonObject.ParseObject("{\"trackers\": \"\"}"));
            Check("Trackers 空串尊重清空", empty.Trackers == "", "got=" + empty.Trackers);
        }

        private static void RunHistoryCases()
        {
            // ISO 8601 日期样例（含带时区偏移与无偏移两种 at）
            string raw = "[\n  {\n    \"url\": \"https://example.com/a.zip\",\n    \"name\": \"a.zip\",\n    \"dir\": \"D:\\\\dl\",\n    \"size\": 1048576,\n    \"status\": \"已完成\",\n    \"at\": \"2026-09-27T12:00:00.1234567+08:00\"\n  },\n  {\n    \"url\": \"https://example.com/b.zip\",\n    \"name\": \"b.zip\",\n    \"dir\": \"D:\\\\dl\",\n    \"size\": 0,\n    \"status\": \"失败\",\n    \"at\": \"2026-09-26T08:30:00\"\n  }\n]";
            List<HistoryEntry> sample = HistoryStore.Deserialize(raw);
            Check("History 读取样例",
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

        private static void RunManagerArgsCases()
        {
            Config cfg = Config.Default();
            cfg.Split = 4;

            DownloadTask http = new DownloadTask();
            http.Url = "https://example.com/f.zip";
            http.Dir = "D:\\dl";
            List<string> a = Manager.BuildArgs(http, cfg, false);
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
            Check("BuildArgs 含 out", Manager.BuildArgs(withOut, cfg, false).Contains("--out=renamed.zip"), "");

            cfg.SpeedLimit = "2M";
            Check("BuildArgs 含限速", Manager.BuildArgs(http, cfg, false).Contains("--max-overall-download-limit=2M"), "");

            DownloadTask mag = new DownloadTask();
            mag.Url = "magnet:?xt=urn:btih:abc";
            mag.Dir = "D:\\dl";
            List<string> b = Manager.BuildArgs(mag, cfg, false);
            Check("BuildArgs BT 参数",
                b.Contains("--save-session=aria2.session") && b.Contains("--bt-metadata-only=false")
                && b.Contains("--seed-ratio=0") && b.Contains("--bt-stop-time-limit=0")
                && b.Contains("--follow-torrent=mem"), "");

            DownloadTask tor = new DownloadTask();
            tor.Url = "https://example.com/a.TORRENT";
            tor.Dir = "D:\\dl";
            Check("BuildArgs .torrent 分支", Manager.BuildArgs(tor, cfg, false).Contains("--follow-torrent=mem"), "");

            // tracker 列表：默认配置下 BT 任务携带 --bt-tracker，HTTP 任务不带
            Check("BuildArgs BT 含默认 tracker",
                b.Contains("--bt-tracker=" + cfg.TrackerArg()) && cfg.TrackerArg().Length > 0,
                "arg=" + cfg.TrackerArg());
            bool httpHasBt = false;
            foreach (string s in a)
                if (s.StartsWith("--bt-tracker", StringComparison.Ordinal)) httpHasBt = true;
            Check("BuildArgs HTTP 无 tracker 参数", !httpHasBt, "");

            // 自定义与清空
            cfg.Trackers = "udp://t1:1/announce\nudp://t2:2/announce";
            DownloadTask mag2 = new DownloadTask();
            mag2.Url = "magnet:?xt=urn:btih:def";
            mag2.Dir = "D:\\dl";
            Check("BuildArgs 自定义 tracker",
                Manager.BuildArgs(mag2, cfg, false).Contains("--bt-tracker=udp://t1:1/announce,udp://t2:2/announce"), "");

            cfg.Trackers = "";
            bool anyBt = false;
            foreach (string s in Manager.BuildArgs(mag2, cfg, false))
                if (s.StartsWith("--bt-tracker", StringComparison.Ordinal)) anyBt = true;
            Check("BuildArgs 清空 tracker 后无参数", !anyBt, "");

            // RPC：仅 useRpc=true 的任务附加；端口与密钥随配置
            cfg.RpcEnabled = true;
            cfg.RpcPort = 7200;
            cfg.RpcSecret = "tok-1";
            List<string> rpcOn = Manager.BuildArgs(http, cfg, true);
            Check("BuildArgs RPC 参数",
                rpcOn.Contains("--enable-rpc=true") && rpcOn.Contains("--rpc-listen-port=7200")
                && rpcOn.Contains("--rpc-allow-origin-all=true") && rpcOn.Contains("--rpc-secret=tok-1"), "");

            bool rpcOff = false;
            foreach (string s in Manager.BuildArgs(http, cfg, false))
                if (s.StartsWith("--enable-rpc", StringComparison.Ordinal) || s.StartsWith("--rpc-listen-port", StringComparison.Ordinal)) rpcOff = true;
            Check("BuildArgs 未持有 RPC 时不附加", !rpcOff, "");

            cfg.RpcSecret = "";
            bool secretAdded = false;
            foreach (string s in Manager.BuildArgs(http, cfg, true))
                if (s.StartsWith("--rpc-secret", StringComparison.Ordinal)) secretAdded = true;
            Check("BuildArgs 密钥为空时无 rpc-secret", !secretAdded, "");

            // 网络代理：非空附加 --all-proxy；清空后无参数
            cfg.Proxy = "http://127.0.0.1:7890";
            List<string> proxyOn = Manager.BuildArgs(http, cfg, false);
            Check("BuildArgs 代理参数", proxyOn.Contains("--all-proxy=http://127.0.0.1:7890"), "");

            cfg.Proxy = "";
            bool anyProxy = false;
            foreach (string s in Manager.BuildArgs(http, cfg, false))
                if (s.StartsWith("--all-proxy", StringComparison.Ordinal)) anyProxy = true;
            Check("BuildArgs 无代理无参数", !anyProxy, "");
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

        /// Sniffer 集成：真机起服务（端口 0 由系统分配），HTTP 客户端走全流程。
        private static void RunSnifferCases()
        {
            List<string> received = null;
            ManualResetEvent got = new ManualResetEvent(false);
            Sniffer s = new Sniffer(0, delegate(List<string> urls)
            {
                received = urls;
                got.Set();
            });
            s.Start();
            int port = s.Port;
            try
            {
                string baseUrl = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);

                string ping = null;
                bool pingOk = false;
                try { ping = HttpGet(baseUrl + "/ping"); pingOk = ping.Contains("\"ok\":true"); } catch { }
                Check("Sniffer ping", pingOk, "resp=" + (ping == null ? "null" : ping));

                string resp;
                int code = HttpPost(baseUrl + "/add",
                    "https://example.com/a.mp4\nnot a url\nmagnet:?xt=urn:btih:abc\nhttps://example.com/a.mp4\n",
                    out resp);
                bool awaited = got.WaitOne(2000);
                Check("Sniffer add 状态码", code == 200, "code=" + code);
                Check("Sniffer add 回调触发", awaited && received != null, "awaited=" + awaited);
                Check("Sniffer add 去重与过滤",
                    received != null && received.Count == 2
                    && received[0] == "https://example.com/a.mp4"
                    && received[1] == "magnet:?xt=urn:btih:abc",
                    received == null ? "null" : string.Join("|", received.ToArray()));

                code = HttpPost(baseUrl + "/add", "plain text only", out resp);
                Check("Sniffer add 无合法链接 400", code == 400, "code=" + code);

                code = HttpPost(baseUrl + "/add", "", out resp);
                Check("Sniffer add 空 body 400", code == 400, "code=" + code);

                Check("Sniffer OPTIONS 预检 204", HttpMethodStatus(baseUrl + "/add", "OPTIONS") == 204, "");
                Check("Sniffer 未知路径 404", HttpGetStatus(baseUrl + "/nope") == 404, "");
            }
            finally
            {
                s.Stop();
            }
        }

        private static string HttpGet(string url)
        {
            using (WebClient wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                return wc.DownloadString(url);
            }
        }

        private static int HttpGetStatus(string url)
        {
            try
            {
                HttpGet(url);
                return 200;
            }
            catch (WebException ex)
            {
                HttpWebResponse r = ex.Response as HttpWebResponse;
                if (r == null) return -1;
                int code = (int)r.StatusCode;
                r.Close();
                return code;
            }
        }

        private static int HttpMethodStatus(string url, string method)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = 5000;
            try
            {
                using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
                    return (int)r.StatusCode;
            }
            catch (WebException ex)
            {
                HttpWebResponse r = ex.Response as HttpWebResponse;
                if (r == null) return -1;
                int code = (int)r.StatusCode;
                r.Close();
                return code;
            }
        }

        private static int HttpPost(string url, string body, out string resp)
        {
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.ContentType] = "text/plain;charset=UTF-8";
                wc.Encoding = Encoding.UTF8;
                try
                {
                    resp = wc.UploadString(url, body);
                    return 200;
                }
                catch (WebException ex)
                {
                    resp = "";
                    HttpWebResponse r = ex.Response as HttpWebResponse;
                    if (r == null) return -1;
                    int code = (int)r.StatusCode;
                    r.Close();
                    return code;
                }
            }
        }

        // ---- m3u8 ----

        private static void RunM3u8Cases()
        {
            string baseUrl = "http://cdn.example.com/media/v/index.m3u8";

            // 媒体清单：相对/绝对 URL 解析
            PlaylistInfo pi = M3u8.Parse(
                "#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:10\n" +
                "#EXTINF:9.9,\nseg0.ts\n#EXTINF:9.9,\nhttp://cdn2.example.com/seg1.ts\n#EXT-X-ENDLIST\n",
                baseUrl);
            Check("M3u8 媒体清单分片数", pi.Segments.Count == 2 && !pi.IsMaster, "n=" + pi.Segments.Count);
            Check("M3u8 相对 URL 解析", pi.Segments[0] == "http://cdn.example.com/media/v/seg0.ts", pi.Segments[0]);
            Check("M3u8 绝对 URL 保留", pi.Segments[1] == "http://cdn2.example.com/seg1.ts", pi.Segments[1]);

            // master：选最高带宽变体
            PlaylistInfo master = M3u8.Parse(
                "#EXTM3U\n" +
                "#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360\nlow.m3u8\n" +
                "#EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720\nhigh.m3u8\n",
                baseUrl);
            Check("M3u8 master 识别", master.IsMaster && master.VariationUrl.Count == 2, "v=" + master.VariationUrl.Count);
            Check("M3u8 最高带宽变体", master.BestVariation() == "http://cdn.example.com/media/v/high.m3u8", master.BestVariation());

            // 加密检测
            PlaylistInfo enc = M3u8.Parse(
                "#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"key.bin\"\n#EXTINF:5,\na.ts\n", baseUrl);
            Check("M3u8 加密检测", enc.Encrypted, "");
            PlaylistInfo noEnc = M3u8.Parse(
                "#EXTM3U\n#EXT-X-KEY:METHOD=NONE\n#EXTINF:5,\na.ts\n", baseUrl);
            Check("M3u8 METHOD=NONE 不加密", !noEnc.Encrypted, "");

            // EXT-X-MAP：init 段并入首位
            PlaylistInfo map = M3u8.Parse(
                "#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:5,\nseg1.m4s\n", baseUrl);
            Check("M3u8 MAP 解析", map.HasMap && map.MapUri == "http://cdn.example.com/media/v/init.mp4", map.MapUri);
            List<string> entries = M3u8.BuildEntries(map);
            Check("M3u8 MAP 条目首位", entries.Count == 2 && entries[0] == map.MapUri, "n=" + entries.Count);
            List<string> plainEntries = M3u8.BuildEntries(pi);
            Check("M3u8 无 MAP 条目一致", plainEntries.Count == 2 && plainEntries[0] == pi.Segments[0], "n=" + plainEntries.Count);

            // 无效内容
            bool thrown = false;
            try { M3u8.Parse("hello world", baseUrl); }
            catch { thrown = true; }
            Check("M3u8 无效内容抛错", thrown, "");

            // URL/命名判定
            Check("M3u8 URL 判定 正例", M3u8.IsPlaylistUrl("http://x/a/index.m3u8") && M3u8.IsPlaylistUrl("http://x/a/index.M3U8?token=1"), "");
            Check("M3u8 URL 判定 反例", !M3u8.IsPlaylistUrl("http://x/a/seg.ts") && !M3u8.IsPlaylistUrl(""), "");
            Check("M3u8 输出名 换扩展", M3u8.GuessOutputName("http://x/a/index.m3u8?k=1") == "index.ts", M3u8.GuessOutputName("http://x/a/index.m3u8?k=1"));
            Check("M3u8 输出名 兜底", M3u8.GuessOutputName("") == "video.ts", M3u8.GuessOutputName(""));
            Check("M3u8 分片命名", M3u8.SegName(0) == "seg_00000.ts" && M3u8.SegName(123) == "seg_00123.ts", M3u8.SegName(123));

            // 输入文件 + 合并 + 参数
            string tmp = Path.Combine(Path.GetTempPath(), "aria-gui-m3u8-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string listPath = Path.Combine(tmp, "input.txt");
                M3u8.WriteInputFile(listPath, new List<string>(new string[] { "http://x/1.ts", "http://x/2.ts" }));
                string text = File.ReadAllText(listPath);
                string want = "http://x/1.ts\r\n  out=seg_00000.ts\r\nhttp://x/2.ts\r\n  out=seg_00001.ts\r\n";
                Check("M3u8 输入文件内容", text == want, text.Replace("\r\n", "\\r\\n"));

                byte[] a = new byte[] { 1, 2, 3 };
                byte[] b = new byte[] { 4, 5 };
                File.WriteAllBytes(Path.Combine(tmp, M3u8.SegName(0)), a);
                File.WriteAllBytes(Path.Combine(tmp, M3u8.SegName(1)), b);
                string merged = Path.Combine(tmp, "merged.bin");
                long bytes = M3u8.MergeSegments(tmp, 2, merged);
                byte[] got = File.ReadAllBytes(merged);
                Check("M3u8 合并字节数", bytes == 5, "bytes=" + bytes);
                Check("M3u8 合并内容", got.Length == 5 && got[0] == 1 && got[3] == 4 && got[4] == 5, "");
                bool miss = false;
                try { M3u8.MergeSegments(tmp, 3, merged); }
                catch (FileNotFoundException) { miss = true; }
                Check("M3u8 缺分片抛错", miss, "");

                Config cfg = Config.Default();
                cfg.MaxConcurrent = 3;
                cfg.SpeedLimit = "";
                List<string> args = M3u8.BuildSegmentArgs(cfg, listPath, tmp);
                string joined = Manager.JoinArgs(args);
                Check("M3u8 分片参数 输入文件续传",
                    joined.IndexOf("--input-file=", StringComparison.Ordinal) >= 0 &&
                    joined.IndexOf("--continue=true", StringComparison.Ordinal) >= 0, joined);
                Check("M3u8 分片参数 单连接固定名",
                    joined.IndexOf("--split=1", StringComparison.Ordinal) >= 0 &&
                    joined.IndexOf("--auto-file-renaming=false", StringComparison.Ordinal) >= 0, "");
                Check("M3u8 分片参数 并发数", joined.IndexOf("--max-concurrent-downloads=3", StringComparison.Ordinal) >= 0, "");
                cfg.SpeedLimit = "1M";
                List<string> limited = M3u8.BuildSegmentArgs(cfg, listPath, tmp);
                Check("M3u8 分片参数 限速", Manager.JoinArgs(limited).IndexOf("--max-overall-download-limit=1M", StringComparison.Ordinal) >= 0, "");
            }
            finally
            {
                try { Directory.Delete(tmp, true); }
                catch { }
            }
        }

        /// 真机全链：本地 HTTP 源（master 两档变体）→ Manager 下载 → 合并 → 选高带宽 + 字节序验证。
        private static void RunManagerPlaylistE2E()
        {
            string root = Path.Combine(Path.GetTempPath(), "aria-gui-m3u8-e2e-" + Guid.NewGuid().ToString("N"));
            string saveDir = Path.Combine(root, "out");
            Directory.CreateDirectory(saveDir);
            try
            {
                using (MiniHttpServer srv = new MiniHttpServer())
                {
                    int segSize = 131072;
                    srv.AddBytes("/media/v/seg1.ts", Fill(0xB2, segSize));
                    srv.AddBytes("/media/v/seg2.ts", Fill(0xC3, segSize));
                    srv.AddText("/media/v/master.m3u8",
                        "#EXTM3U\n" +
                        "#EXT-X-STREAM-INF:BANDWIDTH=800000\nlow.m3u8\n" +
                        "#EXT-X-STREAM-INF:BANDWIDTH=2000000\nhigh.m3u8\n",
                        "application/vnd.apple.mpegurl");
                    srv.AddText("/media/v/low.m3u8",
                        "#EXTM3U\n#EXTINF:10,\nseg1.ts\n", "application/vnd.apple.mpegurl");
                    srv.AddText("/media/v/high.m3u8",
                        "#EXTM3U\n#EXTINF:10,\nseg1.ts\n#EXTINF:10,\nseg2.ts\n", "application/vnd.apple.mpegurl");

                    Config cfg = Config.Default();
                    cfg.SaveDir = saveDir;
                    cfg.MaxConcurrent = 3;
                    cfg.SpeedLimit = "";
                    Manager mgr = new Manager(cfg, null);
                    DownloadTask task = mgr.AddTask(srv.BaseUrl + "/media/v/master.m3u8", saveDir, "");
                    Check("M3U8 E2E 任务识别与命名", task.IsPlaylist && task.Output == "master.ts", task.Output);

                    TaskStatus st = TaskStatus.Queued;
                    string err = "";
                    DateTime deadline = DateTime.Now.AddSeconds(60);
                    while (DateTime.Now < deadline)
                    {
                        Thread.Sleep(200);
                        foreach (DownloadTask s in mgr.Snapshot())
                        {
                            if (s.Id == task.Id) { st = s.Status; err = s.Error; }
                        }
                        if (st == TaskStatus.Completed || st == TaskStatus.Failed) break;
                    }
                    Check("M3U8 E2E 任务完成", st == TaskStatus.Completed, "status=" + st + " err=" + err);

                    string outPath = Path.Combine(saveDir, "master.ts");
                    bool exists = File.Exists(outPath);
                    long len = exists ? new FileInfo(outPath).Length : -1;
                    Check("M3U8 E2E 合并落盘(选高带宽变体)", exists && len == (long)segSize * 2, "len=" + len);

                    if (exists && len == (long)segSize * 2)
                    {
                        byte[] got = File.ReadAllBytes(outPath);
                        bool ok = got[0] == 0xB2 && got[segSize - 1] == 0xB2 &&
                                  got[segSize] == 0xC3 && got[got.Length - 1] == 0xC3;
                        Check("M3U8 E2E 分片拼接字节序", ok,
                            "head=" + got[0] + " mid=" + got[segSize] + " tail=" + got[got.Length - 1]);
                    }

                    string parts = Path.Combine(saveDir, ".master.ts.m3u8parts");
                    DateTime cleanDeadline = DateTime.Now.AddSeconds(5);
                    while (Directory.Exists(parts) && DateTime.Now < cleanDeadline) Thread.Sleep(100);
                    Check("M3U8 E2E 分片目录清理", !Directory.Exists(parts), parts);
                }
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }

        /// 代理真机：下载源指向无监听端口，唯独经 MiniProxyServer 可达 → 完成即证明流量走了代理。
        private static void RunManagerProxyE2E()
        {
            string root = Path.Combine(Path.GetTempPath(), "aria-gui-proxy-e2e-" + Guid.NewGuid().ToString("N"));
            string saveDir = Path.Combine(root, "out");
            Directory.CreateDirectory(saveDir);
            try
            {
                using (MiniProxyServer proxy = new MiniProxyServer(Fill(0xAB, 65536)))
                {
                    proxy.Start();

                    // 取一个必然无监听的端口作为上游地址
                    TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
                    probe.Start();
                    int closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
                    probe.Stop();

                    Config cfg = Config.Default();
                    cfg.SaveDir = saveDir;
                    cfg.MaxConcurrent = 1;
                    cfg.SpeedLimit = "";
                    cfg.Split = 1;
                    cfg.Proxy = "http://127.0.0.1:" + proxy.Port;
                    Manager mgr = new Manager(cfg, null);
                    DownloadTask task = mgr.AddTask("http://127.0.0.1:" + closedPort + "/e2e-proxied.bin", saveDir, "proxied.bin");

                    TaskStatus st = TaskStatus.Queued;
                    string err = "";
                    DateTime deadline = DateTime.Now.AddSeconds(60);
                    while (DateTime.Now < deadline)
                    {
                        Thread.Sleep(200);
                        foreach (DownloadTask s in mgr.Snapshot())
                        {
                            if (s.Id == task.Id) { st = s.Status; err = s.Error; }
                        }
                        if (st == TaskStatus.Completed || st == TaskStatus.Failed) break;
                    }
                    Check("代理 E2E 任务完成(仅经代理可达)", st == TaskStatus.Completed, "status=" + st + " err=" + err);

                    string outPath = Path.Combine(saveDir, "proxied.bin");
                    bool exists = File.Exists(outPath);
                    long len = exists ? new FileInfo(outPath).Length : -1;
                    Check("代理 E2E 落盘完整", exists && len == 65536, "len=" + len);

                    Check("代理 E2E 代理收到请求", proxy.RequestCount > 0, "count=" + proxy.RequestCount);
                    Check("代理 E2E 请求为绝对 URI",
                        proxy.LastRequestLine != null && proxy.LastRequestLine.StartsWith("GET http://", StringComparison.Ordinal),
                        "line=" + proxy.LastRequestLine);
                }
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }

        /// aria2c 版本号：纯逻辑解析 + 真机（嵌入资源提取的 aria2c --version）。
        private static void RunAria2VersionCases()
        {
            Check("版本解析-正常行",
                Manager.ParseVersionLine("aria2 version 1.37.0\r\nCopyright (C) 2006, 2019 Tatsuhiro Tsujikawa") == "1.37.0", "");
            Check("版本解析-前导空行", Manager.ParseVersionLine("\r\naria2 version 1.18.0") == "1.18.0", "");
            Check("版本解析-空输入", Manager.ParseVersionLine("") == "", "");
            Check("版本解析-非版本行", Manager.ParseVersionLine("fatal: unknown option") == "", "");

            Manager mgr = new Manager(Config.Default(), null);
            string ver = mgr.Aria2Version;
            Check("真机 aria2 版本非空", ver != "", "got=" + ver);
            Check("真机 aria2 版本形如 x.y", System.Text.RegularExpressions.Regex.IsMatch(ver, "^[0-9]+\\.[0-9]+"), "got=" + ver);
        }

        private static byte[] Fill(byte v, int n)
        {
            byte[] b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = v;
            return b;
        }
    }
}
