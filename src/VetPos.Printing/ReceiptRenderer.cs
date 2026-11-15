using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VetPos.Core.Domain;

namespace VetPos.Printing;

/// <summary>Turns a design + a bill into printer lines. Pure: the same input always prints the same.</summary>
public static partial class ReceiptRenderer
{
    public static ReceiptDoc Render(ReceiptDesign design, ReceiptData data, int charsPerLine = 48, bool hasLogo = false, int feedLines = 4, bool cut = true)
    {
        var w = Math.Clamp(charsPerLine, 24, 64);
        var ops = new List<ReceiptOp>();
        foreach (var p in design.Pieces)
        {
            if (p.Hidden || !Holds(p.When, data)) continue;
            switch (p.Type)
            {
                case PieceType.Logo:
                    if (hasLogo) ops.Add(new LogoOp(p.Align, p.Id));
                    break;
                case PieceType.Text:
                    var text = Fill(p.Text, design, data);
                    if (string.IsNullOrWhiteSpace(text) || HasEmptyField(p.Text, design, data)) break;
                    var width = p.Size is TextSize.Wide or TextSize.Large ? w / 2 : w;
                    foreach (var line in text.Split('\n').SelectMany(l => Wrap(l, width)))
                        ops.Add(new TextOp(line, p.Align, p.Bold, p.Size, p.Underline, p.Invert, p.Id));
                    break;
                case PieceType.Pair:
                    ops.Add(new TextOp(Columns(Fill(p.Text, design, data), Fill(p.Right, design, data), w), PieceAlign.Left, p.Bold, TextSize.Normal, false, false, p.Id));
                    break;
                case PieceType.Divider:
                    var pattern = p.Style switch { DividerStyle.DoubleLine => "=", DividerStyle.Dotted => ". ", DividerStyle.Stars => "* ", _ => "-" };
                    ops.Add(Plain(Repeat(pattern, w), p.Id));
                    break;
                case PieceType.Space:
                    ops.Add(new FeedOp(Math.Clamp(p.Lines, 1, 5)));
                    break;
                case PieceType.Items:
                    Items(ops, p, data, w);
                    break;
                case PieceType.Totals:
                    Totals(ops, p, data, w);
                    break;
                case PieceType.Payments:
                    foreach (var pay in data.Payments)
                    {
                        var label = pay.Method switch { PaymentMethod.Cash => "Cash", PaymentMethod.Card => "Card", _ => "Bank transfer" };
                        if (pay.Reference is { Length: > 0 } r) label += " " + r;
                        ops.Add(Plain(Columns(label, MoneyText.M(pay.Method == PaymentMethod.Cash ? pay.Tendered : pay.Amount), w), p.Id));
                    }
                    if (data.Change > 0) ops.Add(new TextOp(Columns("Change", MoneyText.M(data.Change), w), PieceAlign.Left, true, TextSize.Normal, false, false, p.Id));
                    break;
                case PieceType.ItemCount:
                    ops.Add(Plain($"Items: {data.Lines.Count}   Units: {data.Units}", p.Id, PieceAlign.Left));
                    break;
                case PieceType.Savings:
                    if (data.Saved > 0) ops.Add(new TextOp($"You saved Rs {MoneyText.M(data.Saved)} today!", PieceAlign.Center, true, TextSize.Normal, false, false, p.Id));
                    break;
                case PieceType.Barcode:
                    ops.Add(new BarcodeOp(data.BillNumber.ToString(CultureInfo.InvariantCulture), p.Id));
                    break;
                case PieceType.Qr:
                    if (!string.IsNullOrWhiteSpace(p.Value)) ops.Add(new QrOp(p.Value.Trim(), p.QrSize, p.Id));
                    break;
                case PieceType.Signature:
                    ops.Add(new FeedOp(2));
                    ops.Add(Plain(new string('.', Math.Min(w, 32)), p.Id));
                    ops.Add(Plain(Fill(string.IsNullOrWhiteSpace(p.Text) ? "Signature" : p.Text, design, data), p.Id));
                    break;
            }
        }
        if (data.IsVoided) ops.Insert(0, new TextOp("*** VOIDED ***", PieceAlign.Center, true, TextSize.Large, false, false, "void"));
        if (feedLines > 0) ops.Add(new FeedOp(feedLines));
        if (cut) ops.Add(new CutOp());
        return new ReceiptDoc(ops, w);
    }

    private static TextOp Plain(string text, string id, PieceAlign align = PieceAlign.Left) => new(text, align, false, TextSize.Normal, false, false, id);

