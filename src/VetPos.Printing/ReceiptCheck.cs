namespace VetPos.Printing;

public enum IssueLevel { Error, Warning, Tip }

public sealed record ReceiptIssue(IssueLevel Level, string Message, string? PieceId, Func<ReceiptDesign, ReceiptDesign>? Fix, string? FixLabel);

/// <summary>A preflight for the receipt design, with one-click fixes where possible.</summary>
public static class ReceiptCheck
{
    public static IReadOnlyList<ReceiptIssue> Check(ReceiptDesign d, int charsPerLine = 48)
    {
        var issues = new List<ReceiptIssue>();
        var visible = d.Pieces.Where(p => !p.Hidden).ToList();

        if (!visible.Any(p => p.Type == PieceType.Items))
            issues.Add(new(IssueLevel.Error, "The receipt does not show the items bought.", null, x => Insert(x, PieceType.Items), "Add items"));
        if (!visible.Any(p => p.Type == PieceType.Totals))
            issues.Add(new(IssueLevel.Error, "The receipt does not show the total.", null, x => Insert(x, PieceType.Totals), "Add total"));
        if (!visible.Any(p => p.Type == PieceType.Barcode) && !visible.Any(p => p.Text.Contains("{bill_no}", StringComparison.Ordinal) || p.Right.Contains("{bill_no}", StringComparison.Ordinal)))
            issues.Add(new(IssueLevel.Warning, "The bill number is not printed, so the bill is hard to find later.", null, x => Insert(x, PieceType.Barcode), "Add bill barcode"));
        if (!visible.Any(p => p.Type == PieceType.Payments))
            issues.Add(new(IssueLevel.Tip, "Customers like to see what they paid and their change.", null, x => Insert(x, PieceType.Payments), "Add payment"));

        foreach (var p in visible)
        {
            if (p.Type == PieceType.Qr && string.IsNullOrWhiteSpace(p.Value))
                issues.Add(new(IssueLevel.Error, "A QR code is empty.", p.Id, null, null));
            if (p.Type == PieceType.Text && p.Size is TextSize.Wide or TextSize.Large && !p.Text.Contains('{', StringComparison.Ordinal) && p.Text.Length > charsPerLine / 2)
                issues.Add(new(IssueLevel.Warning, $"\"{ReceiptFields.Describe(p)}\" is too long for big letters and will wrap.", p.Id, x => Set(x, p.Id, q => q.Size = TextSize.Tall), "Use tall letters"));
            if (p.Type == PieceType.Text && string.IsNullOrWhiteSpace(p.Text))
                issues.Add(new(IssueLevel.Tip, "There is an empty text piece.", p.Id, x => Remove(x, p.Id), "Remove it"));
        }
        for (var i = 1; i < visible.Count; i++)
            if (visible[i].Type == PieceType.Divider && visible[i - 1].Type == PieceType.Divider)
                issues.Add(new(IssueLevel.Tip, "Two lines in a row waste paper.", visible[i].Id, x => Remove(x, visible[i].Id), "Remove one"));
        return issues;
    }

    /// <summary>Less paper: no empty lines, one line per item, no two lines in a row (locked pieces stay).</summary>
    public static ReceiptDesign SavePaper(ReceiptDesign d)
    {
        var copy = d.Clone();
        var result = new List<ReceiptPiece>();
        foreach (var p in copy.Pieces)
        {
            if (!p.Locked)
            {
                if (p.Type == PieceType.Space) continue;
                if (p.Type == PieceType.Items) p.Layout = ItemLayout.OneLine;
                if (p.Type == PieceType.Divider && result.LastOrDefault(x => !x.Hidden)?.Type == PieceType.Divider) continue;
            }
            result.Add(p);
        }
        copy.Pieces = result;
        return copy;
    }

    private static ReceiptDesign Insert(ReceiptDesign d, PieceType type)
    {
        var copy = d.Clone();
        var at = type == PieceType.Items ? copy.Pieces.FindIndex(p => p.Type == PieceType.Totals) : copy.Pieces.FindIndex(p => p.Type == PieceType.Text && p.Text.Contains("{footer}", StringComparison.Ordinal));
        copy.Pieces.Insert(at < 0 ? copy.Pieces.Count : at, new ReceiptPiece { Type = type });
        return copy;
    }

    private static ReceiptDesign Remove(ReceiptDesign d, string id)
    {
        var copy = d.Clone();
        copy.Pieces = d.Pieces.Where(p => p.Id != id).Select(p => p).ToList();
        return copy;
    }

    private static ReceiptDesign Set(ReceiptDesign d, string id, Action<ReceiptPiece> change)
    {
        var copy = new ReceiptDesign { HeaderText = d.HeaderText, FooterText = d.FooterText, Pieces = [.. d.Pieces] };
        var i = copy.Pieces.FindIndex(p => p.Id == id);
        if (i >= 0)
        {
            var clone = copy.Pieces[i].Clone();
            clone.Id = id;
            change(clone);
            copy.Pieces[i] = clone;
        }
        return copy;
    }
}
