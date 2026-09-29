using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace AriaGui.UI
{
    /// 设置页：分类网格首页（图标 + 分类名 + 说明，qBittorrent 风格），点击分类进入子页。
    /// 六类：通用（默认目录）、下载（并发/分片/限速）、网络（代理/UA）、RPC、扩展（嗅探）、关于。
    public sealed class SettingsView : Panel
    {
        private readonly Config _cfg;
        private readonly Action<string> _notify;
        private readonly Action<bool> _applySniff;
        private readonly Action _applyRpc;

        private readonly TextBox _dirBox;
        private readonly TextBox _concBox;
        private readonly TextBox _speedBox;
        private readonly TextBox _splitBox;
        private readonly TextBox _proxyBox;
        private readonly TextBox _uaBox;
        private readonly CheckBox _rpcChk;
        private readonly TextBox _rpcPortBox;
        private readonly TextBox _rpcSecretBox;
        private readonly CheckBox _sniffChk;
        private readonly CheckBox _bypassChk;

        private readonly SettingsGridItem[] _items = new SettingsGridItem[6];
        private readonly Panel[] _subPages = new Panel[6];
        private Panel _gridPage;
        private Panel _line1;
        private Panel _line2;

        public SettingsView(Config cfg, Action<string> notify, Action<bool> applySniff, Action applyRpc)
        {
            _cfg = cfg;
            _notify = notify;
            _applySniff = applySniff;
            _applyRpc = applyRpc;
            BackColor = Theme.WindowBg;

            // ---- 输入控件在构造函数内联创建（readonly），由各子页布局引用 ----
            _dirBox = new ThemedTextBox();
            _dirBox.Text = cfg.SaveDir;
            Theme.StyleTextBox(_dirBox);

            _concBox = new ThemedTextBox();
            _concBox.Text = cfg.MaxConcurrent.ToString(CultureInfo.InvariantCulture);
            Theme.StyleTextBox(_concBox);

            _speedBox = new ThemedTextBox();
            _speedBox.Text = cfg.SpeedLimit;
            Theme.StyleTextBox(_speedBox);
            Win32.SetPlaceholder(_speedBox, "留空不限速，如 512K / 2M");

            _splitBox = new ThemedTextBox();
            _splitBox.Text = cfg.Split.ToString(CultureInfo.InvariantCulture);
            Theme.StyleTextBox(_splitBox);

            _proxyBox = new ThemedTextBox();
            _proxyBox.Text = cfg.Proxy;
            Theme.StyleTextBox(_proxyBox);
            Win32.SetPlaceholder(_proxyBox, "留空直连，如 http://127.0.0.1:7890");

            _uaBox = new ThemedTextBox();
            _uaBox.Text = cfg.UserAgent;
            Theme.StyleTextBox(_uaBox);
            Win32.SetPlaceholder(_uaBox, "留空为 aria2 默认 UA");

            _rpcChk = new CheckBox();
            _rpcChk.Text = "启用 RPC 服务（AriaNg 等外部工具可连接本机）";
            _rpcChk.AutoSize = true;
            _rpcChk.Font = Theme.BaseFont;
            _rpcChk.ForeColor = Theme.TextPrimary;
            _rpcChk.BackColor = Theme.CardBg;
            _rpcChk.Checked = cfg.RpcEnabled;

            _rpcPortBox = new ThemedTextBox();
            _rpcPortBox.Text = cfg.RpcPort.ToString(CultureInfo.InvariantCulture);
            Theme.StyleTextBox(_rpcPortBox);

            _rpcSecretBox = new ThemedTextBox();
            _rpcSecretBox.Text = cfg.RpcSecret;
            Theme.StyleTextBox(_rpcSecretBox);
            Win32.SetPlaceholder(_rpcSecretBox, "可空，如 mysecret");

            _sniffChk = new CheckBox();
            _sniffChk.Text = "启用浏览器扩展嗅探服务（127.0.0.1:6866）";
            _sniffChk.AutoSize = true;
            _sniffChk.Font = Theme.BaseFont;
            _sniffChk.ForeColor = Theme.TextPrimary;
            _sniffChk.BackColor = Theme.CardBg;
            _sniffChk.Checked = cfg.SniffEnabled;

            _bypassChk = new CheckBox();
            _bypassChk.Text = "绕过防盗链（自动携带浏览器 UA 与来源页 Referer）";
            _bypassChk.AutoSize = true;
            _bypassChk.Font = Theme.BaseFont;
            _bypassChk.ForeColor = Theme.TextPrimary;
            _bypassChk.BackColor = Theme.CardBg;
            _bypassChk.Checked = cfg.BypassHotlink;

            BuildSubPages();
            BuildGrid();
            ShowGrid();
        }

        /// 创建六个子页（外层 Dock=Fill 面板 + 内容卡片），控件按归属摆放。
        private void BuildSubPages()
        {
            Panel card;

            // ---- 通用：默认保存目录 ----
            card = NewPage("通用设置", out _subPages[0]);
            card.Controls.Add(NewLabel("默认保存目录", 24, 64, 96));
            _dirBox.SetBounds(130, 61, 300, 25);
            card.Controls.Add(_dirBox);
            FlatButton browse = new FlatButton();
            browse.Text = "浏览…";
            browse.SetBounds(444, 60, 84, 28);
            Theme.StyleSecondaryButton(browse);
            browse.Click += delegate { Theme.BrowseFolderInto(this, _dirBox); };
            card.Controls.Add(browse);
            card.Controls.Add(NewHint("新任务与 AriaNg 添加任务的默认保存目录。", 24, 100));
            AddSave(card);

            // ---- 下载：并发 / 分片 / 限速 ----
            card = NewPage("下载设置", out _subPages[1]);
            card.Controls.Add(NewLabel("并发下载数(1-10)", 24, 64, 96));
            _concBox.SetBounds(130, 61, 80, 25);
            card.Controls.Add(_concBox);
            card.Controls.Add(NewLabel("每任务分片数", 24, 104, 96));
            _splitBox.SetBounds(130, 101, 80, 25);
            card.Controls.Add(_splitBox);
            card.Controls.Add(NewLabel("全局限速", 24, 144, 96));
            _speedBox.SetBounds(130, 141, 180, 25);
            card.Controls.Add(_speedBox);
            AddSave(card);

            // ---- 网络：代理 / User-Agent ----
            card = NewPage("网络设置", out _subPages[2]);
            card.Controls.Add(NewLabel("网络代理", 24, 64, 96));
            _proxyBox.SetBounds(130, 61, 380, 25);
            card.Controls.Add(_proxyBox);
            card.Controls.Add(NewHint("支持 http:// 与 socks5:// 地址；socks5 仅对下载生效（m3u8 清单抓取需 http 代理）。", 24, 94));
            card.Controls.Add(NewLabel("User-Agent", 24, 124, 96));
            _uaBox.SetBounds(130, 121, 300, 25);
            card.Controls.Add(_uaBox);
            FlatButton uaFill = new FlatButton();
            uaFill.Text = "浏览器 UA";
            uaFill.SetBounds(444, 120, 84, 28);
            Theme.StyleSecondaryButton(uaFill);
            uaFill.Click += delegate { _uaBox.Text = Config.BrowserUa; };
            card.Controls.Add(uaFill);
            _bypassChk.SetBounds(24, 152, 480, 22);
            card.Controls.Add(_bypassChk);
            AddSave(card);

            // ---- RPC：开关 / 端口 / 密钥 ----
            card = NewPage("RPC 设置", out _subPages[3]);
            _rpcChk.SetBounds(24, 64, 400, 22); // AutoSize=true，宽度由文本决定
            card.Controls.Add(_rpcChk);
            card.Controls.Add(NewLabel("监听端口", 24, 100, 96));
            _rpcPortBox.SetBounds(130, 97, 80, 25);
            card.Controls.Add(_rpcPortBox);
            card.Controls.Add(NewLabel("RPC 密钥", 222, 100, 64));
            _rpcSecretBox.SetBounds(292, 97, 244, 25);
            card.Controls.Add(_rpcSecretBox);
            card.Controls.Add(NewHint("加密端口 HTTPS/WSS 常驻（仅本机）；AriaNg 选 wss；首次用前需在浏览器接受证书。", 24, 130));
            AddSave(card);

            // ---- 扩展：嗅探开关 ----
            card = NewPage("扩展设置", out _subPages[4]);
            _sniffChk.SetBounds(24, 64, 400, 22);
            card.Controls.Add(_sniffChk);
            card.Controls.Add(NewHint("接收浏览器扩展推送的链接并弹出添加下载窗口；保存后立即生效。", 24, 94));
            AddSave(card);

            // ---- 关于：版本与许可 ----
            card = NewPage("关于", out _subPages[5]);
            Label appName = new Label();
            appName.Text = "aria-gui";
            appName.SetBounds(24, 64, 200, 22);
            appName.BackColor = Color.Transparent;
            appName.ForeColor = Theme.TextPrimary;
            appName.Font = Theme.TitleFont;
            card.Controls.Add(appName);
            card.Controls.Add(NewHint("aria2 下载器的轻量 Windows 图形界面（单文件绿色版）。", 24, 92));
            card.Controls.Add(NewLabel("项目仓库：github.com/flwh/aria2ccc", 24, 122, 512));
            card.Controls.Add(NewHint("许可：本应用 MIT；内置的 aria2c 为 GPLv2（见 THIRD-PARTY-NOTICES.md）。", 24, 146));
        }

        /// 新建子页外层（Dock=Fill，初始隐藏）；返回内容卡片供摆放控件（卡片含「← 返回」与页标题）。
        private Panel NewPage(string title, out Panel page)
        {
            page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Theme.WindowBg;
            page.Visible = false;

            Panel card = new Panel();
            card.BackColor = Theme.WindowBg; // 圆角卡片外区域与父背景同色，卡片本体由 CardPaint 自绘
            card.SetBounds(14, 14, 560, 240);
            card.Paint += Theme.CardPaint;

            FlatButton back = new FlatButton();
            back.Text = "← 返回";
            back.SetBounds(24, 18, 76, 28);
            Theme.StyleSecondaryButton(back);
            back.Click += delegate { ShowGrid(); };

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.SetBounds(112, 25, 200, 20);
            titleLabel.BackColor = Color.Transparent;
            titleLabel.ForeColor = Theme.TextPrimary;
            titleLabel.Font = Theme.TitleFont;

            card.Controls.Add(back);
            card.Controls.Add(titleLabel);
            page.Controls.Add(card);
            Controls.Add(page);
            return card;
        }

        /// 网格首页：3 列 × 2 行分类入口 + 行间分隔线。
        private void BuildGrid()
        {
            _gridPage = new Panel();
            _gridPage.Dock = DockStyle.Fill;
            _gridPage.BackColor = Theme.WindowBg;

            string[] glyphs = new string[] { "\uE713", "\uE896", "\uE774", "\uE943", "\uE8A7", "\uE946" };
            string[] titles = new string[] { "通用", "下载", "网络", "RPC", "扩展", "关于" };
            string[] descs = new string[] {
                "默认保存目录", "并发下载、分片与限速", "代理、UA 与防盗链绕过",
                "AriaNg 等外部工具连接", "浏览器扩展嗅探推送", "版本、源码与许可" };

            for (int i = 0; i < _items.Length; i++)
            {
                int idx = i; // C# 5 闭包：捕获副本
                SettingsGridItem it = new SettingsGridItem(glyphs[i], titles[i], descs[i]);
                it.Click += delegate { ShowSub(idx); };
                _items[i] = it;
                _gridPage.Controls.Add(it);
            }

            _line1 = new Panel();
            _line1.BackColor = Theme.Border;
            _line2 = new Panel();
            _line2.BackColor = Theme.Border;
            _gridPage.Controls.Add(_line1);
            _gridPage.Controls.Add(_line2);

            Controls.Add(_gridPage);
            LayoutGrid();
        }

        /// 网格布局随宽度自适应：3 列，格子间距 16，两条分隔线随行高定位。
        private void LayoutGrid()
        {
            if (_gridPage == null || _items[0] == null) return;
            int w = Width;
            if (w < 580) w = 580; // 构造期宽度未定时按最小窗口内容宽预估，真正布局由 OnResize 重排
            const int pad = 24, gap = 16, cellH = 132, rowGap = 48;
            int cellW = (w - pad * 2 - gap * 2) / 3;
            for (int i = 0; i < _items.Length; i++)
            {
                int col = i % 3;
                int row = i / 3;
                _items[i].SetBounds(pad + col * (cellW + gap), 20 + row * (cellH + rowGap), cellW, cellH);
            }
            int lineW = cellW * 3 + gap * 2;
            _line1.SetBounds(pad, 20 + cellH + 18, lineW, 1);
            _line2.SetBounds(pad, 20 + cellH + rowGap + cellH + 18, lineW, 1);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutGrid();
        }

        /// 显示分类网格首页。
        private void ShowGrid()
        {
            _gridPage.Visible = true;
            for (int i = 0; i < _subPages.Length; i++) _subPages[i].Visible = false;
        }

        /// 显示第 index 个子页。
        private void ShowSub(int index)
        {
            _gridPage.Visible = false;
            for (int i = 0; i < _subPages.Length; i++) _subPages[i].Visible = (i == index);
        }

        /// 表单标签（透明底色，避免卡片上出现灰底块）；width 取精确宽度，避免遮挡相邻输入框。
        private static Label NewLabel(string text, int x, int y, int width)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, width, 18);
            l.BackColor = Color.Transparent;
            Theme.StyleFormLabel(l);
            return l;
        }

        /// 说明文字（小号灰字，通栏宽度；y 需避开输入框行）。
        private static Label NewHint(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, 512, 16);
            l.BackColor = Color.Transparent;
            l.Font = Theme.SmallFont;
            l.ForeColor = Theme.TextMuted;
            return l;
        }

        /// 子页统一的「保存设置」主按钮。
        private void AddSave(Panel card)
        {
            FlatButton save = new FlatButton();
            save.Text = "保存设置";
            save.SetBounds(24, 182, 104, 32);
            Theme.StylePrimaryButton(save);
            save.Click += delegate { SaveSettings(); };
            card.Controls.Add(save);
        }

        /// 校验并保存：数字解析允许前导符号、不允许空白。
        private void SaveSettings()
        {
            int conc;
            if (!int.TryParse(_concBox.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out conc)
                || conc < 1 || conc > 10)
            {
                MessageBox.Show(this, "并发下载数需为 1-10 的整数", "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int split;
            if (!int.TryParse(_splitBox.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out split)
                || split < 1 || split > 16)
            {
                MessageBox.Show(this, "分片数需为 1-16 的整数", "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Config.ValidSpeedLimit(_speedBox.Text))
            {
                MessageBox.Show(this, "限速格式如 512K / 2M / 1G，或留空", "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Config.ValidProxy(_proxyBox.Text.Trim()))
            {
                MessageBox.Show(this, "代理格式如 http://127.0.0.1:7890 或 socks5://127.0.0.1:1080，或留空", "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int rpcPort;
            if (!int.TryParse(_rpcPortBox.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out rpcPort)
                || rpcPort < 1024 || rpcPort > 65535)
            {
                MessageBox.Show(this, "RPC 监听端口需为 1024-65535 的整数", "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _cfg.SaveDir = _dirBox.Text;
            _cfg.MaxConcurrent = conc;
            _cfg.SpeedLimit = _speedBox.Text;
            _cfg.Split = split;
            _cfg.Proxy = _proxyBox.Text.Trim();
            _cfg.UserAgent = _uaBox.Text.Trim();
            _cfg.BypassHotlink = _bypassChk.Checked;
            _cfg.RpcEnabled = _rpcChk.Checked;
            _cfg.RpcPort = rpcPort;
            _cfg.RpcSecret = _rpcSecretBox.Text.Trim();
            _cfg.SniffEnabled = _sniffChk.Checked;
            string err = _cfg.Save(); // 成功返回 null
            if (err != null)
            {
                MessageBox.Show(this, err, "设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _notify("设置已保存（限速、分片、代理、UA、防盗链对新启动的任务生效；嗅探与 RPC 已即时生效）");
            // 嗅探与 RPC 均为应用级：保存后立即启停
            if (_applySniff != null) _applySniff(_sniffChk.Checked);
            if (_applyRpc != null) _applyRpc();
        }
    }
}
