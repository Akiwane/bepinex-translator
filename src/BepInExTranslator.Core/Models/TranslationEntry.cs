namespace BepInExTranslator.Core.Models
{
    /// <summary>
    /// 一条可人工精翻的翻译产物记录。
    /// JSON 数组元素形态：{ "hash", "source", "translation" }。
    /// </summary>
    public sealed class TranslationEntry
    {
        /// <summary>原文的稳定哈希（程序主键）。</summary>
        public string Hash { get; set; } = string.Empty;

        /// <summary>原始文本（人工可读）。</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>译文；非空则视为已译，跳过 API。</summary>
        public string Translation { get; set; } = string.Empty;

        /// <summary>是否已有可用译文。</summary>
        public bool HasTranslation => !string.IsNullOrEmpty(Translation);
    }
}
