using System.IO;
using BepInExTranslator.Core;
using BepInExTranslator.Core.Backends;
using Xunit;

namespace BepInExTranslator.Core.Tests
{
    public class TranslationCacheTests
    {
        [Fact]
        public void RoundTrip_SaveAndLoad_PreservesEntries()
        {
            var path = Path.Combine(Path.GetTempPath(), "bepinex-translator-cache-" + Path.GetRandomFileName() + ".json");
            try
            {
                var cache = new TranslationCache(path);
                cache.Upsert("Hello", "你好");
                cache.Upsert("World", "世界");
                cache.Save();

                var loaded = new TranslationCache(path);
                loaded.Load();

                Assert.True(loaded.TryGetTranslation("Hello", out var hello));
                Assert.Equal("你好", hello);
                Assert.True(loaded.TryGetTranslation("World", out var world));
                Assert.Equal("世界", world);

                // 文件应可读：含 hash/source/translation
                var text = File.ReadAllText(path);
                Assert.Contains("\"hash\"", text);
                Assert.Contains("\"source\"", text);
                Assert.Contains("\"translation\"", text);
                Assert.Contains("Hello", text);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void TryGetTranslation_EmptyValue_ReturnsFalse()
        {
            var path = Path.Combine(Path.GetTempPath(), "bepinex-translator-empty-" + Path.GetRandomFileName() + ".json");
            try
            {
                var cache = new TranslationCache(path);
                cache.EnsurePlaceholder("Pending");
                cache.Save();

                Assert.False(cache.TryGetTranslation("Pending", out _));
                Assert.True(cache.TryGet("Pending", out var entry));
                Assert.NotNull(entry);
                Assert.Equal(string.Empty, entry!.Translation);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task ManualEdit_NonEmptyTranslation_IsUsedWithoutApi()
        {
            var path = Path.Combine(Path.GetTempPath(), "bepinex-translator-manual-" + Path.GetRandomFileName() + ".json");
            try
            {
                var hash = TextHasher.ComputeHash("Save");
                File.WriteAllText(path,
                    "[\n  {\n    \"hash\": \"" + hash + "\",\n    \"source\": \"Save\",\n    \"translation\": \"保存\"\n  }\n]\n");

                var cache = new TranslationCache(path);
                cache.Load();
                Assert.True(cache.TryGetTranslation("Save", out var t));
                Assert.Equal("保存", t);

                // OnDemandTranslator 命中缓存时不应调用后端
                var backend = new CountingBackend();
                var translator = new OnDemandTranslator(cache, backend, "zh-CN");
                var result = await translator.TranslateAsync("Save");
                Assert.Equal("保存", result);
                Assert.Equal(0, backend.Calls);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private sealed class CountingBackend : ITranslationBackend
        {
            public int Calls;
            public string Name => "Counting";

            public System.Threading.Tasks.Task<string> TranslateAsync(
                string source,
                string targetLang,
                System.Threading.CancellationToken cancellationToken)
            {
                Calls++;
                return System.Threading.Tasks.Task.FromResult("X");
            }
        }
    }
}
