using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VetPos.App.DesignSystem;

/// <summary>
/// 12450.5 → "12,450.50" (or "Rs 12,450.50"). Always two decimals and Sri Lankan grouping,
/// whatever the Windows region is set to, so bills and screens always read the same.
/// </summary>
public sealed class MoneyConverter : IValueConverter
{
    public bool WithSymbol { get; set; }

    public static string Format(decimal value, bool withSymbol = false)
    {
        var text = Math.Abs(value).ToString("#,##0.00", CultureInfo.InvariantCulture);
        var sign = value < 0 ? "−" : "";
        return withSymbol ? $"{sign}Rs {text}" : sign + text;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        decimal d => Format(d, WithSymbol),
        null => "",
        _ => value.ToString() ?? "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>null or empty → Collapsed, anything else → Visible.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Number → Visible when greater than zero.</summary>
public sealed class PositiveToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var positive = value switch { int i => i > 0, decimal d => d > 0, _ => false };
        if (parameter is "invert") positive = !positive;
        return positive ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Date → "13 Nov 2026" (or "—").</summary>
public sealed class DateTextConverter : IValueConverter
{
    public string Format { get; set; } = "dd MMM yyyy";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var format = parameter is "x" ? "ddd, dd MMM yyyy" : Format;
        return value switch
        {
            DateOnly d => d.ToString(format, CultureInfo.InvariantCulture),
            DateTime t => t.ToString(format, CultureInfo.InvariantCulture),
            _ => "—",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Equal to the parameter → true (radio buttons bound to an enum or string).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string p && targetType.IsEnum ? Enum.Parse(targetType, p) : value is true ? parameter! : Binding.DoNothing;
}

/// <summary>All bindings true → Visible.</summary>
public sealed class AllTrueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class IsNegativeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int i && i < 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>-3 → "Expired 3 days ago", 0 → "Expires today", 12 → "12 days left".</summary>
public sealed class DaysLeftConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        int d when d < 0 => $"Expired {-d} day{(d == -1 ? "" : "s")} ago",
        0 => "Expires today",
        int d => $"{d} day{(d == 1 ? "" : "s")} left",
        _ => "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible when the value equals the parameter (or one of "A|B").</summary>
public sealed class EqualsVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (parameter as string ?? "").Split('|').Contains(value?.ToString()) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>null → Visible (for "choose something" placeholders).</summary>
public sealed class NullToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Notice level → its colour dot.</summary>
public sealed class NoticeBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.TryFindResource(value?.ToString() switch { "Danger" => "Brush.Danger", "Warning" => "Brush.Warning", _ => "Brush.Accent" });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>0 → "1.", 1 → "2." (numbered steps).</summary>
public sealed class StepNumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int i ? $"{i + 1}." : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
