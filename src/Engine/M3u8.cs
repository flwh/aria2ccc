using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AriaGui.Engine
{
    /// m3u8（HLS）支持：清单抓取/解析、aria2c 批量下载分片、二进制拼接合并。
    /// ts 分片不转码，直接按序拼接为单文件（EXT-X-MAP 的 init 段并入首位）。
    public static class M3u8
    {
        /// 抓取清单与下载分片默认使用的 UA（部分站点校验；设置页自定义 UA 非空时覆盖）。
        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) aria-gui/1.0";

        /// 分片文件名（写入输入文件的 out=，与合并顺序一致）。
        public static string SegName(int index)
        {
            return string.Format(CultureInfo.InvariantCulture, "seg_{0:00000}.ts", index);
        }

        /// URL 路径是否指向 m3u8 播放列表（忽略 query/fragment）。
        public static bool IsPlaylistUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string u = url.Trim();
            int i = u.IndexOfAny(new char[] { '?', '#' });
            if (i >= 0) u = u.Substring(0, i);
            return u.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
        }

        /// 由清单 URL 猜输出文件名：index.m3u8 → index.ts（兜底 video.ts）。
        public static string GuessOutputName(string url)
        {
            string u = url == null ? "" : url;
            int i = u.IndexOfAny(new char[] { '?', '#' });
            if (i >= 0) u = u.Substring(0, i);
            u = u.TrimEnd('/');
            int slash = u.LastIndexOf('/');
            string name = slash >= 0 ? u.Substring(slash + 1) : u;
            if (name != "")
            {
                foreach (char bad in Path.GetInvalidFileNameChars())
                    name = name.Replace(bad, '_');
            }
            string lower = name.ToLowerInvariant();
            if (lower.EndsWith(".ts")) return name;
            if (lower.EndsWith(".m3u8")) name = name.Substring(0, name.Length - 5);
            if (name == "" || name == ".") name = "video";
            return name + ".ts";
        }

        /// 抓取清单文本；finalUrl 输出重定向后的实际地址（相对 URL 以它为基准）。
        /// proxy 为 http/https 地址时经由其访问；空串与 socks5 直连
        /// （.NET Framework 的 WebProxy 不支持 socks5，此情形仅 aria2 下载走代理）。
        /// referer 非空时作为来源页发送（防盗链绕过）。
        public static string FetchText(string url, string proxy, string userAgent, string referer, out string finalUrl)
        {
            finalUrl = url;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = string.IsNullOrEmpty(userAgent) ? UserAgent : userAgent;
            if (!string.IsNullOrEmpty(referer)) req.Referer = referer;
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            req.AllowAutoRedirect = true;
            // 显式直连或指定代理（不走系统默认代理）
            req.Proxy = null;
            if (!string.IsNullOrEmpty(proxy)
                && (proxy.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || proxy.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                req.Proxy = new WebProxy(proxy);
            }
            using (WebResponse resp = req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (StreamReader r = new StreamReader(s, Encoding.UTF8, true))
            {
                string text = r.ReadToEnd();
                try { finalUrl = resp.ResponseUri.ToString(); }
                catch { }
                return text;
            }
        }

        /// 解析播放列表：区分 master（变体列表）与媒体清单；相对 URL 以 baseUrl 解析。
        public static PlaylistInfo Parse(string text, string baseUrl)
        {
            if (string.IsNullOrEmpty(text)) throw new Exception("播放列表内容为空");
            text = text.TrimStart('\uFEFF');
            PlaylistInfo info = new PlaylistInfo();
            bool sawHeader = false;
            bool pendingVariant = false;
            long pendingBandwidth = 0;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string s = raw.Trim();
                if (s == "") continue;
                if (s[0] == '#')
                {
                    if (s.StartsWith("#EXTM3U", StringComparison.Ordinal)) { sawHeader = true; continue; }
                    if (s.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal))
                    {
                        string method = AttrValue(s, "METHOD");
                        if (method != null && !method.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                            info.Encrypted = true;
                        continue;
                    }
                    if (s.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
                    {
                        string uri = AttrValue(s, "URI");
                        if (!string.IsNullOrEmpty(uri))
                        {
                            info.HasMap = true;
                            info.MapUri = Resolve(baseUrl, uri);
                        }
                        continue;
                    }
                    if (s.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
                    {
                        pendingVariant = true;
                        pendingBandwidth = 0;
                        string bw = AttrValue(s, "BANDWIDTH");
                        long v;
                        if (bw != null && long.TryParse(bw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                            pendingBandwidth = v;
                        continue;
                    }
                    continue; // 其余标签忽略
                }

                if (pendingVariant)
                {
                    info.VariationUrl.Add(Resolve(baseUrl, s));
                    info.VariationBandwidth.Add(pendingBandwidth);
                    pendingVariant = false;
                }
                else
                {
                    info.Segments.Add(Resolve(baseUrl, s));
                }
            }

            // 严格要求 #EXTM3U 头：服务器返回 HTML 错误页/登录页时快速失败
            if (!sawHeader)
                throw new Exception("不是有效的 m3u8 播放列表（缺少 #EXTM3U 头）");
            info.IsMaster = info.VariationUrl.Count > 0;
            return info;
        }

        /// 待下载条目：有 EXT-X-MAP 时 init 段放首位（合并顺序即播放顺序）。
        public static List<string> BuildEntries(PlaylistInfo info)
        {
            List<string> list = new List<string>();
            if (info.HasMap && !string.IsNullOrEmpty(info.MapUri)) list.Add(info.MapUri);
            list.AddRange(info.Segments);
            return list;
        }

        /// 写 aria2c -i 输入文件：每条 `URI` + `  out=seg_NNNNN.ts`（UTF-8 无 BOM）。
        public static void WriteInputFile(string path, List<string> entries)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append(entries[i]).Append("\r\n");
                sb.Append("  out=").Append(SegName(i)).Append("\r\n");
            }
            File.WriteAllText(path, sb.ToString(), Config.Utf8NoBom);
        }

        /// 分片下载参数：单连接/分片 + 固定命名（out= 不被改写），进度与完成行供 UI 计数。
        /// refererUrl 为清单地址（防盗链绕过开启时推导来源页随分片请求发送）。
        public static List<string> BuildSegmentArgs(Config cfg, string refererUrl, string inputFile, string segDir)
        {
            int mc = cfg.MaxConcurrent;
            if (mc < 1) mc = 1;
            List<string> args = new List<string>();
            args.Add("--input-file=" + inputFile);
            args.Add("--dir=" + segDir);
            args.Add("--continue=true");
            args.Add("--auto-file-renaming=false");
            args.Add("--summary-interval=0");
            args.Add("--console-log-level=notice");
            args.Add("--download-result=hide");
            args.Add("--split=1");
            args.Add("--max-connection-per-server=1");
            args.Add("--max-concurrent-downloads=" + mc.ToString(CultureInfo.InvariantCulture));
            args.Add("--user-agent=" + cfg.EffectiveUserAgent(UserAgent));
            // 防盗链绕过：分片请求携带来源页（与普通任务同一规则）
            if (cfg.BypassHotlink)
            {
                string referer = Config.RefererFor(refererUrl);
                if (referer != "") args.Add("--referer=" + referer);
            }
            if (!string.IsNullOrEmpty(cfg.SpeedLimit)) args.Add("--max-overall-download-limit=" + cfg.SpeedLimit);
            return args;
        }

        /// 依序拼接 seg_00000…seg_N 到 targetPath（覆盖写），返回总字节数；缺分片抛异常。
        public static long MergeSegments(string segDir, int count, string targetPath)
        {
            string dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            byte[] buf = new byte[1 << 20];
            long total = 0;
            using (FileStream outp = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                for (int i = 0; i < count; i++)
                {
                    string p = Path.Combine(segDir, SegName(i));
                    if (!File.Exists(p))
                        throw new FileNotFoundException("缺少分片文件: " + SegName(i), p);
                    using (FileStream inp = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        int read;
                        while ((read = inp.Read(buf, 0, buf.Length)) > 0)
                        {
                            outp.Write(buf, 0, read);
                            total += read;
                        }
                    }
                }
            }
            return total;
        }

        /// 提取标签属性值：先引号形式（URI="…"），再裸值形式（METHOD=NONE、BANDWIDTH=800000）。
        private static string AttrValue(string line, string name)
        {
            Match m = Regex.Match(line, name + @"\s*=\s*""([^""]*)""");
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(line, name + @"\s*=\s*([^,\s]+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// 相对 URL 基于清单实际地址解析；已是绝对 URL 时原样返回。
        private static string Resolve(string baseUrl, string uri)
        {
            if (string.IsNullOrEmpty(uri)) return "";
            string u = uri.Trim();
            string lower = u.ToLowerInvariant();
            if (lower.StartsWith("http://") || lower.StartsWith("https://") || lower.StartsWith("data:")) return u;
            try
            {
                Uri b;
                if (!string.IsNullOrEmpty(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out b))
                    return new Uri(b, u).ToString();
            }
            catch
            {
            }
            return u;
        }
    }

    /// 播放列表解析结果。
    public sealed class PlaylistInfo
    {
        public bool IsMaster;      // 含 #EXT-X-STREAM-INF 变体（主清单）
        public bool Encrypted;     // 含 EXT-X-KEY METHOD!=NONE
        public bool HasMap;        // 含 EXT-X-MAP（fMP4 init 段）
        public string MapUri = "";
        public List<string> Segments = new List<string>();
        public List<string> VariationUrl = new List<string>();
        public List<long> VariationBandwidth = new List<long>();

        /// 带宽最大的变体 URL（无变体返回 ""）。
        public string BestVariation()
        {
            if (VariationUrl.Count == 0) return "";
            int best = 0;
            for (int i = 1; i < VariationUrl.Count; i++)
            {
                if (VariationBandwidth[i] > VariationBandwidth[best]) best = i;
            }
            return VariationUrl[best];
        }
    }
}
