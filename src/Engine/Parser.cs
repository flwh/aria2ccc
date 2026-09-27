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
