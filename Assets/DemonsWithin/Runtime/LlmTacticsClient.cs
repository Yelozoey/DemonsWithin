using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace DemonsWithin
{
    [Serializable]
    public sealed class LlmTacticsSettings
    {
        public string endpoint = "http://127.0.0.1:11434/v1/chat/completions";
        public string model = "";
        public string apiKeyEnvironment = "";
        public bool jsonMode = true;
        public float timeoutSeconds = 3f;
        public float requestIntervalSeconds = 1.5f;
        public float planLifetimeSeconds = 8f;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "llm-tactics.json");
        public float Timeout => Seconds(timeoutSeconds, 3f, 0.5f, 20f);
        public float Interval => Seconds(requestIntervalSeconds, 1.5f, 0.5f, 30f);
        public float Lifetime => Seconds(planLifetimeSeconds, 8f, 1f, 30f);

        public static LlmTacticsSettings Load(out string error)
        {
            error = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    var settings = new LlmTacticsSettings();
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), settings);
                    return settings;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                error = "Invalid settings file";
            }
            return new LlmTacticsSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
        }

        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(model))
                return "Model not configured";
            if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out Uri address) ||
                (address.Scheme != "https" && (address.Scheme != "http" || !address.IsLoopback)) ||
                address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0 ||
                !address.AbsolutePath.TrimEnd('/').EndsWith("/chat/completions", StringComparison.Ordinal))
                return "Use an HTTPS or loopback HTTP chat/completions URL";
            return null;
        }

        private static float Seconds(float value, float fallback, float minimum, float maximum) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, maximum);
    }

    public sealed class LlmTacticsRequest : IDisposable
    {
        private const int ResponseLimit = 32768;

        [Serializable]
        private struct Message
        {
            public string role, content;
        }

        [Serializable]
        private sealed class CompletionRequest
        {
            public string model;
            public Message[] messages;
            public int max_tokens = 256;
            public bool stream;
        }

        [Serializable]
        private sealed class CompletionResponse
        {
            public Choice[] choices;
        }

        [Serializable]
        private struct Choice
        {
            public Message message;
        }

        private readonly UnityWebRequest request;
        public double Started { get; }
        private readonly double deadline;

        private LlmTacticsRequest(UnityWebRequest request, double started, float timeout)
        {
            this.request = request;
            Started = started;
            deadline = started + timeout;
        }

        public static LlmTacticsRequest Start(LlmTacticsSettings settings, string system, string evidence,
            double now, out string error)
        {
            error = settings.Validate();
            if (error != null)
                return null;

            UnityWebRequest web = null;
            try
            {
                string keyName = settings.apiKeyEnvironment?.Trim();
                string key = string.IsNullOrEmpty(keyName) ? null : Environment.GetEnvironmentVariable(keyName);
                if (!string.IsNullOrEmpty(keyName) && string.IsNullOrWhiteSpace(key))
                {
                    error = "API key environment variable is missing";
                    return null;
                }

                string payload = JsonUtility.ToJson(new CompletionRequest
                {
                    model = settings.model.Trim(),
                    messages = new[]
                    {
                        new Message { role = "system", content = system },
                        new Message { role = "user", content = evidence }
                    }
                });
                if (settings.jsonMode)
                    payload = payload.Insert(payload.Length - 1, ",\"response_format\":{\"type\":\"json_object\"}");
                web = new UnityWebRequest(settings.endpoint.Trim(), UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload)),
                    downloadHandler = new DownloadHandlerBuffer(),
                    redirectLimit = 0,
                    timeout = Mathf.CeilToInt(settings.Timeout)
                };
                web.SetRequestHeader("Content-Type", "application/json");
                if (key != null)
                    web.SetRequestHeader("Authorization", "Bearer " + key.Trim());
                web.SendWebRequest();
                return new LlmTacticsRequest(web, now, settings.Timeout);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException
                or UnityException or System.Security.SecurityException)
            {
                web?.Dispose();
                error = "Request could not start";
                return null;
            }
        }

        public bool Poll(double now, out string content, out string error)
        {
            content = null;
            error = null;
            if (now >= deadline || request.downloadedBytes > ResponseLimit)
            {
                error = now >= deadline ? "Timeout" : "Response too large";
                return true;
            }
            if (!request.isDone)
                return false;
            if (request.result != UnityWebRequest.Result.Success)
            {
                error = request.responseCode > 0 ? $"HTTP {request.responseCode}" : "Connection failed";
                return true;
            }

            try
            {
                var response = JsonUtility.FromJson<CompletionResponse>(request.downloadHandler.text);
                if (response?.choices != null && response.choices.Length > 0)
                    content = response.choices[0].message.content;
            }
            catch (ArgumentException)
            {
                // The provider returned an unreadable completion envelope.
            }

            if (string.IsNullOrWhiteSpace(content))
                error = "Missing completion";
            return true;
        }

        public void Dispose()
        {
            if (!request.isDone)
                request.Abort();
            request.Dispose();
        }
    }
}
