using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AriaGui.Tests
{
    /// 测试用静态 HTTP 源：内存文件表 + TcpListener 简单应答（仅回环地址，200 全量响应）。
    internal sealed class MiniHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>();
        private readonly Dictionary<string, string> _types = new Dictionary<string, string>();
        private volatile bool _stop;

        public MiniHttpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Thread t = new Thread(AcceptLoop);
            t.IsBackground = true;
            t.Start();
        }

        public int Port
        {
            get { return ((IPEndPoint)_listener.LocalEndpoint).Port; }
        }

        public string BaseUrl
        {
            get { return "http://127.0.0.1:" + Port.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        }

        /// 注册文本内容（如 m3u8 清单）。
        public void AddText(string path, string text, string contentType)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            lock (_files)
            {
                _files[path] = data;
                _types[path] = contentType;
            }
        }

        /// 注册二进制内容（如 ts 分片）。
        public void AddBytes(string path, byte[] data)
        {
            lock (_files)
            {
                _files[path] = data;
                _types[path] = "application/octet-stream";
            }
        }

        public void Dispose()
        {
            _stop = true;
            try { _listener.Stop(); }
            catch { }
        }

        private void AcceptLoop()
        {
            while (!_stop)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); }
                catch { return; }
                ThreadPool.QueueUserWorkItem(delegate(object st) { Serve((TcpClient)st); }, c);
            }
        }

        private void Serve(TcpClient c)
        {
            try
            {
                using (c)
                using (NetworkStream ns = c.GetStream())
                {
                    // 读取请求头（至空行为止），忽略请求体
                    byte[] buf = new byte[8192];
                    int total = 0;
                    while (total < buf.Length)
                    {
                        int n = ns.Read(buf, total, buf.Length - total);
                        if (n <= 0) break;
                        total += n;
                        string soFar = Encoding.ASCII.GetString(buf, 0, total);
                        if (soFar.IndexOf("\r\n\r\n", StringComparison.Ordinal) >= 0 ||
                            soFar.IndexOf("\n\n", StringComparison.Ordinal) >= 0) break;
                    }
                    string req = Encoding.ASCII.GetString(buf, 0, total);
                    string path = "/";
                    string[] lines = req.Split('\n');
                    if (lines.Length > 0)
                    {
                        string[] parts = lines[0].Trim().Split(' ');
                        if (parts.Length >= 2) path = parts[1];
                    }
                    int q = path.IndexOf('?');
                    if (q >= 0) path = path.Substring(0, q);

                    byte[] data;
                    string type = "application/octet-stream";
                    bool found;
                    lock (_files)
                    {
                        found = _files.TryGetValue(path, out data);
                        if (found) _types.TryGetValue(path, out type);
                    }
                    if (!found)
                    {
                        byte[] nf = Encoding.UTF8.GetBytes("not found");
                        WriteHead(ns, "404 Not Found", "text/plain; charset=utf-8", nf.Length);
                        ns.Write(nf, 0, nf.Length);
                    }
                    else
                    {
                        WriteHead(ns, "200 OK", type, data.Length);
                        ns.Write(data, 0, data.Length);
                    }
                    ns.Flush();
                }
            }
            catch
            {
                // 客户端中断等：忽略（测试内尽力服务）
            }
        }

        private static void WriteHead(NetworkStream ns, string status, string type, int len)
        {
            string head = "HTTP/1.1 " + status + "\r\n" +
                "Content-Type: " + type + "\r\n" +
                "Content-Length: " + len.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n" +
                "Connection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(head);
            ns.Write(hb, 0, hb.Length);
        }
    }
}
