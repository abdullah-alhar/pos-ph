using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VetPos.Printing;

namespace VetPos.App.Features.BillEditor;

public sealed class PieceNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ReceiptPiece p ? ReceiptFields.Describe(p) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Printable width at real size (96 px per inch): 72 mm for 80 mm paper, 48 mm for 58 mm paper.
/// 48 / 32 Consolas characters at 10.3 px fill it exactly, as on the printer.
/// </summary>
public sealed class PaperWidthConverter : IValueConverter
{
    private const double PxPerMm = 96 / 25.4;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? 48 * PxPerMm : 72 * PxPerMm;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class AlignToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        PieceAlign.Center => TextAlignment.Center,
        PieceAlign.Right => TextAlignment.Right,
        _ => TextAlignment.Left,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class LineHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is double d ? d * 1.3 : 16.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>"Pair:Left text|Signature:Label|*:Text" → the label for the piece type.</summary>
public sealed class EqualsLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value?.ToString();
        string? fallback = null;
        foreach (var part in (parameter as string ?? "").Split('|'))
        {
            var kv = part.Split(':', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == key) return kv[1];
            if (kv[0] == "*") fallback = kv[1];
        }
        return fallback ?? "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class IssueBadgeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        "Error" => "Badge.Danger",
        "Warning" => "Badge.Warning",
        _ => "Badge.Accent",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class AlignToHorizontalConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        PieceAlign.Center => HorizontalAlignment.Center,
        PieceAlign.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
