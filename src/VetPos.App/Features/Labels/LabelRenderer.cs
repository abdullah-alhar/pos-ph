using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using VetPos.Core.Pricing;
using VetPos.Printing;

namespace VetPos.App.Features.Labels;

/// <summary>A sticker sheet or a label roll, in millimetres.</summary>
public sealed record LabelLayout(string Name, double PageW, double PageH, int Columns, int Rows, double LabelW, double LabelH,
    double MarginLeft, double MarginTop, double GapX, double GapY, bool IsRoll)
{
    public int PerPage => Columns * Rows;

    public static IReadOnlyList<LabelLayout> All { get; } =
    [
        new("A4 sheet · 24 labels (70 × 37 mm)", 210, 297, 3, 8, 70, 37, 0, 0.5, 0, 0, false),
        new("A4 sheet · 40 labels (52.5 × 29.7 mm)", 210, 297, 4, 10, 52.5, 29.7, 0, 0, 0, 0, false),
        new("A4 sheet · 65 labels (38.1 × 21.2 mm)", 210, 297, 5, 13, 38.1, 21.2, 4.65, 10.7, 2.5, 0, false),
        new("Label printer roll · 50 × 25 mm", 50, 25, 1, 1, 50, 25, 0, 0, 0, 0, true),
        new("Label printer roll · 38 × 25 mm", 38, 25, 1, 1, 38, 25, 0, 0, 0, 0, true),
    ];

    public override string ToString() => Name;
}

public sealed record LabelContent(string ShopName, string Name, decimal Price, string? Barcode);

public sealed record LabelOptions(bool ShopName, bool ProductName, bool Price, bool Barcode);

/// <summary>Draws labels as WPF visuals: the same code shows the preview and prints the pages.</summary>
public static class LabelRenderer
{
    public static double Mm(double mm) => mm * 96.0 / 25.4;

    public static FixedDocument Document(IReadOnlyList<LabelContent> labels, LabelLayout layout, LabelOptions options)
    {
        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = new Size(Mm(layout.PageW), Mm(layout.PageH));
        for (var start = 0; start < labels.Count; start += layout.PerPage)
            doc.Pages.Add(new PageContent { Child = Page(labels.Skip(start).Take(layout.PerPage).ToList(), layout, options) });
        return doc;
    }

    public static FixedPage Page(IReadOnlyList<LabelContent> labels, LabelLayout layout, LabelOptions options)
    {
        var page = new FixedPage { Width = Mm(layout.PageW), Height = Mm(layout.PageH), Background = Brushes.White };
        for (var i = 0; i < labels.Count; i++)
        {
            var col = i % layout.Columns;
            var row = i / layout.Columns;
            var label = Label(labels[i], layout, options);
            FixedPage.SetLeft(label, Mm(layout.MarginLeft + col * (layout.LabelW + layout.GapX)));
            FixedPage.SetTop(label, Mm(layout.MarginTop + row * (layout.LabelH + layout.GapY)));
            page.Children.Add(label);
        }
        return page;
    }

    public static FrameworkElement Label(LabelContent c, LabelLayout layout, LabelOptions o)
    {
        var w = Mm(layout.LabelW);
        var h = Mm(layout.LabelH);
        var pad = Mm(1.8);
        var small = layout.LabelH < 25;
        var font = new FontFamily("Segoe UI");
        var stack = new StackPanel { Width = w - 2 * pad };
        if (o.ShopName && !string.IsNullOrWhiteSpace(c.ShopName))
            stack.Children.Add(new TextBlock { Text = c.ShopName, FontFamily = font, FontSize = small ? 6 : 7, Foreground = Brushes.Black, TextTrimming = TextTrimming.CharacterEllipsis });
        if (o.ProductName)
            stack.Children.Add(new TextBlock
            {
                Text = c.Name, FontFamily = font, FontSize = small ? 7.5 : 9, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap, MaxHeight = small ? 20 : 24, TextTrimming = TextTrimming.CharacterEllipsis,
            });
        if (o.Price)
            stack.Children.Add(new TextBlock
            {
                Text = "Rs " + Money.Format(c.Price), FontFamily = font, FontSize = small ? 10 : 13, FontWeight = FontWeights.Bold, Foreground = Brushes.Black,
                Margin = new Thickness(0, 1, 0, 1),
            });
        if (o.Barcode && !string.IsNullOrWhiteSpace(c.Barcode))
        {
            var used = stack.Children.OfType<TextBlock>().Sum(t => t.MaxHeight is > 0 and < double.PositiveInfinity ? t.MaxHeight : t.FontSize * 1.35);
            var barHeight = Math.Max(Mm(5), h - 2 * pad - used - 10);
            stack.Children.Add(BarcodeVisual(c.Barcode, stack.Width, barHeight));
        }
        return new Border { Width = w, Height = h, Padding = new Thickness(pad), Child = stack, Background = Brushes.White, ClipToBounds = true };
    }

    /// <summary>Bars as rectangles (sharp at any printer resolution) with the number under them.</summary>
    public static FrameworkElement BarcodeVisual(string value, double width, double height)
    {
        BarcodeImage code;
        try { code = Barcodes.Encode(value); }
        catch (ArgumentException) { return new TextBlock { Text = value, FontSize = 8 }; }
        var quiet = 6;
        var module = Math.Max(0.6, width / (code.Modules.Length + 2 * quiet));
        var canvas = new Canvas { Width = module * (code.Modules.Length + 2 * quiet), Height = height, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < code.Modules.Length; i++)
        {
            if (!code.Modules[i]) continue;
            var run = 1;
            while (i + run < code.Modules.Length && code.Modules[i + run]) run++;
            var bar = new Rectangle { Width = module * run, Height = height, Fill = Brushes.Black, SnapsToDevicePixels = true };
            Canvas.SetLeft(bar, module * (quiet + i));
            canvas.Children.Add(bar);
            i += run - 1;
        }
        var text = new TextBlock
        {
            Text = code.Text, FontFamily = new FontFamily("Consolas"), FontSize = 7, Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0),
        };
        return new StackPanel { Children = { new Viewbox { Child = canvas, Stretch = Stretch.Fill, Width = width, Height = height }, text } };
    }

    public static string Count(int labels, LabelLayout layout) => layout.IsRoll
        ? string.Create(CultureInfo.InvariantCulture, $"{labels} label{(labels == 1 ? "" : "s")}")
        : string.Create(CultureInfo.InvariantCulture, $"{labels} label{(labels == 1 ? "" : "s")} · {(labels + layout.PerPage - 1) / Math.Max(1, layout.PerPage)} sheet(s)");
}
