using System;
using System.Windows.Forms;
using AriaGui.Engine;
using AriaGui.UI;

namespace AriaGui
{
    /// 入口：加载配置/历史 → 构造 Manager（含资源释放）→ 运行主窗口。
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Config cfg = Config.Load();
            HistoryStore hist = HistoryStore.Load();

            Manager mgr;
            try
            {
                mgr = new Manager(cfg, hist); // 构造内含 Embed.Extract，失败抛异常
            }
            catch (Exception ex)
            {
                // 报错窗 + 退出码 1（如内存中资源缺失/临时目录不可写）
                MessageBox.Show(ex.Message, "Aria 下载器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
                return;
            }

            MainForm form = new MainForm(mgr, cfg, hist); // 含嗅探接收端（按 config.SniffEnabled 启停）

            Application.Run(form);
        }
    }
}
