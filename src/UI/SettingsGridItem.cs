using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AriaGui.UI
{
    /// 设置页分类入口（网格项）：彩色圆角图标 + 分类名 + 说明；hover 高亮，整块可点击。
    internal sealed class SettingsGridItem : Control
    {
        private readonly string _glyph;
        private readonly Color _color;
        private readonly string _title;
        private readonly string _desc;
        private bool _hover;

        public SettingsGridItem(string glyph, Color color, string title, string desc)
        {
            _glyph = glyph;
            _color = color;
            _title = title;
            _desc = desc;
            BackColor = Theme.WindowBg;
            Cursor = Cursors.Hand;
            // StandardClick：Control 基类默认关闭，不开则鼠标抬起不触发 Click 事件
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw
                | ControlStyles.StandardClick, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.WindowBg); // 先铺满再画（防 hover 残影，项目自绘控件惯例）
            if (_hover)
            {
                using (GraphicsPath hp = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Theme.RadiusCard))
                using (SolidBrush hb = new SolidBrush(Theme.HoverBg))
                    g.FillPath(hb, hp);
            }

            // 彩色圆角图标块 + 白色字形居中
            using (GraphicsPath ip = Theme.RoundRect(new Rectangle(2, 2, 46, 46), 14))
            using (SolidBrush ib = new SolidBrush(_color))
                g.FillPath(ib, ip);
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                using (SolidBrush gb = new SolidBrush(Color.White))
                    g.DrawString(_glyph, Theme.IconFontLarge, gb, new RectangleF(2, 2, 46, 46), sf);
            }

            // 分类名与说明
            using (SolidBrush tb = new SolidBrush(Theme.TextPrimary))
                g.DrawString(_title, Theme.TitleFont, tb, 2, 60);
            using (SolidBrush db = new SolidBrush(Theme.TextSecondary))
                g.DrawString(_desc, Theme.SmallFont, db, new RectangleF(2, 86, Width - 6, Height - 90));
        }
    }
}
