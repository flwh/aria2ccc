using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AriaGui.Engine
{
    /// 浏览器扩展嗅探接收端：仅监听本机回环地址，POST /add 以换行分隔文本接收媒体链接；
    /// 可选首部 `#ref=<URL编码页面地址>` 行携带来源页（Referer，防盗链用）。
    /// 手写极简 HTTP（TcpListener），避免 HttpListener 的 URL ACL 管理员权限要求。
    public sealed class Sniffer
    {
        /// 默认监听端口。
        public const int DefaultPort = 6866;

        private readonly int _port;
        private readonly Action<string, List<string>> _onUrls;
        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public Sniffer(int port, Action<string, List<string>> onUrls)
        {
            _port = port;
            _onUrls = onUrls;
        }

        /// 实际监听端口（构造传 0 时由系统分配，Start 后有效）。
        public int Port
        {
            get { return _listener == null ? _port : ((IPEndPoint)_listener.LocalEndpoint).Port; }
        }

        /// 当前是否在监听（设置页开关据此避免重复启停）。
        public bool Running
        {
            get { return _running; }
        }

        /// 启动监听线程；端口被占用等失败直接抛出，由调用方决定是否忽略。
        public void Start()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, _port);
            l.Start();
            _listener = l;
            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "aria-sniffer";
            _thread.Start();
        }

        /// 停止监听（幂等）。
        public void Stop()
        {
            _running = false;
            if (_listener != null)
            {
                try { _listener.Stop(); }
                catch { }
            }
        }

        private void Loop()
        {
            while (_running)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); }
                catch { if (!_running) return; continue; }
                try { Handle(c); }
                catch { } // 单个请求失败不影响后续
                finally { try { c.Close(); } catch { } }
            }
        }

        private void Handle(TcpClient c)
        {
            c.ReceiveTimeout = 5000;
            NetworkStream ns = c.GetStream();

            string head = ReadHead(ns);
            if (head == null) return;

            string[] lines = head.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return;
            string[] parts = lines[0].Split(' ');
            if (parts.Length < 2)
            {
                WriteResponse(ns, 400, "{\"ok\":false,\"error\":\"bad request\"}");
                return;
            }
            string method = parts[0].ToUpperInvariant();
            string path = parts[1];

            if (method == "OPTIONS")
            {
                WriteResponse(ns, 204, null);
                return;
            }
            if (method == "GET" && path == "/ping")
            {
                WriteResponse(ns, 200, "{\"ok\":true,\"app\":\"aria-gui\"}");
                return;
            }
            if (method == "POST" && path == "/add")
            {
                int len = ContentLength(lines);
                if (len <= 0 || len > 1048576)
                {
                    WriteResponse(ns, 400, "{\"ok\":false,\"error\":\"empty body\"}");
                    return;
                }
                string body = ReadBody(ns, len);
                if (body == null) return;
                string referer;
                List<string> urls = ParseUrls(body, out referer);
                if (urls.Count == 0)
                {
                    WriteResponse(ns, 400, "{\"ok\":false,\"error\":\"no valid url\"}");
                    return;
                }
                if (_onUrls != null) _onUrls(referer, urls);
                WriteResponse(ns, 200, "{\"ok\":true,\"count\":" + urls.Count.ToString(CultureInfo.InvariantCulture) + "}");
                return;
            }
            WriteResponse(ns, 404, "{\"ok\":false,\"error\":\"not found\"}");
        }

        /// 读取 HTTP 头（含结尾空行）；连接中断/超长返回 null。
        private static string ReadHead(NetworkStream ns)
        {
            MemoryStream ms = new MemoryStream();
            int match = 0; // \r\n\r\n 匹配进度
            while (match < 4)
            {
                int b = ns.ReadByte();
                if (b < 0) return null;
                ms.WriteByte((byte)b);
                if (b == '\r' && (match == 0 || match == 2)) match++;
                else if (b == '\n' && (match == 1 || match == 3)) match++;
                else match = b == '\r' ? 1 : 0;
                if (ms.Length > 65536) return null;
            }
            return Encoding.ASCII.GetString(ms.ToArray());
        }

        /// 从头部行中解析 Content-Length（缺失/非法返回 0）。
        private static int ContentLength(string[] lines)
        {
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                string key = lines[i].Substring(0, colon).Trim();
                if (!string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                int n;
                if (int.TryParse(lines[i].Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                    return n;
            }
            return 0;
        }

        /// 按 Content-Length 读取请求体（UTF-8）；连接中断返回 null。
        private static string ReadBody(NetworkStream ns, int len)
        {
            byte[] buf = new byte[len];
            int off = 0;
            while (off < len)
            {
                int n = ns.Read(buf, off, len - off);
                if (n <= 0) return null;
                off += n;
            }
            return Encoding.UTF8.GetString(buf);
        }

        /// 解析请求体：可选首部 `#ref=<URL编码页面地址>` 行为来源页（防盗链用）；
        /// 其余每行一个链接，仅保留受支持前缀并去重（保持顺序）。
        private static List<string> ParseUrls(string body, out string referer)
        {
            referer = "";
            List<string> urls = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            string[] lines = body.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string u = lines[i].Trim();
                if (u.Length == 0) continue;
                if (u.StartsWith("#ref=", StringComparison.Ordinal))
                {
                    referer = DecodeReferer(u.Substring(5));
                    continue;
                }
                if (!IsSupportedUrl(u)) continue;
                if (seen.Add(u)) urls.Add(u);
            }
            return urls;
        }

        /// 解码来源页并校验：仅接受 http/https、长度 1-2048；非法返回空串。
        private static string DecodeReferer(string encoded)
        {
            try
            {
                string s = Uri.UnescapeDataString(encoded).Trim();
                if (s.Length == 0 || s.Length > 2048) return "";
                if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "";
                return s;
            }
            catch
            {
                return "";
            }
        }

        /// 仅接受下载器支持的链接前缀（防御任意文本注入）。
        private static bool IsSupportedUrl(string u)
        {
            return u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || u.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)
                || u.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase);
        }

        /// 写出响应（含 CORS 头；json 为 null 时无响应体，用于 204 预检）。
        private static void WriteResponse(NetworkStream ns, int code, string json)
        {
            string status = code == 200 ? "200 OK" : code == 204 ? "204 No Content" : code == 400 ? "400 Bad Request" : "404 Not Found";
            StringBuilder sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            sb.Append("Access-Control-Allow-Headers: Content-Type\r\n");
            if (json != null)
            {
                byte[] body = Encoding.UTF8.GetBytes(json);
                sb.Append("Content-Type: application/json; charset=utf-8\r\n");
                sb.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                sb.Append("Connection: close\r\n\r\n");
                byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
                ns.Write(head, 0, head.Length);
                ns.Write(body, 0, body.Length);
            }
            else
            {
                sb.Append("Connection: close\r\n\r\n");
                byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
                ns.Write(head, 0, head.Length);
            }
            ns.Flush();
        }
    }
}
