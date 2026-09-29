using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BepInExTranslator.Core
{
    /// <summary>
    /// 轻量模板填充：支持 {{name}} 与 {name} 占位符。
    /// 专用于 HTTP JSON 体 / URL / 头中的 text、target_lang 等字段。
    /// </summary>
    public static class TemplateFiller
    {
        /// <summary>
        /// 用 values 替换 template 中的占位符。缺失的键替换为空字符串。
        /// 对 JSON 字符串上下文可先调用 <see cref="EscapeJson"/> 再填入。
        /// </summary>
        public static string Fill(string? template, IReadOnlyDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            if (values == null || values.Count == 0)
            {
                return template!;
            }

            var result = template!;

            // 先替换 {{key}}，再替换 {key}，避免双重花括号被拆坏
            foreach (var pair in values)
            {
                var value = pair.Value ?? string.Empty;
                result = ReplaceAll(result, "{{" + pair.Key + "}}", value);
            }

            foreach (var pair in values)
            {
                var value = pair.Value ?? string.Empty;
                result = ReplaceAll(result, "{" + pair.Key + "}", value);
            }

            return result;
        }

        /// <summary>构建翻译请求常用占位符字典。</summary>
        public static Dictionary<string, string> BuildTranslationPlaceholders(
            string text,
            string targetLang,
            string? sourceLang = null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["text"] = text ?? string.Empty,
                ["source"] = text ?? string.Empty,
                ["target_lang"] = targetLang ?? string.Empty,
                ["targetLang"] = targetLang ?? string.Empty,
                ["lang"] = targetLang ?? string.Empty,
                ["source_lang"] = sourceLang ?? string.Empty,
                ["sourceLang"] = sourceLang ?? string.Empty,
            };
        }

        /// <summary>JSON 字符串转义（不含外层引号）。</summary>
        public static string EscapeJson(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length + 8);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (ch < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(ch);
                        }

                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 填充模板，并对指定键的值做 JSON 转义（适合嵌入 JSON body）。
        /// </summary>
        public static string FillJsonSafe(
            string? template,
            IReadOnlyDictionary<string, string> values,
            params string[] jsonEscapeKeys)
        {
            if (values == null)
            {
                return Fill(template, new Dictionary<string, string>());
            }

            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values)
            {
                copy[pair.Key] = pair.Value ?? string.Empty;
            }

            if (jsonEscapeKeys != null)
            {
                foreach (var key in jsonEscapeKeys)
                {
                    if (copy.TryGetValue(key, out var raw))
                    {
                        copy[key] = EscapeJson(raw);
                    }
                }
            }

            return Fill(template, copy);
        }

        private static string ReplaceAll(string input, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(oldValue) || input.IndexOf(oldValue, StringComparison.Ordinal) < 0)
            {
                return input;
            }

            return input.Replace(oldValue, newValue);
        }
    }
}
