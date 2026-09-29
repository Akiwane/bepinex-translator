using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInExTranslator.Core.Models;

namespace BepInExTranslator.Core
{
    /// <summary>
    /// 翻译产物缓存：内存字典 + 可读 JSON 数组文件。
    /// 格式：[{ "hash": "...", "source": "...", "translation": "..." }, ...]
    /// 查找按 hash；translation 非空则视为已译，调用方应跳过 API。
    /// </summary>
    public sealed class TranslationCache
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, TranslationEntry> _byHash =
            new Dictionary<string, TranslationEntry>(StringComparer.OrdinalIgnoreCase);

        private bool _dirty;

        public string FilePath { get; }
        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _byHash.Count;
                }
            }
        }

        public bool IsDirty
        {
            get
            {
                lock (_sync)
                {
                    return _dirty;
                }
            }
        }

        public TranslationCache(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Cache file path is required.", nameof(filePath));
            }

            FilePath = filePath;
        }

        /// <summary>从磁盘加载；文件不存在则空缓存。</summary>
        public void Load()
        {
            lock (_sync)
            {
                _byHash.Clear();
                _dirty = false;

                if (!File.Exists(FilePath))
                {
                    return;
                }

                var json = File.ReadAllText(FilePath, Encoding.UTF8);
                var entries = SimpleJson.ParseEntryArray(json);
                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.Hash))
                    {
                        entry.Hash = TextHasher.ComputeHash(entry.Source);
                    }

                    if (string.IsNullOrEmpty(entry.Hash))
                    {
                        continue;
                    }

                    _byHash[entry.Hash] = entry;
                }
            }
        }

        /// <summary>按原文查找；返回条目（可能 translation 为空）。</summary>
        public bool TryGet(string? source, out TranslationEntry? entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(source))
            {
                return false;
            }

            var hash = TextHasher.ComputeHash(source);
            lock (_sync)
            {
                if (_byHash.TryGetValue(hash, out var found))
                {
                    entry = found;
                    return true;
                }
            }

            return false;
        }

        /// <summary>是否已有非空译文（应跳过 API）。</summary>
        public bool TryGetTranslation(string? source, out string translation)
        {
            translation = string.Empty;
            if (!TryGet(source, out var entry) || entry == null || !entry.HasTranslation)
            {
                return false;
            }

            translation = entry.Translation;
            return true;
        }

        /// <summary>写入/更新一条译文，并标记 dirty。</summary>
        public TranslationEntry Upsert(string source, string translation)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var hash = TextHasher.ComputeHash(source);
            lock (_sync)
            {
                if (_byHash.TryGetValue(hash, out var existing))
                {
                    existing.Source = source;
                    existing.Translation = translation ?? string.Empty;
                    _dirty = true;
                    return existing;
                }

                var created = new TranslationEntry
                {
                    Hash = hash,
                    Source = source,
                    Translation = translation ?? string.Empty,
                };
                _byHash[hash] = created;
                _dirty = true;
                return created;
            }
        }

        /// <summary>确保存在空译文占位（便于人工精翻），不覆盖已有非空译文。</summary>
        public void EnsurePlaceholder(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return;
            }

            var hash = TextHasher.ComputeHash(source);
            lock (_sync)
            {
                if (_byHash.ContainsKey(hash))
                {
                    return;
                }

                _byHash[hash] = new TranslationEntry
                {
                    Hash = hash,
                    Source = source,
                    Translation = string.Empty,
                };
                _dirty = true;
            }
        }

        /// <summary>若 dirty 则写回磁盘（可读缩进 JSON）。</summary>
        public void SaveIfDirty()
        {
            lock (_sync)
            {
                if (!_dirty)
                {
                    return;
                }

                SaveUnlocked();
            }
        }

        /// <summary>强制写回。</summary>
        public void Save()
        {
            lock (_sync)
            {
                SaveUnlocked();
            }
        }

        private void SaveUnlocked()
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var list = new List<TranslationEntry>(_byHash.Values);
            list.Sort((a, b) => string.Compare(a.Source, b.Source, StringComparison.Ordinal));

            var json = SimpleJson.SerializeEntryArray(list);
            File.WriteAllText(FilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _dirty = false;
        }
    }

    /// <summary>
    /// 极简 JSON 读写，仅支持本模组产物数组，避免额外依赖。
    /// </summary>
    internal static class SimpleJson
    {
        public static List<TranslationEntry> ParseEntryArray(string json)
        {
            var result = new List<TranslationEntry>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            var i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '[')
            {
                throw new FormatException("Translation cache JSON must be an array.");
            }

            i++;
            SkipWs(json, ref i);

            while (i < json.Length && json[i] != ']')
            {
                var entry = ParseObject(json, ref i);
                result.Add(entry);
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ',')
                {
                    i++;
                    SkipWs(json, ref i);
                }
            }

            return result;
        }

        public static string SerializeEntryArray(IReadOnlyList<TranslationEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (var idx = 0; idx < entries.Count; idx++)
            {
                var e = entries[idx];
                sb.Append("  {");
                sb.AppendLine();
                AppendProp(sb, "hash", e.Hash, trailingComma: true);
                AppendProp(sb, "source", e.Source, trailingComma: true);
                AppendProp(sb, "translation", e.Translation, trailingComma: false);
                sb.Append("  }");
                if (idx < entries.Count - 1)
                {
                    sb.Append(',');
                }

                sb.AppendLine();
            }

            sb.AppendLine("]");
            return sb.ToString();
        }

        private static void AppendProp(StringBuilder sb, string name, string value, bool trailingComma)
        {
            sb.Append("    \"");
            sb.Append(name);
            sb.Append("\": \"");
            sb.Append(TemplateFiller.EscapeJson(value ?? string.Empty));
            sb.Append('"');
            if (trailingComma)
            {
                sb.Append(',');
            }

            sb.AppendLine();
        }

        private static TranslationEntry ParseObject(string json, ref int i)
        {
            SkipWs(json, ref i);
            Expect(json, ref i, '{');
            var entry = new TranslationEntry();

            SkipWs(json, ref i);
            while (i < json.Length && json[i] != '}')
            {
                var key = ParseString(json, ref i);
                SkipWs(json, ref i);
                Expect(json, ref i, ':');
                SkipWs(json, ref i);
                var value = ParseString(json, ref i);

                if (string.Equals(key, "hash", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Hash = value;
                }
                else if (string.Equals(key, "source", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Source = value;
                }
                else if (string.Equals(key, "translation", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Translation = value;
                }

                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ',')
                {
                    i++;
                    SkipWs(json, ref i);
                }
            }

            Expect(json, ref i, '}');
            return entry;
        }

        private static string ParseString(string json, ref int i)
        {
            SkipWs(json, ref i);
            Expect(json, ref i, '"');
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                var ch = json[i++];
                if (ch == '"')
                {
                    return sb.ToString();
                }

                if (ch == '\\')
                {
                    if (i >= json.Length)
                    {
                        throw new FormatException("Unterminated escape in JSON string.");
                    }

                    var esc = json[i++];
                    switch (esc)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            sb.Append(esc);
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
                            if (i + 4 > json.Length)
                            {
                                throw new FormatException("Invalid \\u escape.");
                            }

                            var hex = json.Substring(i, 4);
                            i += 4;
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }

            throw new FormatException("Unterminated JSON string.");
        }

        private static void Expect(string json, ref int i, char expected)
        {
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != expected)
            {
                throw new FormatException("Expected '" + expected + "' in JSON.");
            }

            i++;
        }

        private static void SkipWs(string json, ref int i)
        {
            while (i < json.Length && char.IsWhiteSpace(json[i]))
            {
                i++;
            }
        }
    }
}
