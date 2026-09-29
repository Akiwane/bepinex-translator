using System;
using System.Collections.Generic;
using System.Text;

namespace BepInExTranslator.Core
{
    /// <summary>
    /// HTTP 请求加固：URL 占位符 EscapeDataString；请求头去 CR/LF、限长、拒绝危险值。
    /// </summary>
    public static class HttpRequestHardening
    {
        public const int MaxHeaderNameLength = 256;
        public const int MaxHeaderValueLength = 8192;

        /// <summary>
        /// 用 Uri.EscapeDataString 转义占位符值后填充 URL / query / path 模板。
        /// </summary>
        public static string FillUrl(string? urlTemplate, IReadOnlyDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(urlTemplate))
            {
                return string.Empty;
            }

            if (values == null || values.Count == 0)
            {
                return urlTemplate!;
            }

            var escaped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values)
            {
                escaped[pair.Key] = Uri.EscapeDataString(pair.Value ?? string.Empty);
            }

            return TemplateFiller.Fill(urlTemplate, escaped);
        }

        /// <summary>
        /// 清洗请求头；不安全或超长时返回 false（应跳过该头）。
        /// </summary>
        public static bool TrySanitizeHeader(
            string? name,
            string? value,
            out string safeName,
            out string safeValue)
        {
            safeName = string.Empty;
            safeValue = string.Empty;

            if (string.IsNullOrWhiteSpace(name) || value == null)
            {
                return false;
            }

            // 原始名含 CR/LF → 直接拒绝（疑似 header injection）
            if (ContainsCrLfOrNull(name))
            {
                return false;
            }

            // 值中的 CR/LF 剥离后保留内容；名已保证无 CR/LF
            safeName = StripCrLfAndControls(name).Trim();
            safeValue = StripCrLfAndControls(value).Trim();

            if (safeName.Length == 0 || safeName.Length > MaxHeaderNameLength)
            {
                return false;
            }

            if (safeValue.Length > MaxHeaderValueLength)
            {
                return false;
            }

            // 名称不得含冒号或空白（HTTP 头名基本规则）
            foreach (var ch in safeName)
            {
                if (ch == ':' || char.IsWhiteSpace(ch))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsCrLfOrNull(string input)
        {
            foreach (var ch in input)
            {
                if (ch == '\r' || ch == '\n' || ch == '\0')
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>清洗一组头；丢弃无法消毒的条目。</summary>
        public static Dictionary<string, string> SanitizeHeaders(IEnumerable<KeyValuePair<string, string>>? headers)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers == null)
            {
                return result;
            }

            foreach (var pair in headers)
            {
                if (TrySanitizeHeader(pair.Key, pair.Value, out var name, out var value))
                {
                    result[name] = value;
                }
            }

            return result;
        }

        private static string StripCrLfAndControls(string input)
        {
            var sb = new StringBuilder(input.Length);
            foreach (var ch in input)
            {
                if (ch == '\r' || ch == '\n' || ch == '\0')
                {
                    continue;
                }

                // 其它 C0 控制字符（除 tab）也去掉
                if (ch < 0x20 && ch != '\t')
                {
                    continue;
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// 异步回写过期判定：expectedSource / pending 世代与当前组件文本不一致则丢弃。
    /// </summary>
    public static class StaleWriteGuard
    {
        /// <summary>
        /// 是否应把 translated 写回组件。
        /// pendingExpected：该组件当前登记的期望原文（更新文本时会改写）；与 expectedSource 不一致=过期。
        /// currentText：组件当下文本；须仍为 expectedSource，或已是 translated。
        /// </summary>
        public static bool ShouldApply(
            string expectedSource,
            string translated,
            string? pendingExpected,
            string? currentText)
        {
            if (string.IsNullOrEmpty(expectedSource))
            {
                return false;
            }

            // 世代令牌：组件已发起更新的请求，旧回调作废
            if (pendingExpected != null
                && !string.Equals(pendingExpected, expectedSource, StringComparison.Ordinal))
            {
                return false;
            }

            // 无法读取当前文本时，仍要求 pending 匹配（上面已查）
            if (currentText == null)
            {
                return true;
            }

            // 仍显示原文，或已显示本轮译文 → 可写
            if (string.Equals(currentText, expectedSource, StringComparison.Ordinal)
                || string.Equals(currentText, translated, StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }
    }
}
