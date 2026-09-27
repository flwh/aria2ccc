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

    /// 线程安全的历史存储：<程序目录>\config\history.json，新→旧，上限 200 条。
    public sealed class HistoryStore
    {
        private const int MaxEntries = 200;
        private readonly object _lock = new object();
        private string _path;
        private List<HistoryEntry> _entries = new List<HistoryEntry>();

        /// 从 <程序目录>\config\history.json 加载；不存在/损坏时静默返回空存储。
        public static HistoryStore Load()
        {
            HistoryStore s = new HistoryStore();
            string dir = Config.DataDir();
            if (dir == null) return s;
            s._path = Path.Combine(dir, "history.json");
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
                // 落盘失败静默
            }
        }

        /// 序列化为 JSON 数组（2 空格缩进）。
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
