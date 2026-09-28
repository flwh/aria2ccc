using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AriaGui.UI
{
    /// 统一主题：现代浅色配色、字体与控件样式辅助（基于系统自带能力，无第三方依赖）。
    internal static class Theme
    {
        public static readonly Color Primary = Color.FromArgb(37, 99, 235);
        public static readonly Color PrimaryHover = Color.FromArgb(29, 78, 216);
        public static readonly Color PrimaryPressed = Color.FromArgb(30, 64, 175);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color DangerHover = Color.FromArgb(254, 242, 242);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color WindowBg = Color.FromArgb(243, 244, 246);
        public static readonly Color CardBg = Color.White;
        public static readonly Color HoverBg = Color.FromArgb(249, 250, 251);
        public static readonly Color Border = Color.FromArgb(229, 231, 235);
        public static readonly Color InputBorder = Color.FromArgb(209, 213, 219);
        public static readonly Color TrackBg = Color.FromArgb(229, 231, 235);
        public static readonly Color TextPrimary = Color.FromArgb(17, 24, 39);
        public static readonly Color TextSecondary = Color.FromArgb(107, 114, 128);
        public static readonly Color TextMuted = Color.FromArgb(156, 163, 175);
        public static readonly Color DangerPressed = Color.FromArgb(254, 226, 226);

        /// 小圆角半径：卡片 / 按钮 / 输入框（统一的小圆角观感）。
        public const int RadiusCard = 8;
        public const int RadiusButton = 6;
        public const int RadiusInput = 6;

        public static readonly Font BaseFont = CreateFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
        public static readonly Font SmallFont = CreateFont("Microsoft YaHei UI", 8.25f, FontStyle.Regular);
        public static readonly Font TitleFont = CreateFont("Microsoft YaHei UI", 10f, FontStyle.Bold);
        public static readonly Font StatusFont = CreateFont("Microsoft YaHei UI", 8.25f, FontStyle.Bold);
        public static readonly Font PageTitleFont = CreateFont("Microsoft YaHei UI", 15f, FontStyle.Bold);
        public static readonly Font IconFont = CreateIconFont(11f);
        public static readonly Font MonoFont = CreateFont("Consolas", 9f, FontStyle.Regular);

        private static Font CreateFont(string family, float size, FontStyle style)
        {
            try
            {
                return new Font(family, size, style);
            }
            catch
            {
                try { return new Font("Segoe UI", size, style); }
                catch { return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style); }
            }
        }

        /// 图标字体：优先 Segoe Fluent Icons（Win11），回退 Segoe MDL2 Assets（Win10），最终回退正文字体。
        private static Font CreateIconFont(float size)
        {
            string[] names = new string[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    Font f = new Font(names[i], size);
                    if (string.Equals(f.Name, names[i], StringComparison.OrdinalIgnoreCase))
                        return f;
                    f.Dispose();
                }
                catch
                {
                }
            }
            return BaseFont;
        }

        /// 状态 → 展示色。
        public static Color StatusColor(Engine.TaskStatus s)
        {
            switch (s)
            {
                case Engine.TaskStatus.Running: return Primary;
                case Engine.TaskStatus.Completed: return Success;
                case Engine.TaskStatus.Failed: return Danger;
                case Engine.TaskStatus.Paused: return TextSecondary;
                default: return TextMuted;
            }
        }

        /// 主按钮：蓝底白字扁平圆角。
        public static void StylePrimaryButton(FlatButton b)
        {
            b.SetPalette(Primary, PrimaryHover, PrimaryPressed, Color.Empty, Color.White);
        }

        /// 次按钮：白底灰边。
        public static void StyleSecondaryButton(FlatButton b)
        {
            b.SetPalette(Color.White, HoverBg, WindowBg, Border, TextPrimary);
        }

        /// 危险幽灵按钮：白底红字（用于"删除"/"清空历史"）。
        public static void StyleDangerGhostButton(FlatButton b)
        {
            b.SetPalette(Color.White, DangerHover, DangerPressed, Border, Danger);
        }

        /// 文本输入框基础样式（1px 边框由 ThemedTextBox 的父容器垫层代绘，见下）。
        public static void StyleTextBox(TextBox t)
        {
            t.BorderStyle = BorderStyle.None;
            t.BackColor = Color.White;
            t.ForeColor = TextPrimary;
            t.Font = BaseFont;
        }

        /// 表单标签：次级灰。
        public static void StyleFormLabel(Label l)
        {
            l.ForeColor = TextPrimary;
            l.Font = BaseFont;
        }

        /// 卡片：小圆角白底 + 1px 细边框（Panel.Paint 处理器；卡片 BackColor 应设为父容器背景色）。
        public static void CardPaint(object sender, PaintEventArgs e)
        {
            Control c = (Control)sender;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, c.Width - 1, c.Height - 1);
            using (GraphicsPath p = RoundRect(r, RadiusCard))
            using (SolidBrush b = new SolidBrush(CardBg))
                e.Graphics.FillPath(b, p);
            using (GraphicsPath p2 = RoundRect(r, RadiusCard))
            using (Pen pen = new Pen(Border))
                e.Graphics.DrawPath(pen, p2);
        }

        /// 圆角矩形路径（直径取 min(2*radius, 宽, 高)）。
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.X + r.Width - d, r.Y, d, d, 270, 90);
            p.AddArc(r.X + r.Width - d, r.Y + r.Height - d, d, d, 0, 90);
            p.AddArc(r.X, r.Y + r.Height - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// 弹出目录选择对话框，选中后写入输入框（「浏览…」按钮共用）。
        public static void BrowseFolderInto(IWin32Window owner, TextBox target)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(target.Text)) dlg.SelectedPath = target.Text;
                if (dlg.ShowDialog(owner) == DialogResult.OK) target.Text = dlg.SelectedPath;
            }
        }
    }

    /// 扁平小圆角按钮：自绘圆角背景与居中文本（替代 FlatStyle 直角系统绘制）。
    /// 三档配色由 StyleXxxButton 经 SetPalette 注入；窗口控制按钮不使用本类（保持直角）。
    internal sealed class FlatButton : Button
    {
        private Color _fill = Color.White;
        private Color _hover = Color.White;
        private Color _down = Color.White;
        private Color _border = Color.Empty;
        private Color _text = Color.Black;
        private bool _hovering;
        private bool _pressing;
        private Region _round;   // 圆角裁剪区（圆角外不显示本控件像素，杜绝黑角/残影）

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White; // 圆角外区域由系统以 BackColor 铺底（即父容器背景）
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            Font = Theme.BaseFont;
        }

        /// 注入配色：border 传 Color.Empty 表示无边框。
        public void SetPalette(Color fill, Color hover, Color down, Color border, Color text)
        {
            _fill = fill;
            _hover = hover;
            _down = down;
            _border = border;
            _text = text;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovering = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovering = false;
            _pressing = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            _pressing = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            _pressing = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRound();
        }

        /// 控件裁剪为圆角矩形：圆角外一律不显示本控件像素，防止系统层/缓冲残留露出黑角。
        private void UpdateRound()
        {
            if (_round != null)
            {
                if (Region == _round) Region = null;
                _round.Dispose();
                _round = null;
            }
            if (Width < 2 || Height < 2) return;
            _round = new Region(Theme.RoundRect(new Rectangle(0, 0, Width, Height), Theme.RadiusButton));
            Region = _round;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = _fill;
            if (Enabled)
            {
                if (_pressing) fill = _down;
                else if (_hovering) fill = _hover;
            }
            // 先填满整个控件区（Region 内），再画圆角：圆角弧外的缓冲残留一律被底色盖掉
            g.Clear(fill);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.RoundRect(r, Theme.RadiusButton))
            using (SolidBrush sb = new SolidBrush(fill))
            {
                g.FillPath(sb, p);
            }
            if (_border != Color.Empty)
            {
                using (GraphicsPath p2 = Theme.RoundRect(r, Theme.RadiusButton))
                using (Pen pen = new Pen(_border))
                {
                    g.DrawPath(pen, p2);
                }
            }
            TextRenderer.DrawText(g, Text, Font, r, Enabled ? _text : Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// 窗口控制按钮（最小化/最大化/关闭）：34x34 小圆角，与标题栏加号同一圆角语言；
    /// 悬停显示圆角高亮块（关闭键红底白图标），点击经 Click 事件。
    internal sealed class WindowButton : Control
    {
        private readonly bool _closeStyle;
        private bool _hover;
        private bool _down;
        private Region _round;   // 圆角裁剪区（圆角外不显示本控件像素）

        public WindowButton(string glyph, bool closeStyle)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.StandardClick, true);
            BackColor = Color.White;
            _closeStyle = closeStyle;
            Cursor = Cursors.Hand;
            TabStop = false;
            Font = new Font(Theme.IconFont.FontFamily, 10f);
            Size = new Size(34, 34);
            Text = glyph;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();   // 字形切换（最大化⇄还原）立即重绘
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRound();
        }

        /// 控件裁剪为小圆角，与 FlatButton 一致。
        private void UpdateRound()
        {
            if (_round != null)
            {
                if (Region == _round) Region = null;
                _round.Dispose();
                _round = null;
            }
            if (Width < 2 || Height < 2) return;
            _round = new Region(Theme.RoundRect(new Rectangle(0, 0, Width, Height), Theme.RadiusButton));
            Region = _round;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _down = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _down = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _down = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Color.White;
            if (_hover) fill = _closeStyle ? Color.FromArgb(232, 17, 35) : Color.FromArgb(233, 233, 233);
            if (_down) fill = _closeStyle ? Color.FromArgb(241, 112, 122) : Color.FromArgb(221, 221, 221);
            g.Clear(fill);
            Color fg = (_closeStyle && (_hover || _down)) ? Color.White : Theme.TextPrimary;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// 扁平圆角进度条（自绘，替代原生 ProgressBar 以统一配色）。
    internal sealed class FlatProgressBar : Control
    {
        private int _value;
        private Region _round;   // 胶囊裁剪区（圆角外不显示本控件像素）

        public FlatProgressBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 10;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRound();
        }

        /// 控件裁剪为胶囊形（半径 = 高），圆角外不绘制。
        private void UpdateRound()
        {
            if (_round != null)
            {
                if (Region == _round) Region = null;
                _round.Dispose();
                _round = null;
            }
            if (Width < 2 || Height < 2) return;
            _round = new Region(Theme.RoundRect(new Rectangle(0, 0, Width, Height), Height));
            Region = _round;
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v > 100) v = 100;
                if (v == _value) return;
                _value = v;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = Width;
            int h = Height;
            if (w <= 2 || h <= 2) return;
            g.Clear(Theme.TrackBg);   // 先铺满整个控件区，再画胶囊（消除弧外残留）

            using (GraphicsPath bg = Theme.RoundRect(new Rectangle(0, 0, w, h), h))
            using (SolidBrush bgb = new SolidBrush(Theme.TrackBg))
            {
                g.FillPath(bgb, bg);
            }
            int fw = (int)Math.Round((double)w * _value / 100.0);
            if (fw > 0)
            {
                if (fw < h) fw = h;
                using (GraphicsPath fg = Theme.RoundRect(new Rectangle(0, 0, fw, h), h))
                using (SolidBrush fgb = new SolidBrush(Theme.Primary))
                {
                    g.FillPath(fgb, fg);
                }
            }
        }
    }

    /// 工具栏浅色渲染：白底、灰边、浅灰悬停。
    internal sealed class LightColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin { get { return Color.White; } }
        public override Color ToolStripGradientMiddle { get { return Color.White; } }
        public override Color ToolStripGradientEnd { get { return Color.White; } }
        public override Color ToolStripBorder { get { return Theme.Border; } }
        public override Color ButtonSelectedHighlight { get { return Theme.HoverBg; } }
        public override Color ButtonSelectedHighlightBorder { get { return Theme.HoverBg; } }
        public override Color ButtonSelectedBorder { get { return Theme.HoverBg; } }
        public override Color ButtonPressedHighlight { get { return Theme.WindowBg; } }
        public override Color ButtonPressedHighlightBorder { get { return Theme.WindowBg; } }
        public override Color ButtonPressedBorder { get { return Theme.WindowBg; } }
        public override Color ButtonCheckedHighlight { get { return Theme.HoverBg; } }
        public override Color ButtonCheckedHighlightBorder { get { return Theme.HoverBg; } }
        public override Color ButtonCheckedGradientBegin { get { return Theme.HoverBg; } }
        public override Color ButtonCheckedGradientMiddle { get { return Theme.HoverBg; } }
        public override Color ButtonCheckedGradientEnd { get { return Theme.HoverBg; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
        public override Color StatusStripGradientBegin { get { return Color.White; } }
        public override Color StatusStripGradientEnd { get { return Color.White; } }
    }

    /// 扁平化输入框：无系统边框；外圈 1px 细边框由父容器代绘（常态浅灰 ↔ 聚焦主题蓝），
    /// 左右 8px 内边距。实现为「1px 边框垫层」：实际控件内缩 1px，父容器在环区补画边框。
    internal sealed class ThemedTextBox : TextBox
    {
        private const int EM_SETMARGINS = 0x00D3;
        private const int EC_LEFTMARGIN = 0x0001;
        private const int EC_RIGHTMARGIN = 0x0002;
        private const int TextPad = 8;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private Rectangle _frame;   // 含边框的外矩形（相对父容器）
        private Control _hooked;    // 已挂钩 Paint 的父容器
        private Region _round;      // 控件圆角裁剪区（随尺寸重建）

        public ThemedTextBox()
        {
            BorderStyle = BorderStyle.None;
            AutoSize = false; // 单行 TextBox 默认 AutoSize=true 会把高度调回首选项，撑破 1px 边框环
        }

        /// 外部（SetBounds）给出的是含边框的外矩形；实际控件内缩 1px 留出边框环。
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            if (width < 2) width = 2;
            if (height < 2) height = 2;
            _frame = new Rectangle(x, y, width, height);
            base.SetBoundsCore(x + 1, y + 1, width - 2, height - 2, specified);
            UpdateRound();
            InvalidateFrame();
        }

        /// 控件裁剪为小圆角（内半径比边框环内缩 1px，与外框弧同心）。
        private void UpdateRound()
        {
            if (_round != null)
            {
                if (Region == _round) Region = null;
                _round.Dispose();
                _round = null;
            }
            if (Width < 2 || Height < 2) return;
            _round = new Region(Theme.RoundRect(new Rectangle(0, 0, Width, Height), Theme.RadiusInput - 1));
            Region = _round;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 左右内边距（lParam = MAKELONG(left, right)）
            IntPtr margins = (IntPtr)(TextPad | (TextPad << 16));
            SendMessage(Handle, EM_SETMARGINS, (IntPtr)(EC_LEFTMARGIN | EC_RIGHTMARGIN), margins);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (_hooked != null) _hooked.Paint -= FramePaint;
            _hooked = Parent;
            if (_hooked != null) _hooked.Paint += FramePaint;
            InvalidateFrame();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            InvalidateFrame();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            InvalidateFrame();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            InvalidateFrame();
        }

        /// 只失效 1px 边框环（不触碰控件区域），父容器重绘时补画。
        private void InvalidateFrame()
        {
            if (_hooked == null || _frame.Width <= 2 || _frame.Height <= 2) return;
            using (Region r = new Region(_frame))
            {
                r.Exclude(new Rectangle(_frame.X + 1, _frame.Y + 1, _frame.Width - 2, _frame.Height - 2));
                _hooked.Invalidate(r);
            }
        }

        /// 父容器代绘小圆角边框环：白底填充 + 1px 描边（常态浅灰 ↔ 聚焦主题蓝）。
        private void FramePaint(object sender, PaintEventArgs e)
        {
            if (_frame.Width <= 1 || _frame.Height <= 1) return;
            Color c = Focused ? Theme.Primary : Theme.InputBorder;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(_frame.X, _frame.Y, _frame.Width - 1, _frame.Height - 1);
            using (GraphicsPath p = Theme.RoundRect(r, Theme.RadiusInput))
            {
                using (SolidBrush b = new SolidBrush(Color.White))
                    e.Graphics.FillPath(b, p);
                using (Pen pen = new Pen(c))
                    e.Graphics.DrawPath(pen, p);
            }
        }
    }

    /// Win32 互操作：占位符（EM_SETCUEBANNER）、无边框窗口（缩放命中/最大化修正/DWM 圆角/标题拖拽）。
    internal static class Win32
    {
        private const int EM_SETCUEBANNER = 0x1501;

        // ---- 无边框窗口消息与命中码 ----
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_GETMINMAXINFO = 0x0024;
        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int HTCLIENT = 1;
        public const int HTCAPTION = 2;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT Reserved;
            public POINT MaxSize;
            public POINT MaxPosition;
            public POINT MinTrackSize;
            public POINT MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int Size;
            public RECT Monitor;
            public RECT Work;
            public int Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int Left; public int Right; public int Top; public int Bottom; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        public static void SetPlaceholder(TextBox box, string text)
        {
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }

        /// 开始系统级窗口拖动（自带 Aero Snap；调用方负责最大化状态下的先还原）。
        public static void DragWindow(IntPtr hwnd)
        {
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        /// 修正最大化尺寸/位置到显示器工作区（无边框窗口默认会盖住任务栏）。
        public static void ClampMaximized(IntPtr hwnd, IntPtr lParam)
        {
            IntPtr mon = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            MONITORINFO mi = new MONITORINFO();
            mi.Size = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(mon, ref mi)) return;
            MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            // ptMaxPosition 相对显示器左上角；工作区与显示器的差值即任务栏让位
            mmi.MaxPosition.X = Math.Abs(mi.Work.Left - mi.Monitor.Left);
            mmi.MaxPosition.Y = Math.Abs(mi.Work.Top - mi.Monitor.Top);
            mmi.MaxSize.X = Math.Abs(mi.Work.Right - mi.Work.Left);
            mmi.MaxSize.Y = Math.Abs(mi.Work.Bottom - mi.Work.Top);
            Marshal.StructureToPtr(mmi, lParam, false);
        }

        /// Win11：DWM 圆角 + 投影边框（老系统无此属性时静默忽略）。
        public static void ApplyFramelessChrome(IntPtr hwnd)
        {
            try
            {
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, 4);
            }
            catch
            {
            }
            try
            {
                MARGINS m = new MARGINS();
                m.Left = 1; m.Right = 1; m.Top = 1; m.Bottom = 1;
                DwmExtendFrameIntoClientArea(hwnd, ref m); // 1px 扩展框：启用原生投影与圆角生效
            }
            catch
            {
            }
        }
    }
}
