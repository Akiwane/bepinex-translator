using BepInExTranslator.Core;
using BepInExTranslator.Core.Backends;
using Xunit;

namespace BepInExTranslator.Core.Tests
{
    public class FontSizeAdjusterTests
    {
        [Fact]
        public void ComputeAdjustedFontSize_ShorterOrEqual_KeepsOriginal()
        {
            var size = FontSizeAdjuster.ComputeAdjustedFontSize(24f, "Hello World", "你好");
            Assert.Equal(24f, size);
        }

        [Fact]
        public void ComputeAdjustedFontSize_Longer_ScalesDown()
        {
            var size = FontSizeAdjuster.ComputeAdjustedFontSize(
                20f,
                "Hi",
                "这是一段明显更长的中文翻译文本示例");
            Assert.True(size < 20f);
            Assert.True(size >= 8f);
        }

        [Fact]
        public void ShouldEnableWrap_WhenSignificantlyLonger()
        {
            Assert.True(FontSizeAdjuster.ShouldEnableWrap("OK", "这是更长的译文内容"));
            Assert.False(FontSizeAdjuster.ShouldEnableWrap("Hello", "你好"));
        }

        [Fact]
        public void MeasureLength_CountsWideCharsHeavier()
        {
            Assert.Equal(2, FontSizeAdjuster.MeasureLength("你"));
            Assert.Equal(1, FontSizeAdjuster.MeasureLength("A"));
        }
    }

    public class JsonPathExtractorTests
    {
        [Fact]
        public void ExtractString_NestedPath()
        {
            var json = "{\"data\":{\"translatedText\":\"世界\"}}";
            var value = JsonPathExtractor.ExtractString(json, "data.translatedText");
            Assert.Equal("世界", value);
        }

        [Fact]
        public void ExtractString_ArrayIndex_OpenAiShape()
        {
            var json = "{\"choices\":[{\"message\":{\"content\":\"你好\"}}]}";
            var value = JsonPathExtractor.ExtractString(json, "choices[0].message.content");
            Assert.Equal("你好", value);
        }
    }
}
