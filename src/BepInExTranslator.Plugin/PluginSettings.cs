using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace BepInExTranslator
{
    /// <summary>
    /// BepInEx ConfigFile 绑定。字段名与 config/Translator.cfg.example 契约一致。
    /// 密钥仅存本地 cfg，切勿提交仓库。
    /// </summary>
    public sealed class PluginSettings
    {
        // —— General ——
        public ConfigEntry<string> TargetLanguage { get; private set; } = null!;
        public ConfigEntry<string> ProductJsonPath { get; private set; } = null!;
        public ConfigEntry<int> TimeoutMs { get; private set; } = null!;
        public ConfigEntry<string> BackendType { get; private set; } = null!;

        // —— HttpTemplate ——
        public ConfigEntry<string> EndpointUrl { get; private set; } = null!;
        public ConfigEntry<string> HeadersJson { get; private set; } = null!;
        public ConfigEntry<string> BodyTemplate { get; private set; } = null!;
        public ConfigEntry<string> ResponseTranslationPath { get; private set; } = null!;

        // —— LlmOpenAiCompatible ——
        public ConfigEntry<string> BaseUrl { get; private set; } = null!;
        public ConfigEntry<string> Model { get; private set; } = null!;
        public ConfigEntry<string> ApiKey { get; private set; } = null!;
        public ConfigEntry<string> SystemPrompt { get; private set; } = null!;
        public ConfigEntry<string> ChatCompletionsPath { get; private set; } = null!;

        // —— Layout / Font ——
        public ConfigEntry<bool> AutoShrinkFontSize { get; private set; } = null!;
        public ConfigEntry<string> FontSourceType { get; private set; } = null!;
        public ConfigEntry<string> FontPath { get; private set; } = null!;

        // —— Hooks（实现细节，契约未强制；默认全开）——
        public ConfigEntry<bool> EnableUgui { get; private set; } = null!;
        public ConfigEntry<bool> EnableTextMeshPro { get; private set; } = null!;
        public ConfigEntry<bool> EnableTextMesh { get; private set; } = null!;

        // 布局辅助（契约仅要求 AutoShrinkFontSize；以下为安全下限）
        public ConfigEntry<float> MinFontScale { get; private set; } = null!;
        public ConfigEntry<float> MinFontSize { get; private set; } = null!;

        public static PluginSettings Bind(ConfigFile config)
        {
            var s = new PluginSettings();

            // General — 与 Translator.cfg.example 同名
            s.TargetLanguage = config.Bind(
                "General",
                "TargetLanguage",
                "zh-CN",
                "目标语言（BCP-47），默认 zh-CN");

            s.ProductJsonPath = config.Bind(
                "General",
                "ProductJsonPath",
                "BepInEx/plugins/Translator/translations.json",
                "翻译产物 JSON 路径（绝对路径，或相对游戏/BepInEx 根目录）");

            s.TimeoutMs = config.Bind(
                "General",
                "TimeoutMs",
                15000,
                "HTTP / LLM 请求超时（毫秒）");

            s.BackendType = config.Bind(
                "General",
                "BackendType",
                "HttpTemplate",
                "后端类型：HttpTemplate | LlmOpenAiCompatible");

            // HttpTemplate
            s.EndpointUrl = config.Bind(
                "HttpTemplate",
                "EndpointUrl",
                "https://example.com/api/translate",
                "完整请求 URL；占位符 {source} {targetLanguage} {hash}");

            s.HeadersJson = config.Bind(
                "HttpTemplate",
                "HeadersJson",
                "{\"Authorization\":\"Bearer YOUR_API_KEY\",\"Content-Type\":\"application/json\"}",
                "额外 HTTP 头（JSON 对象）；值可含占位符。勿提交真实密钥");

            s.BodyTemplate = config.Bind(
                "HttpTemplate",
                "BodyTemplate",
                "{\"q\":\"{source}\",\"target\":\"{targetLanguage}\",\"id\":\"{hash}\"}",
                "请求体模板；占位符 {source} {targetLanguage} {hash}");

            s.ResponseTranslationPath = config.Bind(
                "HttpTemplate",
                "ResponseTranslationPath",
                "data.translation",
                "响应中译文的 JSON 路径，如 data.translation");

            // LlmOpenAiCompatible
            s.BaseUrl = config.Bind(
                "LlmOpenAiCompatible",
                "BaseUrl",
                "https://api.openai.com/v1",
                "OpenAI 兼容 API Base URL");

            s.Model = config.Bind(
                "LlmOpenAiCompatible",
                "Model",
                "gpt-4o-mini",
                "模型 ID");

            s.ApiKey = config.Bind(
                "LlmOpenAiCompatible",
                "ApiKey",
                "YOUR_API_KEY",
                "API Key（仅本地配置，切勿提交仓库）");

            s.SystemPrompt = config.Bind(
                "LlmOpenAiCompatible",
                "SystemPrompt",
                "You are a game UI translator. Translate faithfully; keep placeholders and markup intact.",
                "系统提示词");

            s.ChatCompletionsPath = config.Bind(
                "LlmOpenAiCompatible",
                "ChatCompletionsPath",
                "/chat/completions",
                "相对 BaseUrl 的 Chat Completions 路径");

            // Layout
            s.AutoShrinkFontSize = config.Bind(
                "Layout",
                "AutoShrinkFontSize",
                true,
                "长译文在容器内自动缩小字号并尝试开启换行");

            s.MinFontScale = config.Bind(
                "Layout",
                "MinFontScale",
                0.5f,
                "相对原字号的最小缩放比例（实现细节）");

            s.MinFontSize = config.Bind(
                "Layout",
                "MinFontSize",
                8f,
                "最小字号绝对值（实现细节）");

            // Font
            s.FontSourceType = config.Bind(
                "Font",
                "FontSourceType",
                "System",
                "字体来源：Unity | System | CustomFile");

            s.FontPath = config.Bind(
                "Font",
                "FontPath",
                "Microsoft YaHei",
                "含义随 FontSourceType 变化，见 docs/fonts.md");

            // Hooks
            s.EnableUgui = config.Bind("Hooks", "EnableUguiText", true, "Hook UnityEngine.UI.Text");
            s.EnableTextMeshPro = config.Bind("Hooks", "EnableTextMeshPro", true, "Hook TextMeshPro / TMP_Text");
            s.EnableTextMesh = config.Bind("Hooks", "EnableTextMesh", true, "Hook 旧版 TextMesh");

            return s;
        }

        /// <summary>解析 HeadersJson（简单 JSON 对象字符串）。</summary>
        public Dictionary<string, string> ParseHeadersJson()
        {
            return SimpleHeaderJson.ParseObject(HeadersJson.Value);
        }
    }

    /// <summary>解析 {"Key":"Value",...} 形态的简易 JSON 对象（仅字符串值）。</summary>
    internal static class SimpleHeaderJson
    {
        public static Dictionary<string, string> ParseObject(string? json)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json))
            {
                return map;
            }

            var s = json.Trim();
            if (s.Length < 2 || s[0] != '{')
            {
                return map;
            }

            var i = 1;
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}')
                {
                    break;
                }

                if (i >= s.Length || s[i] != '"')
                {
                    break;
                }

                var key = ReadString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':')
                {
                    break;
                }

                i++;
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"')
                {
                    break;
                }

                var value = ReadString(s, ref i);
                if (!string.IsNullOrEmpty(key))
                {
                    map[key] = value;
                }

                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                }
            }

            return map;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }

        private static string ReadString(string s, ref int i)
        {
            i++; // opening quote
            var start = i;
            var sb = new System.Text.StringBuilder();
            while (i < s.Length)
            {
                var ch = s[i++];
                if (ch == '\\' && i < s.Length)
                {
                    sb.Append(s[i++]);
                    continue;
                }

                if (ch == '"')
                {
                    return sb.ToString();
                }

                sb.Append(ch);
            }

            return s.Substring(start);
        }
    }
}
