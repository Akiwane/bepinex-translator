using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BepInExTranslator.Core;
using BepInExTranslator.Core.Backends;
using Xunit;

namespace BepInExTranslator.Core.Tests
{
    public class OnDemandTranslatorTests
    {
        [Fact]
        public async Task TranslateAsync_CacheMiss_CallsBackendAndPersists()
        {
            var path = Path.Combine(Path.GetTempPath(), "bepinex-translator-ondemand-" + Path.GetRandomFileName() + ".json");
            try
            {
                var cache = new TranslationCache(path);
                var backend = new FixedBackend("译文");
                var translator = new OnDemandTranslator(cache, backend, "zh-CN");

                var first = await translator.TranslateAsync("New Text");
                Assert.Equal("译文", first);
                Assert.Equal(1, backend.Calls);

                // 第二次应走缓存
                var second = await translator.TranslateAsync("New Text");
                Assert.Equal("译文", second);
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

        private sealed class FixedBackend : ITranslationBackend
        {
            private readonly string _value;
            public int Calls;
            public string Name => "Fixed";

            public FixedBackend(string value) => _value = value;

            public Task<string> TranslateAsync(string source, string targetLang, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(_value);
            }
        }
    }
}
