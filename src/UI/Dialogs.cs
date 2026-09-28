using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AriaGui.Engine;

namespace AriaGui.UI
{
    /// 主题化模态对话框基类：固定尺寸、居中、白色底 + 右下角「主操作/取消」按钮对。
    public abstract class ThemedDialog : Form
    {
        protected ThemedDialog(string title, Size clientSize)
        {
            Text = title;
            Font = Theme.BaseFont;
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = clientSize;
        }

        /// 右下角按钮对：取消按钮右缘距内容右缘 18px、两按钮间距 10px、高 32；返回主操作按钮。
        protected Button AddActionPair(int top, string actionText, int actionWidth, int cancelWidth)
        {
            FlatButton ok = new FlatButton();
            ok.Text = actionText;
            Theme.StylePrimaryButton(ok);

            FlatButton cancel = new FlatButton();
            cancel.Text = "取消";
            Theme.StyleSecondaryButton(cancel);
            cancel.DialogResult = DialogResult.Cancel;

            int cancelX = ClientSize.Width - 18 - cancelWidth;
            ok.SetBounds(cancelX - 10 - actionWidth, top, actionWidth, 32);
            cancel.SetBounds(cancelX, top, cancelWidth, 32);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(ok);
            Controls.Add(cancel);
            return ok;
        }
    }

    /// 添加下载对话框：确认后创建任务并自动入队（initialUrl 供浏览器嗅探预填）。
    public sealed class AddDialogForm : ThemedDialog
    {
        private readonly Manager _mgr;
        private readonly Action<string> _notify;

        private readonly TextBox _urlBox;
        private readonly TextBox _dirBox;
        private readonly TextBox _nameBox;

        public AddDialogForm(Manager mgr, Action<string> notify, string defaultDir, string initialUrl = null)
            : base("添加下载", new Size(560, 218))
        {
            _mgr = mgr;
            _notify = notify;

            Label urlLabel = new Label();
            urlLabel.Text = "下载链接";
            Theme.StyleFormLabel(urlLabel);
            urlLabel.SetBounds(18, 28, 72, 18);

            _urlBox = new ThemedTextBox();
            Theme.StyleTextBox(_urlBox);
            _urlBox.SetBounds(96, 25, 446, 23);
            Win32.SetPlaceholder(_urlBox, "HTTP/FTP 链接、磁力链接(magnet:...) 或 .torrent 地址，支持 m3u8 视频流");
            if (!string.IsNullOrEmpty(initialUrl)) _urlBox.Text = initialUrl;

            Label dirLabel = new Label();
            dirLabel.Text = "保存目录";
            Theme.StyleFormLabel(dirLabel);
            dirLabel.SetBounds(18, 68, 72, 18);

            _dirBox = new ThemedTextBox();
            Theme.StyleTextBox(_dirBox);
            _dirBox.SetBounds(96, 65, 352, 23);
            _dirBox.Text = defaultDir;

            FlatButton browse = new FlatButton();
            browse.Text = "浏览…";
            Theme.StyleSecondaryButton(browse);
            browse.SetBounds(456, 63, 86, 27);
            browse.Click += delegate { Theme.BrowseFolderInto(this, _dirBox); };

            Label nameLabel = new Label();
            nameLabel.Text = "文件名";
            Theme.StyleFormLabel(nameLabel);
            nameLabel.SetBounds(18, 108, 72, 18);

            _nameBox = new ThemedTextBox();
            Theme.StyleTextBox(_nameBox);
            _nameBox.SetBounds(96, 105, 446, 23);
            Win32.SetPlaceholder(_nameBox, "可选，默认自动命名");

            Button ok = AddActionPair(166, "开始下载", 106, 96);
            ok.Click += delegate { Confirm(); };

            Controls.Add(urlLabel);
            Controls.Add(_urlBox);
            Controls.Add(dirLabel);
            Controls.Add(_dirBox);
            Controls.Add(browse);
            Controls.Add(nameLabel);
            Controls.Add(_nameBox);
        }

        /// 校验并创建任务：失败弹错并保持对话框打开。
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

    /// 批量嗅探结果确认：列出全部链接（默认全选），确认后逐个入队。
    public sealed class SniffBatchForm : ThemedDialog
    {
        private readonly Manager _mgr;
        private readonly Action<string> _notify;
        private readonly string _saveDir;
        private readonly CheckedListBox _list;

        public SniffBatchForm(Manager mgr, Action<string> notify, string saveDir, List<string> urls)
            : base("嗅探结果", new Size(560, 400))
        {
            _mgr = mgr;
            _notify = notify;
            _saveDir = saveDir;

            Label tip = new Label();
            tip.Text = "从浏览器嗅探到 " + urls.Count + " 个链接（已全部选中），确认后加入下载队列：";
            Theme.StyleFormLabel(tip);
            tip.SetBounds(18, 16, 524, 18);

            _list = new CheckedListBox();
            _list.CheckOnClick = true;
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.Font = Theme.BaseFont;
            _list.SetBounds(18, 42, 524, 300);
            for (int i = 0; i < urls.Count; i++)
                _list.Items.Add(urls[i], true);

            Button ok = AddActionPair(356, "加入下载", 106, 104);
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
