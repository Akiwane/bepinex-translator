using System;
using BepInExTranslator.Core.Backends;

namespace BepInExTranslator
{
    /// <summary>根据契约配置创建翻译后端（HttpTemplate | LlmOpenAiCompatible）。</summary>
    public static class BackendFactory
    {
        public static ITranslationBackend Create(PluginSettings settings)
        {
            var type = (settings.BackendType.Value ?? string.Empty).Trim();
            var timeout = Math.Max(1000, settings.TimeoutMs.Value);

            if (type.Equals("LlmOpenAiCompatible", StringComparison.OrdinalIgnoreCase)
                || type.Equals("OpenAiCompatible", StringComparison.OrdinalIgnoreCase)
                || type.Equals("LLM", StringComparison.OrdinalIgnoreCase))
            {
                var baseUrl = (settings.BaseUrl.Value ?? string.Empty).TrimEnd('/');
                var path = settings.ChatCompletionsPath.Value ?? "/chat/completions";
                if (!path.StartsWith("/", StringComparison.Ordinal))
                {
                    path = "/" + path;
                }

                // YOUR_API_KEY 占位视为未配置
                var apiKey = settings.ApiKey.Value ?? string.Empty;
                if (string.Equals(apiKey, "YOUR_API_KEY", StringComparison.Ordinal))
                {
                    apiKey = string.Empty;
                }

                return new OpenAiCompatibleBackend(new OpenAiCompatibleOptions
                {
                    Endpoint = baseUrl + path,
                    ApiKey = apiKey,
                    Model = settings.Model.Value,
                    SystemPrompt = settings.SystemPrompt.Value,
                    TimeoutMs = timeout,
                });
            }

            // 默认 HttpTemplate
            return new HttpJsonTemplateBackend(new HttpJsonTemplateOptions
            {
                Url = settings.EndpointUrl.Value,
                Method = "POST",
                BodyTemplate = settings.BodyTemplate.Value,
                ResponseJsonPath = settings.ResponseTranslationPath.Value,
                TimeoutMs = timeout,
                Headers = settings.ParseHeadersJson(),
            });
        }
    }
}
