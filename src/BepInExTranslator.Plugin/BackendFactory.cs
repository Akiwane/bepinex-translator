using System;
using BepInExTranslator.Core.Backends;

namespace BepInExTranslator
{
    /// <summary>根据配置创建翻译后端。</summary>
    public static class BackendFactory
    {
        public static ITranslationBackend Create(PluginSettings settings)
        {
            var type = (settings.BackendType.Value ?? string.Empty).Trim();
            var timeout = Math.Max(1000, settings.TimeoutMs.Value);

            if (type.Equals("OpenAiCompatible", StringComparison.OrdinalIgnoreCase)
                || type.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
                || type.Equals("LLM", StringComparison.OrdinalIgnoreCase))
            {
                return new OpenAiCompatibleBackend(new OpenAiCompatibleOptions
                {
                    Endpoint = settings.OpenAiEndpoint.Value,
                    ApiKey = settings.OpenAiApiKey.Value,
                    Model = settings.OpenAiModel.Value,
                    SystemPrompt = settings.OpenAiSystemPrompt.Value,
                    TimeoutMs = timeout,
                });
            }

            // 默认：通用 HTTP JSON 模板
            return new HttpJsonTemplateBackend(new HttpJsonTemplateOptions
            {
                Url = settings.HttpUrl.Value,
                Method = settings.HttpMethod.Value,
                BodyTemplate = settings.HttpBodyTemplate.Value,
                ResponseJsonPath = settings.HttpResponseJsonPath.Value,
                TimeoutMs = timeout,
                Headers = settings.ParseHeaders(),
            });
        }
    }
}
