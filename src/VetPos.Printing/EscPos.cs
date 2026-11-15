using System.Text;

namespace VetPos.Printing;

/// <summary>1-bit logo for the printer: rows of bytes, 8 dots per byte, most significant bit first.</summary>
public sealed record LogoBitmap(int Width, int Height, byte[] Data)
{
    public int BytesPerRow => (Width + 7) / 8;
}

public sealed record EscPosOptions(LogoBitmap? Logo = null, bool OpenDrawer = false);

/// <summary>
/// ESC/POS bytes for 58 / 80 mm thermal printers. Only widely supported commands, so generic
/// printers work: text styles, raster logo, CODE128 barcode, QR, feed, cut and the cash drawer.
/// </summary>
public static class EscPos
{
    private const byte Esc = 0x1B, Gs = 0x1D;

    public static byte[] Encode(ReceiptDoc doc, EscPosOptions options)
    {
        var o = new List<byte>(4096);
        o.AddRange([Esc, (byte)'@']);      // initialise
        o.AddRange([Esc, (byte)'t', 0]);    // code page 437
        foreach (var op in doc.Ops)
        {
            switch (op)
            {
                case TextOp t:
                    Align(o, t.Align);
                    o.AddRange([Esc, (byte)'E', (byte)(t.Bold ? 1 : 0)]);
                    o.AddRange([Esc, (byte)'-', (byte)(t.Underline ? 1 : 0)]);
                    o.AddRange([Gs, (byte)'B', (byte)(t.Invert ? 1 : 0)]);
                    o.AddRange([Gs, (byte)'!', t.Size switch { TextSize.Tall => (byte)0x01, TextSize.Wide => (byte)0x10, TextSize.Large => (byte)0x11, _ => (byte)0 }]);
                    o.AddRange(Encoding.ASCII.GetBytes(ReceiptRenderer.Printable(t.Text)));
                    o.Add(0x0A);
                    Reset(o);
                    break;
                case LogoOp l when options.Logo is { } logo:
                    Align(o, l.Align);
                    Raster(o, logo);
                    Align(o, PieceAlign.Left);
                    break;
                case BarcodeOp b:
                    Align(o, PieceAlign.Center);
                    o.AddRange([Gs, (byte)'h', 60, Gs, (byte)'w', 2, Gs, (byte)'H', 2]); // height, width, numbers below
                    var data = Encoding.ASCII.GetBytes("{B" + b.Value);
                    o.AddRange([Gs, (byte)'k', 73, (byte)data.Length]);
                    o.AddRange(data);
                    o.Add(0x0A);
                    Align(o, PieceAlign.Left);
                    break;
                case QrOp q:
                    Align(o, PieceAlign.Center);
                    Qr(o, q);
                    Align(o, PieceAlign.Left);
                    break;
                case FeedOp f:
                    o.AddRange([Esc, (byte)'d', (byte)Math.Clamp(f.Lines, 0, 10)]);
                    break;
                case CutOp:
                    o.AddRange([Gs, (byte)'V', 66, 0]); // feed and partial cut
                    break;
            }
        }
        if (options.OpenDrawer) o.AddRange(DrawerKick());
        return [.. o];
    }

    /// <summary>ESC p: pulse the cash drawer plugged into the printer.</summary>
    public static byte[] DrawerKick() => [Esc, (byte)'p', 0, 25, 250];

    private static void Align(List<byte> o, PieceAlign a) =>
        o.AddRange([Esc, (byte)'a', a switch { PieceAlign.Center => (byte)1, PieceAlign.Right => (byte)2, _ => (byte)0 }]);

    private static void Reset(List<byte> o) => o.AddRange([Esc, (byte)'E', 0, Esc, (byte)'-', 0, Gs, (byte)'B', 0, Gs, (byte)'!', 0]);

    private static void Raster(List<byte> o, LogoBitmap logo)
    {
        var bpr = logo.BytesPerRow;
        o.AddRange([Gs, (byte)'v', (byte)'0', 0, (byte)(bpr & 0xFF), (byte)(bpr >> 8), (byte)(logo.Height & 0xFF), (byte)(logo.Height >> 8)]);
        o.AddRange(logo.Data);
        o.Add(0x0A);
    }

    private static void Qr(List<byte> o, QrOp q)
    {
        var data = Encoding.UTF8.GetBytes(q.Value);
        var size = q.Size switch { QrSize.Small => (byte)4, QrSize.Medium => (byte)6, _ => (byte)8 };
        o.AddRange([Gs, (byte)'(', (byte)'k', 4, 0, 49, 65, 50, 0]);       // model 2
        o.AddRange([Gs, (byte)'(', (byte)'k', 3, 0, 49, 67, size]);         // dot size
        o.AddRange([Gs, (byte)'(', (byte)'k', 3, 0, 49, 69, 49]);           // error correction M
        var len = data.Length + 3;
        o.AddRange([Gs, (byte)'(', (byte)'k', (byte)(len & 0xFF), (byte)(len >> 8), 49, 80, 48]);
        o.AddRange(data);
        o.AddRange([Gs, (byte)'(', (byte)'k', 3, 0, 49, 81, 48]);           // print
        o.Add(0x0A);
    }
}
