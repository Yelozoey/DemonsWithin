using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DemonsWithin.Editor
{
    public sealed class LlmTacticsWindow : EditorWindow
    {
        private LlmTacticsSettings settings;
        private LlmTacticsRequest test;
        private double testStarted;
        private string status;
        private MessageType statusType;

        [MenuItem("Tools/Demons Within/LLM 战术")]
        public static void Open()
        {
            var window = GetWindow<LlmTacticsWindow>("LLM 战术");
            window.minSize = new Vector2(460, 330);
        }

        private void OnEnable()
        {
            settings = LlmTacticsSettings.Load(out string error);
            if (error != null)
                SetStatus(error, MessageType.Error);
        }

        private void OnDisable() => CloseTest();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("心魔 · LLM 战术连接", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("保存后，在对局里按 F2 切到 LLM 模式。已在 LLM 模式时，先切出再切回以载入新配置。",
                MessageType.Info);
            settings.endpoint = EditorGUILayout.TextField("请求地址", settings.endpoint);
            settings.model = EditorGUILayout.TextField("模型名称", settings.model);
            settings.apiKeyEnvironment = EditorGUILayout.TextField("密钥环境变量名", settings.apiKeyEnvironment);
            settings.jsonMode = EditorGUILayout.Toggle("启用 JSON 模式", settings.jsonMode);
            settings.timeoutSeconds = EditorGUILayout.Slider("请求超时（秒）", settings.Timeout, 0.5f, 20f);
            settings.requestIntervalSeconds = EditorGUILayout.Slider("最短请求间隔（秒）", settings.Interval, 0.5f, 30f);
            settings.planLifetimeSeconds = EditorGUILayout.Slider("战术有效期（秒）", settings.Lifetime, 1f, 30f);
            EditorGUILayout.HelpBox("本地 HTTP 服务可使用 127.0.0.1。使用云服务时填写 HTTPS 地址，变量栏只填名称。",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("保存设置"))
                    Save();
                if (test == null && GUILayout.Button("测试连接"))
                    StartTest();
                else if (test != null && GUILayout.Button("取消测试"))
                {
                    CloseTest();
                    SetStatus("已取消", MessageType.Info);
                }
            }

            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, statusType);
        }

        private void Save()
        {
            try
            {
                settings.Save();
                SetStatus("已保存到本机", MessageType.Info);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                SetStatus("无法写入本机配置", MessageType.Error);
            }
        }

        private void StartTest()
        {
            testStarted = EditorApplication.timeSinceStartup;
            test = LlmTacticsRequest.Start(settings, "Return exactly this JSON object: {\"ok\":true}.",
                "Test the connection using JSON.", testStarted, out string error);
            SetStatus(test != null ? "正在测试…" : error, test != null ? MessageType.Info : MessageType.Error);
        }

        private void Update()
        {
            double now = EditorApplication.timeSinceStartup;
            if (test == null || !test.Poll(now, out _, out string error))
                return;

            CloseTest();
            SetStatus(error ?? $"模型返回了响应 · {(now - testStarted) * 1000:0} ms",
                error == null ? MessageType.Info : MessageType.Error);
        }

        private void CloseTest()
        {
            test?.Dispose();
            test = null;
        }

        private void SetStatus(string message, MessageType type)
        {
            status = message;
            statusType = type;
            Repaint();
        }
    }
}
