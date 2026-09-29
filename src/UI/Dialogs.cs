using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AriaGui.Engine;
using Sunny.UI;

namespace AriaGui.UI
{
    /// 主题化模态对话框基类：无标题栏圆角卡片（白底 + 10px 圆角 + 系统投影），
    /// 左上自绘标题、右上 × 关闭、顶部 44px 区域可拖动；底部右下角「主操作/取消」按钮对。
    public abstract class ThemedDialog : UIForm
    {
        private const int CardRadius = 10;
        private const int DragHeight = 44;

        protected ThemedDialog(string title, Size cardSize)
        {
            StyleCustomMode = true;
            Style = UIStyle.Custom;
            ShowTitle = false;   // 不绘制 SunnyUI 标题栏：卡片式外观
            ShowRect = false;    // 不绘制边框（圆角由本类 Region 统一控制）
            ShowIcon = false;
            ShowRadius = false;
            ShowShadow = true;   // DWM 投影（与主窗口同一机制）

            Text = title;
            Font = Theme.BaseFont;
            BackColor = Color.White;

            Resizable = false;
            ShowFullScreen = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = cardSize;

            WindowButton close = new WindowButton("\uE8BB", false);
            close.SetBounds(cardSize.Width - 34 - 14, 12, 34, 34);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            ApplyCardRegion();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Win32.ApplyFramelessChrome(Handle); // Win11: DWM 圆角 + 投影（与主窗口一致）
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyCardRegion();
        }

        /// 卡片圆角：窗口 Region 统一裁为 10px 圆角（固定尺寸，尺寸变化时重建）。
        private void ApplyCardRegion()
        {
            if (Width < 2 || Height < 2)
            {
                Region = null;
                return;
            }
            using (GraphicsPath p = Theme.RoundRect(new Rectangle(0, 0, Width, Height), CardRadius))
            {
                Region old = Region;
                Region = new Region(p);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < DragHeight)
                Win32.DragWindow(Handle); // 顶部空白区（含自绘标题）拖动窗口
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            TextRenderer.DrawText(e.Graphics, Text, Theme.DialogTitleFont,
                new Point(22, 20), Theme.TextPrimary, TextFormatFlags.NoPrefix);
        }

        /// 右下角按钮对：取消按钮右缘距卡片右缘 22px、两按钮间距 10px、高 32；
        /// 返回主操作按钮（调用方可通过 Enabled 控制禁用态）。
        protected Button AddActionPair(int top, string actionText, int actionWidth, int cancelWidth)
        {
            FlatButton ok = new FlatButton();
            ok.Text = actionText;
            Theme.StylePrimaryButton(ok);

            FlatButton cancel = new FlatButton();
            cancel.Text = "取消";
            Theme.StyleGrayButton(cancel);
            cancel.DialogResult = DialogResult.Cancel;

            int cancelX = ClientSize.Width - 22 - cancelWidth;
            ok.SetBounds(cancelX - 10 - actionWidth, top, actionWidth, 32);
            cancel.SetBounds(cancelX, top, cancelWidth, 32);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(ok);
            Controls.Add(cancel);
            return ok;
        }
    }

    /// 添加下载对话框（极简卡片）：输入 URL 或选择 .torrent 种子文件；
    /// 保存目录用默认目录、文件名自动命名（initialUrl 供浏览器嗅探预填）。
    public sealed class AddDialogForm : ThemedDialog
    {
        private readonly Manager _mgr;
        private readonly Action<string> _notify;
        private readonly TextBox _urlBox;

