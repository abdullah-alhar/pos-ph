using VetPos.Core.Security;

namespace VetPos.Core.Tests;

public class PinHasherTests
{
    [Fact]
    public void Right_pin_matches_wrong_pin_does_not()
    {
        var hash = PinHasher.Hash("4821");
        Assert.True(PinHasher.Verify("4821", hash));
        Assert.False(PinHasher.Verify("4822", hash));
        Assert.DoesNotContain("4821", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_pin_gives_different_hashes() => Assert.NotEqual(PinHasher.Hash("1234"), PinHasher.Hash("1234"));

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("pbkdf2$x$y$z")]
    public void Broken_hash_never_matches(string stored) => Assert.False(PinHasher.Verify("1234", stored));

    [Theory]
    [InlineData("1234", true)]
    [InlineData("12345678", true)]
    [InlineData("123", false)]
    [InlineData("123456789", false)]
    [InlineData("12a4", false)]
    public void Pin_rules(string pin, bool ok) => Assert.Equal(ok, PinHasher.CheckPin(pin) is null);
}
