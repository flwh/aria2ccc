using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using AriaGui.Engine;

namespace AriaGui.UI
{
    /// 主窗口：左侧导航栏 + 头部标题区（大标题/计数/操作）+ 内容区 + 底部状态栏。
    public sealed class MainForm : Form
    {
        private readonly Manager _mgr;
        private readonly Config _cfg;
        private readonly HistoryStore _hist;
        private readonly Sniffer _sniffer;

        private readonly Sidebar _sidebar;
        private readonly TaskListPanel _taskList;
        private readonly HistoryView _historyV;
        private readonly TrackerView _trackerView;
        private readonly SettingsView _settingsV;
        private readonly Panel[] _pages;

        private readonly Label _pageTitle;
        private readonly Label _pageCount;
        private readonly Label _statusLeft;
        private readonly Label _statusSpeed;
        private readonly Label _natText;
        private readonly Label _engineDot;
        private readonly Label _engineText;
        private readonly Timer _timer;
        private readonly FlatButton _addBtn;
        private readonly WindowButton _minBtn;
        private readonly WindowButton _maxBtn;
        private readonly WindowButton _closeBtn;

        private DateTime _notifyAt = DateTime.MinValue;

        private const int SideW = 200;
        private const int HeaderH = 64;
        private const int StatusH = 38;

