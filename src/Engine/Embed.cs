using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AriaGui.Engine
{
    /// 把内嵌的 aria2c.exe 释放到 %TEMP%\aria-gui\，sha256 相同则复用。
    public static class Embed
    {
        private const string ResourceName = "aria2c.exe";
        private static string _binaryPath;

        /// 返回释放后的可执行文件绝对路径（懒加载，失败抛异常）。
        public static string Extract()
        {
            if (!string.IsNullOrEmpty(_binaryPath)) return _binaryPath;

            byte[] data = ReadResource();
            string dir = Path.Combine(Path.GetTempPath(), "aria-gui");
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                throw new Exception("创建临时目录失败: " + ex.Message);
            }

            string target = Path.Combine(dir, "aria2c.exe");
            string wantHash = Hash(data);
            try
            {
                if (File.Exists(target) && Hash(File.ReadAllBytes(target)) == wantHash)
                {
                    _binaryPath = target;
                    return target;
                }
                File.WriteAllBytes(target, data);
            }
            catch (Exception ex)
            {
                throw new Exception("写出 aria2c 失败: " + ex.Message);
            }
            _binaryPath = target;
            return target;
        }

        /// 已释放的 aria2c 路径（供外部查询，未释放时为 null）。
        public static string BinaryPath
        {
            get { return _binaryPath; }
        }

        private static byte[] ReadResource()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            Stream s = null;
            foreach (string n in asm.GetManifestResourceNames())
            {
                if (n == ResourceName)
                {
                    s = asm.GetManifestResourceStream(n);
                    break;
                }
            }
            if (s == null)
                throw new Exception("读取嵌入的 aria2c 失败: 资源 " + ResourceName + " 不存在");
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[81920];
                    int read;
                    while ((read = s.Read(buf, 0, buf.Length)) > 0)
                        ms.Write(buf, 0, read);
                    return ms.ToArray();
                }
            }
            finally
            {
                s.Dispose();
            }
        }

        private static string Hash(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data);
                StringBuilder sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
