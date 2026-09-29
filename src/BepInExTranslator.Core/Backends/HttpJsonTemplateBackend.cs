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
    /// 通用 HTTP JSON 模板后端。
    /// URL / Headers / Body 均可含 {{text}}、{{target_lang}} 等占位符；
    /// 从响应 JSON 中按点分路径提取译文（如 translation 或 data.text）。
    /// </summary>
    public sealed class HttpJsonTemplateBackend : ITranslationBackend
    {
        private readonly HttpJsonTemplateOptions _options;

        public string Name => "HttpTemplate";

        public HttpJsonTemplateBackend(HttpJsonTemplateOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(_options.Url))
            {
                throw new ArgumentException("HTTP template URL is required.", nameof(options));
            }
        }

        public async Task<string> TranslateAsync(string source, string targetLang, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.Empty;
            }

            var placeholders = TemplateFiller.BuildTranslationPlaceholders(source, targetLang);
            // Body 内 source/text 需 JSON 转义
            var body = TemplateFiller.FillJsonSafe(
                _options.BodyTemplate,
                placeholders,
                "text", "source");

            // URL / query / path：占位符值 EscapeDataString，防止注入与破环
            var url = HttpRequestHardening.FillUrl(_options.Url, placeholders);
            var method = string.IsNullOrWhiteSpace(_options.Method) ? "POST" : _options.Method.Trim().ToUpperInvariant();

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = _options.TimeoutMs;
            request.ReadWriteTimeout = _options.TimeoutMs;
            request.ContentType = string.IsNullOrWhiteSpace(_options.ContentType)
                ? "application/json; charset=utf-8"
                : _options.ContentType;

            // 应用可配置请求头：先填充占位符，再消毒（去 CR/LF、限长）
            if (_options.Headers != null)
            {
                foreach (var header in _options.Headers)
                {
                    var filledValue = TemplateFiller.Fill(header.Value, placeholders);
                    if (!HttpRequestHardening.TrySanitizeHeader(header.Key, filledValue, out var safeName, out var safeValue))
                    {
                        continue;
                    }

                    if (string.Equals(safeName, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        request.ContentType = safeValue;
                        continue;
                    }

                    request.Headers[safeName] = safeValue;
                }
            }

            if (method != "GET" && method != "HEAD")
            {
                var bodyBytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
                request.ContentLength = bodyBytes.Length;
                using (var stream = await request.GetRequestStreamAsync().ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
                }
            }

            using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
            using (var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var responseText = await reader.ReadToEndAsync().ConfigureAwait(false);
                return JsonPathExtractor.ExtractString(responseText, _options.ResponseJsonPath) ?? string.Empty;
            }
        }
    }

    public sealed class HttpJsonTemplateOptions
    {
        public string Url { get; set; } = string.Empty;
        public string Method { get; set; } = "POST";
        public string ContentType { get; set; } = "application/json; charset=utf-8";
        public string BodyTemplate { get; set; } =
            "{\"q\":\"{source}\",\"target\":\"{targetLanguage}\",\"id\":\"{hash}\"}";
        /// <summary>点分路径，如 data.translation 或 choices.0.message.content。</summary>
        public string ResponseJsonPath { get; set; } = "data.translation";
        public int TimeoutMs { get; set; } = 15000;
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// 从 JSON 文本按简单点分路径取字符串值（支持对象嵌套与一小段数组 [0]）。
    /// </summary>
    public static class JsonPathExtractor
    {
        public static string? ExtractString(string? json, string? path)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                // 无路径时：尝试常见字段，否则整段去引号
                return TryCommonFields(json) ?? json.Trim().Trim('"');
            }

            var segments = path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            var current = json.Trim();

            foreach (var rawSegment in segments)
            {
                var segment = rawSegment.Trim();
                var index = -1;
                var name = segment;

            // 支持 field[0] 与 field.0 两种下标写法（契约示例用 choices.0.message.content）
            var bracket = segment.IndexOf('[');
            if (bracket >= 0 && segment.EndsWith("]", StringComparison.Ordinal))
            {
                name = segment.Substring(0, bracket);
                var idxText = segment.Substring(bracket + 1, segment.Length - bracket - 2);
                if (!int.TryParse(idxText, out index))
                {
                    return null;
                }
            }
            else if (int.TryParse(segment, out var dottedIndex))
            {
                // 纯数字段：当作数组下标
                name = string.Empty;
                index = dottedIndex;
            }

                if (!string.IsNullOrEmpty(name))
                {
                    if (!TryGetObjectProperty(current, name, out current) || current == null)
                    {
                        return null;
                    }
                }

                if (index >= 0)
                {
                    if (!TryGetArrayElement(current, index, out current) || current == null)
                    {
                        return null;
                    }
                }
            }

            return Unquote(current);
        }

        private static string? TryCommonFields(string json)
        {
            foreach (var key in new[] { "translation", "translatedText", "text", "result", "content" })
            {
                if (TryGetObjectProperty(json, key, out var value) && value != null)
                {
                    return Unquote(value);
                }
            }

            return null;
        }

        private static bool TryGetObjectProperty(string json, string name, out string? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            // 朴素扫描："name"\s*:\s*<value>
            var needle = "\"" + name + "\"";
            var idx = 0;
            while (true)
            {
                idx = json.IndexOf(needle, idx, StringComparison.Ordinal);
                if (idx < 0)
                {
                    return false;
                }

                var after = idx + needle.Length;
                while (after < json.Length && char.IsWhiteSpace(json[after]))
                {
                    after++;
                }

                if (after < json.Length && json[after] == ':')
                {
                    after++;
                    value = ReadJsonValue(json, ref after);
                    return value != null;
                }

                idx = after;
            }
        }

        private static bool TryGetArrayElement(string json, int index, out string? value)
        {
            value = null;
            var i = 0;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
            {
                i++;
            }

            if (i >= json.Length || json[i] != '[')
            {
                return false;
            }

            i++;
            var current = 0;
            while (i < json.Length)
            {
                while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ','))
                {
                    i++;
                }

                if (i < json.Length && json[i] == ']')
                {
                    return false;
                }

                var element = ReadJsonValue(json, ref i);
                if (current == index)
                {
                    value = element;
                    return element != null;
                }

                current++;
            }

            return false;
        }

        private static string? ReadJsonValue(string json, ref int i)
        {
            while (i < json.Length && char.IsWhiteSpace(json[i]))
            {
                i++;
            }

            if (i >= json.Length)
            {
                return null;
            }

            var ch = json[i];
            if (ch == '"')
            {
                return ReadQuoted(json, ref i);
            }

            if (ch == '{' || ch == '[')
            {
                return ReadBalanced(json, ref i);
            }

            // 数字 / true / false / null
            var start = i;
            while (i < json.Length && !",}]".Contains(json[i].ToString()) && !char.IsWhiteSpace(json[i]))
            {
                i++;
            }

            return json.Substring(start, i - start);
        }

        private static string ReadQuoted(string json, ref int i)
        {
            var start = i;
            i++; // skip opening quote
            while (i < json.Length)
            {
                var ch = json[i++];
                if (ch == '\\' && i < json.Length)
                {
                    i++;
                    continue;
                }

                if (ch == '"')
                {
                    return json.Substring(start, i - start);
                }
            }

            return json.Substring(start);
        }

        private static string ReadBalanced(string json, ref int i)
        {
            var open = json[i];
            var close = open == '{' ? '}' : ']';
            var start = i;
            var depth = 0;
            var inString = false;
            for (; i < json.Length; i++)
            {
                var ch = json[i];
                if (inString)
                {
                    if (ch == '\\' && i + 1 < json.Length)
                    {
                        i++;
                        continue;
                    }

                    if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch == open)
                {
                    depth++;
                }
                else if (ch == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        i++;
                        return json.Substring(start, i - start);
                    }
                }
            }

            return json.Substring(start);
        }

        private static string Unquote(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            return DecodeJsonString(raw.Trim());
        }

        private static string DecodeJsonString(string s)
        {
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                s = s.Substring(1, s.Length - 2);
            }
            else
            {
                return s;
            }

            var sb = new StringBuilder(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];
                if (ch != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(ch);
                    continue;
                }

                var next = s[++i];
                switch (next)
                {
                    case '"':
                    case '\\':
                    case '/':
                        sb.Append(next);
                        break;
                    case 'b':
                        sb.Append('\b');
                        break;
                    case 'f':
                        sb.Append('\f');
                        break;
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    case 'u':
                        if (i + 4 < s.Length)
                        {
                            var hex = s.Substring(i + 1, 4);
                            i += 4;
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
                            {
                                sb.Append((char)code);
                            }
                        }

                        break;
                    default:
                        sb.Append(next);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
