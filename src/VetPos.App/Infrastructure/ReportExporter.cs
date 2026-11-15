using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace VetPos.App.Infrastructure;

/// <summary>A report table ready to export: title, period, summary figures and rows.</summary>
public sealed record ExportTable(
    string Title,
    string Period,
    IReadOnlyList<(string Label, string Value)> Summary,
    IReadOnlyList<string> Columns,
    IReadOnlyList<bool> RightAligned,
    IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Clean, printable PDF (A4) and real Excel files for every report.</summary>
public static class ReportExporter
{
    static ReportExporter() => QuestPDF.Settings.License = LicenseType.Community; // free for businesses under USD 1M a year

    private const string Clay = "#0B7A75";
    private const string Ink = "#13201F";
    private const string Muted = "#536563";
    private const string Line = "#E1E8E8";

    public static void Pdf(ExportTable t, string shopName, string file)
    {
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontSize(9.5f).FontColor(Ink).FontFamily("Segoe UI", "Arial"));
            page.Header().PaddingBottom(14).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(shopName).FontSize(10).FontColor(Muted);
                    c.Item().Text(t.Title).FontSize(20).SemiBold().FontColor(Ink);
                    c.Item().Text(t.Period).FontColor(Muted);
                });
                row.ConstantItem(130).AlignRight().AlignBottom().Text($"Printed {DateTime.Now:dd MMM yyyy HH:mm}").FontSize(8).FontColor(Muted);
            });
            page.Content().Column(col =>
            {
                if (t.Summary.Count > 0)
                {
                    col.Item().PaddingBottom(16).Row(row =>
                    {
                        foreach (var (label, value) in t.Summary.Take(6))
                            row.RelativeItem().PaddingRight(8).Border(0.75f).BorderColor(Line).Padding(8).Column(c =>
                            {
                                c.Item().Text(label).FontSize(8).FontColor(Muted);
                                c.Item().Text(value).FontSize(12).SemiBold();
                            });
                    });
                }
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        for (var i = 0; i < t.Columns.Count; i++)
                            if (t.RightAligned[i]) cd.ConstantColumn(78); else cd.RelativeColumn();
                    });
                    table.Header(h =>
                    {
                        for (var i = 0; i < t.Columns.Count; i++)
                        {
                            var cell = h.Cell().BorderBottom(1).BorderColor(Clay).PaddingVertical(5).PaddingHorizontal(3);
                            var text = (t.RightAligned[i] ? cell.AlignRight() : cell).Text(t.Columns[i].ToUpperInvariant());
                            text.FontSize(7.5f).SemiBold().FontColor(Muted);
                        }
                    });
                    foreach (var r in t.Rows)
                        for (var i = 0; i < t.Columns.Count; i++)
                        {
                            var cell = table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4).PaddingHorizontal(3);
                            (t.RightAligned[i] ? cell.AlignRight() : cell).Text(i < r.Count ? r[i] : "");
                        }
                });
                if (t.Rows.Count == 0) col.Item().PaddingTop(20).AlignCenter().Text("No data for this period.").FontColor(Muted);
            });
            page.Footer().AlignRight().Text(x =>
            {
                x.Span("VetPOS · page ").FontSize(8).FontColor(Muted);
                x.CurrentPageNumber().FontSize(8).FontColor(Muted);
                x.Span(" of ").FontSize(8).FontColor(Muted);
                x.TotalPages().FontSize(8).FontColor(Muted);
            });
        })).GeneratePdf(file);
    }

    public static void Excel(ExportTable t, string shopName, string file)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName(t.Title));
        ws.Cell(1, 1).Value = shopName;
        ws.Cell(2, 1).Value = t.Title;
        ws.Cell(2, 1).Style.Font.SetBold().Font.SetFontSize(16);
        ws.Cell(3, 1).Value = t.Period;
        var row = 5;
        foreach (var (label, value) in t.Summary)
        {
            ws.Cell(row, 1).Value = label;
            SetValue(ws.Cell(row, 2), value);
            ws.Cell(row, 1).Style.Font.SetFontColor(XLColor.FromHtml(Muted));
            row++;
        }
        if (t.Summary.Count > 0) row++;
        var headerRow = row;
        for (var i = 0; i < t.Columns.Count; i++)
        {
            var c = ws.Cell(row, i + 1);
            c.Value = t.Columns[i];
            c.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#EEF3F3"));
        }
        foreach (var r in t.Rows)
        {
            row++;
            for (var i = 0; i < r.Count; i++) SetValue(ws.Cell(row, i + 1), r[i]);
        }
        if (t.Rows.Count > 0) ws.Range(headerRow, 1, row, t.Columns.Count).SetAutoFilter();
        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents(8, 60);
        wb.SaveAs(file);
    }

    /// <summary>Numbers ("12,450.00") become real numbers so Excel can add them up.</summary>
    private static void SetValue(IXLCell cell, string text)
    {
        var clean = text.Replace("Rs", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal).Replace("−", "-", StringComparison.Ordinal).Trim();
        if (clean.Length > 0 && decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && !text.Contains('/', StringComparison.Ordinal))
        {
            cell.Value = d;
            if (text.Contains('.', StringComparison.Ordinal)) cell.Style.NumberFormat.Format = "#,##0.00";
        }
        else cell.Value = text;
    }

    private static string SheetName(string title)
    {
        var s = new string(title.Where(c => !"[]:*?/\\".Contains(c, StringComparison.Ordinal)).ToArray());
        return s.Length > 31 ? s[..31] : s;
    }

    public static string SafeFileName(string s) => string.Concat(s.Split(Path.GetInvalidFileNameChars())).Replace(' ', '-');
}