        public MainForm(Manager mgr, Config cfg, HistoryStore hist)
        {
            _mgr = mgr;
            _cfg = cfg;
            _hist = hist;

            Text = "Aria 下载器";
            Font = Theme.BaseFont;
            BackColor = Color.White;
            ClientSize = new Size(980, 640);
            MinimumSize = new Size(780, 520);
            FormBorderStyle = FormBorderStyle.None; // 无边框：自绘标题栏 + WndProc 边缘缩放
            StartPosition = FormStartPosition.CenterScreen;
            Icon appIcon = null;
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
            if (appIcon != null) Icon = appIcon;

            int mainW = ClientSize.Width - SideW;

            // 四个内容页（Dock=Fill 叠放，按可见性切换）
            _taskList = new TaskListPanel(mgr, Notify);
            _taskList.Dock = DockStyle.Fill;
            _trackerView = new TrackerView(cfg, Notify);
            _trackerView.Dock = DockStyle.Fill;
            _trackerView.Visible = false;
            _historyV = new HistoryView(hist);
            _historyV.Dock = DockStyle.Fill;
            _historyV.Visible = false;
            // 嗅探接收端（仅本机 127.0.0.1）：按配置启停；启动失败（如端口被占用）不影响主程序
            _sniffer = new Sniffer(Sniffer.DefaultPort, OnSniffedUrls);
            if (cfg.SniffEnabled)
            {
                try { _sniffer.Start(); }
                catch { }
            }
            _settingsV = new SettingsView(cfg, Notify, ApplySniff);
            _settingsV.Dock = DockStyle.Fill;
            _settingsV.Visible = false;
            _pages = new Panel[] { _taskList, _trackerView, _historyV, _settingsV };

            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.WindowBg;
            content.Controls.Add(_settingsV);
            content.Controls.Add(_historyV);
            content.Controls.Add(_trackerView);
            content.Controls.Add(_taskList);

            // 头部：大标题 + 计数 + 右侧操作按钮
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = HeaderH;
            header.BackColor = Color.White;
            header.Paint += PaintBottomBorder;

            _pageTitle = new Label();
            _pageTitle.Text = "全部下载";
            _pageTitle.Font = Theme.PageTitleFont;
            _pageTitle.ForeColor = Theme.TextPrimary;
            _pageTitle.AutoSize = true;
            _pageTitle.Location = new Point(24, 18);
            header.Controls.Add(_pageTitle);

            _pageCount = new Label();
            _pageCount.Text = "0/0";
            _pageCount.Font = Theme.SmallFont;
            _pageCount.ForeColor = Theme.TextMuted;
            _pageCount.AutoSize = true;
            _pageCount.Location = new Point(140, 31);
            header.Controls.Add(_pageCount);

            // 添加下载：加号图标按钮（主色圆角，位置由 LayoutHeaderButtons 重算）
            _addBtn = new FlatButton();
            _addBtn.Text = "\uE710";
            _addBtn.Font = new Font(Theme.IconFont.FontFamily, 10f);
            _addBtn.SetBounds(mainW - 16 - 34, 15, 34, 34);
            Theme.StylePrimaryButton(_addBtn);
            _addBtn.Click += delegate { ShowAddDialog(); };
            header.Controls.Add(_addBtn);

            // 无边框窗口控制按钮：最小化 / 最大化(还原) / 关闭（34x34 小圆角，与加号同一风格）
            _minBtn = new WindowButton("\uE921", false);
            _minBtn.Click += delegate { WindowState = FormWindowState.Minimized; };
            _maxBtn = new WindowButton("\uE922", false);
            _maxBtn.Click += delegate { ToggleMaximize(); };
            _closeBtn = new WindowButton("\uE8BB", true);
            _closeBtn.Click += delegate { Close(); };
            header.Controls.Add(_minBtn);
            header.Controls.Add(_maxBtn);
            header.Controls.Add(_closeBtn);

            // 自绘标题栏：拖拽移动 + 双击最大化
            AttachTitleDrag(header);
            AttachTitleDrag(_pageTitle);
            AttachTitleDrag(_pageCount);

            // 手动右对齐布局：Anchor 若在加入父容器前设置会按默认尺寸计算边距，Dock 生效后偏移飞出可视区
            header.Resize += delegate { LayoutHeaderButtons(header, _addBtn, _minBtn, _maxBtn, _closeBtn); };

            // 底部状态栏：左状态文字，右速度 + 引擎状态
            Panel status = new Panel();
            status.Dock = DockStyle.Bottom;
            status.Height = StatusH;
            status.BackColor = Color.White;
            status.Paint += PaintTopBorder;

            _statusLeft = new Label();
            _statusLeft.Text = "就绪";
            _statusLeft.Font = Theme.SmallFont;
            _statusLeft.ForeColor = Theme.TextSecondary;
            _statusLeft.AutoSize = true;
            _statusLeft.Location = new Point(24, 11);
            status.Controls.Add(_statusLeft);

            // NAT 类型检测：位于引擎状态左侧，后台线程探测 stun.miwifi.com（UDP 阻塞不卡 UI）
            _natText = new Label();
            _natText.Text = "NAT 检测中…";
            _natText.Font = Theme.SmallFont;
            _natText.ForeColor = Theme.TextSecondary;
            _natText.AutoSize = true;
            _natText.Location = new Point(mainW - 16 - 50 - 16 - 12 - 90 - 12 - 96, 11);
            status.Controls.Add(_natText);

            _engineDot = new Label();
            _engineDot.Text = "●";
            _engineDot.Font = Theme.SmallFont;
            _engineDot.ForeColor = Theme.Success;
            _engineDot.AutoSize = true;
            _engineDot.Location = new Point(mainW - 16 - 50 - 16, 10);
            status.Controls.Add(_engineDot);

            _engineText = new Label();
            _engineText.Text = "引擎就绪";
            _engineText.Font = Theme.SmallFont;
            _engineText.ForeColor = Theme.TextSecondary;
            _engineText.AutoSize = true;
            _engineText.Location = new Point(mainW - 16 - 50, 11);
            status.Controls.Add(_engineText);

            _statusSpeed = new Label();
            _statusSpeed.Text = "↓ 0 B/s";
            _statusSpeed.Font = Theme.SmallFont;
            _statusSpeed.ForeColor = Theme.TextSecondary;
            _statusSpeed.AutoSize = true;
            _statusSpeed.Location = new Point(mainW - 16 - 50 - 16 - 12 - 90, 11);
            status.Controls.Add(_statusSpeed);

            status.Resize += delegate { LayoutStatusRight(status, _statusSpeed, _natText, _engineDot, _engineText); };

            // 组装：内容(Fill 先加) → 状态栏(Bottom) → 头部(Top) → 外壳对 → 侧边栏
            Panel shell = new Panel();
            shell.Dock = DockStyle.Fill;
            shell.Controls.Add(content);
            shell.Controls.Add(status);
            shell.Controls.Add(header);

            _sidebar = new Sidebar(appIcon);
            _sidebar.Dock = DockStyle.Left;
            _sidebar.SelectedIndexChanged += delegate { SwitchPage(); };
            _sidebar.OpenFolderRequested += delegate { OpenSaveDir(); };

            // Trackers 编辑/更新后联动头部计数（仅当前页生效）
            _trackerView.CountChanged += delegate
            {
                if (_sidebar.SelectedIndex == 1) SwitchPage();
            };

            Controls.Add(shell);
            Controls.Add(_sidebar);

            SwitchPage();

            // 状态栏速度与任务行同帧刷新（引擎事件驱动，Timer 只兜底计数）
            _taskList.Synced += delegate { RefreshStatusFromEngine(); };

            // 状态栏每秒刷新（WinForms Timer 在 UI 线程触发）
            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { OnTick(); };
            _timer.Start();

            // 状态栏补引擎版本号（查询失败保持「引擎就绪」）；NAT 检测异步完成后同样重排
            string aria2Ver = _mgr.Aria2Version;
            if (aria2Ver != "") _engineText.Text = "引擎就绪 · aria2 " + aria2Ver;
            LayoutStatusRight(status, _statusSpeed, _natText, _engineDot, _engineText);
            StartNatCheck(status);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            // 退订 Manager 事件（不强杀 aria2c 子进程）
            _taskList.Detach();
            base.OnFormClosed(e);
        }

