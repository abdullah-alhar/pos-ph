using System.Globalization;
using System.Windows.Data;

namespace VetPos.App.Features.Labels;

/// <summary>Barcode text → the bars, for the product form.</summary>
public sealed class BarcodePreviewConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } s ? LabelRenderer.BarcodeVisual(s.Trim(), 220, 46) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
