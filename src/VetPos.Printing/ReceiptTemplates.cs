namespace VetPos.Printing;

/// <summary>Ready-made designs to start from (same set as the old ShopPOS Receipt Studio).</summary>
public static class ReceiptTemplates
{
    public static readonly IReadOnlyList<(string Name, string Hint, Func<ReceiptDesign> Build)> All =
    [
        ("Classic", "Logo, shop details, items, totals, barcode and a thank-you note", Classic),
        ("Compact", "Short and paper-saving, one line per item", Compact),
        ("Minimal", "Only the shop name, items and the total", Minimal),
        ("Modern", "Big shop name, two-column details, highlighted thank-you and a WhatsApp QR", Modern),
    ];

    private static ReceiptPiece Text(string text, PieceAlign align = PieceAlign.Center, bool bold = false, TextSize size = TextSize.Normal,
        PrintWhen when = PrintWhen.Always, bool invert = false) =>
        new() { Type = PieceType.Text, Text = text, Align = align, Bold = bold, Size = size, When = when, Invert = invert };

    private static ReceiptPiece Pair(string left, string right) => new() { Type = PieceType.Pair, Text = left, Right = right };
    private static ReceiptPiece Line(DividerStyle style = DividerStyle.Dashed) => new() { Type = PieceType.Divider, Style = style };
    private static ReceiptPiece Of(PieceType type) => new() { Type = type };

    public static ReceiptDesign Classic() => new()
    {
        Pieces =
        [
            Of(PieceType.Logo),
            Text("{shop_name}", bold: true, size: TextSize.Tall),
            Text("{shop_address}"),
            Text("Tel: {shop_phone}"),
            Text("{header}"),
            Line(),
            Pair("Bill: {bill_no}", "{day}"),
            Pair("Cashier: {cashier}", "{time}"),
            Text("COPY", bold: true, when: PrintWhen.Reprint),
            Line(),
            Of(PieceType.Items),
            Line(),
            Of(PieceType.Totals),
            Of(PieceType.Payments),
            Line(),
            Of(PieceType.ItemCount),
            Of(PieceType.Savings),
            Of(PieceType.Barcode),
            Text("{footer}", bold: true),
            Text("Medicines once sold cannot be returned."),
        ],
    };

    public static ReceiptDesign Compact() => new()
    {
        Pieces =
        [
            Text("{shop_name}", bold: true, size: TextSize.Large),
            Text("Tel: {shop_phone}"),
            Line(),
            Text("{bill_no}  {date}", PieceAlign.Left),
            Line(),
            new ReceiptPiece { Type = PieceType.Items, Layout = ItemLayout.OneLine },
            Line(),
            Of(PieceType.Totals),
            Of(PieceType.Payments),
            Line(),
            Text("{footer}", bold: true),
        ],
    };

    public static ReceiptDesign Minimal() => new()
    {
        Pieces =
        [
            Text("{shop_name}", bold: true),
            Text("{date}"),
            Line(),
            new ReceiptPiece { Type = PieceType.Items, Layout = ItemLayout.OneLine },
            Line(),
            new ReceiptPiece { Type = PieceType.Totals, BigTotal = true },
        ],
    };

    public static ReceiptDesign Modern() => new()
    {
        Pieces =
        [
            Of(PieceType.Logo),
            Text("{shop_name}", bold: true, size: TextSize.Large),
            Text("{shop_address}"),
            Text("{shop_phone}"),
            Line(DividerStyle.Dotted),
            Pair("Bill {bill_no}", "{day}"),
            Pair("Cashier: {cashier}", "{time}"),
            Line(DividerStyle.Dotted),
            new ReceiptPiece { Type = PieceType.Items, Numbered = true },
            Line(DividerStyle.Dotted),
            Of(PieceType.Totals),
            Of(PieceType.Payments),
            Of(PieceType.Savings),
            Line(DividerStyle.Dotted),
            Text(" THANK YOU ", bold: true, invert: true),
            new ReceiptPiece { Type = PieceType.Qr, Value = "https://wa.me/94771234567", QrSize = QrSize.Small },
            Text("Chat with us on WhatsApp"),
            Of(PieceType.Barcode),
        ],
    };
}
