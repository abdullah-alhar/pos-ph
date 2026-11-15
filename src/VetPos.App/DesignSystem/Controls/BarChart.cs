using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace VetPos.App.DesignSystem.Controls;

/// <summary>One bar: the label under it, its value, and the text shown when the mouse is on it.</summary>
public sealed record ChartBar(string Label, decimal Value, string Detail, bool Highlight = false);

/// <summary>
/// A light, fast bar chart (drawn directly, no chart library). One series in the theme colour,
/// rounded tops on a shared baseline, faint grid lines with Rs amounts, and a label above the bar
/// under the mouse. Labels under the bars are thinned out so they never overlap.
/// </summary>
public sealed class BarChart : FrameworkElement
{
    public static readonly DependencyProperty BarsProperty = DependencyProperty.Register(nameof(Bars), typeof(IEnumerable), typeof(BarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Bars
    {
        get => (IEnumerable?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    private const double AxisWidth = 64;
    private const double LabelHeight = 26;
    private const double TopPad = 34;
    private int _hover = -1;

    public BarChart()
    {
        MinHeight = 200;
        Cursor = Cursors.Arrow;
        // Re-draw when the colour theme changes (the bars use the accent colour).
        SetResourceReference(BarBrushProperty, "Brush.Accent");
        SetResourceReference(HoverBrushProperty, "Brush.AccentHover");
        SetResourceReference(TextBrushProperty, "Brush.Text");
        SetResourceReference(MutedBrushProperty, "Brush.TextTertiary");
        SetResourceReference(LineBrushProperty, "Brush.Line");
    }

    private static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register("BarBrush", typeof(Brush), typeof(BarChart), new FrameworkPropertyMetadata(Brushes.Teal, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty HoverBrushProperty = DependencyProperty.Register("HoverBrush", typeof(Brush), typeof(BarChart), new FrameworkPropertyMetadata(Brushes.DarkCyan, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register("TextBrush", typeof(Brush), typeof(BarChart), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty MutedBrushProperty = DependencyProperty.Register("MutedBrush", typeof(Brush), typeof(BarChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register("LineBrush", typeof(Brush), typeof(BarChart), new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    private List<ChartBar> Items => Bars?.OfType<ChartBar>().ToList() ?? [];

    private Rect Plot => new(AxisWidth, TopPad, Math.Max(0, ActualWidth - AxisWidth - 8), Math.Max(0, ActualHeight - TopPad - LabelHeight));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var items = Items;
        var plot = Plot;
        var x = e.GetPosition(this).X;
        var i = items.Count == 0 || plot.Width <= 0 ? -1 : (int)Math.Floor((x - plot.X) / (plot.Width / items.Count));
        if (i < 0 || i >= items.Count) i = -1;
        if (i != _hover) { _hover = i; InvalidateVisual(); }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1) { _hover = -1; InvalidateVisual(); }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;
        // Transparent background so the whole area takes the mouse (hit target bigger than the bars).
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var items = Items;
        var plot = Plot;
        if (plot.Width < 20 || plot.Height < 20) return;

        var bar = (Brush)GetValue(BarBrushProperty);
        var hoverBrush = (Brush)GetValue(HoverBrushProperty);
        var text = (Brush)GetValue(TextBrushProperty);
        var muted = (Brush)GetValue(MutedBrushProperty);
        var linePen = new Pen((Brush)GetValue(LineBrushProperty), 1);
        linePen.Freeze();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var font = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var fontFamily = TryFindResource("Font.Text") as FontFamily;
        if (fontFamily is not null) font = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var bold = new Typeface(font.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        FormattedText Text(string s, double size, Brush b, Typeface? t = null) =>
            new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, t ?? font, size, b, dpi);

        // Grid: 4 steps of a round number.
        var max = items.Count == 0 ? 0 : items.Max(b => b.Value);
        var step = NiceStep(max <= 0 ? 1000 : (double)max / 4);
        var top = step * 4;
        if (top < (double)max) top = step * Math.Ceiling((double)max / step);
        for (var g = 0; g <= 4; g++)
        {
            var v = top / 4 * g;
            var y = Math.Round(plot.Bottom - plot.Height * (v / top)) + 0.5;
            dc.DrawLine(linePen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(Compact(v), 13, muted);
            dc.DrawText(label, new Point(plot.Left - 10 - label.Width, y - label.Height / 2));
        }

        if (items.Count == 0) return;
        var slot = plot.Width / items.Count;
        var gap = Math.Max(2, Math.Min(slot * 0.28, 14));
        var width = Math.Max(1, slot - gap);
        // Labels under the bars: show every n-th so they never overlap.
        var widest = items.Max(b => Text(b.Label, 13, muted).Width) + 10;
        var every = Math.Max(1, (int)Math.Ceiling(widest / slot));

        for (var i = 0; i < items.Count; i++)
        {
            var b = items[i];
            var x = plot.Left + slot * i + gap / 2;
            var h = top <= 0 ? 0 : plot.Height * ((double)b.Value / top);
            if (b.Value > 0 && h < 3) h = 3;
            if (h > 0)
            {
                var r = Math.Min(4, width / 2);
                var rect = new Rect(x, plot.Bottom - h, width, h);
                dc.DrawGeometry(i == _hover ? hoverBrush : bar, null, TopRounded(rect, r));
            }
            if (i % every == 0 || b.Highlight)
            {
                var label = Text(b.Label, 13, b.Highlight ? text : muted, b.Highlight ? bold : null);
                dc.DrawText(label, new Point(x + width / 2 - label.Width / 2, plot.Bottom + 6));
            }
        }

        // Hover label: the exact amount above the bar.
        if (_hover >= 0 && _hover < items.Count)
        {
            var b = items[_hover];
            var tip = Text(b.Detail, 14, Brushes.White, bold);
            var cx = plot.Left + slot * _hover + slot / 2;
            var h = top <= 0 ? 0 : plot.Height * ((double)b.Value / top);
            var w = tip.Width + 20;
            var tx = Math.Clamp(cx - w / 2, 0, ActualWidth - w);
            var ty = Math.Max(0, plot.Bottom - h - tip.Height - 18);
            dc.DrawRoundedRectangle(text, null, new Rect(tx, ty, w, tip.Height + 10), 8, 8);
            dc.DrawText(tip, new Point(tx + 10, ty + 5));
        }
    }

    private static Geometry TopRounded(Rect r, double radius)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left, r.Bottom), true, true);
            c.LineTo(new Point(r.Left, r.Top + radius), false, false);
            c.ArcTo(new Point(r.Left + radius, r.Top), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(r.Right - radius, r.Top), false, false);
            c.ArcTo(new Point(r.Right, r.Top + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(r.Right, r.Bottom), false, false);
        }
        g.Freeze();
        return g;
    }

    private static double NiceStep(double raw)
    {
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var n = raw / mag;
        return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10) * mag;
    }

    /// <summary>12 500 → "12.5k", 2 000 000 → "2M".</summary>
    public static string Compact(double v) => v switch
    {
        >= 1_000_000 => (v / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (v / 1_000).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        _ => v.ToString("0", CultureInfo.InvariantCulture),
    };
}
