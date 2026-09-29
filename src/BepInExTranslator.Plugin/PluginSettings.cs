using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace BepInExTranslator
{
    /// <summary>
    /// BepInEx ConfigFile 绑定。密钥仅存本地 cfg，切勿提交仓库。
    /// </summary>
    public sealed class PluginSettings
    {
        public ConfigEntry<string> TargetLanguage { get; private set; } = null!;
        public ConfigEntry<string> BackendType { get; private set; } = null!;
        public ConfigEntry<int> TimeoutMs { get; private set; } = null!;
        public ConfigEntry<string> CacheFilePath { get; private set; } = null!;

        // HTTP JSON 模板
        public ConfigEntry<string> HttpUrl { get; private set; } = null!;
        public ConfigEntry<string> HttpMethod { get; private set; } = null!;
        public ConfigEntry<string> HttpBodyTemplate { get; private set; } = null!;
        public ConfigEntry<string> HttpResponseJsonPath { get; private set; } = null!;
        public ConfigEntry<string> HttpHeaders { get; private set; } = null!;

        // OpenAI 兼容
        public ConfigEntry<string> OpenAiEndpoint { get; private set; } = null!;
        public ConfigEntry<string> OpenAiApiKey { get; private set; } = null!;
        public ConfigEntry<string> OpenAiModel { get; private set; } = null!;
        public ConfigEntry<string> OpenAiSystemPrompt { get; private set; } = null!;

        // 布局 / 字体
        public ConfigEntry<bool> AutoResizeFont { get; private set; } = null!;
        public ConfigEntry<float> MinFontScale { get; private set; } = null!;
        public ConfigEntry<float> MinFontSize { get; private set; } = null!;
        public ConfigEntry<string> FontSource { get; private set; } = null!;
        public ConfigEntry<string> SystemFontName { get; private set; } = null!;
        public ConfigEntry<string> CustomFontPath { get; private set; } = null!;

        public ConfigEntry<bool> EnableUgui { get; private set; } = null!;
        public ConfigEntry<bool> EnableTextMeshPro { get; private set; } = null!;
        public ConfigEntry<bool> EnableTextMesh { get; private set; } = null!;

        public static PluginSettings Bind(ConfigFile config)
        {
            var s = new PluginSettings();

            // —— 通用 ——
            s.TargetLanguage = config.Bind(
                "General",
                "TargetLanguage",
                "zh-CN",
                "目标语言（BCP-47），默认 zh-CN");

            s.BackendType = config.Bind(
                "General",
                "BackendType",
                "HttpJsonTemplate",
                "后端类型：HttpJsonTemplate | OpenAiCompatible");

            s.TimeoutMs = config.Bind(
                "General",
                "TimeoutMs",
                20000,
                "HTTP 请求超时（毫秒）");

            s.CacheFilePath = config.Bind(
                "General",
                "CacheFilePath",
                "",
                "翻译产物 JSON 路径。留空= BepInEx/plugins/BepInExTranslator/translations.json；相对路径相对 BepInEx 根目录");

            // —— HTTP 模板 ——
            s.HttpUrl = config.Bind(
                "HttpJsonTemplate",
                "Url",
                "https://example.invalid/translate",
                "翻译 API URL，可含 {{text}} / {{target_lang}} 占位符");

            s.HttpMethod = config.Bind(
                "HttpJsonTemplate",
                "Method",
                "POST",
                "HTTP 方法");

            s.HttpBodyTemplate = config.Bind(
                "HttpJsonTemplate",
                "BodyTemplate",
                "{\"text\":\"{{text}}\",\"target\":\"{{target_lang}}\"}",
                "JSON body 模板；{{text}} 会自动做 JSON 转义");

            s.HttpResponseJsonPath = config.Bind(
                "HttpJsonTemplate",
                "ResponseJsonPath",
                "translation",
                "响应中译文的点分路径，如 translation 或 data.translatedText");

            s.HttpHeaders = config.Bind(
                "HttpJsonTemplate",
                "Headers",
                "",
                "额外请求头，每行 Key: Value；值可含占位符。勿把密钥提交到 git");

            // —— OpenAI 兼容 ——
            s.OpenAiEndpoint = config.Bind(
                "OpenAiCompatible",
                "Endpoint",
                "https://api.openai.com/v1/chat/completions",
                "Chat Completions 完整 URL");

            s.OpenAiApiKey = config.Bind(
                "OpenAiCompatible",
                "ApiKey",
                "",
                "API Key（仅本地配置，切勿提交仓库）");

            s.OpenAiModel = config.Bind(
                "OpenAiCompatible",
                "Model",
                "gpt-4o-mini",
                "模型名");

            s.OpenAiSystemPrompt = config.Bind(
                "OpenAiCompatible",
                "SystemPrompt",
                "You are a translator. Translate the user message into {{target_lang}}. Reply with only the translation, no quotes or explanation.",
                "系统提示词模板");

            // —— 布局 / 字体 ——
            s.AutoResizeFont = config.Bind(
                "Layout",
                "AutoResizeFont",
                true,
                "译文更长时自动缩小字号并尝试开启换行");

            s.MinFontScale = config.Bind(
                "Layout",
                "MinFontScale",
                0.5f,
                "相对原字号的最小缩放比例");

            s.MinFontSize = config.Bind(
                "Layout",
                "MinFontSize",
                8f,
                "最小字号绝对值");

            s.FontSource = config.Bind(
                "Font",
                "Source",
                "Default",
                "字体来源：Default | BuiltIn | System | CustomFile");

            s.SystemFontName = config.Bind(
                "Font",
                "SystemFontName",
                "Arial",
                "Source=System 时的系统字体名（如 Microsoft YaHei）");

            s.CustomFontPath = config.Bind(
                "Font",
                "CustomFontPath",
                "",
                "Source=CustomFile 时的字体文件路径或已安装字体名。Unity 版本差异见 README");

            s.EnableUgui = config.Bind("Hooks", "EnableUguiText", true, "Hook UnityEngine.UI.Text");
            s.EnableTextMeshPro = config.Bind("Hooks", "EnableTextMeshPro", true, "Hook TextMeshPro / TMP_Text（反射软依赖）");
            s.EnableTextMesh = config.Bind("Hooks", "EnableTextMesh", true, "Hook 旧版 TextMesh");

            return s;
        }

        /// <summary>解析 Headers 配置（每行 Key: Value）。</summary>
        public Dictionary<string, string> ParseHeaders()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var raw = HttpHeaders.Value;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return map;
            }

            var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var idx = line.IndexOf(':');
                if (idx <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, idx).Trim();
                var value = line.Substring(idx + 1).Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    map[key] = value;
                }
            }

            return map;
        }
    }
}