        public AddDialogForm(Manager mgr, Action<string> notify, string defaultDir, string initialUrl = null)
            : base("添加下载", new Size(560, 186))
        {
            _mgr = mgr;
            _notify = notify;

            Label urlLabel = new Label();
            urlLabel.Text = "输入 URL 或者选择种子文件";
            urlLabel.ForeColor = Color.FromArgb(60, 60, 67);
            urlLabel.Font = Theme.BaseFont;
            urlLabel.SetBounds(22, 64, 320, 18);

            _urlBox = new ThemedTextBox();
            Theme.StyleTextBox(_urlBox);
            _urlBox.SetBounds(22, 86, 386, 36);
            Win32.SetPlaceholder(_urlBox, "HTTP/FTP 链接、磁力链接(magnet:...) 或 .torrent 路径");
            if (!string.IsNullOrEmpty(initialUrl)) _urlBox.Text = initialUrl;

            FlatButton browse = new FlatButton();
            browse.Text = "浏览……";
            Theme.StyleGrayButton(browse);
            browse.SetIcon(MiniSvg.Icons.FolderOpen, 18);
            browse.SetBounds(420, 86, 118, 36);
            browse.Click += delegate
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Title = "选择种子文件";
                    dlg.Filter = "种子文件 (*.torrent)|*.torrent|所有文件 (*.*)|*.*";
                    if (dlg.ShowDialog(this) == DialogResult.OK) _urlBox.Text = dlg.FileName;
                }
            };

            Button ok = AddActionPair(136, "确定", 88, 88);
            ok.Enabled = _urlBox.Text.Trim().Length > 0; // 空输入时禁用（浅蓝态）
            ok.Click += delegate { Confirm(); };
            _urlBox.TextChanged += delegate { ok.Enabled = _urlBox.Text.Trim().Length > 0; };

            Controls.Add(urlLabel);
            Controls.Add(_urlBox);
            Controls.Add(browse);
            Shown += delegate { _urlBox.Focus(); }; // 打开即聚焦输入框（蓝框聚焦态）
        }

        /// 校验并创建任务：失败弹错并保持对话框打开；目录留空用默认目录、文件名自动。
        private void Confirm()
        {
            try
            {
                DownloadTask task = _mgr.AddTask(_urlBox.Text, "", "");
                _notify("已添加任务: " + task.DisplayName());
                DialogResult = DialogResult.OK; // 关闭对话框
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "添加下载", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    /// 批量嗅探结果确认：列出全部链接（默认全选），确认后逐个入队。
    public sealed class SniffBatchForm : ThemedDialog
    {
        private readonly Manager _mgr;
        private readonly Action<string> _notify;
        private readonly string _saveDir;
        private readonly CheckedListBox _list;

        public SniffBatchForm(Manager mgr, Action<string> notify, string saveDir, List<string> urls)
            : base("嗅探结果", new Size(560, 420))
        {
            _mgr = mgr;
            _notify = notify;
            _saveDir = saveDir;

            Label tip = new Label();
            tip.Text = "从浏览器嗅探到 " + urls.Count + " 个链接（已全部选中），确认后加入下载队列：";
            tip.ForeColor = Color.FromArgb(60, 60, 67);
            tip.Font = Theme.BaseFont;
            tip.SetBounds(22, 64, 516, 18);

            _list = new CheckedListBox();
            _list.CheckOnClick = true;
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.Font = Theme.BaseFont;
            _list.SetBounds(22, 88, 516, 264);
            for (int i = 0; i < urls.Count; i++)
                _list.Items.Add(urls[i], true);

            Button ok = AddActionPair(370, "加入下载", 106, 88);
            ok.Click += delegate { Confirm(); };

            Controls.Add(tip);
            Controls.Add(_list);
        }

        /// 校验并逐个创建任务：跳过失败项，汇总结果。
        private void Confirm()
        {
            int added = 0;
            string lastErr = null;
            for (int i = 0; i < _list.CheckedItems.Count; i++)
            {
                string url = (string)_list.CheckedItems[i];
                try
                {
                    _mgr.AddTask(url, _saveDir, "");
                    added++;
                }
                catch (InvalidOperationException ex)
                {
                    lastErr = ex.Message;
                }
            }
            if (added == 0)
            {
                string msg = lastErr == null ? "请至少勾选一个链接" : lastErr;
                MessageBox.Show(this, msg, "添加下载", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _notify("已从嗅探结果添加 " + added + " 个任务");
            DialogResult = DialogResult.OK;
        }
    }
}
