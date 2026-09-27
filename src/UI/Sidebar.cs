using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AriaGui.UI
{
    /// 左侧导航栏：浅蓝渐变背景 + 自绘圆角导航项（图标 + 文本，含悬停/选中态）。
    public sealed class Sidebar : Control
    {
        public const int WidthPx = 200;

        private static readonly string[] NavIcons = new string[] { "\uE896", "\uE774", "\uE81C", "\uE713" };
        private static readonly string[] NavTitles = new string[] { "下载", "Trackers", "历史", "设置" };

        private readonly Font _itemFont;
        private readonly Font _iconFont;
        private readonly Font _brandFont;
        private readonly Bitmap _brandIcon;
        private readonly StringFormat _sf;
        private int _selected;
        private int _hover = -1;

        private const int BrandH = 64;
        private const int ItemH = 40;
        private const int ItemGap = 4;
        private const int PadX = 12;

        /// 选中的导航项变化（用户点击导致）。
        public event EventHandler SelectedIndexChanged;

        public Sidebar(Icon appIcon)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = WidthPx;
            _itemFont = Theme.BaseFont;
            _iconFont = Theme.IconFont;
            _brandFont = Theme.TitleFont;
            if (appIcon != null)
            {
                try { _brandIcon = appIcon.ToBitmap(); }
                catch { _brandIcon = null; }
            }
            _sf = new StringFormat();
            _sf.LineAlignment = StringAlignment.Center;
            _sf.Alignment = StringAlignment.Near;
            _sf.FormatFlags = StringFormatFlags.NoWrap;
            _sf.Trimming = StringTrimming.EllipsisCharacter;
        }

        /// 当前选中项（0=下载，1=Trackers，2=历史，3=设置）。
        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v > NavTitles.Length - 1) v = NavTitles.Length - 1;
                if (v == _selected) return;
                _selected = v;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        private Rectangle ItemRect(int i)
        {
            return new Rectangle(PadX, BrandH + i * (ItemH + ItemGap), Width - PadX * 2, ItemH);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < NavTitles.Length; i++)
            {
                if (ItemRect(i).Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h != _hover)
            {
                _hover = h;
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int h = HitTest(e.Location);
            if (h >= 0) SelectedIndex = h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // 垂直渐变：左上浅蓝 → 下方近白（近似参考布局的侧栏质感）
            using (LinearGradientBrush bg = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(224, 237, 252), Color.FromArgb(249, 251, 255), LinearGradientMode.Vertical))
            {
                g.FillRectangle(bg, ClientRectangle);
            }
            using (Pen edge = new Pen(Color.FromArgb(219, 230, 246)))
            {
                g.DrawLine(edge, Width - 1, 0, Width - 1, Height);
            }

            // 品牌区：应用图标 + 名称
            if (_brandIcon != null)
                g.DrawImage(_brandIcon, new Rectangle(16, 20, 24, 24));
            using (SolidBrush tb = new SolidBrush(Theme.TextPrimary))
                g.DrawString("Aria 下载器", _brandFont, tb, 48, 24);

            using (Pen line = new Pen(Color.FromArgb(213, 226, 245)))
                g.DrawLine(line, PadX, BrandH - 8, Width - PadX, BrandH - 8);

            // 导航项
            for (int i = 0; i < NavTitles.Length; i++)
            {
                Rectangle r = ItemRect(i);
                bool sel = i == _selected;
                if (sel || i == _hover)
                {
                    using (GraphicsPath p = Theme.RoundRect(r, 8))
                    using (SolidBrush sb = new SolidBrush(sel ? Color.White : Color.FromArgb(120, 255, 255, 255)))
                    {
                        g.FillPath(sb, p);
                    }
                    if (sel)
                    {
                        using (GraphicsPath p2 = Theme.RoundRect(r, 8))
                        using (Pen pen = new Pen(Color.FromArgb(226, 232, 240)))
                            g.DrawPath(pen, p2);
                    }
                }

                Color iconColor = sel ? Theme.Primary : Color.FromArgb(107, 114, 128);
                Color textColor = sel ? Theme.TextPrimary : Color.FromArgb(75, 85, 99);
                using (SolidBrush ib = new SolidBrush(iconColor))
                    g.DrawString(NavIcons[i], _iconFont, ib,
                        new RectangleF(r.X + 14, r.Y, 28, r.Height), _sf);
                using (SolidBrush tb = new SolidBrush(textColor))
                    g.DrawString(NavTitles[i], _itemFont, tb,
                        new RectangleF(r.X + 46, r.Y, r.Width - 58, r.Height), _sf);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_brandIcon != null) _brandIcon.Dispose();
                _sf.Dispose(); // 字体为 Theme 共享实例，不释放
            }
            base.Dispose(disposing);
        }
    }
}
