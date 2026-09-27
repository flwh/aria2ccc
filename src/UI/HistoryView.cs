using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AriaGui.Engine;

namespace AriaGui.UI
{
    /// 历史记录页：两行条目（标题 + 详情斜体）与"清空历史"按钮（现代浅色风格）。
    public sealed class HistoryView : Panel
    {
        private readonly HistoryStore _store;
        private readonly ListBox _list;
        private readonly SolidBrush _selBg = new SolidBrush(Color.FromArgb(239, 246, 255));
        private Font _subFont;

        public HistoryView(HistoryStore store)
        {
            _store = store;
            BackColor = Theme.WindowBg;

            _list = new ListBox();
            _list.Dock = DockStyle.Fill;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = 44; // 两行：标题 18px + 详情 16px，留白舒展
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = Color.White;
            _list.DrawItem += DrawItem;

            _subFont = new Font(Font, FontStyle.Italic);

            Button clear = new Button();
            clear.Text = "清空历史";
            clear.SetBounds(14, 8, 104, 30);
            Theme.StyleDangerGhostButton(clear);
            clear.Click += delegate { OnClearClicked(); };

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 46;
            bottom.BackColor = Theme.WindowBg;
            bottom.Controls.Add(clear);

            // Dock 顺序：Fill 先加，Bottom 后加（WinForms 反序布局规则）。
            Controls.Add(_list);
            Controls.Add(bottom);
            Reload();
        }

        /// 重新读取历史并刷新（主窗口切到本页时调用）。
        public void Reload()
        {
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                List<HistoryEntry> items = _store.All();
                for (int i = 0; i < items.Count; i++)
                    _list.Items.Add(items[i]);
            }
            finally
            {
                _list.EndUpdate();
            }
        }

        private void OnClearClicked()
        {
            DialogResult r = MessageBox.Show(this, "确定删除全部历史记录？", "清空历史",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;
            _store.Clear();
            Reload();
        }

        /// 两行绘制：`[状态] 名称`，次行 `大小   时间   目录`；选中项浅蓝底 + 左侧主色条。
        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _list.Items.Count) return;
            HistoryEntry it = (HistoryEntry)_list.Items[e.Index];

            Rectangle b = e.Bounds;
            bool sel = (e.State & DrawItemState.Selected) != 0;

            if (sel)
            {
                e.Graphics.FillRectangle(_selBg, b);
                using (SolidBrush accent = new SolidBrush(Theme.Primary))
                    e.Graphics.FillRectangle(accent, new Rectangle(b.X, b.Y, 3, b.Height));
            }
            else
            {
                e.Graphics.FillRectangle(Brushes.White, b);
            }

            string title = "[" + it.Status + "] " + it.Name;
            string sub = string.Format(CultureInfo.InvariantCulture, "{0}   {1}   {2}",
                Parser.FormatSize(it.Size),
                it.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                it.Dir);

            Color titleColor = sel ? Theme.Primary : Theme.TextPrimary;
            TextRenderer.DrawText(e.Graphics, title, Font,
                new Rectangle(b.X + 14, b.Y + 5, b.Width - 22, 18), titleColor,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, sub, _subFont,
                new Rectangle(b.X + 14, b.Y + 24, b.Width - 22, 16), Theme.TextMuted,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (_subFont != null) _subFont.Dispose();
            _subFont = new Font(Font, FontStyle.Italic);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_subFont != null) _subFont.Dispose();
                _selBg.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
