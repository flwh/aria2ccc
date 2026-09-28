using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace AriaGui.UI
{
    /// 设置页：默认目录、并发数、限速、分片数、网络代理、RPC、嗅探开关（卡片式现代浅色布局）。
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
        private readonly CheckBox _rpcChk;
        private readonly TextBox _rpcPortBox;
        private readonly TextBox _rpcSecretBox;
        private readonly CheckBox _sniffChk;

        public SettingsView(Config cfg, Action<string> notify, Action<bool> applySniff, Action applyRpc)
        {
            _cfg = cfg;
            _notify = notify;
            _applySniff = applySniff;
            _applyRpc = applyRpc;
            BackColor = Theme.WindowBg;

            Panel card = new Panel();
            card.BackColor = Theme.WindowBg; // 圆角卡片外区域与父背景同色，卡片本体由 CardPaint 自绘
            card.SetBounds(14, 14, 560, 476);
            card.Paint += Theme.CardPaint;

            Label dirLabel = new Label();
            dirLabel.Text = "默认保存目录";
            dirLabel.SetBounds(24, 31, 96, 18);
            Theme.StyleFormLabel(dirLabel);

            _dirBox = new ThemedTextBox();
            _dirBox.SetBounds(130, 28, 300, 25);
            _dirBox.Text = cfg.SaveDir;
            Theme.StyleTextBox(_dirBox);

            FlatButton browse = new FlatButton();
            browse.Text = "浏览…";
            browse.SetBounds(444, 27, 84, 28);
            Theme.StyleSecondaryButton(browse);
            browse.Click += delegate { Theme.BrowseFolderInto(this, _dirBox); };

            Label concLabel = new Label();
            concLabel.Text = "并发下载数(1-10)";
            concLabel.SetBounds(24, 71, 96, 18);
            Theme.StyleFormLabel(concLabel);

            _concBox = new ThemedTextBox();
            _concBox.SetBounds(130, 68, 80, 25);
            _concBox.Text = cfg.MaxConcurrent.ToString(CultureInfo.InvariantCulture);
            Theme.StyleTextBox(_concBox);

            Label speedLabel = new Label();
            speedLabel.Text = "全局限速";
            speedLabel.SetBounds(24, 111, 96, 18);
            Theme.StyleFormLabel(speedLabel);

            _speedBox = new ThemedTextBox();
            _speedBox.SetBounds(130, 108, 180, 25);
            _speedBox.Text = cfg.SpeedLimit;
            Theme.StyleTextBox(_speedBox);
            Win32.SetPlaceholder(_speedBox, "留空不限速，如 512K / 2M");

            Label splitLabel = new Label();
            splitLabel.Text = "每任务分片数";
            splitLabel.SetBounds(24, 151, 96, 18);
            Theme.StyleFormLabel(splitLabel);

            _splitBox = new ThemedTextBox();
            _splitBox.SetBounds(130, 148, 80, 25);
            _splitBox.Text = cfg.Split.ToString(CultureInfo.InvariantCulture);
            Theme.StyleTextBox(_splitBox);

            Label proxyLabel = new Label();
            proxyLabel.Text = "网络代理";
            proxyLabel.SetBounds(24, 191, 96, 18);
            Theme.StyleFormLabel(proxyLabel);

            _proxyBox = new ThemedTextBox();
            _proxyBox.SetBounds(130, 188, 380, 25);
            _proxyBox.Text = cfg.Proxy;
            Theme.StyleTextBox(_proxyBox);
            Win32.SetPlaceholder(_proxyBox, "留空直连，如 http://127.0.0.1:7890");

            Label proxyHint = new Label();
            proxyHint.Text = "支持 http:// 与 socks5:// 地址；socks5 仅对下载生效（m3u8 清单抓取需 http 代理）。";
            proxyHint.SetBounds(24, 222, 512, 16);
            proxyHint.Font = Theme.SmallFont;
            proxyHint.ForeColor = Theme.TextMuted;

            Panel divider = new Panel();
            divider.BackColor = Theme.Border;
            divider.SetBounds(24, 252, 512, 1);

            _rpcChk = new CheckBox();
            _rpcChk.Text = "启用 RPC 服务（AriaNg 等外部工具可连接本机）";
            _rpcChk.SetBounds(24, 266, 400, 22);
            _rpcChk.AutoSize = true;
            _rpcChk.Font = Theme.BaseFont;
            _rpcChk.ForeColor = Theme.TextPrimary;
            _rpcChk.BackColor = Theme.CardBg;
            _rpcChk.Checked = cfg.RpcEnabled;

            Label rpcPortLabel = new Label();
            rpcPortLabel.Text = "监听端口";
            rpcPortLabel.SetBounds(24, 302, 96, 18);
            Theme.StyleFormLabel(rpcPortLabel);

            _rpcPortBox = new ThemedTextBox();
            _rpcPortBox.SetBounds(130, 299, 80, 25);
            _rpcPortBox.Text = cfg.RpcPort.ToString(CultureInfo.InvariantCulture);
            Theme.StyleTextBox(_rpcPortBox);

            Label rpcSecretLabel = new Label();
            rpcSecretLabel.Text = "RPC 密钥";
            rpcSecretLabel.SetBounds(222, 302, 64, 18);
            Theme.StyleFormLabel(rpcSecretLabel);

            _rpcSecretBox = new ThemedTextBox();
            _rpcSecretBox.SetBounds(292, 299, 244, 25);
            _rpcSecretBox.Text = cfg.RpcSecret;
            Theme.StyleTextBox(_rpcSecretBox);
            Win32.SetPlaceholder(_rpcSecretBox, "可空，如 mysecret");

            Label rpcHint = new Label();
            rpcHint.Text = "应用启动后常驻提供端口（仅监听本机）；保存后立即生效。";
            rpcHint.SetBounds(24, 334, 512, 16);
            rpcHint.Font = Theme.SmallFont;
            rpcHint.ForeColor = Theme.TextMuted;

            Panel divider2 = new Panel();
            divider2.BackColor = Theme.Border;
            divider2.SetBounds(24, 358, 512, 1);

            _sniffChk = new CheckBox();
            _sniffChk.Text = "启用浏览器扩展嗅探服务（127.0.0.1:6866）";
            _sniffChk.SetBounds(24, 372, 400, 22);
            _sniffChk.AutoSize = true;
            _sniffChk.Font = Theme.BaseFont;
            _sniffChk.ForeColor = Theme.TextPrimary;
            _sniffChk.BackColor = Theme.CardBg;
            _sniffChk.Checked = cfg.SniffEnabled;

            Label sniffHint = new Label();
            sniffHint.Text = "接收浏览器扩展推送的链接并弹出添加下载窗口；保存后立即生效。";
            sniffHint.SetBounds(24, 402, 512, 16);
            sniffHint.Font = Theme.SmallFont;
            sniffHint.ForeColor = Theme.TextMuted;

            FlatButton save = new FlatButton();
            save.Text = "保存设置";
            save.SetBounds(24, 428, 104, 32);
            Theme.StylePrimaryButton(save);
            save.Click += delegate { SaveSettings(); };

            card.Controls.Add(dirLabel);
            card.Controls.Add(_dirBox);
            card.Controls.Add(browse);
            card.Controls.Add(concLabel);
            card.Controls.Add(_concBox);
            card.Controls.Add(speedLabel);
            card.Controls.Add(_speedBox);
            card.Controls.Add(splitLabel);
            card.Controls.Add(_splitBox);
            card.Controls.Add(proxyLabel);
            card.Controls.Add(_proxyBox);
            card.Controls.Add(proxyHint);
            card.Controls.Add(divider);
            card.Controls.Add(_rpcChk);
            card.Controls.Add(rpcPortLabel);
            card.Controls.Add(_rpcPortBox);
            card.Controls.Add(rpcSecretLabel);
            card.Controls.Add(_rpcSecretBox);
            card.Controls.Add(rpcHint);
            card.Controls.Add(divider2);
            card.Controls.Add(_sniffChk);
            card.Controls.Add(sniffHint);
            card.Controls.Add(save);
            Controls.Add(card);
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
            _notify("设置已保存（限速、分片、代理对新启动的任务生效；嗅探与 RPC 已即时生效）");
            // 嗅探与 RPC 均为应用级：保存后立即启停
            if (_applySniff != null) _applySniff(_sniffChk.Checked);
            if (_applyRpc != null) _applyRpc();
        }
    }
}
