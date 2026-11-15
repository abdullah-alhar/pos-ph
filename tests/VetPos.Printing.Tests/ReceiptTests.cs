using VetPos.Core.Domain;
using VetPos.Printing;

namespace VetPos.Printing.Tests;

public class ReceiptTests
{
    private static readonly ReceiptData Bill = ReceiptData.Sample();

    [Fact]
    public void Classic_receipt_has_shop_items_totals_and_change()
    {
        var text = ReceiptRenderer.ToText(ReceiptRenderer.Render(ReceiptTemplates.Classic(), Bill));
        Assert.Contains("Happy Paws Vet Shop", text, StringComparison.Ordinal);
        Assert.Contains("Bill: 1043", text, StringComparison.Ordinal);
        Assert.Contains("Royal Canin Kitten 2 kg", text, StringComparison.Ordinal);
        Assert.DoesNotContain("->", text, StringComparison.Ordinal); // a changed price is not shown to the customer by default
        Assert.DoesNotContain("(was ", text, StringComparison.Ordinal);
        Assert.Contains("Discount (Rs 20.00 each)", text, StringComparison.Ordinal);
        Assert.Contains("Change", text, StringComparison.Ordinal);
        Assert.Contains("640.00", text, StringComparison.Ordinal);
        var total = ReceiptRenderer.Render(ReceiptTemplates.Classic(), Bill).Ops.OfType<TextOp>().Single(t => t.Text.StartsWith("TOTAL", StringComparison.Ordinal));
        Assert.Equal(TextSize.Large, total.Size);
        Assert.EndsWith("Rs 13,360.00", total.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("COPY", text, StringComparison.Ordinal); // only on reprints
    }

    [Fact]
    public void Every_line_fits_the_paper()
    {
        foreach (var (_, _, build) in ReceiptTemplates.All)
            foreach (var width in new[] { 32, 48 })
            {
                var doc = ReceiptRenderer.Render(build(), Bill, width);
                Assert.All(doc.Ops.OfType<TextOp>(), t =>
                    Assert.True(t.Text.Length <= (t.Size is TextSize.Wide or TextSize.Large ? width / 2 : width), $"\"{t.Text}\" is wider than {width}"));
            }
    }

    [Fact]
    public void Lines_with_only_empty_fields_are_left_out()
    {
        var d = new ReceiptDesign { Pieces = [new ReceiptPiece { Type = PieceType.Text, Text = "Tel: {shop_phone}" }] };
        var data = Bill with { ShopPhone = "" };
        Assert.DoesNotContain(ReceiptRenderer.Render(d, data).Ops, o => o is TextOp);
    }

    [Fact]
    public void Print_when_rules()
    {
        var d = new ReceiptDesign
        {
            Pieces =
            [
                new ReceiptPiece { Type = PieceType.Text, Text = "COPY", When = PrintWhen.Reprint },
                new ReceiptPiece { Type = PieceType.Text, Text = "Paid by card", When = PrintWhen.Card },
                new ReceiptPiece { Type = PieceType.Text, Text = "Hidden", Hidden = true },
            ],
        };
        Assert.Empty(ReceiptRenderer.Render(d, Bill).Ops.OfType<TextOp>());
        var reprint = Bill with { IsReprint = true, Payments = [new ReceiptPayment(PaymentMethod.Card, 13360m, 13360m, "4821")] };
        Assert.Equal(["COPY", "Paid by card"], ReceiptRenderer.Render(d, reprint).Ops.OfType<TextOp>().Select(t => t.Text));
    }

    [Fact]
    public void Voided_bill_says_so_at_the_top()
    {
        var doc = ReceiptRenderer.Render(ReceiptTemplates.Minimal(), Bill with { IsVoided = true });
        Assert.Equal("*** VOIDED ***", ((TextOp)doc.Ops[0]).Text);
    }

    [Fact]
    public void Wrap_breaks_on_spaces_and_splits_long_words()
    {
        Assert.Equal(["one two", "three"], ReceiptRenderer.Wrap("one two three", 8));
        Assert.Equal(["abcdefgh", "ij"], ReceiptRenderer.Wrap("abcdefghij", 8));
    }

    [Fact]
    public void Printable_swaps_symbols_printers_lack() =>
        Assert.Equal("Price 450 -> 400 - ok x2", ReceiptRenderer.Printable("Price 450 → 400 – ok ×2"));

    [Fact]
    public void Check_finds_missing_items_and_fixes_it()
    {
        var d = new ReceiptDesign { Pieces = [new ReceiptPiece { Type = PieceType.Totals }] };
        var issue = Assert.Single(ReceiptCheck.Check(d), i => i.Level == IssueLevel.Error);
        var fixedDesign = issue.Fix!(d);
        Assert.Contains(fixedDesign.Pieces, p => p.Type == PieceType.Items);
        Assert.DoesNotContain(ReceiptCheck.Check(fixedDesign), i => i.Level == IssueLevel.Error);
        Assert.DoesNotContain(ReceiptCheck.Check(ReceiptTemplates.Classic()), i => i.Level == IssueLevel.Error);
    }

    [Fact]
    public void Save_paper_removes_spaces_and_double_lines_but_keeps_locked()
    {
        var d = new ReceiptDesign
        {
            Pieces =
            [
                new ReceiptPiece { Type = PieceType.Space },
                new ReceiptPiece { Type = PieceType.Space, Locked = true },
                new ReceiptPiece { Type = PieceType.Divider },
                new ReceiptPiece { Type = PieceType.Divider },
                new ReceiptPiece { Type = PieceType.Items },
            ],
        };
        var saved = ReceiptCheck.SavePaper(d);
        Assert.Equal([PieceType.Space, PieceType.Divider, PieceType.Items], saved.Pieces.Select(p => p.Type));
        Assert.Equal(ItemLayout.OneLine, saved.Pieces[2].Layout);
    }

    [Fact]
    public void Design_round_trips_as_json_and_bad_json_gives_the_classic()
    {
        var d = ReceiptTemplates.Modern();
        Assert.Equal(d.Pieces.Count, ReceiptDesign.FromJson(d.ToJson()).Pieces.Count);
        Assert.Equal(ReceiptTemplates.Classic().Pieces.Count, ReceiptDesign.FromJson("{nonsense").Pieces.Count);
    }

    [Fact]
    public void Esc_pos_starts_with_init_and_ends_with_cut()
    {
        var bytes = EscPos.Encode(ReceiptRenderer.Render(ReceiptTemplates.Classic(), Bill), new EscPosOptions(OpenDrawer: true));
        Assert.Equal(new byte[] { 0x1B, (byte)'@' }, bytes[..2]);
        Assert.Equal(EscPos.DrawerKick(), bytes[^5..]);
        Assert.Contains((byte)'V', bytes);
    }
}
