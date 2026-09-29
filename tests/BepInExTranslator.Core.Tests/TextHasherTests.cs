using BepInExTranslator.Core;
using Xunit;

namespace BepInExTranslator.Core.Tests
{
    public class TextHasherTests
    {
        [Fact]
        public void ComputeHash_IsStable_ForSameInput()
        {
            var a = TextHasher.ComputeHash("Hello, world!");
            var b = TextHasher.ComputeHash("Hello, world!");
            Assert.Equal(a, b);
            Assert.Equal(32, a.Length);
        }

        [Fact]
        public void ComputeHash_NormalizesLineEndings()
        {
            var unix = TextHasher.ComputeHash("line1\nline2");
            var windows = TextHasher.ComputeHash("line1\r\nline2");
            var oldMac = TextHasher.ComputeHash("line1\rline2");
            Assert.Equal(unix, windows);
            Assert.Equal(unix, oldMac);
        }

        [Fact]
        public void ComputeHash_Differs_ForDifferentText()
        {
            var a = TextHasher.ComputeHash("alpha");
            var b = TextHasher.ComputeHash("beta");
            Assert.NotEqual(a, b);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ComputeHash_Empty_ReturnsEmpty(string? input)
        {
            Assert.Equal(string.Empty, TextHasher.ComputeHash(input));
        }
    }
}
