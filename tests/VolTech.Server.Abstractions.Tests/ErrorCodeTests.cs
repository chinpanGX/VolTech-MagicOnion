using VolTech.Server.Abstractions;

namespace VolTech.Server.Abstractions.Tests
{
    public class ErrorCodeTests
    {
        [Fact]
        public void Equals_SameDomainAndReason_IsEqual()
        {
            var first = new ErrorCode("voltech", "MAINTENANCE");
            var second = new ErrorCode("voltech", "MAINTENANCE");

            Assert.Equal(first, second);
            Assert.True(first == second);
            Assert.False(first != second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
        }

        [Fact]
        public void Equals_SameReasonInDifferentDomain_IsNotEqual()
        {
            var reserved = new ErrorCode("voltech", "INSUFFICIENT_GEMS");
            var app = new ErrorCode("sandbox", "INSUFFICIENT_GEMS");

            Assert.NotEqual(reserved, app);
            Assert.True(reserved != app);
        }

        [Fact]
        public void ToString_ReturnsDomainSlashReason()
        {
            var code = new ErrorCode("voltech", "MAINTENANCE");

            Assert.Equal("voltech/MAINTENANCE", code.ToString());
        }

        [Fact]
        public void Constructor_NullDomain_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ErrorCode(null!, "MAINTENANCE"));
        }

        [Fact]
        public void Constructor_NullReason_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ErrorCode("voltech", null!));
        }
    }
}
