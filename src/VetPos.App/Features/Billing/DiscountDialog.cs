using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VetPos.App.DesignSystem;
using VetPos.App.DesignSystem.Controls;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;

namespace VetPos.App.Features.Billing;

public sealed record DiscountResult(Discount? Discount);

/// <summary>
/// Line discount (Rs off each · Rs off the line · %) or bill discount (Rs · %). Tab switches the
/// type; typing "%" switches to percent. A discount can never take the total below zero.
/// </summary>
public static class DiscountDialog
{
    public static async Task<DiscountResult?> ShowAsync(IDialogService dialogs, string title, decimal baseAmount, decimal unitPrice, int qty, Discount? current, bool isBill)
    {
        var types = isBill
            ? new[] { (DiscountType.Amount, "Rs off the bill"), (DiscountType.Percent, "% off") }
            : new[] { (DiscountType.PerUnit, "Rs off each"), (DiscountType.Amount, "Rs off the line"), (DiscountType.Percent, "% off") };
        var type = current?.Type ?? types[0].Item1;

        var sheet = new DialogSheet(dialogs, title, isBill
            ? $"The bill after item discounts is {Money.Format(baseAmount, true)}."
            : $"{qty} × {Money.Format(unitPrice, true)} = {Money.Format(baseAmount, true)}", 460);

        var segments = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var buttons = new List<(DiscountType Type, RadioButton Button)>();
        foreach (var (t, label) in types)
        {
            var rb = new RadioButton { Content = label, GroupName = "dtype" + title, IsChecked = t == type, Focusable = false };
            rb.SetResourceReference(FrameworkElement.StyleProperty, "Segment");
            segments.Children.Add(rb);
            buttons.Add((t, rb));
        }
        var segWrap = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left, Child = segments };
        segWrap.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceSubtle");
        sheet.Add(segWrap);

        var input = sheet.Field("Discount", DialogSheet.Input(current?.ToInput() ?? "", number: true), "Tab changes the type · type 10% for a percentage · empty or 0 removes it");
        var preview = sheet.Add(new TextBlock { Margin = new Thickness(2, 14, 0, 0), FontSize = 15, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });

        Discount? Parse(out string? error)
        {
            var text = input.Text.Trim();
            if (type == DiscountType.Percent && text.Length > 0 && !text.EndsWith('%')) text += "%";
            if (!Discount.TryParse(text, type == DiscountType.Percent ? DiscountType.Amount : type, out var d, out error)) return null;
            error = isBill ? CartCalculator.CheckBillDiscount(baseAmount, d) : CartCalculator.CheckLineDiscount(unitPrice, qty, d);
            return d;
        }

        void Refresh()
        {
            foreach (var (t, b) in buttons) b.IsChecked = t == type;
            var d = Parse(out var error);
            if (error is not null)
            {
                preview.Text = error;
                preview.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
                return;
            }
            var amount = d is null ? 0 : isBill ? CartCalculator.BillDiscountAmount(baseAmount, d) : CartCalculator.LineDiscountAmount(unitPrice, qty, d);
            preview.Text = d is null ? "No discount." : $"Discount {Money.Format(amount, true)}  ·  new total {Money.Format(baseAmount - amount, true)}";
            preview.SetResourceReference(TextBlock.ForegroundProperty, d is null ? "Brush.TextSecondary" : "Brush.Success");
        }

        foreach (var (t, b) in buttons)
            b.Checked += (_, _) => { type = t; Refresh(); input.Focus(); };
        input.TextChanged += (_, _) =>
        {
            if (input.Text.Contains('%', StringComparison.Ordinal) && type != DiscountType.Percent) type = DiscountType.Percent;
            Refresh();
        };
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Tab) return;
            var i = Array.FindIndex(types, x => x.Item1 == type);
            type = types[(i + (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? types.Length - 1 : 1)) % types.Length].Item1;
            input.Text = input.Text.Replace("%", "", StringComparison.Ordinal);
            Refresh();
            e.Handled = true;
        };
        input.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        Refresh();

        if (current is not null) sheet.Button("Remove discount", () => sheet.Close(new DiscountResult(null)), "Button.Subtle");
        sheet.Buttons("Apply", () =>
        {
            var d = Parse(out var error);
            if (error is not null) { Ds.SetError(input, null); Ds.SetError(input, error); return; }
            sheet.Close(new DiscountResult(d));
        });
        return await dialogs.ShowAsync(sheet) as DiscountResult;
    }
}
