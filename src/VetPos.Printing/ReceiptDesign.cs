using System.Text.Json;
using System.Text.Json.Serialization;

namespace VetPos.Printing;

public enum PieceType { Text, Pair, Logo, Divider, Space, Items, Totals, Payments, ItemCount, Savings, Barcode, Qr, Signature }

public enum PieceAlign { Left, Center, Right }

public enum TextSize { Normal, Tall, Wide, Large }

public enum DividerStyle { Dashed, DoubleLine, Dotted, Stars }

public enum ItemLayout { TwoLine, OneLine }

public enum QrSize { Small, Medium, Large }

/// <summary>When a piece is printed (most: always; some only on certain bills).</summary>
public enum PrintWhen { Always, Cash, Card, Bank, Discount, PriceChanged, Reprint, NotReprint }

/// <summary>
/// One piece of the receipt, printed top to bottom (receipt printers print line by line). Text can
/// hold fields such as {bill_no}. A flat class so the editor can bind every option directly.
/// </summary>
public sealed class ReceiptPiece
{
    public string Id { get; set; } = NewId();
    public PieceType Type { get; set; }
    public string? Name { get; set; }
    public bool Hidden { get; set; }
    public bool Locked { get; set; }
    public PrintWhen When { get; set; } = PrintWhen.Always;

    // Text / Pair / Signature
    public string Text { get; set; } = "";
    public string Right { get; set; } = "";
    public PieceAlign Align { get; set; } = PieceAlign.Center;
    public bool Bold { get; set; }
    public TextSize Size { get; set; }
    public bool Underline { get; set; }
    public bool Invert { get; set; }

    // Divider / Space
    public DividerStyle Style { get; set; }
    public int Lines { get; set; } = 1;

    // Items
    public ItemLayout Layout { get; set; }
    public bool ShowBarcode { get; set; }
    public bool ShowDiscounts { get; set; } = true;

    /// <summary>Print "Price 450 → 400" under items whose price was changed (off: customers see only the price paid).</summary>
    public bool ShowPriceChanges { get; set; }
    public bool Numbered { get; set; }

    // Totals
    public bool BigTotal { get; set; } = true;

    // QR
    public string Value { get; set; } = "";
    public QrSize QrSize { get; set; } = QrSize.Medium;

    public ReceiptPiece Clone()
    {
        var c = (ReceiptPiece)MemberwiseClone();
        c.Id = NewId();
        return c;
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..10];
}

/// <summary>The saved receipt design (Settings key "receipt").</summary>
public sealed class ReceiptDesign
{
    public const string SettingsKey = "receipt";

    public List<ReceiptPiece> Pieces { get; set; } = [];

    /// <summary>Text after the shop details, e.g. "Vet consultations every Saturday".</summary>
    public string HeaderText { get; set; } = "";

    public string FooterText { get; set; } = "Thank you! Please come again.";

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() }, WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ReceiptDesign FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return ReceiptTemplates.Classic();
        try
        {
            var d = JsonSerializer.Deserialize<ReceiptDesign>(json, Json);
            return d is { Pieces.Count: > 0 } ? d : ReceiptTemplates.Classic();
        }
        catch (JsonException)
        {
            return ReceiptTemplates.Classic();
        }
    }

    public ReceiptDesign Clone() => FromJson(ToJson());
}

public static class ReceiptFields
{
    /// <summary>Fields that can go in any text, with their names for the editor.</summary>
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["shop_name"] = "Shop name",
        ["shop_address"] = "Shop address",
        ["shop_phone"] = "Shop phone",
        ["shop_email"] = "Shop email",
        ["header"] = "Header text",
        ["footer"] = "Footer text",
        ["bill_no"] = "Bill number",
        ["date"] = "Date and time",
        ["day"] = "Date",
        ["time"] = "Time",
        ["cashier"] = "Cashier",
        ["total"] = "Bill total",
        ["item_count"] = "Number of items",
        ["payment_method"] = "Paid by",
        ["note"] = "Bill note",
    };

    public static readonly IReadOnlyDictionary<PieceType, (string Label, string Hint)> PieceInfo = new Dictionary<PieceType, (string, string)>
    {
        [PieceType.Text] = ("Text", "Any words, with fields like {bill_no}"),
        [PieceType.Pair] = ("Two columns", "Words on the left and right of one line"),
        [PieceType.Logo] = ("Logo", "The shop logo"),
        [PieceType.Divider] = ("Line", "A line across the paper"),
        [PieceType.Space] = ("Space", "Empty lines"),
        [PieceType.Items] = ("Items", "What was bought"),
        [PieceType.Totals] = ("Total", "Subtotal, discounts and TOTAL"),
        [PieceType.Payments] = ("Payment", "Cash / card paid and change"),
        [PieceType.ItemCount] = ("Item count", "\"Items: 4\""),
        [PieceType.Savings] = ("You saved", "\"You saved Rs 450.00 today!\""),
        [PieceType.Barcode] = ("Bill barcode", "Scan it to find the bill"),
        [PieceType.Qr] = ("QR code", "WhatsApp, Facebook or a review link"),
        [PieceType.Signature] = ("Signature", "A line to sign on"),
    };

    public static string Describe(ReceiptPiece p)
    {
        if (!string.IsNullOrWhiteSpace(p.Name)) return p.Name;
        static string Short(string s) => s.Length > 30 ? s[..29] + "…" : s;
        return p.Type switch
        {
            PieceType.Text => string.IsNullOrWhiteSpace(p.Text) ? "Empty text" : Short(Pretty(p.Text.Replace('\n', ' '))),
            PieceType.Pair => Short(Pretty($"{p.Text} | {p.Right}")),
            PieceType.Space => p.Lines == 1 ? "Space" : $"Space × {p.Lines}",
            PieceType.Qr => string.IsNullOrWhiteSpace(p.Value) ? "QR (empty)" : "QR " + Short(p.Value),
            PieceType.Signature => "Signature: " + p.Text,
            PieceType.Items => p.Layout == ItemLayout.OneLine ? "Items (one line)" : "Items",
            _ => PieceInfo[p.Type].Label,
        };
    }

    /// <summary>"{shop_name}" → "[Shop name]" so people see words, not codes.</summary>
    public static string Pretty(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\{(\w+)\}", m => All.TryGetValue(m.Groups[1].Value, out var n) ? $"[{n}]" : m.Value);
}