    private static void Items(List<ReceiptOp> ops, ReceiptPiece p, ReceiptData data, int w)
    {
        var n = 0;
        foreach (var l in data.Lines)
        {
            n++;
            var prefix = p.Numbered ? $"{n}. " : "";
            if (p.Layout == ItemLayout.OneLine)
            {
                var right = $"{l.Qty} x {MoneyText.M(l.UnitPrice)} {MoneyText.M(l.UnitPrice * l.Qty),10}";
                var nameWidth = w - right.Length - 1;
                if (nameWidth >= 12)
                {
                    ops.Add(Plain(Fit(ReceiptRenderer.Printable(prefix + l.Name), nameWidth).PadRight(nameWidth) + " " + right, p.Id));
                }
                else
                {
                    // Narrow paper: the name on its own line, the numbers right-aligned under it.
                    ops.Add(Plain(Fit(ReceiptRenderer.Printable(prefix + l.Name), w), p.Id));
                    ops.Add(Plain(right.Trim().PadLeft(w), p.Id));
                }
            }
            else
            {
                foreach (var line in Wrap(prefix + l.Name, w)) ops.Add(new TextOp(line, PieceAlign.Left, true, TextSize.Normal, false, false, p.Id));
                ops.Add(Plain(Columns($"  {l.Qty} x {MoneyText.M(l.UnitPrice)}", MoneyText.M(l.UnitPrice * l.Qty), w), p.Id));
            }
            if (p.ShowBarcode && l.Barcode is { Length: > 0 }) ops.Add(Plain("  " + l.Barcode, p.Id));
            if (p.ShowDiscounts)
            {
                if (l.DiscountAmount > 0) ops.Add(Plain(Columns($"  Discount{(l.DiscountText is { } dt ? $" ({dt})" : "")}", "-" + MoneyText.M(l.DiscountAmount), w), p.Id));
            }
            if (p.ShowPriceChanges && l.PriceChanged)
            {
                ops.Add(Plain($"  (was {MoneyText.M(l.ListPrice)})", p.Id));
            }
        }
    }

    private static void Totals(List<ReceiptOp> ops, ReceiptPiece p, ReceiptData data, int w)
    {
        ops.Add(Plain(Columns("Subtotal", MoneyText.M(data.Subtotal), w), p.Id));
        if (data.LineDiscountTotal > 0) ops.Add(Plain(Columns("Item discounts", "-" + MoneyText.M(data.LineDiscountTotal), w), p.Id));
        if (data.BillDiscountAmount > 0)
            ops.Add(Plain(Columns($"Bill discount{(data.BillDiscountText is { } t ? $" ({t})" : "")}", "-" + MoneyText.M(data.BillDiscountAmount), w), p.Id));
        var amount = "Rs " + MoneyText.M(data.Total);
        if (p.BigTotal && "TOTAL".Length + 1 + amount.Length <= w / 2)
            ops.Add(new TextOp(Columns("TOTAL", amount, w / 2), PieceAlign.Left, true, TextSize.Large, false, false, p.Id));
        else if (p.BigTotal)
        {
            // Narrow paper or a big amount: the word on one line, the amount large (or tall) under it.
            ops.Add(new TextOp("TOTAL", PieceAlign.Left, true, TextSize.Normal, false, false, p.Id));
            ops.Add(new TextOp(amount, PieceAlign.Right, true, amount.Length <= w / 2 ? TextSize.Large : TextSize.Tall, false, false, p.Id));
        }
        else
            ops.Add(new TextOp(Columns("TOTAL", "Rs " + MoneyText.M(data.Total), w), PieceAlign.Left, true, TextSize.Normal, false, false, p.Id));
    }

    public static bool Holds(PrintWhen when, ReceiptData d) => when switch
    {
        PrintWhen.Cash => d.Payments.Any(p => p.Method == PaymentMethod.Cash),
        PrintWhen.Card => d.Payments.Any(p => p.Method == PaymentMethod.Card),
        PrintWhen.Bank => d.Payments.Any(p => p.Method == PaymentMethod.BankTransfer),
        PrintWhen.Discount => d.LineDiscountTotal + d.BillDiscountAmount > 0,
        PrintWhen.PriceChanged => d.Lines.Any(l => l.PriceChanged),
        PrintWhen.Reprint => d.IsReprint,
        PrintWhen.NotReprint => !d.IsReprint,
        _ => true,
    };

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex FieldRe();

    public static string Fill(string text, ReceiptDesign design, ReceiptData d) =>
        FieldRe().Replace(text ?? "", m => Value(m.Groups[1].Value, design, d) ?? m.Value);

