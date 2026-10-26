using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace WebLocalRemote
{
    // 로컬 루트 CA와 그 CA로 서명한 서버 인증서를 .cert/에 만든다.
    // 폰에 루트 CA를 한 번 설치하면(설정 페이지 /__wlr/setup) 경고 없는 HTTPS가 되어 카메라,
    // 서비스 워커, Brotli 빌드가 Android와 iPhone 모두에서 동작한다. iOS는 자체 서명 인증서를
    // 신뢰할 방법이 없고, SAN 없는 인증서는 어느 브라우저도 받지 않으므로 이 구조가 필요하다.
    internal static class LocalCertificates
    {
        internal static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../.cert"));
        internal static string CaCert => Path.Combine(Folder, "rootCA.crt");
        internal static string CaKey => Path.Combine(Folder, "rootCA.key");
        internal static string ServerCert => Path.Combine(Folder, "server.crt");
        internal static string ServerKey => Path.Combine(Folder, "server.key");
        private static string SanFile => Path.Combine(Folder, "server.san");

        internal static bool HasCa => File.Exists(CaCert) && File.Exists(CaKey);

        // 자동 생성한 인증서를 쓰는 중인지(그때만 IP가 바뀌면 다시 만든다)
        internal static bool IsManaged(string certPath, string keyPath) =>
            SamePath(certPath, ServerCert) && SamePath(keyPath, ServerKey) && HasCa;

        // 설정 탭의 '인증서 상태' 내용
        internal static string Describe(string certPath, string keyPath)
        {
            var lines = new List<string>();
            lines.Add(HasCa
                ? "루트 CA: .cert/rootCA.crt (폰에는 서버 탭 QR '기기 설정 · 인증서'로 설치)"
                : "루트 CA: 없음 — '로컬 CA + 인증서 생성'을 누르세요");

            bool hasServer = !string.IsNullOrEmpty(certPath) && File.Exists(certPath) && !string.IsNullOrEmpty(keyPath) && File.Exists(keyPath);
            if (!hasServer)
            {
                lines.Add("서버 인증서: 없음 — 서버는 HTTP로 실행됩니다 (폰에서 카메라·Brotli 제한)");
            }
            else if (IsManaged(certPath, keyPath))
            {
                // 397일짜리라 파일을 쓴 시각으로 만료일을 알 수 있다
                var expires = File.GetLastWriteTime(ServerCert).AddDays(397);
                lines.Add($"서버 인증서: 자동 생성 · {expires:yyyy-MM-dd}까지");
                if (File.Exists(SanFile)) lines.Add("주소: " + File.ReadAllText(SanFile).Replace("DNS:", "").Replace("IP:", ""));
                lines.Add("IP가 바뀌면 서버를 켤 때 다시 발급합니다.");
            }
            else
            {
                lines.Add("서버 인증서: 직접 지정한 파일 (자동 재발급 안 함)");
            }

            return string.Join("\n", lines);
        }

        // 루트 CA가 없으면 만들고, 서버 인증서를 현재 네트워크 주소로 다시 발급한다.
        internal static bool Generate(out string error)
        {
            Directory.CreateDirectory(Folder);
            if (!HasCa && !CreateCa(out error)) return false;
            return CreateServer(out error);
        }

        // 서버 인증서의 SAN이 지금 IP와 다르면(와이파이가 바뀌는 등) 서버 인증서만 다시 만든다.
        internal static bool RefreshIfNeeded(out string error)
        {
            error = null;
            if (File.Exists(ServerCert) && File.Exists(SanFile) && File.ReadAllText(SanFile) == SanLine()) return true;
            return CreateServer(out error);
        }

        private static bool CreateCa(out string error)
        {
            string config = Path.Combine(Folder, "rootCA.cnf");
            File.WriteAllText(config,
                "[req]\ndistinguished_name = dn\nprompt = no\nx509_extensions = v3_ca\n" +
                $"[dn]\nCN = Web Local Remote CA ({Sanitize(Dns.GetHostName().Split('.')[0])})\nO = Web Local Remote\n" +
                "[v3_ca]\nbasicConstraints = critical, CA:TRUE\nkeyUsage = critical, keyCertSign, cRLSign\nsubjectKeyIdentifier = hash\n");
            bool ok = Run($"req -x509 -new -newkey rsa:2048 -nodes -sha256 -days 3650 -keyout \"{CaKey}\" -out \"{CaCert}\" -config \"{config}\"", out error);
            File.Delete(config);
            return ok && HasCa;
        }

        private static bool CreateServer(out string error)
        {
            string csr = Path.Combine(Folder, "server.csr");
            string ext = Path.Combine(Folder, "server.ext");
            string san = SanLine();
            // iOS는 사용자가 설치한 CA로 서명한 인증서도 825일 이하, serverAuth EKU, SAN을 요구한다.
            File.WriteAllText(ext,
                "[v3_leaf]\nbasicConstraints = CA:FALSE\nkeyUsage = critical, digitalSignature, keyEncipherment\n" +
                "extendedKeyUsage = serverAuth\nsubjectKeyIdentifier = hash\nauthorityKeyIdentifier = keyid\n" +
                $"subjectAltName = {san}\n");
            bool ok = Run($"req -new -newkey rsa:2048 -nodes -keyout \"{ServerKey}\" -out \"{csr}\" -subj \"/CN=Web Local Remote\"", out error) &&
                      Run($"x509 -req -in \"{csr}\" -CA \"{CaCert}\" -CAkey \"{CaKey}\" -CAcreateserial -out \"{ServerCert}\" -days 397 -sha256 -extfile \"{ext}\" -extensions v3_leaf", out error);
            File.Delete(csr);
            File.Delete(ext);
            if (ok) File.WriteAllText(SanFile, san);
            return ok && File.Exists(ServerCert);
        }

        private static string SanLine()
        {
            var names = new List<string> { "DNS:localhost" };
            string host = Sanitize(Dns.GetHostName().Split('.')[0]);
            if (host.Length > 0) names.Add($"DNS:{host}.local");
            names.Add("IP:127.0.0.1");
            names.AddRange(LocalAddresses().Select(ip => "IP:" + ip));
            return string.Join(", ", names.Distinct());
        }

        // 폰이 닿을 수 있는 IPv4 주소. 기본 게이트웨이가 있는 인터페이스(실제 와이파이/유선)를 앞에 둔다.
        internal static List<string> LocalAddresses()
        {
            var result = new List<(string ip, bool gateway)>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = nic.GetIPProperties();
                    bool gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(addr.Address)) continue;
                        string ip = addr.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;
                        result.Add((ip, gateway));
                    }
                }
            }
            catch
            {
                // 일부 환경에서 인터페이스 조회가 막히면 아래 DNS 방식으로 대신한다.
            }

            if (result.Count == 0)
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) result.Add((ip.ToString(), false));
            }

            return result.OrderByDescending(r => r.gateway).Select(r => r.ip).Distinct().ToList();
        }

        internal static string OpenSslPath()
        {
            // 1. Git for Windows의 OpenSSL 경로 확인
            string gitOpenSSL = @"C:\Program Files\Git\usr\bin\openssl.exe";
            if (File.Exists(gitOpenSSL)) return gitOpenSSL;

            // 2. macOS: 에디터 프로세스의 PATH에는 Homebrew가 없을 수 있다
            foreach (var path in new[] { "/opt/homebrew/bin/openssl", "/usr/local/bin/openssl", "/usr/bin/openssl" })
                if (File.Exists(path)) return path;

            // 3. 환경 변수 PATH에서 확인
            return "openssl";
        }

        private static bool Run(string args, out string error)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = OpenSslPath(),
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            try
            {
                using var process = Process.Start(startInfo);
                process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string Sanitize(string name) =>
            new string(name.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());

        private static bool SamePath(string a, string b) =>
            !string.IsNullOrEmpty(a) && Path.GetFullPath(a) == Path.GetFullPath(b);
    }
}
