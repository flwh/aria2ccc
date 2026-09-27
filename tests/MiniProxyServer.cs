using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AriaGui.Tests
{
    /// 极简 HTTP 代理测试替身：接受连接、记录请求行、直接回 200 + 固定内容（不做真转发）。
    /// 用途：验证 aria2c 是否真的经由 --all-proxy 发起请求（不自转发 → 上游无需真实存在）。
    public sealed class MiniProxyServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _body;
        private readonly List<string> _requestLines = new List<string>();
        private readonly object _lock = new object();
        private volatile bool _running;

        public MiniProxyServer(byte[] body)
        {
            _body = body;
            _listener = new TcpListener(IPAddress.Loopback, 0);
        }

        public int Port
        {
            get { return ((IPEndPoint)_listener.LocalEndpoint).Port; }
        }

        public int RequestCount
        {
            get { lock (_lock) return _requestLines.Count; }
        }

        public string LastRequestLine
        {
            get { lock (_lock) return _requestLines.Count == 0 ? null : _requestLines[_requestLines.Count - 1]; }
        }

        public void Start()
        {
            _listener.Start();
            _running = true;
            Thread t = new Thread(Loop);
            t.IsBackground = true;
            t.Name = "mini-proxy";
            t.Start();
        }

        private void Loop()
        {
            while (_running)
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
                    ns.ReadTimeout = 5000;
                    byte[] buf = new byte[8192];
                    int total = 0;
                    while (total < buf.Length)
                    {
                        int n = ns.Read(buf, total, buf.Length - total);
                        if (n <= 0) break;
                        total += n;
                        if (Encoding.ASCII.GetString(buf, 0, total).IndexOf("\r\n\r\n", StringComparison.Ordinal) >= 0) break;
                    }
                    string req = Encoding.ASCII.GetString(buf, 0, total);
                    string[] lines = req.Split('\n');
                    string first = lines.Length > 0 ? lines[0].Trim() : "";
                    lock (_lock) _requestLines.Add(first);

                    string head = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: "
                        + _body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "\r\nConnection: close\r\n\r\n";
                    byte[] hb = Encoding.ASCII.GetBytes(head);
                    ns.Write(hb, 0, hb.Length);
                    ns.Write(_body, 0, _body.Length);
                    ns.Flush();
                }
            }
            catch { }
        }

        public void Dispose()
        {
            _running = false;
            try { _listener.Stop(); }
            catch { }
        }
    }
}