        /// 后台探测 NAT 类型并更新状态栏（UDP 阻塞放后台线程，完成后回 UI 线程重排）。
        private void StartNatCheck(Panel status)
        {
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                string text;
                switch (StunCheck.DetectType())
                {
                    case NatType.Open: text = "NAT 公网直连"; break;
                    case NatType.Cone: text = "NAT 锥形"; break;
                    case NatType.Symmetric: text = "NAT 对称型"; break;
                    default: text = "NAT 检测失败"; break;
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate()
                    {
                        if (IsDisposed) return;
                        _natText.Text = text;
                        LayoutStatusRight(status, _statusSpeed, _natText, _engineDot, _engineText);
                    });
                }
                catch { } // 窗口已关闭时忽略
            });
            t.IsBackground = true;
            t.Name = "nat-check";
            t.Start();
        }

        /// 在状态栏显示一条应用内通知（5 秒内不被定时刷新覆盖）。
        private void Notify(string text)
        {
            _notifyAt = DateTime.Now;
            _statusLeft.Text = text;
        }

        /// 切换内容页并更新头部标题/计数。
        private void SwitchPage()
        {
            int idx = _sidebar.SelectedIndex;
            for (int i = 0; i < _pages.Length; i++)
                _pages[i].Visible = i == idx;

            // 添加下载为下载页专属操作，仅下载页显示（打开目录已移至侧边栏底部常驻）
            _addBtn.Visible = idx == 0;

            if (idx == 0)
            {
                _pageTitle.Text = "全部下载";
                _pageCount.Visible = true;
                UpdateCount();
            }
            else if (idx == 1)
            {
                _pageTitle.Text = "Trackers";
                _pageCount.Text = _trackerView.Count + " 条";
                _pageCount.Visible = true;
            }
            else if (idx == 2)
            {
                _pageTitle.Text = "下载历史";
                _pageCount.Text = _hist.All().Count + " 条记录";
                _pageCount.Visible = true;
                _historyV.Reload();
            }
            else
            {
                _pageTitle.Text = "设置";
                _pageCount.Visible = false;
            }
            if (_pageCount.Visible)
                _pageCount.Location = new Point(24 + _pageTitle.Width + 10, 31);
        }

        /// 下载页计数：完成数/总数。
        private void UpdateCount()
        {
            int done = 0;
            int total = 0;
            foreach (DownloadTask t in _mgr.Snapshot())
            {
                total++;
                if (t.Status == TaskStatus.Completed) done++;
            }
            _pageCount.Text = done + "/" + total;
        }

        /// 每秒兜底：标题计数 + 通知过期恢复文案（速度刷新走引擎事件路径，与任务行同帧）。
        private void OnTick()
        {
            if (_sidebar.SelectedIndex == 0) UpdateCount();
            if (_notifyAt != DateTime.MinValue && DateTime.Now - _notifyAt >= TimeSpan.FromSeconds(5))
            {
                _notifyAt = DateTime.MinValue; // 通知期结束只恢复一次，避免每秒重设
                RefreshStatusFromEngine();
            }
        }

        /// 与任务行同帧刷新状态栏：每次引擎事件同步后调用，速度显示与行内完全一致。
        private void RefreshStatusFromEngine()
        {
            long speed = _mgr.TotalSpeed();
            int running = 0;
            foreach (DownloadTask t in _mgr.Snapshot())
            {
                if (t.Status == TaskStatus.Running) running++;
            }
            _statusSpeed.Text = speed > 0 ? "↓ " + Parser.FormatSize(speed) + "/s" : "↓ 0 B/s";
            if (DateTime.Now - _notifyAt >= TimeSpan.FromSeconds(5))
                _statusLeft.Text = running > 0 ? "下载中 " + running + " 个任务" : "就绪";
        }

        private void ShowAddDialog()
        {
            using (AddDialogForm dlg = new AddDialogForm(_mgr, Notify, _cfg.SaveDir))
            {
                dlg.ShowDialog(this);
            }
        }

        /// 嗅探服务开关（设置页保存后回调，UI 线程）：立即启停；启动失败（如端口被占用）提示。
        private void ApplySniff(bool enabled)
        {
            if (enabled)
            {
                if (_sniffer.Running) return;
                try { _sniffer.Start(); }
                catch (Exception ex) { Notify("嗅探服务启动失败：" + ex.Message); }
            }
            else if (_sniffer.Running)
            {
                _sniffer.Stop();
            }
        }

        /// 嗅探服务回调（后台线程）：转 UI 线程弹确认窗（单链接预填 / 多链接批量）。
        public void OnSniffedUrls(List<string> urls)
        {
            if (urls == null || urls.Count == 0) return;
            try
            {
                IntPtr h = Handle; // 访问 Handle 确保句柄已创建（Application.Run 前也可能收到请求）
                if (h == IntPtr.Zero) return;
                BeginInvoke((MethodInvoker)delegate { ShowSniffDialog(urls); });
            }
            catch
            {
                // 窗体未创建或已释放：忽略
            }
        }

        /// 展示嗅探结果：单链接预填添加对话框；多链接走批量确认窗。
        private void ShowSniffDialog(List<string> urls)
        {
            if (IsDisposed) return;
            if (urls.Count == 1)
            {
                using (AddDialogForm dlg = new AddDialogForm(_mgr, Notify, _cfg.SaveDir, urls[0]))
                {
                    dlg.ShowDialog(this);
                }
            }
            else
            {
                using (SniffBatchForm dlg = new SniffBatchForm(_mgr, Notify, _cfg.SaveDir, urls))
                {
                    dlg.ShowDialog(this);
                }
            }
        }

        private void OpenSaveDir()
        {
            try
            {
                Process.Start("explorer.exe", "\"" + _cfg.SaveDir + "\"");
            }
            catch
            {
                // 启动 explorer 失败静默忽略
            }
        }

        // ---- 无边框窗口行为 ----

        private const int ResizeEdge = 6;
        private const int CaptionZoneW = 3 * 46 + 8; // 右上三个窗口按钮区的横向保护宽度
        private const int CaptionZoneH = 36;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Win32.ApplyFramelessChrome(Handle); // Win11: DWM 圆角 + 投影
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateMaxGlyph();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                if ((int)m.Result == Win32.HTCLIENT)
                {
                    int lp = m.LParam.ToInt32();
                    int ht = HitTestEdge(PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF))));
                    if (ht != 0) m.Result = (IntPtr)ht;
                }
                return;
            }
            if (m.Msg == Win32.WM_GETMINMAXINFO)
            {
                base.WndProc(ref m);
                Win32.ClampMaximized(Handle, m.LParam);
                return;
            }
            base.WndProc(ref m);
        }

        /// 客户区边缘 → 缩放命中码（0 = 非边缘）；右上窗口按钮区不参与，避免遮挡按钮点击。
        private int HitTestEdge(Point p)
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (p.X >= w - CaptionZoneW && p.Y <= CaptionZoneH) return 0;
            bool left = p.X < ResizeEdge;
            bool right = p.X >= w - ResizeEdge;
            bool top = p.Y < ResizeEdge;
            bool bottom = p.Y >= h - ResizeEdge;
            if (top && left) return Win32.HTTOPLEFT;
            if (top && right) return Win32.HTTOPRIGHT;
            if (bottom && left) return Win32.HTBOTTOMLEFT;
            if (bottom && right) return Win32.HTBOTTOMRIGHT;
            if (left) return Win32.HTLEFT;
            if (right) return Win32.HTRIGHT;
            if (top) return Win32.HTTOP;
            if (bottom) return Win32.HTBOTTOM;
            return 0;
        }

        /// 标题区拖拽/双击：最大化时先按鼠标比例还原，再进入系统拖动循环（自带 Aero Snap）。
        private void AttachTitleDrag(Control c)
        {
            c.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
                if (WindowState == FormWindowState.Maximized)
                {
                    Point cursor = Cursor.Position;
                    double ratio = Width <= 0 ? 0.5 : (double)cursor.X / Width;
                    Rectangle rb = RestoreBounds;
                    WindowState = FormWindowState.Normal;
                    int x = cursor.X - (int)(rb.Width * ratio);
                    int y = cursor.Y - Math.Max(8, rb.Height / 30);
                    Screen sc = Screen.FromPoint(cursor);
                    if (x < sc.WorkingArea.Left) x = sc.WorkingArea.Left;
                    if (x + rb.Width > sc.WorkingArea.Right) x = sc.WorkingArea.Right - rb.Width;
                    if (y < sc.WorkingArea.Top) y = sc.WorkingArea.Top;
                    Location = new Point(x, y);
                }
                Win32.DragWindow(Handle);
            };
            c.MouseDoubleClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                ToggleMaximize();
            };
        }

        /// 最大化/还原切换。
        private void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            else WindowState = FormWindowState.Maximized;
            UpdateMaxGlyph();
        }

        /// 最大化按钮字形随窗口状态切换（E922 最大化 / E923 还原）。
        private void UpdateMaxGlyph()
        {
            if (_maxBtn == null) return;
            _maxBtn.Text = WindowState == FormWindowState.Maximized ? "\uE923" : "\uE922";
        }

        /// 头部右侧按钮布局（宽度变化时重算，避免 Anchor 在加入父容器前计算的偏移问题）。
        private static void LayoutHeaderButtons(Control header, Control addBtn, Control minBtn, Control maxBtn, Control closeBtn)
        {
            closeBtn.Location = new Point(header.Width - 16 - closeBtn.Width, 15);
            maxBtn.Location = new Point(closeBtn.Left - 6 - maxBtn.Width, 15);
            minBtn.Location = new Point(maxBtn.Left - 6 - minBtn.Width, 15);
            addBtn.Location = new Point(minBtn.Left - 16 - addBtn.Width, 15);
        }

        /// 状态栏右侧组布局：速度 + NAT 检测 + 引擎绿点 + 引擎文字。
        private static void LayoutStatusRight(Control status, Label speed, Label nat, Label dot, Label engineText)
        {
            engineText.Location = new Point(status.Width - 16 - engineText.Width, 11);
            dot.Location = new Point(engineText.Left - 6 - dot.Width, 10);
            nat.Location = new Point(dot.Left - 12 - nat.Width, 11);
            speed.Location = new Point(nat.Left - 12 - speed.Width, 11);
        }

        /// 头部底边线。
        private static void PaintBottomBorder(object sender, PaintEventArgs e)
        {
            Control c = (Control)sender;
            using (Pen p = new Pen(Theme.Border))
                e.Graphics.DrawLine(p, 0, c.Height - 1, c.Width, c.Height - 1);
        }

        /// 状态栏顶边线。
        private static void PaintTopBorder(object sender, PaintEventArgs e)
        {
            Control c = (Control)sender;
            using (Pen p = new Pen(Theme.Border))
                e.Graphics.DrawLine(p, 0, 0, c.Width, 0);
        }
    }
}
