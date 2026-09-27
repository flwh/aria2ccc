using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AriaGui
{
    /// 应用设置，持久化于 <程序目录>\config\config.json（便携式，不写用户目录）。
    public sealed class Config
    {
        public string SaveDir = "";
        public int MaxConcurrent = 3;
        public string SpeedLimit = "";
        public int Split = 4;
        public string Trackers = "";
        public bool RpcEnabled = false;
        public int RpcPort = 6800;
        public string RpcSecret = "";
        public bool SniffEnabled = true; // 默认开启：与加开关前的行为保持一致
        public string Proxy = ""; // 网络代理（http/https/socks5 地址；空 = 直连）

        /// UTF-8 无 BOM 编码。
        public static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static readonly Regex SpeedLimitRe = new Regex(@"^\d+(\.\d+)?[KMGkmg]?$");

        /// 空串（不限速）或合法格式返回 true。
        public static bool ValidSpeedLimit(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            return SpeedLimitRe.IsMatch(s);
        }

        /// 空串（直连）或 http/https/socks5 绝对地址返回 true。
        public static bool ValidProxy(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            Uri u;
            if (!Uri.TryCreate(s, UriKind.Absolute, out u)) return false;
            string scheme = u.Scheme.ToLowerInvariant();
            if (scheme != "http" && scheme != "https" && scheme != "socks5") return false;
            return u.Host.Length > 0;
        }

        /// 默认配置：保存目录取用户 Downloads。
        public static Config Default()
        {
            Config c = new Config();
            c.SaveDir = DownloadsDir();
            c.MaxConcurrent = 3;
            c.SpeedLimit = "";
            c.Split = 4;
            c.Trackers = BuiltinTrackers();
            c.RpcEnabled = false;
            c.RpcPort = 6800;
            c.RpcSecret = "";
            c.SniffEnabled = true;
            c.Proxy = "";
            return c;
        }

        /// 内置常用 tracker 列表（每行一个），首次运行与「恢复默认」使用。
        public static string BuiltinTrackers()
        {
            return string.Join("\n", new string[] {
                "udp://tracker.opentrackr.org:1337/announce",
                "udp://open.tracker.cl:1337/announce",
                "udp://tracker.openbittorrent.com:6969/announce",
                "udp://opentracker.io:6969/announce",
                "udp://tracker.torrent.eu.org:451/announce",
                "udp://exodus.desync.com:6969/announce",
                "udp://open.demonii.com:1337/announce",
                "http://tracker.openbittorrent.com:80/announce"
            });
        }

        /// 规范化 tracker 文本：逐行 trim、去空行、去重，以 \n 连接（保留原顺序）。
        public static string NormalizeTrackers(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            List<string> kept = new List<string>();
            foreach (string raw in lines)
            {
                string s = raw.Trim();
                if (s == "" || kept.Contains(s)) continue;
                kept.Add(s);
            }
            return string.Join("\n", kept.ToArray());
        }

        /// 传给 aria2c --bt-tracker 的逗号分隔形式；为空时返回 ""。
        public string TrackerArg()
        {
            string norm = NormalizeTrackers(Trackers);
            return norm == "" ? "" : norm.Replace("\n", ",");
        }

        private static string DownloadsDir()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home)) return Path.Combine(home, "Downloads");
            return ".";
        }

        /// 运行期数据目录：程序所在目录下的 config 子目录（便携式，不写用户目录）。
        public static string DataDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(baseDir)) return null;
            return Path.Combine(baseDir, "config");
        }

        /// 配置文件绝对路径。
        public static string ConfigPath()
        {
            string dir = DataDir();
            if (dir == null) return null;
            return Path.Combine(dir, "config.json");
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
            cfg.Trackers = NormalizeTrackers(root.GetString("trackers", cfg.Trackers));
            cfg.RpcEnabled = root.GetBool("rpc_enabled", cfg.RpcEnabled);
            cfg.RpcPort = (int)root.GetLong("rpc_port", cfg.RpcPort);
            cfg.RpcSecret = root.GetString("rpc_secret", cfg.RpcSecret);
            cfg.SniffEnabled = root.GetBool("sniff_enabled", cfg.SniffEnabled);
            cfg.Proxy = root.GetString("proxy", cfg.Proxy);
            Clamp(cfg);
        }

        /// 兜底钳制（Load 与测试复用）。
        public static void Clamp(Config cfg)
        {
            if (cfg.MaxConcurrent < 1) cfg.MaxConcurrent = 1;
            if (cfg.MaxConcurrent > 10) cfg.MaxConcurrent = 10;
            if (cfg.Split < 1) cfg.Split = 1;
            if (!ValidSpeedLimit(cfg.SpeedLimit)) cfg.SpeedLimit = "";
            if (cfg.RpcPort < 1024) cfg.RpcPort = 1024;
            if (cfg.RpcPort > 65535) cfg.RpcPort = 65535;
            if (!ValidProxy(cfg.Proxy)) cfg.Proxy = "";
        }

        /// 序列化为 JSON 文本（2 空格缩进）。
        public string ToJson()
        {
            JsonObject o = new JsonObject();
            o.Set("save_dir", SaveDir);
            o.Set("max_concurrent", (long)MaxConcurrent);
            o.Set("speed_limit", SpeedLimit);
            o.Set("split", (long)Split);
            o.Set("trackers", Trackers);
            o.Set("rpc_enabled", RpcEnabled);
            o.Set("rpc_port", (long)RpcPort);
            o.Set("rpc_secret", RpcSecret);
            o.Set("sniff_enabled", SniffEnabled);
            o.Set("proxy", Proxy);
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
