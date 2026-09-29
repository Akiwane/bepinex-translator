using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BepInExTranslator.Core.Backends
{
    /// <summary>
    /// OpenAI 兼容 Chat Completions 后端（/v1/chat/completions）。
    /// 可用于官方 OpenAI、Azure OpenAI 兼容网关、本地 LLM 代理等。
    /// </summary>
    public sealed class OpenAiCompatibleBackend : ITranslationBackend
    {
        private readonly OpenAiCompatibleOptions _options;

        public string Name => "OpenAiCompatible";

        public OpenAiCompatibleBackend(OpenAiCompatibleOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(_options.Endpoint))
            {
                throw new ArgumentException("OpenAI-compatible endpoint is required.", nameof(options));
            }
        }

        public async Task<string> TranslateAsync(string source, string targetLang, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.Empty;
            }

            var placeholders = TemplateFiller.BuildTranslationPlaceholders(source, targetLang);
            var systemPrompt = TemplateFiller.Fill(
                string.IsNullOrWhiteSpace(_options.SystemPrompt)
                    ? "You are a translator. Translate the user message into {{target_lang}}. Reply with only the translation, no quotes or explanation."
                    : _options.SystemPrompt,
                placeholders);

            var userContent = TemplateFiller.Fill(
                string.IsNullOrWhiteSpace(_options.UserPromptTemplate)
                    ? "{{text}}"
                    : _options.UserPromptTemplate,
                placeholders);

            // 组装 Chat Completions JSON 请求体
            var body =
                "{"
                + "\"model\":\"" + TemplateFiller.EscapeJson(_options.Model) + "\","
                + "\"temperature\":" + _options.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"messages\":["
                + "{\"role\":\"system\",\"content\":\"" + TemplateFiller.EscapeJson(systemPrompt) + "\"},"
                + "{\"role\":\"user\",\"content\":\"" + TemplateFiller.EscapeJson(userContent) + "\"}"
                + "]"
                + "}";

            var request = (HttpWebRequest)WebRequest.Create(_options.Endpoint);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.Timeout = _options.TimeoutMs;
            request.ReadWriteTimeout = _options.TimeoutMs;

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                request.Headers["Authorization"] = "Bearer " + _options.ApiKey;
            }

            if (_options.ExtraHeaders != null)
            {
                foreach (var header in _options.ExtraHeaders)
                {
                    if (string.IsNullOrWhiteSpace(header.Key))
                    {
                        continue;
                    }

                    if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        request.ContentType = header.Value;
                        continue;
                    }

                    request.Headers[header.Key] = header.Value;
                }
            }

            var bodyBytes = Encoding.UTF8.GetBytes(body);
            request.ContentLength = bodyBytes.Length;
            using (var stream = await request.GetRequestStreamAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
            }

            using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
            using (var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var responseText = await reader.ReadToEndAsync().ConfigureAwait(false);
                // 标准路径：choices[0].message.content
                var path = string.IsNullOrWhiteSpace(_options.ResponseJsonPath)
                    ? "choices[0].message.content"
                    : _options.ResponseJsonPath;
                var extracted = JsonPathExtractor.ExtractString(responseText, path) ?? string.Empty;
                return extracted.Trim().Trim('"');
            }
        }
    }

    public sealed class OpenAiCompatibleOptions
    {
        /// <summary>完整 URL，例如 https://api.openai.com/v1/chat/completions</summary>
        public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";

        /// <summary>API Key；切勿提交到仓库。</summary>
        public string ApiKey { get; set; } = string.Empty;

        public string Model { get; set; } = "gpt-4o-mini";
        public float Temperature { get; set; } = 0.2f;
        public int TimeoutMs { get; set; } = 30000;

        public string SystemPrompt { get; set; } =
            "You are a translator. Translate the user message into {{target_lang}}. Reply with only the translation, no quotes or explanation.";

        public string UserPromptTemplate { get; set; } = "{{text}}";
        public string ResponseJsonPath { get; set; } = "choices[0].message.content";
        public Dictionary<string, string> ExtraHeaders { get; set; } = new Dictionary<string, string>();
    }
}
