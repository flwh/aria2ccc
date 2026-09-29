using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AriaGui.UI
{
    /// 左侧导航栏：macOS 风格浅灰背景（与窗口 chrome 同色）+ 自绘圆角导航项（图标 + 文本，含悬停/选中态）。
    public sealed class Sidebar : Control
    {
        public const int WidthPx = 200;

        /// 导航图标（IconPark 线条风格，48 视图，绘制时统一描边；与文字同色：未选中灰、选中白）。
        private static readonly string[][] NavIconPaths = new string[][]
        {
            MiniSvg.Icons.Download,
            MiniSvg.Icons.Earth,
            MiniSvg.Icons.History,
            MiniSvg.Icons.Setting,
        };
        private static readonly string[] NavTitles = new string[] { "下载", "Trackers", "历史", "设置" };

        private readonly Font _itemFont;
        private readonly StringFormat _sf;
        private int _selected;
        private int _hover = -1;
        private bool _hoverOpen;

        private const int TopPad = 12;
        private const int ItemH = 40;
        private const int ItemGap = 4;
        private const int PadX = 12;

        /// 选中的导航项变化（用户点击导致）。
        public event EventHandler SelectedIndexChanged;

        /// 点击底部「打开目录」动作按钮。
        public event EventHandler OpenFolderRequested;

        public Sidebar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = WidthPx;
            _itemFont = Theme.BaseFont;
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
            return new Rectangle(PadX, TopPad + i * (ItemH + ItemGap), Width - PadX * 2, ItemH);
        }

        /// 底部「打开目录」动作按钮区域（非导航项，不参与选中）。
        private Rectangle OpenRect
        {
            get { return new Rectangle(PadX, Height - 12 - ItemH, Width - PadX * 2, ItemH); }
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
            bool ho = OpenRect.Contains(e.Location);
            if (h != _hover || ho != _hoverOpen)
            {
                _hover = h;
                _hoverOpen = ho;
                Cursor = (h >= 0 || ho) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1 || _hoverOpen)
            {
                _hover = -1;
                _hoverOpen = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (OpenRect.Contains(e.Location))
            {
                if (OpenFolderRequested != null) OpenFolderRequested(this, EventArgs.Empty);
                return;
            }
            int h = HitTest(e.Location);
            if (h >= 0) SelectedIndex = h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // macOS 风格：窗口 chrome 浅灰纯色（与标题栏/工具栏同色），右侧细分隔线
            using (SolidBrush bg = new SolidBrush(Theme.ChromeBg))
            {
                g.FillRectangle(bg, ClientRectangle);
            }
            using (Pen edge = new Pen(Theme.TrackBg))
            {
                g.DrawLine(edge, Width - 1, 0, Width - 1, Height);
            }

            // 导航项（应用名已由窗口标题栏展示，此处不再重复品牌区）
            for (int i = 0; i < NavTitles.Length; i++)
            {
                Rectangle r = ItemRect(i);
                bool sel = i == _selected;
                if (sel || i == _hover)
                {
                    using (GraphicsPath p = Theme.RoundRect(r, 8))
                    using (SolidBrush sb = new SolidBrush(sel ? Theme.Primary : Color.FromArgb(236, 236, 240)))
                    {
                        g.FillPath(sb, p);
                    }
                }

                Color textColor = sel ? Color.White : Color.FromArgb(60, 60, 67);
                // IconPark 线条图标（未选中灰、选中白）
                Rectangle ir = new Rectangle(r.X + 12, r.Y + (r.Height - 20) / 2, 20, 20);
                MiniSvg.DrawIcon(g, ir, textColor, NavIconPaths[i]);
                using (SolidBrush tb = new SolidBrush(textColor))
                    g.DrawString(NavTitles[i], _itemFont, tb,
                        new RectangleF(r.X + 46, r.Y, r.Width - 58, r.Height), _sf);
            }

            // 底部「打开目录」动作按钮：分隔线 + 悬停高亮，不参与选中态
            Rectangle o = OpenRect;
            using (Pen line = new Pen(Theme.TrackBg))
                g.DrawLine(line, PadX, o.Top - 10, Width - PadX, o.Top - 10);
            if (_hoverOpen)
            {
                using (GraphicsPath p = Theme.RoundRect(o, 8))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(236, 236, 240)))
                    g.FillPath(sb, p);
            }
            Rectangle oi = new Rectangle(o.X + 12, o.Y + (o.Height - 20) / 2, 20, 20);
            MiniSvg.DrawIcon(g, oi, Theme.Primary, MiniSvg.Icons.FolderOpen);
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(60, 60, 67)))
                g.DrawString("打开目录", _itemFont, tb, new RectangleF(o.X + 46, o.Y, o.Width - 58, o.Height), _sf);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sf.Dispose(); // 字体为 Theme 共享实例，不释放
            }
            base.Dispose(disposing);
        }
    }
}
