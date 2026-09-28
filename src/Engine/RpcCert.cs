using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AriaGui.Engine
{
    /// RPC TLS 自签名证书：首次使用时生成并缓存于 config 目录（每安装独立密钥）。
    /// aria2 官方 Windows 构建使用 wintls(Schannel)，仅接受无密码 PKCS#12（.p12），不支持 PEM；
    /// 供 aria2c --rpc-secure 使用（HTTPS/WSS 加密 RPC，AriaNg 连接无混合内容顾虑）。
    internal static class RpcCert
    {
        public const string CertFileName = "rpc-cert.p12";

        /// 确保证书存在且未临近过期，返回文件绝对路径；失败抛异常（调用方降级非加密模式）。
        public static string Ensure(string dir)
        {
            string path = Path.Combine(dir, CertFileName);
            if (File.Exists(path) && !ExpiringSoon(path)) return path;
            Generate(path);
            return path;
        }

        /// 证书缺失/损坏/临近过期（7 天内）时返回 true，触发重新生成。
        private static bool ExpiringSoon(string path)
        {
            try
            {
                X509Certificate2 c = new X509Certificate2(path);
                return c.NotAfter < DateTime.Now.AddDays(7);
            }
            catch { return true; }
        }

        /// 生成自签名证书（SAN: 127.0.0.1 + localhost，10 年有效），导出为无密码 PKCS#12。
        public static void Generate(string path)
        {
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048))
            {
                CertificateRequest req = new CertificateRequest("CN=aria-gui RPC", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
                san.AddIpAddress(System.Net.IPAddress.Loopback);
                san.AddDnsName("localhost");
                req.CertificateExtensions.Add(san.Build());
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                req.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
                OidCollection usages = new OidCollection();
                usages.Add(new Oid("1.3.6.1.5.5.7.3.1")); // serverAuth
                req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));

                X509Certificate2 cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(10));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, ""));
                cert.Dispose();
            }
        }
    }
}