    /// <summary>A line whose fields are all empty (e.g. "Tel: {shop_phone}" with no phone) is left out.</summary>
    private static bool HasEmptyField(string text, ReceiptDesign design, ReceiptData d)
    {
        var fields = FieldRe().Matches(text ?? "").Select(m => Value(m.Groups[1].Value, design, d)).ToList();
        return fields.Count > 0 && fields.All(string.IsNullOrWhiteSpace);
    }

    private static string? Value(string field, ReceiptDesign design, ReceiptData d) => field switch
    {
        "shop_name" => d.ShopName,
        "shop_address" => d.ShopAddress,
        "shop_phone" => d.ShopPhone,
        "shop_email" => d.ShopEmail,
        "header" => design.HeaderText,
        "footer" => design.FooterText,
        "bill_no" => d.BillNumber.ToString(CultureInfo.InvariantCulture),
        "date" => d.Date.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        "day" => d.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        "time" => d.Date.ToString("HH:mm", CultureInfo.InvariantCulture),
        "cashier" => d.Cashier,
        "total" => "Rs " + MoneyText.M(d.Total),
        "item_count" => d.Lines.Count.ToString(CultureInfo.InvariantCulture),
        "payment_method" => string.Join(" + ", d.Payments.Select(p => p.Method switch { PaymentMethod.Cash => "Cash", PaymentMethod.Card => "Card", _ => "Bank" }).Distinct()),
        "note" => d.Note ?? "",
        _ => null,
    };

    public static string Columns(string left, string right, int width)
    {
        left = Printable(left);
        right = Printable(right);
        var space = width - right.Length - 1;
        if (space < 4) return Fit(left + " " + right, width);
        return Fit(left, space).PadRight(space) + " " + right;
    }

    private static string Fit(string s, int width) => s.Length <= width ? s : s[..Math.Max(0, width - 1)] + ".";

    private static string Repeat(string pattern, int width)
    {
        var sb = new StringBuilder();
        while (sb.Length < width) sb.Append(pattern);
        return sb.ToString()[..width].TrimEnd();
    }

    /// <summary>Splits text into lines no longer than <paramref name="width"/>, breaking on spaces.</summary>
    public static IEnumerable<string> Wrap(string text, int width)
    {
        text = Printable(text).TrimEnd();
        if (text.Length == 0) yield break;
        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            var w = word;
            while (w.Length > width)
            {
                if (line.Length > 0) { yield return line.ToString(); line.Clear(); }
                yield return w[..width];
                w = w[width..];
            }
            if (line.Length + (line.Length > 0 ? 1 : 0) + w.Length > width) { yield return line.ToString(); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(w);
        }
        if (line.Length > 0) yield return line.ToString();
    }

    private static readonly Dictionary<char, string> Swap = new()
    {
        ['–'] = "-", ['—'] = "-", ['−'] = "-", ['‘'] = "'", ['’'] = "'", ['“'] = "\"", ['”'] = "\"",
        ['•'] = "*", ['×'] = "x", ['…'] = "...", ['→'] = "->", [' '] = " ",
    };

    /// <summary>Receipt printers use a simple character set: keep plain ASCII, swap common symbols.</summary>
    public static string Printable(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormKD))
        {
            if (Swap.TryGetValue(ch, out var r)) sb.Append(r);
            else if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            else sb.Append(ch is >= ' ' and <= '~' ? ch : '?');
        }
        return sb.ToString();
    }

    /// <summary>The receipt as plain text (file printing, tests and the "printer view").</summary>
    public static string ToText(ReceiptDoc doc)
    {
        var sb = new StringBuilder();
        var w = doc.CharsPerLine;
        foreach (var op in doc.Ops)
        {
            switch (op)
            {
                case TextOp t:
                    var text = t.Size is TextSize.Wide or TextSize.Large ? string.Join(" ", t.Text.ToCharArray()) : t.Text;
                    sb.AppendLine(t.Align switch
                    {
                        PieceAlign.Center => text.PadLeft((w + text.Length) / 2),
                        PieceAlign.Right => text.PadLeft(w),
                        _ => text,
                    });
                    break;
                case LogoOp: sb.AppendLine("[LOGO]".PadLeft((w + 6) / 2)); break;
                case BarcodeOp b: sb.AppendLine($"||| {b.Value} |||".PadLeft((w + b.Value.Length + 8) / 2)); break;
                case QrOp q: sb.AppendLine("[QR " + q.Value + "]"); break;
                case FeedOp f: for (var i = 0; i < f.Lines; i++) sb.AppendLine(); break;
                case CutOp: sb.AppendLine(new string('~', w)); break;
            }
        }
        return sb.ToString();
    }
}
