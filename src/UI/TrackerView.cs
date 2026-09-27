using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AriaGui.UI
{
    /// Trackers 页：BT / 磁力任务的 tracker 列表编辑（保存 / 恢复默认 / 从网络更新）。
    public sealed class TrackerView : Panel
    {
        private readonly Config _cfg;
        private readonly Action<string> _notify;

        private readonly TextBox _box;
        private readonly Label _countLabel;
        private readonly Button _fetchBtn;
        private bool _fetching;

        private const string FetchText = "从网络更新";
        private const string FetchBusyText = "获取中…";

        /// 列表条数变化（编辑或网络更新后），供头部计数联动。
        public event EventHandler CountChanged;

        public TrackerView(Config cfg, Action<string> notify)
        {
            _cfg = cfg;
            _notify = notify;
            BackColor = Theme.WindowBg;

            Panel card = new Panel();
            card.BackColor = Theme.CardBg;
            card.SetBounds(14, 14, 600, 468);
            card.Paint += Theme.CardPaint;

            Label title = new Label();
            title.Text = "BT / 磁力 Tracker 列表";
            title.Font = Theme.TitleFont;
            title.ForeColor = Theme.TextPrimary;
            title.SetBounds(24, 18, 320, 22);

            Label hint = new Label();
            hint.Text = "每行一个 tracker 地址；保存后，对新启动的 BT / 磁力任务生效。";
            hint.Font = Theme.SmallFont;
            hint.ForeColor = Theme.TextSecondary;
            hint.SetBounds(24, 46, 552, 18);

            _box = new ThemedTextBox();
            _box.Multiline = true;
            _box.ScrollBars = ScrollBars.Vertical;
            _box.WordWrap = true;
            _box.AcceptsReturn = true;
            _box.SetBounds(24, 74, 552, 300);
            _box.BackColor = Color.White;
            _box.ForeColor = Theme.TextPrimary;
            _box.Font = Theme.MonoFont;
            SetBoxText(cfg.Trackers);

            _countLabel = new Label();
            _countLabel.Font = Theme.SmallFont;
            _countLabel.ForeColor = Theme.TextMuted;
            _countLabel.SetBounds(24, 382, 240, 18);

            _box.TextChanged += delegate { UpdateCount(); };

            Button save = new Button();
            save.Text = "保存";
            save.SetBounds(24, 410, 88, 32);
            Theme.StylePrimaryButton(save);
            save.Click += delegate { SaveTrackers(); };

            Button reset = new Button();
            reset.Text = "恢复默认";
            reset.SetBounds(120, 410, 96, 32);
            Theme.StyleSecondaryButton(reset);
            reset.Click += delegate { RestoreDefault(); };

            _fetchBtn = new Button();
            _fetchBtn.Text = FetchText;
            _fetchBtn.SetBounds(224, 410, 112, 32);
            Theme.StyleSecondaryButton(_fetchBtn);
            _fetchBtn.Click += delegate { FetchOnline(); };

            card.Controls.Add(title);
            card.Controls.Add(hint);
            card.Controls.Add(_box);
            card.Controls.Add(_countLabel);
            card.Controls.Add(save);
            card.Controls.Add(reset);
            card.Controls.Add(_fetchBtn);
            Controls.Add(card);

            UpdateCount();
        }

        /// 当前编辑框中的 tracker 条数（非空行）。
        public int Count
        {
            get { return CountLines(_box.Text); }
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int n = 0;
            foreach (string raw in lines)
                if (raw.Trim() != "") n++;
            return n;
        }

        private void UpdateCount()
        {
            _countLabel.Text = "共 " + CountLines(_box.Text) + " 条";
            if (CountChanged != null) CountChanged(this, EventArgs.Empty);
        }

        /// 显示用：内部格式 \n 转为 Windows 文本框换行 \r\n（读取时由 NormalizeTrackers 归一化回 \n）。
        private void SetBoxText(string normalized)
        {
            _box.Text = normalized.Replace("\n", "\r\n");
        }

        /// 保存到配置（规范化后回显），对新启动的 BT / 磁力任务生效。
        private void SaveTrackers()
        {
            _cfg.Trackers = Config.NormalizeTrackers(_box.Text);
            SetBoxText(_cfg.Trackers);
            string err = _cfg.Save(); // 成功返回 null
            if (err != null)
            {
                MessageBox.Show(this, err, "Trackers", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _notify("Trackers 已保存（对新启动的 BT / 磁力任务生效）");
        }

        /// 填入内置默认列表（不自动保存）。
        private void RestoreDefault()
        {
            SetBoxText(Config.BuiltinTrackers());
            _notify("已填入内置默认列表，点击「保存」后生效");
        }

        /// 从公开源拉取最新热门列表（后台线程，失败自动换源）。
        private void FetchOnline()
        {
            if (_fetching) return;
            _fetching = true;
            _fetchBtn.Enabled = false;
            _fetchBtn.Text = FetchBusyText;
            Task.Run(delegate { FetchWorker(); });
        }

        private void FetchWorker()
        {
            // 显式启用 TLS 1.2：部分系统默认协议较老，会导致握手被关闭
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
            string[] sources = new string[] {
                "https://trackerslist.com/best.txt",
                "https://cdn.jsdelivr.net/gh/ngosang/trackerslist@master/trackers_best.txt",
                "https://raw.githubusercontent.com/ngosang/trackerslist/master/trackers_best.txt"
            };
            string data = null;
            Exception last = null;
            foreach (string url in sources)
            {
                // 网络抖动常见，每源重试一次
                for (int i = 0; i < 2; i++)
                {
                    try
                    {
                        data = DownloadText(url, 12000);
                        if (data != null && data.Trim().Length > 0) break;
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                    }
                }
                if (data != null && data.Trim().Length > 0) break;
            }
            string err = last == null ? null : last.Message;
            try
            {
                BeginInvoke((MethodInvoker)delegate { ApplyFetched(data, err); });
            }
            catch
            {
                // 窗口已关闭：忽略
            }
        }

        /// 回到 UI 线程：过滤 URL 行、规范化后填入编辑框（需手动保存生效）。
        private void ApplyFetched(string data, string err)
        {
            _fetching = false;
            _fetchBtn.Enabled = true;
            _fetchBtn.Text = FetchText;
            if (data == null || data.Trim().Length == 0)
            {
                MessageBox.Show(this, "网络获取失败：" + (err == null ? "列表为空" : err),
                    "Trackers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            List<string> kept = new List<string>();
            string[] lines = data.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string s = raw.Trim();
                if (s == "" || s.IndexOf("://", StringComparison.Ordinal) < 0) continue;
                kept.Add(s);
            }
            if (kept.Count == 0)
            {
                MessageBox.Show(this, "获取到的列表为空", "Trackers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SetBoxText(Config.NormalizeTrackers(string.Join("\n", kept.ToArray())));
            _notify("已获取 " + CountLines(_box.Text) + " 条 tracker，点击「保存」后生效");
        }

        private static string DownloadText(string url, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "aria-gui";
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }
    }
}
