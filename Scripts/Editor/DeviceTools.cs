using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WebLocalRemote
{
    // USB로 연결한 폰의 브라우저 개발자 도구를 여는 도우미.
    // Android: adb reverse로 폰의 localhost:포트를 이 PC로 넘기고 Chrome DevTools(chrome://inspect)를 연다.
    // iPhone: Mac Safari의 개발자 메뉴(웹 속성 보기)를 쓴다. Windows에서는 Apple이 지원하지 않는다.
    internal static class DeviceTools
    {
        internal static string AdbPath()
        {
            var roots = new List<string>
            {
                EditorPrefs.GetString("AndroidSdkRoot"),
                Environment.GetEnvironmentVariable("ANDROID_HOME"),
                Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
#if UNITY_EDITOR_OSX
                // Unity.app과 같은 폴더의 PlaybackEngines
                Path.Combine(EditorApplication.applicationContentsPath, "..", "..", "PlaybackEngines/AndroidPlayer/SDK"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library/Android/sdk"),
#else
                Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android/Sdk"),
#endif
            };
            string exe = Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb";
            foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
            {
                string path = Path.GetFullPath(Path.Combine(root, "platform-tools", exe));
                if (File.Exists(path)) return path;
            }

            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string path = Path.Combine(dir, exe);
                if (File.Exists(path)) return path;
            }

            return null;
        }

        // 연결된 모든 Android 기기에서 localhost:port를 이 PC로 넘기고, 원하면 그 주소를 기본 브라우저로 연다.
        // 반환값은 결과 메시지. 실패해도 예외 대신 메시지로 알린다.
        internal static string ConnectAndroid(int port, string scheme, bool open)
        {
            string adb = AdbPath();
            if (adb == null)
                return "adb를 찾을 수 없습니다. Unity Hub에서 Android Build Support(SDK)를 설치하거나 Android SDK의 platform-tools를 PATH에 추가하세요.";

            Run(adb, "start-server", out _);
            Run(adb, "devices", out string list);
            var lines = list.Split('\n').Skip(1).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var ready = lines.Where(l => l.EndsWith("\tdevice")).Select(l => l.Split('\t')[0]).ToList();
            if (ready.Count == 0)
            {
                if (lines.Any(l => l.EndsWith("unauthorized")))
                    return "폰에 뜬 'USB 디버깅을 허용하시겠습니까?'에서 허용을 누른 뒤 다시 시도하세요.";
                return "연결된 Android 기기가 없습니다. USB로 연결하고 개발자 옵션의 USB 디버깅을 켜세요.";
            }

            string url = $"{scheme}://localhost:{port}/";
            var done = new List<string>();
            foreach (var serial in ready)
            {
                if (!Run(adb, $"-s {serial} reverse tcp:{port} tcp:{port}", out string output))
                {
                    done.Add($"{serial}: 실패 ({output.Trim()})");
                    continue;
                }
                if (open) Run(adb, $"-s {serial} shell am start -a android.intent.action.VIEW -d {url}", out _);
                done.Add($"{serial}: 연결됨");
            }

            return $"폰에서 {url} 로 접속할 수 있습니다.\n" + string.Join("\n", done) +
                   "\n\nChrome DevTools: PC Chrome에서 chrome://inspect/#devices";
        }

        // Chrome은 밖에서 넘긴 chrome:// 주소를 무시하기도 해서, 주소를 클립보드에도 넣어 둔다.
        internal static string OpenChromeInspect()
        {
            const string url = "chrome://inspect/#devices";
            EditorGUIUtility.systemCopyBuffer = url;
#if UNITY_EDITOR_OSX
            bool started = Start("open", $"-a \"Google Chrome\" \"{url}\"");
#elif UNITY_EDITOR_WIN
            bool started = Start("cmd.exe", $"/c start chrome \"{url}\"");
#else
            bool started = Start("google-chrome", url);
#endif
            return started
                ? $"Chrome을 열었습니다. 페이지가 안 열리면 주소창에 붙여넣으세요 ({url}, 복사됨)."
                : $"Chrome을 열 수 없습니다. Chrome 주소창에 붙여넣으세요 ({url}, 복사됨).";
        }

        // 반환값은 창의 다이얼로그에 보여 줄 안내 문구.
        internal static string OpenSafariInspector()
        {
#if UNITY_EDITOR_OSX
            Start("open", "-a Safari");
            return "1. iPhone: 설정 › Safari › 고급 › 웹 속성 보기 켜기\n" +
                   "2. Mac Safari: 설정 › 고급 › '웹 개발자용 기능 보기' 켜기\n" +
                   "3. iPhone을 USB로 연결하고 Safari에서 빌드 페이지를 엽니다\n" +
                   "4. Mac Safari 메뉴의 개발자 › [iPhone 이름] › 페이지를 고릅니다\n\n" +
                   "USB 없이 로그만 보려면 '원격 콘솔'을 쓰세요.";
#else
            return "iPhone 브라우저의 개발자 도구는 Mac Safari에서만 열 수 있습니다.\n" +
                   "Windows에서는 '원격 콘솔'로 iPhone의 로그를 보고 JS를 실행하세요.";
#endif
        }

        // 이 PC가 루트 CA를 신뢰하게 해서 대시보드(https://localhost)를 경고 없이 연다.
        // OS가 직접 암호/확인 창을 띄운다. message는 창의 다이얼로그에 보여 줄 결과.
        internal static bool TrustCaOnThisComputer(out string message)
        {
            if (!LocalCertificates.HasCa)
            {
                message = "먼저 인증서를 생성하세요.";
                return false;
            }
#if UNITY_EDITOR_OSX
            string keychain = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library/Keychains/login.keychain-db");
            bool ok = Run("/usr/bin/security", $"add-trusted-cert -r trustRoot -k \"{keychain}\" \"{LocalCertificates.CaCert}\"", out string output);
#elif UNITY_EDITOR_WIN
            bool ok = Run("certutil", $"-user -addstore Root \"{LocalCertificates.CaCert}\"", out string output);
#else
            bool ok = false;
            string output = "이 OS는 지원하지 않습니다.";
#endif
            message = ok ? "이 컴퓨터가 루트 CA를 신뢰합니다. 브라우저를 다시 열면 적용됩니다." : "등록하지 못했습니다.\n" + output;
            return ok;
        }

        private static bool Start(string file, string args)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true });
                return process != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool Run(string file, string args, out string output)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                if (!process.WaitForExit(15000))
                {
                    process.Kill();
                    output += "\n(시간 초과)";
                    return false;
                }
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return false;
            }
        }
    }
}
