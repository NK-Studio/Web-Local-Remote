using UnityEditor;

namespace WebLocalRemote
{
    internal static class ProjectPrefs
    {
        // 문자 저장 및 로드
        internal static string GetString(string key, string defaultValue = "")
        {
            string value = EditorUserSettings.GetConfigValue(key);
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        internal static void SetString(string key, string value)
            => EditorUserSettings.SetConfigValue(key, value);

        // 정수(int) 저장 및 로드
        internal static void SetInt(string key, int value)
            => EditorUserSettings.SetConfigValue(key, value.ToString());

        internal static int GetInt(string key, int defaultValue = 0)
        {
            string value = EditorUserSettings.GetConfigValue(key);
            return int.TryParse(value, out int result) ? result : defaultValue;
        }

        // 불리언(bool) 저장 및 로드
        internal static void SetBool(string key, bool value)
            => EditorUserSettings.SetConfigValue(key, value.ToString().ToLower());

        internal static bool GetBool(string key, bool defaultValue = false)
        {
            string value = EditorUserSettings.GetConfigValue(key);
            return bool.TryParse(value, out bool result) ? result : defaultValue;
        }

        // 실수(float) 저장 및 로드
        internal static void SetFloat(string key, float value)
            => EditorUserSettings.SetConfigValue(key, value.ToString("R")); // R은 정밀도 유지

        internal static float GetFloat(string key, float defaultValue = 0f)
        {
            string value = EditorUserSettings.GetConfigValue(key);
            return float.TryParse(value, out float result) ? result : defaultValue;
        }
        
        internal static void DeleteKey(string key)
        {
            // null을 넣어주면 해당 키값이 초기화됩니다.
            EditorUserSettings.SetConfigValue(key, null);
        }
    }
}