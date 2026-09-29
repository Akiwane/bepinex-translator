using System;

namespace BepInExTranslator.Core
{
    /// <summary>
    /// 长译文字号/换行调整助手（纯逻辑，不依赖 Unity）。
    /// 按字符长度比例在容器约束内缩小字号，并给出是否应开启换行的建议。
    /// </summary>
    public static class FontSizeAdjuster
    {
        public const float DefaultMinScale = 0.5f;
        public const float DefaultMinFontSize = 8f;

        /// <summary>
        /// 根据原文与译文长度，计算建议字号。
        /// 译文不长于原文时保持原字号；更长则按比例缩小，但不低于 minFontSize / minScale。
        /// </summary>
        public static float ComputeAdjustedFontSize(
            float originalFontSize,
            string? sourceText,
            string? translatedText,
            float minScale = DefaultMinScale,
            float minFontSize = DefaultMinFontSize)
        {
            if (originalFontSize <= 0f)
            {
                return originalFontSize;
            }

            var sourceLen = MeasureLength(sourceText);
            var translatedLen = MeasureLength(translatedText);

            if (translatedLen <= 0 || sourceLen <= 0 || translatedLen <= sourceLen)
            {
                return originalFontSize;
            }

            // 长度比的倒数作为缩放；限制在 [minScale, 1]
            var scale = (float)sourceLen / translatedLen;
            if (scale < minScale)
            {
                scale = minScale;
            }

            if (scale > 1f)
            {
                scale = 1f;
            }

            var adjusted = originalFontSize * scale;
            if (adjusted < minFontSize)
            {
                adjusted = Math.Min(originalFontSize, minFontSize);
            }

            return adjusted;
        }

        /// <summary>
        /// 译文显著变长时建议开启换行（UGUI/TMP 的 horizontalOverflow / enableWordWrapping）。
        /// </summary>
        public static bool ShouldEnableWrap(
            string? sourceText,
            string? translatedText,
            float lengthRatioThreshold = 1.15f)
        {
            var sourceLen = MeasureLength(sourceText);
            var translatedLen = MeasureLength(translatedText);
            if (sourceLen <= 0 || translatedLen <= 0)
            {
                return false;
            }

            return (float)translatedLen / sourceLen >= lengthRatioThreshold;
        }

        /// <summary>
        /// 估算显示长度：CJK 宽字符计 2，其余计 1（粗略，足够做字号启发）。
        /// </summary>
        public static int MeasureLength(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var total = 0;
            foreach (var ch in text)
            {
                total += IsWideChar(ch) ? 2 : 1;
            }

            return total;
        }

        private static bool IsWideChar(char ch)
        {
            // 常见 CJK / 全角区间
            return ch >= 0x1100 && (
                ch <= 0x115F ||
                ch == 0x2329 ||
                ch == 0x232A ||
                (ch >= 0x2E80 && ch <= 0xA4CF) ||
                (ch >= 0xAC00 && ch <= 0xD7A3) ||
                (ch >= 0xF900 && ch <= 0xFAFF) ||
                (ch >= 0xFE10 && ch <= 0xFE6F) ||
                (ch >= 0xFF00 && ch <= 0xFF60) ||
                (ch >= 0xFFE0 && ch <= 0xFFE6));
        }
    }
}
