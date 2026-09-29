using System.Collections.Generic;
using BepInExTranslator.Core;
using Xunit;

namespace BepInExTranslator.Core.Tests
{
    public class TemplateFillerTests
    {
        [Fact]
        public void Fill_ReplacesDoubleAndSingleBraces()
        {
            var values = new Dictionary<string, string>
            {
                ["text"] = "Hello",
                ["target_lang"] = "zh-CN",
            };

            var result = TemplateFiller.Fill("T={{text}} L={target_lang}", values);
            Assert.Equal("T=Hello L=zh-CN", result);
        }

        [Fact]
        public void EscapeJson_EscapesSpecialCharacters()
        {
            var escaped = TemplateFiller.EscapeJson("a\"b\\c\n");
            Assert.Equal("a\\\"b\\\\c\\n", escaped);
        }

        [Fact]
        public void FillJsonSafe_EscapesSelectedKeys()
        {
            var values = TemplateFiller.BuildTranslationPlaceholders("say \"hi\"", "zh-CN");
            var body = TemplateFiller.FillJsonSafe(
                "{\"q\":\"{{text}}\",\"lang\":\"{{target_lang}}\"}",
                values,
                "text", "source");

            Assert.Contains("say \\\"hi\\\"", body);
            Assert.Contains("zh-CN", body);
        }

        [Fact]
        public void BuildTranslationPlaceholders_ContainsAliases()
        {
            var map = TemplateFiller.BuildTranslationPlaceholders("x", "ja");
            Assert.Equal("x", map["text"]);
            Assert.Equal("x", map["source"]);
            Assert.Equal("ja", map["target_lang"]);
            Assert.Equal("ja", map["lang"]);
        }
    }
}
