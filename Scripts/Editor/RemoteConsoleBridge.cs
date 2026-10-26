using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_6000_7_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace WebLocalRemote
{
    // 서버가 받은 폰의 콘솔 로그(/__wlr/logs)를 유니티 콘솔에 그대로 띄운다.
    // 에디터 창을 닫아도, 도메인 리로드가 일어나도 서버가 살아 있으면 이어서 받는다.
    [InitializeOnLoad]
#if UNITY_6000_7_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class RemoteConsoleBridge
    {
        private const string PrefEnabled = "WebLocalRemote_MirrorConsole";
        private const string SessionAfter = "WebLocalRemote_MirrorAfter";
        private const double Interval = 0.5;
        private const int MaxReplay = 200;

        private static UnityWebRequest _request;
        private static double _next;

        [Serializable]
        private class Entry
        {
            public int seq;
            public string device;
            public string name;
            public string level;
            public string message;
        }

        [Serializable]
        private class Response
        {
            public int seq;
            public Entry[] logs;
        }

        static RemoteConsoleBridge()
        {
            EditorApplication.update += Update;
        }

        internal static bool Enabled
        {
            get => ProjectPrefs.GetBool(PrefEnabled, true);
            set => ProjectPrefs.SetBool(PrefEnabled, value);
        }

        private static void Update()
        {
            if (_request != null)
            {
                if (!_request.isDone) return;
                if (_request.result == UnityWebRequest.Result.Success) Handle(_request.downloadHandler.text);
                _request.Dispose();
                _request = null;
            }

            if (!Enabled || EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + Interval;
            if (ProjectPrefs.GetInt(WebLocalRemoteWindow.PrefPid, -1) == -1) return;

            string url = ProjectPrefs.GetString(WebLocalRemoteWindow.PrefURL, "");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var server)) return;

            int after = SessionState.GetInt(SessionAfter, 0);
            _request = UnityWebRequest.Get($"{server.Scheme}://127.0.0.1:{server.Port}/__wlr/logs?after={after}");
            _request.timeout = 2;
            _request.certificateHandler = new BypassCertificateHandler();
            _request.SendWebRequest();
        }

        private static void Handle(string json)
        {
            Response response;
            try { response = JsonUtility.FromJson<Response>(json); }
            catch { return; }
            if (response?.logs == null) return;

            int after = SessionState.GetInt(SessionAfter, 0);
            // 서버를 다시 켜면 번호가 처음부터 시작한다.
            if (response.seq < after)
            {
                SessionState.SetInt(SessionAfter, 0);
                return;
            }

            // 처음 붙을 때 쌓여 있던 로그를 전부 쏟지 않는다.
            int start = after == 0 ? Math.Max(0, response.logs.Length - MaxReplay) : 0;
            for (int i = start; i < response.logs.Length; i++)
            {
                var log = response.logs[i];
                string id = string.IsNullOrEmpty(log.device) ? "" : " " + log.device.Substring(0, Math.Min(4, log.device.Length));
                var type = log.level == "error" ? LogType.Error : log.level == "warn" ? LogType.Warning : LogType.Log;
                Debug.LogFormat(type, LogOption.NoStacktrace, null, "[{0}{1}] {2}", log.name, id, log.message);
            }

            SessionState.SetInt(SessionAfter, response.seq);
        }
    }
}
