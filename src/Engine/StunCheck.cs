using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace AriaGui.Engine
{
    /// NAT 映射地址检测：向 STUN 服务器（默认 stun.miwifi.com:3478）发送 Binding Request，
    /// 解析响应中的 XOR-MAPPED-ADDRESS / MAPPED-ADDRESS 得到经 NAT 后的公网 ip:port。
    public static class StunCheck
    {
        /// 默认 STUN 服务器与端口（实测 stun.miwifi.com 仅 3478 可用）。
        public const string DefaultServer = "stun.miwifi.com";
        public const int DefaultPort = 3478;

        /// 检测 NAT 后的公网映射地址；成功返回 "ip:port"，失败（DNS/超时/无属性）返回 null。
        /// 同步阻塞（默认 2 秒超时），调用方应放后台线程执行。
        public static string Detect()
        {
            return Detect(DefaultServer, DefaultPort, 2000);
        }

        public static string Detect(string server, int port, int timeoutMs)
        {
            IPAddress host = null;
            try
            {
                IPAddress[] addrs = Dns.GetHostAddresses(server);
                for (int i = 0; i < addrs.Length; i++)
                {
                    if (addrs[i].AddressFamily == AddressFamily.InterNetwork) { host = addrs[i]; break; }
                }
            }
            catch { }
            if (host == null) return null;

            UdpClient udp = null;
            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork);
                udp.Client.ReceiveTimeout = timeoutMs;
                byte[] req = BuildRequest();
                udp.Send(req, req.Length, new IPEndPoint(host, port));
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                return ParseMapped(udp.Receive(ref from));
            }
            catch { return null; }
            finally { if (udp != null) { try { udp.Close(); } catch { } } }
        }

        /// 构造 20 字节 Binding Request：类型 0x0001 + Magic Cookie + GUID 前 12 字节作事务号。
        private static byte[] BuildRequest()
        {
            byte[] b = new byte[20];
            b[0] = 0x00; b[1] = 0x01;
            b[4] = 0x21; b[5] = 0x12; b[6] = 0xA4; b[7] = 0x42;
            Array.Copy(Guid.NewGuid().ToByteArray(), 0, b, 8, 12);
            return b;
        }

        /// 遍历 STUN 属性：XOR-MAPPED-ADDRESS(0x0020) 优先，MAPPED-ADDRESS(0x0001) 兜底；仅取 IPv4。
        private static string ParseMapped(byte[] resp)
        {
            if (resp == null || resp.Length < 20) return null;
            string fallback = null;
            int off = 20;
            while (off + 4 <= resp.Length)
            {
                int type = (resp[off] << 8) | resp[off + 1];
                int len = (resp[off + 2] << 8) | resp[off + 3];
                int vo = off + 4;
                if (vo + len > resp.Length) break;
                if ((type == 0x0020 || type == 0x0001) && len >= 8 && resp[vo + 1] == 0x01)
                {
                    int port = (resp[vo + 2] << 8) | resp[vo + 3];
                    byte[] ip = new byte[4];
                    for (int i = 0; i < 4; i++)
                    {
                        byte v = resp[vo + 4 + i];
                        if (type == 0x0020) v = (byte)(v ^ resp[4 + i]);
                        ip[i] = v;
                    }
                    if (type == 0x0020) port = port ^ ((resp[4] << 8) | resp[5]);
                    string s = new IPAddress(ip).ToString() + ":" + port.ToString(CultureInfo.InvariantCulture);
                    if (type == 0x0020) return s;
                    fallback = s;
                }
                off = vo + len;
                if (off % 4 != 0) off += 4 - (off % 4);
            }
            return fallback;
        }
    }
}
