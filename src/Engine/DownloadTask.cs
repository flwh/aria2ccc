using System;
using System.Diagnostics;

namespace AriaGui.Engine
{
    /// 任务状态机：
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
        public string Referer = ""; // 来源页地址（浏览器嗅探携带；防盗链校验用，可空）
        public TaskStatus Status;
        public long Completed;
        public long Total;   // 0 = 未知（BT 元数据未就绪）
        public long Speed;   // bytes/s
        public long UpSpeed; // bytes/s（上传；aria2 readout 的 UL 字段，BT 分享/做种时非零）
        public int Eta = -1; // 秒，-1 = 未知
        public double Percent;
        public string Error = "";
        public DateTime CreatedAt;
        public DateTime FinishedAt;

        // m3u8 分片模式（Url 为 .m3u8 播放列表时）
        public bool IsPlaylist;
        public int SegmentDone;   // 已下载分片数
        public int SegmentTotal;  // 分片总数（0 = 清单尚未解析）

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
