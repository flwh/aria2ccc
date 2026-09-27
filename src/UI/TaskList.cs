using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AriaGui.Engine;

namespace AriaGui.UI
{
    /// 单任务行：卡片化呈现（标题/状态/扁平进度条/信息行 + 开始/暂停/删除按钮）。
    public sealed class TaskRow : UserControl
    {
        public const int RowHeight = 88;

        private readonly Label _title;
        private readonly Label _status;
        private readonly FlatProgressBar _bar;
        private readonly Label _info;
        private readonly Button _startBtn;
        private readonly Button _pauseBtn;
        private readonly Button _removeBtn;

        public string TaskId;

        public TaskRow(string taskId, Action<string> onStart, Action<string> onPause, Action<string> onRemove)
        {
            TaskId = taskId;
            BackColor = Theme.CardBg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _title = new Label();
            _title.AutoEllipsis = true;
            _title.Font = Theme.TitleFont;
            _title.ForeColor = Theme.TextPrimary;
            _title.BackColor = Color.Transparent;

            _status = new Label();
            _status.TextAlign = ContentAlignment.MiddleRight;
            _status.Font = Theme.StatusFont;
            _status.BackColor = Color.Transparent;

            _bar = new FlatProgressBar();

            _info = new Label();
            _info.AutoEllipsis = true;
            _info.Font = Theme.SmallFont;
            _info.ForeColor = Theme.TextSecondary;
            _info.BackColor = Color.Transparent;

            _startBtn = new Button();
            _startBtn.Text = "开始";
            Theme.StylePrimaryButton(_startBtn);
            _startBtn.Click += delegate { onStart(TaskId); };

            _pauseBtn = new Button();
            _pauseBtn.Text = "暂停";
            Theme.StyleSecondaryButton(_pauseBtn);
            _pauseBtn.Click += delegate { onPause(TaskId); };

            _removeBtn = new Button();
            _removeBtn.Text = "删除";
            Theme.StyleDangerGhostButton(_removeBtn);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInner();
        }

        private void LayoutInner()
        {
            int w = ClientSize.Width;
            _title.SetBounds(14, 12, w - 186, 21);
            _status.SetBounds(w - 166, 12, 152, 21);
            _bar.SetBounds(14, 42, w - 28, 10);
            _info.SetBounds(14, 60, w - 268, 18);
            _startBtn.SetBounds(w - 240, 54, 72, 28);
            _pauseBtn.SetBounds(w - 160, 54, 72, 28);
            _removeBtn.SetBounds(w - 80, 54, 66, 28);
        }

        /// 把任务数据绑定到卡片控件。
        public void Bind(DownloadTask task)
        {
            _title.Text = task.DisplayName();
            _status.Text = TaskStatusText.ToText(task.Status);
            _status.ForeColor = Theme.StatusColor(task.Status);

            if (task.IsPlaylist && task.Status != TaskStatus.Completed && task.Status != TaskStatus.Failed)
            {
                // m3u8 任务：解析 → 分片计数 → 合并三阶段文案
                if (task.SegmentTotal > 0)
                {
                    _bar.Value = (int)(100L * task.SegmentDone / task.SegmentTotal);
                    if (task.SegmentDone < task.SegmentTotal)
                        _info.Text = string.Format(CultureInfo.InvariantCulture,
                            "m3u8 分片 {0}/{1}", task.SegmentDone, task.SegmentTotal);
                    else
                        _info.Text = "分片下载完成，正在合并为单文件…";
                }
                else
                {
                    _bar.Value = 0;
                    _info.Text = "正在解析 m3u8 播放列表…";
                }
            }
            else if (task.Total > 0)
            {
                _bar.Value = (int)task.Percent;
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
            _info.ForeColor = task.Status == TaskStatus.Failed ? Theme.Danger : Theme.TextSecondary;

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

        private static string FirstLine(string s)
        {
            int i = s.IndexOf('\n');
            if (i < 0) return s;
            return s.Substring(0, i);
        }
    }

    /// 任务列表：订阅 Manager 事件做增量更新；行宽随容器宽度、纵向手动布局。
    public sealed class TaskListPanel : Panel
    {
        private readonly Manager _mgr;
        private readonly Action<string> _notify;
        private readonly Dictionary<string, TaskRow> _rows = new Dictionary<string, TaskRow>();
        private readonly Dictionary<string, TaskStatus> _prev = new Dictionary<string, TaskStatus>();
        private readonly Action<TaskEvent> _handler;
        private readonly Label _empty;

        /// 每轮引擎事件同步完成后触发（UI 线程）：主窗口借此与任务行同帧刷新状态栏。
        public event Action Synced;

        public TaskListPanel(Manager mgr, Action<string> notify)
        {
            _mgr = mgr;
            _notify = notify;
            AutoScroll = true;
            BackColor = Theme.WindowBg;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            _empty = new Label();
            _empty.Text = "暂无任务\n\n点击上方「添加下载」创建任务";
            _empty.Font = Theme.BaseFont;
            _empty.ForeColor = Theme.TextMuted;
            _empty.TextAlign = ContentAlignment.MiddleCenter;
            _empty.AutoSize = false;
            Controls.Add(_empty);

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

            _empty.Visible = _rows.Count == 0;
            RelayoutRows();
            if (Synced != null) Synced();
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
            if (_empty.Visible)
                _empty.SetBounds(0, 0, width, Math.Max(ClientSize.Height - 4, 60));
            int y = 0;
            foreach (Control c in Controls)
            {
                if (c == _empty) continue;
                c.SetBounds(0, y, width, TaskRow.RowHeight);
                y += TaskRow.RowHeight + 4;
            }
        }
    }
}
