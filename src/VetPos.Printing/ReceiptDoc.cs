namespace VetPos.Printing;

/// <summary>A receipt laid out into printer operations (shared by the ESC/POS encoder and the preview).</summary>
public abstract record ReceiptOp;

public sealed record TextOp(string Text, PieceAlign Align, bool Bold, TextSize Size, bool Underline, bool Invert, string PieceId) : ReceiptOp;

public sealed record LogoOp(PieceAlign Align, string PieceId) : ReceiptOp;

public sealed record BarcodeOp(string Value, string PieceId) : ReceiptOp;

public sealed record QrOp(string Value, QrSize Size, string PieceId) : ReceiptOp;

public sealed record FeedOp(int Lines) : ReceiptOp;

public sealed record CutOp : ReceiptOp;

public sealed record ReceiptDoc(IReadOnlyList<ReceiptOp> Ops, int CharsPerLine)
{
    /// <summary>Roughly how long the paper is, in mm (80 mm printer, ~4 mm per normal line).</summary>
    public int EstimatedLengthMm => (int)Ops.Sum(o => o switch
    {
        TextOp t => t.Size is TextSize.Tall or TextSize.Large ? 8 : 4,
        LogoOp => 22,
        BarcodeOp => 16,
        QrOp q => q.Size switch { QrSize.Small => 20, QrSize.Medium => 28, _ => 36 },
        FeedOp f => 4 * f.Lines,
        _ => 0,
    });
}
