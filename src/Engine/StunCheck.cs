using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace AriaGui.Engine
{
    /// NAT 类型（单 STUN 服务器可达范围内的判定结论）。
    public enum NatType
    {
        /// 检测失败（离线 / DNS / 超时）。
        Unknown,
        /// 公网直连：映射地址与本机出接口完全一致，无 NAT。
        Open,
        /// 锥形（非对称）：映射与目标无关。
        Cone,
        /// 对称型：同一本地端口对不同目标得到不同映射。
        Symmetric
    }

    /// NAT 类型检测：向 STUN 服务器（默认 stun.miwifi.com:3478）发 Binding Request。
    /// 判定：映射 == 本机出接口 → 公网直连；同一 socket 向服务器两个不同 IP 请求，
    /// 映射一致 → 锥形，不一致 → 对称型（服务器只有一个 IP 时无法测对称，按锥形报）。
    public static class StunCheck
    {
        /// 默认 STUN 服务器与端口（实测 stun.miwifi.com 仅 3478 可用）。
        public const string DefaultServer = "stun.miwifi.com";
        public const int DefaultPort = 3478;

        /// 检测 NAT 类型；任何一步失败返回 Unknown。同步阻塞（默认 2 秒/步），调用方应放后台线程。
        public static NatType DetectType()
        {
            return DetectType(DefaultServer, DefaultPort, 2000);
        }

        public static NatType DetectType(string server, int port, int timeoutMs)
        {
            IPAddress[] v4 = ResolveV4(server);
            if (v4.Length == 0) return NatType.Unknown;

            // 本机出接口 IP：临时 socket connect（仅路由选择，不产生流量）
            IPAddress localIp = null;
            try
            {
                using (Socket probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    probe.Connect(new IPEndPoint(v4[0], port));
                    IPEndPoint lep = probe.LocalEndPoint as IPEndPoint;
                    if (lep != null) localIp = lep.Address;
                }
            }
            catch { }
            if (localIp == null) return NatType.Unknown;

            UdpClient udp = null;
            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork);
                udp.Client.ReceiveTimeout = timeoutMs;

                // 第一步：向第一个地址请求映射（SendTo 后本地端口已绑定）
                IPEndPoint m1 = Query(udp, v4[0], port, timeoutMs);
                if (m1 == null) return NatType.Unknown;

                IPEndPoint local = udp.Client.LocalEndPoint as IPEndPoint;
                if (local == null) return NatType.Unknown;

                // 公网直连：映射与本机出接口完全一致
                if (m1.Address.Equals(localIp) && m1.Port == local.Port) return NatType.Open;

                // 第二步：同一 socket 向第二个地址请求（不同目标）；映射变化 → 对称型
                if (v4.Length < 2) return NatType.Cone;
                IPEndPoint m2 = Query(udp, v4[1], port, timeoutMs);
                if (m2 == null) return NatType.Cone; // 第二目标超时不推翻「已确认有 NAT」的结论
                return m1.Equals(m2) ? NatType.Cone : NatType.Symmetric;
            }
            catch { return NatType.Unknown; }
            finally { if (udp != null) { try { udp.Close(); } catch { } } }
        }

        /// DNS 解析出全部 IPv4 地址（去重；失败返回空数组）。
        private static IPAddress[] ResolveV4(string server)
        {
            try
            {
                IPAddress[] all = Dns.GetHostAddresses(server);
                List<IPAddress> v4 = new List<IPAddress>();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].AddressFamily == AddressFamily.InterNetwork && !v4.Contains(all[i]))
                        v4.Add(all[i]);
                }
                return v4.ToArray();
            }
            catch { return new IPAddress[0]; }
        }

        /// 向目标发一条 Binding Request 并等待响应；返回解析出的映射地址或 null。
        private static IPEndPoint Query(UdpClient udp, IPAddress host, int port, int timeoutMs)
        {
            try
            {
                byte[] req = BuildRequest();
                udp.Send(req, req.Length, new IPEndPoint(host, port));
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                return ParseMapped(udp.Receive(ref from));
            }
            catch { return null; }
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
        private static IPEndPoint ParseMapped(byte[] resp)
        {
            if (resp == null || resp.Length < 20) return null;
            IPEndPoint fallback = null;
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
                    IPEndPoint ep = new IPEndPoint(new IPAddress(ip), port);
                    if (type == 0x0020) return ep; // XOR 属性优先
                    fallback = ep;
                }
                off = vo + len;
                if (off % 4 != 0) off += 4 - (off % 4);
            }
            return fallback;
        }
    }
}
