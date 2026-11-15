namespace VetPos.Printing;

/// <summary>A barcode as a row of modules (true = black bar), plus the text printed under it.</summary>
public sealed record BarcodeImage(bool[] Modules, string Text, string Kind);

/// <summary>
/// Barcodes for shelf / product labels. A valid 13-digit code is printed as EAN-13 (what retail
/// scanners expect); anything else as Code 128 (set C for even-length numbers, else set B).
/// </summary>
public static class Barcodes
{
    public static BarcodeImage Encode(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0) throw new ArgumentException("Empty barcode", nameof(value));
        return IsEan13(value) ? Ean13(value) : Code128(value);
    }

    public static bool IsEan13(string v) => v.Length == 13 && v.All(char.IsAsciiDigit) && Ean13Check(v[..12]) == v[12] - '0';

    public static int Ean13Check(string twelve)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (twelve[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10;
    }

    // ───────── EAN-13 ─────────

    private static readonly string[] L = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
    private static readonly string[] G = ["0100111", "0110011", "0011011", "0100001", "0011101", "0111001", "0000101", "0010001", "0001001", "0010111"];
    private static readonly string[] R = ["1110010", "1100110", "1101100", "1000010", "1011100", "1001110", "1010000", "1000100", "1001000", "1110100"];
    private static readonly string[] Parity = ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

    public static BarcodeImage Ean13(string v)
    {
        if (!IsEan13(v)) throw new ArgumentException("Not a valid EAN-13", nameof(v));
        var bits = new System.Text.StringBuilder("101");
        var parity = Parity[v[0] - '0'];
        for (var i = 1; i <= 6; i++) bits.Append(parity[i - 1] == 'L' ? L[v[i] - '0'] : G[v[i] - '0']);
        bits.Append("01010");
        for (var i = 7; i <= 12; i++) bits.Append(R[v[i] - '0']);
        bits.Append("101");
        return new BarcodeImage(bits.ToString().Select(c => c == '1').ToArray(), v, "EAN-13");
    }

    // ───────── Code 128 ─────────

    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    private const int StartB = 104, StartC = 105, Stop = 106;

    public static BarcodeImage Code128(string v)
    {
        if (v.Any(c => c < 32 || c > 126)) throw new ArgumentException("Code 128 here supports plain characters only", nameof(v));
        var codes = new List<int>();
        if (v.Length >= 4 && v.Length % 2 == 0 && v.All(char.IsAsciiDigit))
        {
            codes.Add(StartC);
            for (var i = 0; i < v.Length; i += 2) codes.Add((v[i] - '0') * 10 + (v[i + 1] - '0'));
        }
        else
        {
            codes.Add(StartB);
            codes.AddRange(v.Select(c => c - 32));
        }
        var checksum = codes[0];
        for (var i = 1; i < codes.Count; i++) checksum += codes[i] * i;
        codes.Add(checksum % 103);
        codes.Add(Stop);

        var modules = new List<bool>();
        foreach (var code in codes)
        {
            var black = true;
            foreach (var w in Patterns[code])
            {
                for (var k = 0; k < w - '0'; k++) modules.Add(black);
                black = !black;
            }
        }
        return new BarcodeImage([.. modules], v, "Code 128");
    }
}
