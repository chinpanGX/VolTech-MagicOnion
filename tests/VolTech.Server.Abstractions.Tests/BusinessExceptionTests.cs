using VolTech.Server.Abstractions;

namespace VolTech.Server.Abstractions.Tests
{
    public class BusinessExceptionTests
    {
        private static readonly ErrorCode insufficientGems = new ErrorCode("sandbox", "INSUFFICIENT_GEMS");

        [Fact]
        public void Constructor_WithoutMetadata_HasEmptyMetadata()
        {
            var exception = new BusinessException(insufficientGems, "not enough gems");

            Assert.Equal(insufficientGems, exception.Code);
            Assert.Equal("not enough gems", exception.Message);
            Assert.Empty(exception.Metadata);
        }

        [Fact]
        public void Constructor_WithMetadata_KeepsValues()
        {
            var metadata = new Dictionary<string, string>
            {
                ["required"] = "300",
            };

            var exception = new BusinessException(insufficientGems, "not enough gems", metadata);

            Assert.Equal("300", exception.Metadata["required"]);
        }

        [Fact]
        public void Constructor_WithMetadata_CopiesTheValues()
        {
            var metadata = new Dictionary<string, string>
            {
                ["required"] = "300",
            };
            var exception = new BusinessException(insufficientGems, "not enough gems", metadata);

            metadata["required"] = "999";
            metadata["added"] = "later";

            Assert.Equal("300", exception.Metadata["required"]);
            Assert.False(exception.Metadata.ContainsKey("added"));
        }

        [Fact]
        public void Constructor_MetadataWithActionKey_Throws()
        {
            var metadata = new Dictionary<string, string>
            {
                [ErrorMetadataKeys.Action] = "ERROR_ACTION_HOME",
            };

            Assert.Throws<ArgumentException>(() => new BusinessException(insufficientGems, "not enough gems", metadata));
        }
    }
}
