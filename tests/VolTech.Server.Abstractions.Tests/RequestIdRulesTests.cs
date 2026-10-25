using VolTech.Server.Abstractions;

namespace VolTech.Server.Abstractions.Tests
{
    public class RequestIdRulesTests
    {
        [Theory]
        [InlineData("a")]
        [InlineData("abc-123")]
        [InlineData("ABC-xyz-0")]
        public void IsValid_AllowedCharacters_ReturnsTrue(string value)
        {
            Assert.True(RequestIdRules.IsValid(value));
        }

        [Fact]
        public void IsValid_ClientGeneratedGuid_ReturnsTrue()
        {
            // クライアントは Guid.NewGuid().ToString("N") で採番する
            var value = Guid.NewGuid().ToString("N");

            Assert.True(RequestIdRules.IsValid(value));
        }

        [Fact]
        public void IsValid_MaxLength_ReturnsTrue()
        {
            var value = new string('a', RequestIdRules.MaxLength);

            Assert.True(RequestIdRules.IsValid(value));
        }

        [Fact]
        public void IsValid_TooLong_ReturnsFalse()
        {
            var value = new string('a', RequestIdRules.MaxLength + 1);

            Assert.False(RequestIdRules.IsValid(value));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("a b")]
        [InlineData("a_b")]
        [InlineData("a/b")]
        [InlineData("あ")]
        public void IsValid_EmptyOrDisallowedCharacters_ReturnsFalse(string? value)
        {
            Assert.False(RequestIdRules.IsValid(value));
        }
    }
}
