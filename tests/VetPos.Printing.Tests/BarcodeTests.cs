using VetPos.Printing;

namespace VetPos.Printing.Tests;

public class BarcodeTests
{
    [Fact]
    public void Ean13_has_95_modules_with_guards()
    {
        var b = Barcodes.Encode("2000000000015");
        Assert.Equal("EAN-13", b.Kind);
        Assert.Equal(95, b.Modules.Length);
        Assert.Equal([true, false, true], b.Modules[..3]);
        Assert.Equal([false, true, false, true, false], b.Modules[45..50]);
        Assert.Equal([true, false, true], b.Modules[^3..]);
    }

    [Fact]
    public void Known_ean13_encodes_like_the_standard()
    {
        // 4006381333931: first digit 4 → parity LGLLGG; digit 0 (L) = 0001101.
        var b = Barcodes.Ean13("4006381333931");
        Assert.Equal("0001101", string.Concat(b.Modules[3..10].Select(m => m ? '1' : '0')));
    }

    [Fact]
    public void Wrong_check_digit_falls_back_to_code128()
    {
        Assert.False(Barcodes.IsEan13("2000000000016"));
        Assert.Equal("Code 128", Barcodes.Encode("2000000000016").Kind);
    }

    [Theory]
    [InlineData("ABC-12", 6)]   // set B: start + 6 chars + check
    [InlineData("12345678", 4)] // set C: start + 4 pairs + check
    public void Code128_length_and_frame(string value, int dataSymbols)
    {
        var b = Barcodes.Code128(value);
        Assert.Equal(11 * (dataSymbols + 2) + 13, b.Modules.Length);
        Assert.True(b.Modules[0]);
        Assert.Equal("1100011101011", string.Concat(b.Modules[^13..].Select(m => m ? '1' : '0'))); // stop
    }

    [Fact]
    public void Code128_checksum_matches_reference()
    {
        // "PJJ123C" in set B: 104 + 48·1 + 42·2 + 42·3 + 17·4 + 18·5 + 19·6 + 35·7 = 879; 879 mod 103 = 55.
        var b = Barcodes.Code128("PJJ123C");
        var checksumModules = string.Concat(b.Modules[^24..^13].Select(m => m ? '1' : '0'));
        Assert.Equal("11101000110", checksumModules); // symbol 55 = pattern 311321
    }
}
