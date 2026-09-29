using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BepInExTranslator.Core;
using BepInExTranslator.Core.Backends;
using Xunit;

namespace BepInExTranslator.Core.Tests
{
    public class HttpRequestHardeningTests
    {
        [Fact]
        public void FillUrl_UsesEscapeDataString_ForPlaceholders()
        {
            var values = new Dictionary<string, string>
            {
                ["source"] = "a b&c=d",
                ["targetLanguage"] = "zh-CN",
                ["hash"] = "abc",
            };

            var url = HttpRequestHardening.FillUrl(
                "https://example.com/t?q={source}&lang={targetLanguage}&id={hash}",
                values);

            Assert.Contains(Uri.EscapeDataString("a b&c=d"), url);
            Assert.DoesNotContain("a b&c=d", url);
            Assert.Contains("zh-CN", url);
            Assert.Contains("id=abc", url);
        }

        [Fact]
        public void TrySanitizeHeader_StripsCrLf()
        {
            var ok = HttpRequestHardening.TrySanitizeHeader(
                "X-Token",
                "good\r\nInjected: evil",
                out var name,
                out var value);

            Assert.True(ok);
            Assert.Equal("X-Token", name);
            Assert.Equal("goodInjected: evil", value);
            Assert.DoesNotContain("\r", value);
            Assert.DoesNotContain("\n", value);
        }

        [Fact]
        public void TrySanitizeHeader_RejectsNameWithColonOrWhitespace()
        {
            Assert.False(HttpRequestHardening.TrySanitizeHeader("Bad Name", "v", out _, out _));
            Assert.False(HttpRequestHardening.TrySanitizeHeader("Bad:Name", "v", out _, out _));
        }

        [Fact]
        public void TrySanitizeHeader_RejectsOverlongValue()
        {
            var longValue = new string('x', HttpRequestHardening.MaxHeaderValueLength + 1);
            Assert.False(HttpRequestHardening.TrySanitizeHeader("X-Long", longValue, out _, out _));
        }

        [Fact]
        public void SanitizeHeaders_DropsDangerousEntries()
        {
            var input = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Authorization", "Bearer tok"),
                new KeyValuePair<string, string>("Evil\nHeader", "x"),
                new KeyValuePair<string, string>("Ok", "1\r\n2"),
            };

            var cleaned = HttpRequestHardening.SanitizeHeaders(input);
            Assert.True(cleaned.ContainsKey("Authorization"));
            Assert.False(cleaned.ContainsKey("EvilHeader"));
            Assert.Equal(2, cleaned.Count);
            Assert.Equal("12", cleaned["Ok"]);
        }

        [Fact]
        public void TrySanitizeHeader_RejectsNameContainingCrLf()
        {
            Assert.False(HttpRequestHardening.TrySanitizeHeader("X\nInject", "v", out _, out _));
        }
    }

    public class StaleWriteGuardTests
    {
        [Fact]
        public void ShouldApply_WhenCurrentStillExpectedSource()
        {
            Assert.True(StaleWriteGuard.ShouldApply("Hello", "你好", "Hello", "Hello"));
        }

        [Fact]
        public void ShouldApply_False_WhenPendingExpectedChanged()
        {
            Assert.False(StaleWriteGuard.ShouldApply("Hello", "你好", "Other", "Hello"));
        }

        [Fact]
        public void ShouldApply_False_WhenCurrentTextMovedOn()
        {
            Assert.False(StaleWriteGuard.ShouldApply("Hello", "你好", "Hello", "Settings"));
        }

        [Fact]
        public void ShouldApply_True_WhenCurrentAlreadyTranslated()
        {
            Assert.True(StaleWriteGuard.ShouldApply("Hello", "你好", "Hello", "你好"));
        }
    }

    public class CooldownTranslatorTests
    {
        [Fact]
        public async Task EmptyTranslation_EntersNegativeCache_SkipsFurtherApi()
        {
            var path = Path.Combine(Path.GetTempPath(), "bepinex-neg-" + Path.GetRandomFileName() + ".json");
            try
            {
                var cache = new TranslationCache(path);
                var backend = new CountingBackend(string.Empty);
                var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
                var translator = new OnDemandTranslator(
                    cache,
                    backend,
                    "zh-CN",
                    negativeTtl: TimeSpan.FromMinutes(1),
                    utcNow: () => now);

                var first = await translator.TranslateAsync("Spam");
                Assert.Equal("Spam", first);
                Assert.Equal(1, backend.Calls);
                Assert.True(translator.IsInCooldown("Spam"));

                var second = await translator.TranslateAsync("Spam");
                Assert.Equal("Spam", second);
                Assert.Equal(1, backend.Calls); // 冷却中不再请求
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
        public async Task Exception_EntersBackoff_SkipsFurtherApi()
        {
            var path = Path.Combine(Path.GetTempPath(), "bepinex-backoff-" + Path.GetRandomFileName() + ".json");
            try
            {
                var cache = new TranslationCache(path);
                var backend = new ThrowingBackend();
                var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
                var translator = new OnDemandTranslator(
                    cache,
                    backend,
                    "zh-CN",
                    initialBackoff: TimeSpan.FromSeconds(30),
                    utcNow: () => now);

                await Assert.ThrowsAsync<InvalidOperationException>(() => translator.TranslateAsync("X"));
                Assert.Equal(1, backend.Calls);
                Assert.True(translator.IsInCooldown("X"));

                var again = await translator.TranslateAsync("X");
                Assert.Equal("X", again);
                Assert.Equal(1, backend.Calls);
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
            private readonly string _value;
            public int Calls;
            public string Name => "Counting";
            public CountingBackend(string value) => _value = value;

            public Task<string> TranslateAsync(string source, string targetLang, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(_value);
            }
        }

        private sealed class ThrowingBackend : ITranslationBackend
        {
            public int Calls;
            public string Name => "Throwing";

            public Task<string> TranslateAsync(string source, string targetLang, CancellationToken cancellationToken)
            {
                Calls++;
                throw new InvalidOperationException("boom");
            }
        }
    }
}
