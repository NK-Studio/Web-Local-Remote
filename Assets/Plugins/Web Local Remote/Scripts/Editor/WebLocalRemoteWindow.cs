using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using ZXing;
using ZXing.QrCode;
using Debug = UnityEngine.Debug;

namespace WebLocalRemote
{
    public class BypassCertificateHandler : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData) => true;
    }

    public class WebLocalRemoteWindow : EditorWindow
    {
        private string _buildPath = "";
        private string _certPath = "";
        private string _keyPath = "";
        private Process _serverProcess;

        private VisualElement _statusDot;
        private Label _statusText;
        private VisualElement _qrContainer;
        private Image _qrImageElement;
        private Label _urlLabel;
        private Button _toggleBtn;
        private Texture2D _qrTexture;

        private const string PrefPid = "WebLocalRemote_ServerPID";
        private const string PrefURL = "WebLocalRemote_ServerURL";
        private const string PrefCertPath = "WebLocalRemote_CertPath";
        private const string PrefKeyPath = "WebLocalRemote_KeyPath";
        private const string PrefPort = "WebLocalRemote_ServerPort";

        [MenuItem("Tools/Web Local Remote")]
        public static void ShowWindow()
        {
            var window = GetWindow<WebLocalRemoteWindow>();
            window.titleContent = new GUIContent("Web Local Remote");
            window.minSize = new Vector2(350, 640);
            window.maxSize = new Vector2(450, 730);
        }

        public void CreateGUI()
        {
            string[] uxmlGuids = AssetDatabase.FindAssets("WebLocalRemoteWindow t:VisualTreeAsset");
            if (uxmlGuids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(uxmlGuids[0]);
                var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                if (visualTree != null) visualTree.CloneTree(rootVisualElement);
            }
            else
            {
                rootVisualElement.Add(new Label("UXML 파일을 찾을 수 없습니다. 파일 이름을 확인해주세요."));
                return;
            }

            // Apply USS explicitly
            string[] ussGuids = AssetDatabase.FindAssets("WebLocalRemoteWindow t:StyleSheet");
            if (ussGuids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(ussGuids[0]);
                var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                if (styleSheet != null) rootVisualElement.styleSheets.Add(styleSheet);
            }

            // UI references
            var missingModuleView = rootVisualElement.Q<VisualElement>("missing-module-view");
            var mainContentView = rootVisualElement.Q<VisualElement>("main-content-view");
            _statusDot = rootVisualElement.Q<VisualElement>("status-dot");
            _statusText = rootVisualElement.Q<Label>("status-text");
            _qrContainer = rootVisualElement.Q<VisualElement>("qr-container");
            _qrImageElement = rootVisualElement.Q<Image>("qr-image");
            _urlLabel = rootVisualElement.Q<Label>("url-label");
            _toggleBtn = rootVisualElement.Q<Button>("toggle-server-btn");
            var settingsToggleBtn = rootVisualElement.Q<Button>("settings-toggle-btn");
            var settingsView = rootVisualElement.Q<VisualElement>("settings-view");

            if (settingsToggleBtn != null && settingsView != null && mainContentView != null)
            {
                // 초기 상태 강제 설정 (UXML과 동기화)
                settingsView.style.display = DisplayStyle.None;
                mainContentView.style.display = DisplayStyle.Flex;

                settingsToggleBtn.clicked += () =>
                {
                    // resolvedStyle을 사용하면 인라인/USS/C# 스타일이 결합된 실제 출력 상태를 정확히 체크 가능
                    bool showSettings = settingsView.resolvedStyle.display == DisplayStyle.None;
                    settingsView.style.display = showSettings ? DisplayStyle.Flex : DisplayStyle.None;
                    mainContentView.style.display = showSettings ? DisplayStyle.None : DisplayStyle.Flex;
                };
            }

            // UI state init
            _qrContainer.style.display = DisplayStyle.None;

            // Title click event
            var headerTitle = rootVisualElement.Q<Label>("header-title");
            if (headerTitle != null)
            {
                headerTitle.RegisterCallback<ClickEvent>(evt => OpenScript());
            }

            // Check WebGL module
            bool hasWebGL = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            if (!hasWebGL)
            {
                missingModuleView.style.display = DisplayStyle.Flex;
                mainContentView.style.display = DisplayStyle.None;
                _statusDot.RemoveFromClassList("status-online");
                _statusDot.AddToClassList("status-error");
                _statusText.text = "WebGL Missing";
                return;
            }
            else
            {
                missingModuleView.style.display = DisplayStyle.None;
                mainContentView.style.display = DisplayStyle.Flex;
            }

            // Restore Process State
            RecoverProcess();

            // Build Path initialization
            var pathField = rootVisualElement.Q<TextField>("build-path");
            _buildPath = ProjectPrefs.GetString("WebLocalRemote_BuildPath", "");
            pathField.value = _buildPath;

            var certField = rootVisualElement.Q<TextField>("cert-path-field");
            var keyField = rootVisualElement.Q<TextField>("key-path-field");
            
            _certPath = ProjectPrefs.GetString(PrefCertPath, "");
            _keyPath = ProjectPrefs.GetString(PrefKeyPath, "");
            int savedPort = ProjectPrefs.GetInt(PrefPort, 8080);
            
            if (certField != null) certField.value = _certPath;
            if (keyField != null) keyField.value = _keyPath;

            var portField = rootVisualElement.Q<IntegerField>("port-field");
            if (portField != null)
            {
                portField.value = savedPort;
                portField.RegisterValueChangedCallback(evt =>
                {
                    int val = evt.newValue;
                    if (val < 1024) val = 1024; // Well-known 포트 예약 방지
                    if (val > 65535) val = 65535;
                    portField.SetValueWithoutNotify(val);
                    ProjectPrefs.SetInt(PrefPort, val);
                });
            }

            if (rootVisualElement.Q<Button>("select-cert-btn") is Button btnCert)
            {
                btnCert.clicked += () =>
                {
                    string path = EditorUtility.OpenFilePanel("Select Certificate", "", "cert,crt,pem");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _certPath = path;
                        certField.value = path;
                        ProjectPrefs.SetString(PrefCertPath, path);
                        CheckSSLFilesExistence();
                    }
                };
            }
            if (rootVisualElement.Q<Button>("clear-cert-btn") is Button btnClearCert)
            {
                btnClearCert.clicked += () =>
                {
                    _certPath = "";
                    certField.value = "";
                    ProjectPrefs.SetString(PrefCertPath, "");
                    CheckSSLFilesExistence();
                };
            }

            if (rootVisualElement.Q<Button>("select-key-btn") is Button btnKey)
            {
                btnKey.clicked += () =>
                {
                    string path = EditorUtility.OpenFilePanel("Select Private Key", "", "key,pem");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _keyPath = path;
                        keyField.value = path;
                        ProjectPrefs.SetString(PrefKeyPath, path);
                        CheckSSLFilesExistence();
                    }
                };
            }
            if (rootVisualElement.Q<Button>("clear-key-btn") is Button btnClearKey)
            {
                btnClearKey.clicked += () =>
                {
                    _keyPath = "";
                    keyField.value = "";
                    ProjectPrefs.SetString(PrefKeyPath, "");
                    CheckSSLFilesExistence();
                };
            }

            if (rootVisualElement.Q<Button>("generate-cert-btn") is Button btnGen)
            {
                btnGen.clicked += OnGenerateCertClicked;
            }

            CheckSSLFilesExistence();

            rootVisualElement.Q<Button>("select-path-btn").clicked += () =>
            {
                string panelTitle = Application.systemLanguage == SystemLanguage.Korean ? "WebGL 빌드 폴더 선택" : "Select WebGL Build Folder";
                string selected = EditorUtility.OpenFolderPanel(panelTitle, _buildPath, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    _buildPath = selected;
                    pathField.value = selected;
                    ProjectPrefs.SetString("WebLocalRemote_BuildPath", _buildPath);
                }
            };

            _toggleBtn.clicked += ToggleServer;

            UpdateStatusUI(IsServerRunning());
        }

        private void RecoverProcess()
        {
            int pid = ProjectPrefs.GetInt(PrefPid, -1);
            if (pid != -1)
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (proc != null && !proc.HasExited)
                    {
                        _serverProcess = proc;
                        string savedUrl = ProjectPrefs.GetString(PrefURL, "");
                        if (!string.IsNullOrEmpty(savedUrl))
                        {
                            GenerateQrCode(savedUrl);
                            _urlLabel.text = savedUrl;
                        }
                    }
                }
                catch
                {
                    ProjectPrefs.DeleteKey(PrefPid);
                }
            }
        }

        private bool IsServerRunning()
        {
            return _serverProcess != null && !_serverProcess.HasExited;
        }

        private void ToggleServer()
        {
            if (IsServerRunning()) StopNodeServer();
            else StartNodeServer();
        }

        private async void StartNodeServer()
        {
            if (IsServerRunning()) return;

            if (string.IsNullOrEmpty(_buildPath) || !Directory.Exists(_buildPath))
            {
                EditorUtility.DisplayDialog("Warning", "유효한 WebGL 빌드 경로를 선택해주세요.", "OK");
                return;
            }

            // 시각적 피드백 즉시 제공 (렉 방지)
            UpdateStatusUI(true);
            _statusText.text = "Starting...";
            _toggleBtn.SetEnabled(false);

            string executablePath = "";
#if UNITY_EDITOR_WIN
            executablePath = Path.Combine(EditorApplication.applicationContentsPath,
                "PlaybackEngines/WebGLSupport/BuildTools/Emscripten/node/node.exe");
#else
        executablePath =
 Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/WebGLSupport/BuildTools/Emscripten/node/node");
#endif

            if (!File.Exists(executablePath))
            {
                EditorUtility.DisplayDialog("Node.js Not Found", "내장 Node.js를 찾을 수 없습니다.\n경로: " + executablePath, "OK");
                UpdateStatusUI(false);
                _toggleBtn.SetEnabled(true);
                return;
            }

            string[] guids = AssetDatabase.FindAssets("server t:DefaultAsset");
            string serverScriptPath = "";

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("server.js"))
                {
                    serverScriptPath = Path.GetFullPath(path);
                    break;
                }
            }

            if (string.IsNullOrEmpty(serverScriptPath) || !File.Exists(serverScriptPath))
            {
                Debug.LogError("server.js를 찾을 수 없습니다.");
                UpdateStatusUI(false);
                _toggleBtn.SetEnabled(true);
                return;
            }

            string localIP = GetLocalIPAddress();
            int port = ProjectPrefs.GetInt(PrefPort, 8080);

            // 안전 장치: 혹시라도 이전에 비정상 종료되어 남아있는 Node.js 서버가 포트를 점유하고 있다면 종료 시도
            await StopOrphanedServer(port);

            bool useHttps = !string.IsNullOrEmpty(_certPath) && !string.IsNullOrEmpty(_keyPath) && File.Exists(_certPath) && File.Exists(_keyPath);
            string args = $"\"{serverScriptPath}\" \"{_buildPath}\" {port}";
            if (useHttps)
            {
                args += $" \"{_certPath}\" \"{_keyPath}\"";
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // 프로세스 시작을 별도 태스크로 실행하여 메인 스레드 프리징 방지
            _serverProcess = await Task.Run(() => 
            {
                try { return Process.Start(startInfo); }
                catch (System.Exception ex) { Debug.LogError(ex); return null; }
            });

            if (_serverProcess != null)
            {
                ProjectPrefs.SetInt(PrefPid, _serverProcess.Id);
                
                _serverProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) Debug.LogError($"[Server Error]: {e.Data}");
                };
                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();

                string protocol = useHttps ? "https" : "http";
                string serverUrl = $"{protocol}://{localIP}:{port}";
                ProjectPrefs.SetString(PrefURL, serverUrl);

                Debug.Log($"WebLocalRemote Started: {serverUrl}");
                GenerateQrCode(serverUrl);
                
                // UI 마무리
                _urlLabel.text = serverUrl;
                _statusText.text = "Server Running";
                _toggleBtn.SetEnabled(true);
            }
            else
            {
                UpdateStatusUI(false);
                _toggleBtn.SetEnabled(true);
            }
        }

        private void UpdateStatusUI(bool isRunning)
        {
            if (_statusDot == null || _statusText == null || _toggleBtn == null) return;

            _statusDot.RemoveFromClassList("status-offline");
            _statusDot.RemoveFromClassList("status-online");
            _statusDot.RemoveFromClassList("status-error");

            _toggleBtn.RemoveFromClassList("run-button");
            _toggleBtn.RemoveFromClassList("stop-button");

            if (isRunning)
            {
                _statusDot.AddToClassList("status-online");
                _statusText.text = "Server Running";

                _toggleBtn.AddToClassList("stop-button");
                _toggleBtn.text = "■ Stop";
                _qrContainer.style.display = DisplayStyle.Flex;
            }
            else
            {
                _statusDot.AddToClassList("status-offline");
                _statusText.text = "Offline";

                _toggleBtn.AddToClassList("run-button");
                _toggleBtn.text = "▶ Run";
                _qrContainer.style.display = DisplayStyle.None;
            }
        }

        private async Task StopOrphanedServer(int port)
        {
            var t1 = TryStopRequest($"http://127.0.0.1:{port}/stop-web-local-remote-server");
            var t2 = TryStopRequest($"https://127.0.0.1:{port}/stop-web-local-remote-server");
            await Task.WhenAll(t1, t2);
            await Task.Delay(100);
        }

        private async Task TryStopRequest(string url)
        {
            try
            {
                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    request.timeout = 1;
                    request.certificateHandler = new BypassCertificateHandler();
                    var operation = request.SendWebRequest();
                    
                    int elapsed = 0;
                    while (!operation.isDone && elapsed < 500)
                    {
                        await Task.Delay(20);
                        elapsed += 20;
                    }
                }
            }
            catch
            {
                // Ignore
            }
        }

        private void OnDestroy()
        {
            if (_qrTexture != null)
            {
                DestroyImmediate(_qrTexture);
                _qrTexture = null;
            }
        }

        private void StopNodeServer()
        {
            if (IsServerRunning())
            {
                _serverProcess.Kill();
                _serverProcess.Dispose();
            }

            _serverProcess = null;
            ProjectPrefs.DeleteKey(PrefPid);

            if (_qrTexture != null)
            {
                DestroyImmediate(_qrTexture);
                _qrTexture = null;
            }

            UpdateStatusUI(false);
        }

        private void GenerateQrCode(string text)
        {
            if (_qrTexture != null)
            {
                DestroyImmediate(_qrTexture);
            }

            BarcodeWriter writer = new BarcodeWriter
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions { Width = 256, Height = 256, Margin = 0 }
            };

            var pixels = writer.Write(text);
            _qrTexture = new Texture2D(256, 256);
            _qrTexture.SetPixels32(pixels);
            _qrTexture.Apply();

            _qrImageElement.image = _qrTexture;
        }

        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork) return ip.ToString();
            }

            return "127.0.0.1";
        }

        private void OpenScript()
        {
            var script = MonoScript.FromScriptableObject(this);
            var path = AssetDatabase.GetAssetPath(script);
            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<TextAsset>(path));
        }

        private string GetOpenSSLPath()
        {
            // 1. Git for Windows의 OpenSSL 경로 확인
            string gitOpenSSL = @"C:\Program Files\Git\usr\bin\openssl.exe";
            if (File.Exists(gitOpenSSL)) return gitOpenSSL;

            // 2. 환경 변수 PATH에서 확인
            return "openssl";
        }

        private void OnGenerateCertClicked()
        {
            string openssl = GetOpenSSLPath();
            string certFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.cert"));
            
            if (!Directory.Exists(certFolder))
            {
                Directory.CreateDirectory(certFolder);
            }

            string certFile = Path.Combine(certFolder, "server.crt");
            string keyFile = Path.Combine(certFolder, "server.key");

            // OpenSSL 명령: -x509 인증서 생성 (10년 만기), 비대화형 설정 (-subj)
            string args = $"req -x509 -newkey rsa:2048 -keyout \"{keyFile}\" -out \"{certFile}\" -days 3650 -nodes -subj \"/CN=localhost\"";

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = openssl,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0 && File.Exists(certFile) && File.Exists(keyFile))
                    {
                        _certPath = certFile;
                        _keyPath = keyFile;
                        
                        ProjectPrefs.SetString(PrefCertPath, _certPath);
                        ProjectPrefs.SetString(PrefKeyPath, _keyPath);

                        var certField = rootVisualElement.Q<TextField>("cert-path-field");
                        var keyField = rootVisualElement.Q<TextField>("key-path-field");
                        if (certField != null) certField.value = _certPath;
                        if (keyField != null) keyField.value = _keyPath;

                        CheckSSLFilesExistence();
                        EditorUtility.DisplayDialog("Success", "SSL 인증서가 성공적으로 생성되었습니다!\n폴더: .cert/", "OK");
                        
                        // .gitignore 추가 제안
                        TryAddGitIgnore();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Error", "인증서 생성 실패. Git(OpenSSL)이 설치되어 있는지 확인해주세요.\n\n" + error, "OK");
                    }
                }
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Error", "OpenSSL 실행 중 오류 발생: " + ex.Message, "OK");
            }
        }

        private void CheckSSLFilesExistence()
        {
            var warningLabel = rootVisualElement.Q<Label>("ssl-warning-label");
            if (warningLabel == null) return;

            bool isKorean = Application.systemLanguage == SystemLanguage.Korean;
            warningLabel.text = isKorean 
                ? "⚠️ 인증서 파일을 찾을 수 없습니다! HTTP 모드로 전환됩니다." 
                : "⚠️ Certificate files not found! Falling back to HTTP.";

            var secLabel = rootVisualElement.Q<Label>("help-box-security");
            if (secLabel != null)
            {
                secLabel.text = isKorean 
                    ? "🔒 보안: .cert/ 폴더의 개인키는 .gitignore에 추가하여 유출되지 않게 하세요."
                    : "🔒 Security: Private certificates in .cert/ should be added to .gitignore.";
            }

            var perfLabel = rootVisualElement.Q<Label>("help-box-performance");
            if (perfLabel != null)
            {
                perfLabel.text = isKorean 
                    ? "🚀 성능: 모바일 브라우저에서 GZip/Brotli 압축을 쓰려면 HTTPS 환경이 권장됩니다."
                    : "🚀 Performance: GZip/Brotli compression requires HTTPS to function correctly on most mobile browsers.";
            }

            bool hasPaths = !string.IsNullOrEmpty(_certPath) || !string.IsNullOrEmpty(_keyPath);
            bool filesMissing = !File.Exists(_certPath) || !File.Exists(_keyPath);

            if (hasPaths && filesMissing)
            {
                warningLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                warningLabel.style.display = DisplayStyle.None;
            }
        }

        private void TryAddGitIgnore()
        {
            string gitignorePath = Path.GetFullPath(Path.Combine(Application.dataPath, "../.gitignore"));
            string entry = "/.cert/";

            // 이미 등록되어 있는지 확인
            if (File.Exists(gitignorePath))
            {
                string content = File.ReadAllText(gitignorePath);
                if (content.Contains(entry)) return;
            }

            bool isKorean = Application.systemLanguage == SystemLanguage.Korean;
            string title = isKorean ? "깃 보안 권고" : "Git Security Recommendation";
            string msg = isKorean 
                ? "사설 인증서(.cert) 폴더를 .gitignore에 추가하여 보안 위험(개인키 유출)을 방지하시겠습니까?\n\n(추가 시 GZip/Brotli 압축 기능 사용을 위한 HTTPS 환경 설정이 권장됩니다.)"
                : "Would you like to add the '.cert' folder to your .gitignore to prevent leaking private keys?\n\n(Note: HTTPS is recommended for GZip/Brotli compression features.)";
            string yes = isKorean ? "예 (권장)" : "Yes (Recommended)";
            string no = isKorean ? "아니오" : "No";

            bool confirm = EditorUtility.DisplayDialog(title, msg, yes, no);

            if (confirm)
            {
                try
                {
                    string appendText = $"\n# Web Local Remote Certificates\n{entry}\n";
                    File.AppendAllText(gitignorePath, appendText);
                    Debug.Log("[WebLocalRemote] .cert/ added to .gitignore");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("[WebLocalRemote] Failed to update .gitignore: " + ex.Message);
                }
            }
        }
    }
}