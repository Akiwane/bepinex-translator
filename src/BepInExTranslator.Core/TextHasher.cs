using System;
using System.Security.Cryptography;
using System.Text;

namespace BepInExTranslator.Core
{
    /// <summary>
    /// 为原文生成稳定、可复现的哈希键。
    /// 使用 SHA-256（UTF-8），输出小写十六进制；默认取前 16 字节（32 hex 字符）以兼顾可读与碰撞风险。
    /// </summary>
    public static class TextHasher
    {
        public const int DefaultHexLength = 32;

        /// <summary>
        /// 计算原文哈希。空/null 原文返回空字符串。
        /// </summary>
        public static string ComputeHash(string? source, int hexLength = DefaultHexLength)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.Empty;
            }

            if (hexLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(hexLength));
            }

            // 归一化换行，避免 Windows/Unix 差异导致同一文案哈希不一致
            var normalized = source.Replace("\r\n", "\n").Replace('\r', '\n');
            var bytes = Encoding.UTF8.GetBytes(normalized);

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var hex = ToHex(hash);
                var take = Math.Min(hexLength, hex.Length);
                return hex.Substring(0, take);
            }
        }

        private static string ToHex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            for (var i = 0; i < data.Length; i++)
            {
                sb.Append(data[i].ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
