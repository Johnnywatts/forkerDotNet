using Forker.Domain;

namespace Forker.Domain.Tests;

public class ValueObjectTests
{
    public class FileJobIdTests
    {
        [Fact]
        public void New_CreatesUniqueIds()
        {
            // Act
            var id1 = FileJobId.New();
            var id2 = FileJobId.New();

            // Assert
            Assert.NotEqual(id1, id2);
            Assert.NotEqual(Guid.Empty, id1.Value);
            Assert.NotEqual(Guid.Empty, id2.Value);
        }

        [Fact]
        public void From_CreatesIdFromGuid()
        {
            // Arrange
            var guid = Guid.NewGuid();

            // Act
            var id = FileJobId.From(guid);

            // Assert
            Assert.Equal(guid, id.Value);
        }

        [Fact]
        public void ImplicitConversion_ToGuid_Works()
        {
            // Arrange
            var id = FileJobId.New();

            // Act
            Guid guid = id;

            // Assert
            Assert.Equal(id.Value, guid);
        }

        [Fact]
        public void ToString_ReturnsGuidString()
        {
            // Arrange
            var guid = Guid.NewGuid();
            var id = FileJobId.From(guid);

            // Act
            var result = id.ToString();

            // Assert
            Assert.Equal(guid.ToString(), result);
        }

        [Fact]
        public void Equality_SameValue_AreEqual()
        {
            // Arrange
            var guid = Guid.NewGuid();
            var id1 = FileJobId.From(guid);
            var id2 = FileJobId.From(guid);

            // Act & Assert
            Assert.Equal(id1, id2);
            Assert.True(id1 == id2);
            Assert.False(id1 != id2);
            Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
        }
    }

    public class TargetIdTests
    {
        [Fact]
        public void Constructor_ValidValue_CreatesTargetId()
        {
            // Arrange
            var value = "TargetA";

            // Act
            var targetId = new TargetId(value);

            // Assert
            Assert.Equal(value, targetId.Value);
        }

        [Fact]
        public void Constructor_ValueWithWhitespace_TrimsValue()
        {
            // Arrange
            var value = "  TargetA  ";

            // Act
            var targetId = new TargetId(value);

            // Assert
            Assert.Equal("TargetA", targetId.Value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData(null)]
        public void Constructor_InvalidValue_ThrowsArgumentException(string invalidValue)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new TargetId(invalidValue));
        }

        [Fact]
        public void From_CreatesTargetIdFromString()
        {
            // Arrange
            var value = "TargetB";

            // Act
            var targetId = TargetId.From(value);

            // Assert
            Assert.Equal(value, targetId.Value);
        }

        [Fact]
        public void ImplicitConversion_ToString_Works()
        {
            // Arrange
            var targetId = TargetId.From("TargetA");

            // Act
            string value = targetId;

            // Assert
            Assert.Equal("TargetA", value);
        }

        [Fact]
        public void ToString_ReturnsValue()
        {
            // Arrange
            var targetId = TargetId.From("TargetA");

            // Act
            var result = targetId.ToString();

            // Assert
            Assert.Equal("TargetA", result);
        }

        [Fact]
        public void Equality_SameValue_AreEqual()
        {
            // Arrange
            var id1 = TargetId.From("TargetA");
            var id2 = TargetId.From("TargetA");

            // Act & Assert
            Assert.Equal(id1, id2);
            Assert.True(id1 == id2);
            Assert.False(id1 != id2);
            Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
        }
    }

    public class VersionTokenTests
    {
        [Fact]
        public void Constructor_PositiveValue_CreatesVersionToken()
        {
            // Arrange
            var value = 5L;

            // Act
            var token = new VersionToken(value);

            // Assert
            Assert.Equal(value, token.Value);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void Constructor_NonPositiveValue_ThrowsArgumentOutOfRangeException(long invalidValue)
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new VersionToken(invalidValue));
        }

        [Fact]
        public void Initial_ReturnsVersionTokenWithValueOne()
        {
            // Act
            var initial = VersionToken.Initial;

            // Assert
            Assert.Equal(1L, initial.Value);
        }

        [Fact]
        public void From_CreatesVersionTokenFromLong()
        {
            // Arrange
            var value = 42L;

            // Act
            var token = VersionToken.From(value);

            // Assert
            Assert.Equal(value, token.Value);
        }

        [Fact]
        public void Next_ReturnsIncrementedVersionToken()
        {
            // Arrange
            var token = VersionToken.From(5L);

            // Act
            var next = token.Next();

            // Assert
            Assert.Equal(6L, next.Value);
            Assert.Equal(5L, token.Value); // Original unchanged
        }

        [Fact]
        public void ImplicitConversion_ToLong_Works()
        {
            // Arrange
            var token = VersionToken.From(42L);

            // Act
            long value = token;

            // Assert
            Assert.Equal(42L, value);
        }

        [Fact]
        public void ToString_ReturnsValueAsString()
        {
            // Arrange
            var token = VersionToken.From(123L);

            // Act
            var result = token.ToString();

            // Assert
            Assert.Equal("123", result);
        }

        [Fact]
        public void Equality_SameValue_AreEqual()
        {
            // Arrange
            var token1 = VersionToken.From(10L);
            var token2 = VersionToken.From(10L);

            // Act & Assert
            Assert.Equal(token1, token2);
            Assert.True(token1 == token2);
            Assert.False(token1 != token2);
            Assert.Equal(token1.GetHashCode(), token2.GetHashCode());
        }

        [Fact]
        public void Comparison_DifferentValues_AreNotEqual()
        {
            // Arrange
            var token1 = VersionToken.From(10L);
            var token2 = VersionToken.From(11L);

            // Act & Assert
            Assert.NotEqual(token1, token2);
            Assert.False(token1 == token2);
            Assert.True(token1 != token2);
        }
    }
}