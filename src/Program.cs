using System;
using System.IO;
using System.Reflection;
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
            // SunnyUI 两个 dll 以资源内嵌在 exe 内（单文件分发）。必须在任何 SunnyUI 类型
            // 被 JIT 之前注册解析器，因此 Main 不直接引用 UI 类型，启动逻辑拆到 Run()。
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;
            Run();
        }

        private static void Run()
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

        /// 从内嵌资源解析程序集（SunnyUI / SunnyUI.Common）。
        private static Assembly ResolveEmbedded(object sender, ResolveEventArgs args)
        {
            string resName = new AssemblyName(args.Name).Name + ".dll";
            using (Stream s = typeof(Program).Assembly.GetManifestResourceStream(resName))
            {
                if (s == null) { return null; }
                byte[] buf = new byte[s.Length];
                s.Read(buf, 0, buf.Length);
                return Assembly.Load(buf);
            }
        }
    }
}
