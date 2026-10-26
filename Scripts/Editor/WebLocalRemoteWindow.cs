using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ZXing;
using ZXing.QrCode;
using Debug = UnityEngine.Debug;

namespace WebLocalRemote
{
    public class BypassCertificateHandler : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData) => true;
    }

    public class WebLocalRemoteWindow : EditorWindow, IHasCustomMenu
    {
        private string _buildPath = "";
        private string _certPath = "";
        private string _keyPath = "";
        private Process _serverProcess;

        private VisualElement _statusDot;
        private Label _statusText;
        private Image _qrImageElement;
        private Label _urlLabel;
        private Button _toggleBtn;
        private IntegerField _portField;
        private Button _resetPortBtn;
        private Texture2D _qrTexture;
        private VisualElement _qrBox;
        private Label _qrPlaceholder;
        private Button[] _tabs;
        private VisualElement[] _pages;
        private Button[] _remoteButtons;
        private Button _qrAppBtn;
        private Button _qrSetupBtn;
        private Label _remoteStatus;
        private bool _qrSetup;

        internal const string PrefPid = "WebLocalRemote_ServerPID";
        internal const string PrefURL = "WebLocalRemote_ServerURL";
        private const string PrefCertPath = "WebLocalRemote_CertPath";
        private const string PrefKeyPath = "WebLocalRemote_KeyPath";
        private const string PrefPort = "WebLocalRemote_ServerPort";
        private const int DefaultPort = 8080;
        private const int MinPort = 1024; // Well-known 포트 예약 방지
        private const int MaxPort = 65535;
        private const string PrefTab = "WebLocalRemote_Tab";
        private const string NoResult = "버튼을 누르면 결과가 여기에 표시됩니다.";

        // 모든 탭이 이 크기 안에 맞도록 짜여 있다(마지막 카드가 남은 높이를 채움).
        private static readonly Vector2 WindowSize = new Vector2(400, 600);

        [MenuItem("Tools/Web Local Remote")]
        public static void ShowWindow()
        {
            var window = GetWindow<WebLocalRemoteWindow>();
            window.titleContent = new GUIContent("Web Local Remote");
        }

        private void OnEnable()
        {
            minSize = WindowSize;
            maxSize = WindowSize;
        }

        // 탭의 ⋮ 메뉴
        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("사용법 (Documentation)"), false, OpenDocumentation);
        }

        // Documentation~은 Unity가 가져오지 않는 폴더라 패키지 경로에서 직접 찾아 브라우저로 연다.
        private void OpenDocumentation()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WebLocalRemoteWindow).Assembly);
            string path = package != null ? Path.Combine(package.resolvedPath, "Documentation~", "index.html") : "";
            if (!File.Exists(path))
            {
                ShowDialog(WebLocalRemoteDialog.Kind.Error, "Web Local Remote", "사용법 문서를 찾을 수 없습니다.\n" + path);
                return;
            }
            Application.OpenURL(new System.Uri(path).AbsoluteUri);
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
            _qrImageElement = rootVisualElement.Q<Image>("qr-image");
            _urlLabel = rootVisualElement.Q<Label>("url-label");
            _toggleBtn = rootVisualElement.Q<Button>("toggle-server-btn");
            _qrAppBtn = rootVisualElement.Q<Button>("qr-app-btn");
            _qrSetupBtn = rootVisualElement.Q<Button>("qr-setup-btn");
            _qrBox = rootVisualElement.Q<VisualElement>("qr-box");
            _qrPlaceholder = rootVisualElement.Q<Label>("qr-placeholder");
            _remoteStatus = rootVisualElement.Q<Label>("remote-status-label");
            _remoteButtons = new[] { "open-console-btn", "adb-open-btn" }
                .Select(n => rootVisualElement.Q<Button>(n)).Where(b => b != null).ToArray();

            // 탭: 서버 / 원격 디버그 / 설정. 마지막에 본 탭을 기억한다.
            _tabs = new[] { "tab-server", "tab-debug", "tab-settings" }.Select(n => rootVisualElement.Q<Button>(n)).ToArray();
            _pages = new[] { "page-server", "page-debug", "page-settings" }.Select(n => rootVisualElement.Q<VisualElement>(n)).ToArray();
            for (int i = 0; i < _tabs.Length; i++)
            {
                int index = i;
                _tabs[i].clicked += () => SelectTab(index);
            }
            SelectTab(ProjectPrefs.GetInt(PrefTab, 0));

            if (rootVisualElement.Q<Button>("help-btn") is Button helpBtn)
            {
                helpBtn.clicked += OpenDocumentation;
            }

            SetRemoteStatus("");

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
            int savedPort = ProjectPrefs.GetInt(PrefPort, DefaultPort);
            
            if (certField != null) certField.value = _certPath;
            if (keyField != null) keyField.value = _keyPath;

            _portField = rootVisualElement.Q<IntegerField>("port-field");
            if (_portField != null)
            {
                // 키 입력마다 클램프하면 "8080"을 한 자리씩 칠 수 없으므로 Enter/포커스 아웃 시에만 확정
                _portField.isDelayed = true;
                _portField.value = savedPort;
                _portField.RegisterValueChangedCallback(evt => SavePort(evt.newValue));
            }

            _resetPortBtn = rootVisualElement.Q<Button>("reset-port-btn");
            if (_resetPortBtn != null)
            {
                _resetPortBtn.clicked += () => SavePort(DefaultPort);
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

            if (rootVisualElement.Q<Button>("trust-ca-btn") is Button btnTrust)
            {
                btnTrust.clicked += () =>
                {
                    bool ok = DeviceTools.TrustCaOnThisComputer(out string message);
                    ShowDialog(ok ? WebLocalRemoteDialog.Kind.Success : WebLocalRemoteDialog.Kind.Error, "Web Local Remote", message);
                };
            }

            CheckSSLFilesExistence();
            SetupRemoteDebug();

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
                        if (!string.IsNullOrEmpty(ProjectPrefs.GetString(PrefURL, ""))) UpdateQr();
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
                ShowDialog(WebLocalRemoteDialog.Kind.Warning, "Warning", "유효한 WebGL 빌드 경로를 선택해주세요.");
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
#elif UNITY_EDITOR_OSX
            // On macOS, applicationContentsPath is Unity.app/Contents.
            // PlaybackEngines is sibling to Unity.app.
            executablePath = Path.GetFullPath(Path.Combine(EditorApplication.applicationContentsPath, "..", "..", "PlaybackEngines/WebGLSupport/BuildTools/Emscripten/node/node"));

            if (!File.Exists(executablePath))
            {
                executablePath = Path.GetFullPath(Path.Combine(EditorApplication.applicationContentsPath, "..", "..", "PlaybackEngines/WebGLSupport/BuildTools/Emscripten/node"));
            }
#else
            executablePath = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/WebGLSupport/BuildTools/Emscripten/node/node");
#endif

            if (!File.Exists(executablePath))
            {
                ShowDialog(WebLocalRemoteDialog.Kind.Error, "Node.js Not Found", "내장 Node.js를 찾을 수 없습니다.\n경로: " + executablePath);
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
            int port = ProjectPrefs.GetInt(PrefPort, DefaultPort);

            // 안전 장치: 혹시라도 이전에 비정상 종료되어 남아있는 Node.js 서버가 포트를 점유하고 있다면 종료 시도
            await StopOrphanedServer(port);

            // 자동 생성한 인증서라면 지금 네트워크 주소가 SAN에 들어 있는지 확인하고 필요하면 다시 발급
            if (LocalCertificates.IsManaged(_certPath, _keyPath) && !LocalCertificates.RefreshIfNeeded(out string certError))
            {
                Debug.LogWarning("[WebLocalRemote] 서버 인증서를 현재 IP로 다시 만들지 못했습니다: " + certError);
            }

            bool useHttps = !string.IsNullOrEmpty(_certPath) && !string.IsNullOrEmpty(_keyPath) && File.Exists(_certPath) && File.Exists(_keyPath);
            // 인증서 인자는 비워도 자리를 지켜야 CA(설정 페이지에서 폰에 내려줄 파일)가 6번째로 간다
            string args = $"\"{serverScriptPath}\" \"{_buildPath}\" {port}";
            args += useHttps ? $" \"{_certPath}\" \"{_keyPath}\"" : " \"\" \"\"";
            if (LocalCertificates.HasCa)
            {
                args += $" \"{LocalCertificates.CaCert}\"";
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

                Debug.Log($"WebLocalRemote Started: {serverUrl}  (원격 콘솔: {protocol}://localhost:{port}/__wlr/)");
                UpdateQr();

                // UI 마무리
                _statusText.text = "Server Running";
                _toggleBtn.SetEnabled(true);
            }
            else
            {
                UpdateStatusUI(false);
                _toggleBtn.SetEnabled(true);
            }
        }

        private void SavePort(int port)
        {
            port = Mathf.Clamp(port, MinPort, MaxPort);
            _portField?.SetValueWithoutNotify(port);
            ProjectPrefs.SetInt(PrefPort, port);
        }

        private void UpdateStatusUI(bool isRunning)
        {
            if (_statusDot == null || _statusText == null || _toggleBtn == null) return;

            _statusDot.RemoveFromClassList("status-offline");
            _statusDot.RemoveFromClassList("status-online");
            _statusDot.RemoveFromClassList("status-error");

            _toggleBtn.RemoveFromClassList("run-button");
            _toggleBtn.RemoveFromClassList("stop-button");

            // 실행 중에는 포트를 바꿔도 적용되지 않으므로 잠금
            _portField?.SetEnabled(!isRunning);
            _resetPortBtn?.SetEnabled(!isRunning);

            if (isRunning)
            {
                _statusDot.AddToClassList("status-online");
                _statusText.text = "Server Running";

                _toggleBtn.AddToClassList("stop-button");
                _toggleBtn.text = "■ Stop";
            }
            else
            {
                _statusDot.AddToClassList("status-offline");
                _statusText.text = "Offline";

                _toggleBtn.AddToClassList("run-button");
                _toggleBtn.text = "▶ Run";
            }

            // 레이아웃은 그대로 두고 내용만 바꾼다: 꺼져 있으면 QR 자리에 안내, 서버가 필요한 버튼은 잠금
            bool hasQr = isRunning && _qrTexture != null;
            _qrImageElement.style.display = hasQr ? DisplayStyle.Flex : DisplayStyle.None;
            _qrPlaceholder.style.display = hasQr ? DisplayStyle.None : DisplayStyle.Flex;
            _qrBox?.EnableInClassList("qr-empty", !hasQr);
            if (!isRunning) _urlLabel.text = "";
            _qrAppBtn?.SetEnabled(isRunning);
            _qrSetupBtn?.SetEnabled(isRunning);
            if (_remoteButtons != null)
                foreach (var button in _remoteButtons) button.SetEnabled(isRunning);
        }

        private void SelectTab(int index)
        {
            index = Mathf.Clamp(index, 0, _tabs.Length - 1);
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].EnableInClassList("tab-selected", i == index);
                _pages[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
            }
            ProjectPrefs.SetInt(PrefTab, index);
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

        // 기본 게이트웨이가 있는 인터페이스를 고른다(VPN, 가상 어댑터 주소가 QR에 들어가지 않도록).
        private string GetLocalIPAddress()
        {
            return LocalCertificates.LocalAddresses().FirstOrDefault() ?? "127.0.0.1";
        }

        // ---- 원격 디버그 ---------------------------------------------------------

        private void SetupRemoteDebug()
        {
            if (_qrAppBtn != null) _qrAppBtn.clicked += () => SetQrMode(false);
            if (_qrSetupBtn != null) _qrSetupBtn.clicked += () => SetQrMode(true);

            if (rootVisualElement.Q<Button>("open-console-btn") is Button btnConsole)
            {
                btnConsole.clicked += () =>
                {
                    if (!TryGetServer(out var scheme, out var port)) return;
                    Application.OpenURL($"{scheme}://localhost:{port}/__wlr/");
                    if (scheme == "https" && LocalCertificates.HasCa)
                        SetRemoteStatus("원격 콘솔을 열었습니다. 브라우저에 인증서 경고가 뜨면 설정 탭의 '이 PC에서 CA 신뢰'를 한 번 누르세요.");
                };
            }

            if (rootVisualElement.Q<Toggle>("mirror-toggle") is Toggle mirror)
            {
                mirror.SetValueWithoutNotify(RemoteConsoleBridge.Enabled);
                mirror.RegisterValueChangedCallback(evt => RemoteConsoleBridge.Enabled = evt.newValue);
            }

            if (rootVisualElement.Q<Button>("adb-open-btn") is Button btnAdb)
            {
                btnAdb.clicked += () =>
                {
                    if (!TryGetServer(out var scheme, out var port)) return;
                    SetRemoteStatus("Android 기기에 연결하는 중...");
                    SetRemoteStatus(DeviceTools.ConnectAndroid(port, scheme, true));
                };
            }

            if (rootVisualElement.Q<Button>("chrome-inspect-btn") is Button btnChrome)
            {
                btnChrome.clicked += () => SetRemoteStatus(DeviceTools.OpenChromeInspect());
            }

            if (rootVisualElement.Q<Button>("safari-inspect-btn") is Button btnSafari)
            {
                btnSafari.clicked += () => ShowDialog(WebLocalRemoteDialog.Kind.Info, "Safari 웹 속성 보기", DeviceTools.OpenSafariInspector());
            }
        }

        private bool TryGetServer(out string scheme, out int port)
        {
            scheme = null;
            port = 0;
            if (!IsServerRunning() || !System.Uri.TryCreate(ProjectPrefs.GetString(PrefURL, ""), System.UriKind.Absolute, out var uri))
            {
                SetRemoteStatus("서버가 실행 중이 아닙니다.");
                return false;
            }
            scheme = uri.Scheme;
            port = uri.Port;
            return true;
        }

        private void SetRemoteStatus(string text)
        {
            if (_remoteStatus == null) return;
            _remoteStatus.text = string.IsNullOrEmpty(text) ? NoResult : text;
            _remoteStatus.style.opacity = string.IsNullOrEmpty(text) ? 0.5f : 1f;
        }

        private void SetQrMode(bool setup)
        {
            _qrSetup = setup;
            UpdateQr();
        }

        // QR: 빌드 주소, 또는 폰에 루트 CA를 설치하고 개발자 도구 설정을 안내하는 페이지
        private void UpdateQr()
        {
            string serverUrl = ProjectPrefs.GetString(PrefURL, "");
            if (string.IsNullOrEmpty(serverUrl)) return;

            string url = _qrSetup ? serverUrl + "/__wlr/setup" : serverUrl;
            GenerateQrCode(url);
            _urlLabel.text = url;

            _qrAppBtn?.EnableInClassList("segment-selected", !_qrSetup);
            _qrSetupBtn?.EnableInClassList("segment-selected", _qrSetup);
            UpdateStatusUI(IsServerRunning());
        }

        private void OpenScript()
        {
            var script = MonoScript.FromScriptableObject(this);
            var path = AssetDatabase.GetAssetPath(script);
            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<TextAsset>(path));
        }

        // 로컬 루트 CA(처음 한 번)와 그 CA로 서명한 서버 인증서(현재 IP 포함)를 만든다.
        private void OnGenerateCertClicked()
        {
            if (!LocalCertificates.Generate(out string error))
            {
                ShowDialog(WebLocalRemoteDialog.Kind.Error, "Error", "인증서 생성 실패. Git(OpenSSL)이 설치되어 있는지 확인해주세요.\n\n" + error);
                return;
            }

            _certPath = LocalCertificates.ServerCert;
            _keyPath = LocalCertificates.ServerKey;

            ProjectPrefs.SetString(PrefCertPath, _certPath);
            ProjectPrefs.SetString(PrefKeyPath, _keyPath);

            var certField = rootVisualElement.Q<TextField>("cert-path-field");
            var keyField = rootVisualElement.Q<TextField>("key-path-field");
            if (certField != null) certField.value = _certPath;
            if (keyField != null) keyField.value = _keyPath;

            CheckSSLFilesExistence();
            // 창 안의 다이얼로그는 기다려 주지 않으므로 닫힌 뒤에 .gitignore 추가를 제안한다.
            ShowDialog(WebLocalRemoteDialog.Kind.Success, "Success",
                "로컬 루트 CA와 SSL 인증서를 만들었습니다.\n폴더: .cert/\n\n" +
                "서버를 실행한 뒤 QR의 '기기 설정 · 인증서'를 폰으로 스캔해 루트 CA를 한 번 설치하면 " +
                "Android와 iPhone 모두 경고 없는 HTTPS로 접속합니다.",
                onClose: _ => TryAddGitIgnore());
        }

        private void ShowDialog(WebLocalRemoteDialog.Kind kind, string title, string message,
            string ok = "OK", string cancel = null, System.Action<bool> onClose = null)
        {
            WebLocalRemoteDialog.Show(rootVisualElement, kind, title, message, ok, cancel, onClose);
        }

        private void CheckSSLFilesExistence()
        {
            var warningLabel = rootVisualElement.Q<Label>("ssl-warning-label");
            if (warningLabel == null) return;

            bool isKorean = Application.systemLanguage == SystemLanguage.Korean;
            warningLabel.text = isKorean 
                ? "인증서 파일을 찾을 수 없습니다! HTTP 모드로 전환됩니다." 
                : "Certificate files not found! Falling back to HTTP.";

            var secLabel = rootVisualElement.Q<Label>("help-box-security");
            if (secLabel != null)
            {
                secLabel.text = isKorean 
                    ? "보안: .cert/ 폴더의 개인키는 .gitignore에 추가하여 유출되지 않게 하세요."
                    : "Security: Private certificates in .cert/ should be added to .gitignore.";
            }

            var perfLabel = rootVisualElement.Q<Label>("help-box-performance");
            if (perfLabel != null)
            {
                perfLabel.text = isKorean 
                    ? "성능: 모바일에서 GZip/Brotli 압축 빌드를 쓰려면 HTTPS가 필요합니다."
                    : "Performance: GZip/Brotli compression requires HTTPS to function correctly on most mobile browsers.";
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

            if (rootVisualElement.Q<Label>("ca-status-label") is Label caLabel)
            {
                caLabel.text = LocalCertificates.Describe(_certPath, _keyPath);
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

            ShowDialog(WebLocalRemoteDialog.Kind.Warning, title, msg, yes, no, confirm =>
            {
                if (!confirm) return;
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
            });
        }
    }
}