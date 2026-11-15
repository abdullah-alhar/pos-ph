using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VetPos.App.DesignSystem;
using VetPos.App.DesignSystem.Controls;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;

namespace VetPos.App.Features.Billing;

/// <summary>
/// Paying. Left: the total and the bill discount (Rs or % switch). Right: how it is paid.
/// Card and bank transfer start with the exact amount already filled in (that is almost always
/// what is paid). Part by card, the rest in cash: type the card amount, Enter, and the dialog moves
/// to cash for what is left. Enter with nothing typed = the exact amount · F12 = exact cash and
/// finish · C / K / B = cash / card / bank · Esc = back to the bill.
/// </summary>
public static class PaymentDialog
{
    /// <param name="applyBillDiscount">Sets the bill discount on the bill and returns the new totals, or an error message.</param>
    public static async Task<IReadOnlyList<PaymentInput>?> ShowAsync(
        IDialogService dialogs, CartTotals start, Discount? currentDiscount, int itemCount,
        Func<Discount?, (CartTotals Totals, string? Error)> applyBillDiscount)
    {
        var totals = start;
        var payments = new ObservableCollection<PaymentInput>();
        var method = PaymentMethod.Cash;
        var percentMode = currentDiscount?.Type == DiscountType.Percent;
        var sheet = new DialogSheet(dialogs, "Take payment", null, 940);

        static TextBlock T(string text, double size = 14, string? brush = null, FontWeight? weight = null)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
            if (brush is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            return t;
        }
        static Border Hairline(Thickness margin)
        {
            var b = new Border { Height = 1, Margin = margin };
            b.SetResourceReference(Border.BackgroundProperty, "Brush.Line");
            return b;
        }

        // ───────── Left: the total and the bill discount ─────────
        var left = new StackPanel();
        left.Children.Add(T("Total to pay", 14, "Brush.TextSecondary", FontWeights.Medium));
        var totalText = T("", 44, null, FontWeights.Bold);
        totalText.Margin = new Thickness(0, 0, 0, 2);
        left.Children.Add(new Viewbox { Child = totalText, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left });
        left.Children.Add(T($"{itemCount} item{(itemCount == 1 ? "" : "s")}, {start.UnitCount} unit{(start.UnitCount == 1 ? "" : "s")}", 13, "Brush.TextSecondary"));
        left.Children.Add(Hairline(new Thickness(0, 16, 0, 12)));

        TextBlock Row(string label)
        {
            var value = T("", 14);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.Children.Add(T(label, 14, "Brush.TextSecondary"));
            g.Children.Add(value);
            left.Children.Add(g);
            return value;
        }
        var subtotalText = Row("Subtotal");
        var itemDiscText = Row("Item discounts");
        var billDiscText = Row("Bill discount");
        left.Children.Add(Hairline(new Thickness(0, 8, 0, 16)));

        // Bill discount: Rs | % switch + amount
        var discHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var switchBox = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(3) };
        switchBox.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        var rsButton = new RadioButton { Content = "Rs", GroupName = "disctype", Focusable = false, MinWidth = 46 };
        var pctButton = new RadioButton { Content = "%", GroupName = "disctype", Focusable = false, MinWidth = 46 };
        rsButton.SetResourceReference(FrameworkElement.StyleProperty, "Segment");
        pctButton.SetResourceReference(FrameworkElement.StyleProperty, "Segment");
        switchBox.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { rsButton, pctButton } };
        DockPanel.SetDock(switchBox, Dock.Right);
        discHead.Children.Add(switchBox);
        discHead.Children.Add(T("Bill discount", 15, null, FontWeights.SemiBold));
        left.Children.Add(discHead);
        var discount = DialogSheet.Input(currentDiscount is null ? "" : percentMode ? currentDiscount.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : Money.ToInput(currentDiscount.Value), number: true);
        discount.FontSize = 18;
        discount.MinHeight = 48;
        left.Children.Add(discount);
        var discHint = T("", 12, "Brush.TextSecondary");
        discHint.Margin = new Thickness(2, 6, 0, 0);
        discHint.TextWrapping = TextWrapping.Wrap;
        left.Children.Add(discHint);
        var pctChips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var pct in new[] { 5, 10, 15, 20 })
        {
            var chip = new Button { Content = $"{pct}%", Margin = new Thickness(0, 0, 6, 0), Focusable = false, MinWidth = 52 };
            chip.SetResourceReference(FrameworkElement.StyleProperty, "Button.Small");
            chip.Tag = pct;
            pctChips.Children.Add(chip);
        }
        left.Children.Add(pctChips);
        var removeDisc = new Button { Content = "Remove the bill discount", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Focusable = false };
        removeDisc.SetResourceReference(FrameworkElement.StyleProperty, "Button.Subtle");
        left.Children.Add(removeDisc);

        // ───────── Right: how it is paid ─────────
        var right = new StackPanel();
        right.Children.Add(T("Paid by", 15, null, FontWeights.SemiBold));
        var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 16) };
        var tileButtons = new Dictionary<PaymentMethod, RadioButton>();
        foreach (var (m, label, key, glyph) in new[]
                 {
                     (PaymentMethod.Cash, "Cash", "C", ""),
                     (PaymentMethod.Card, "Card", "K", "\uE8C7"),
                     (PaymentMethod.BankTransfer, "Bank transfer", "B", "\uE825"),
                 })
        {
            var rb = new RadioButton { GroupName = "paymethod", Focusable = false, Margin = new Thickness(0, 0, 8, 0), MinHeight = 76 };
            rb.SetResourceReference(FrameworkElement.StyleProperty, "PayTile");
            UIElement icon;
            if (glyph.Length == 0)
            {
                icon = new TextBlock { Text = "Rs", FontSize = 17, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            }
            else
            {
                var g = new TextBlock { Text = glyph, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center };
                g.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
                icon = g;
            }
            var title = new TextBlock { Text = label, FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            var keyHint = new TextBlock { Text = key, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.6 };
            rb.Content = new StackPanel { Children = { icon, title, keyHint } };
            tiles.Children.Add(rb);
            tileButtons[m] = rb;
        }
        right.Children.Add(tiles);

        var amountLabel = T("", 14, "Brush.TextSecondary", FontWeights.Medium);
        amountLabel.Margin = new Thickness(2, 0, 0, 6);
        right.Children.Add(amountLabel);
        var amount = DialogSheet.Input(null, number: true);
        amount.FontSize = 26;
        amount.MinHeight = 60;
        amount.FontWeight = FontWeights.SemiBold;
        right.Children.Add(amount);
        var reference = DialogSheet.Input(null, placeholder: "Reference or last 4 digits (optional)");
        reference.Margin = new Thickness(0, 8, 0, 0);
        right.Children.Add(reference);
        var quick = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        right.Children.Add(quick);

        var paidTitle = T("Paid so far", 13, "Brush.TextSecondary", FontWeights.Medium);
        paidTitle.Margin = new Thickness(2, 14, 0, 6);
        right.Children.Add(paidTitle);
        var paidList = new StackPanel();
        right.Children.Add(paidList);

        var status = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 12, 18, 12), Margin = new Thickness(0, 14, 0, 0) };
        var statusLabel = T("", 13, null, FontWeights.SemiBold);
        var statusValue = T("", 32, null, FontWeights.Bold);
        status.Child = new StackPanel { Children = { statusLabel, statusValue } };
        right.Children.Add(status);
        var splitHint = T("Paying part by card and part in cash? Choose Card, type the card amount, press Enter, then take the rest in cash.", 12, "Brush.TextSecondary");
        splitHint.TextWrapping = TextWrapping.Wrap;
        splitHint.Margin = new Thickness(2, 10, 0, 0);
        right.Children.Add(splitHint);

        var leftCard = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(22, 20, 22, 20), Margin = new Thickness(0, 8, 24, 0), Child = left };
        leftCard.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceSubtle");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var rightWrap = new Border { Margin = new Thickness(0, 8, 0, 0), Child = right };
        Grid.SetColumn(rightWrap, 1);
        grid.Children.Add(leftCard);
        grid.Children.Add(rightWrap);
        sheet.Add(grid);

        PaymentSummary Summary(decimal? extra = null) =>
            PaymentCalculator.Summarize(totals.Total, extra is { } x && x > 0 ? [.. payments, new PaymentInput(method, x, reference.Text)] : [.. payments]);
        decimal Remaining() => Summary().Remaining;
        static string MethodName(PaymentMethod m) => m switch { PaymentMethod.Card => "Card", PaymentMethod.BankTransfer => "Bank transfer", _ => "Cash" };

        void Refresh()
        {
            subtotalText.Text = Money.Format(totals.Subtotal, true);
            itemDiscText.Text = totals.LineDiscountTotal > 0 ? "− " + Money.Format(totals.LineDiscountTotal, true) : "None";
            billDiscText.Text = totals.BillDiscountAmount > 0 ? "− " + Money.Format(totals.BillDiscountAmount, true) : "None";
            totalText.Text = Money.Format(totals.Total, true);
            rsButton.IsChecked = !percentMode;
            pctButton.IsChecked = percentMode;
            Ds.SetPlaceholder(discount, percentMode ? "Percent off, e.g. 10" : "Rupees off, e.g. 200");
            discHint.Text = percentMode ? "Percent off the whole bill. Press Enter to apply." : "Rupees off the whole bill. Press Enter to apply.";
            pctChips.Visibility = percentMode ? Visibility.Visible : Visibility.Collapsed;
            removeDisc.Visibility = totals.BillDiscountAmount > 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var (m, rb) in tileButtons) rb.IsChecked = m == method;
            reference.Visibility = method == PaymentMethod.Cash ? Visibility.Collapsed : Visibility.Visible;
            var s = Summary();
            amountLabel.Text = method == PaymentMethod.Cash
                ? payments.Count == 0 ? $"Cash received (Enter alone = exact {Money.Format(Remaining(), true)})" : $"Cash received for the rest ({Money.Format(Remaining(), true)})"
                : $"{MethodName(method)} amount";
            quick.Children.Clear();
            if (method == PaymentMethod.Cash && Remaining() > 0)
                foreach (var q in PaymentCalculator.QuickCash(Remaining()))
                {
                    var b = new Button
                    {
                        Content = Money.Format(q, true), Margin = new Thickness(0, 0, 8, 8), Focusable = false,
                        MinHeight = 52, MinWidth = 130, FontSize = 18, FontWeight = FontWeights.SemiBold, Padding = new Thickness(16, 0, 16, 0),
                        ToolTip = q == Remaining() ? "Exact amount" : $"Change {Money.Format(q - Remaining(), true)}",
                    };
                    b.SetResourceReference(FrameworkElement.StyleProperty, q == Remaining() ? "Button.Secondary" : "Button.Outline");
                    b.Click += (_, _) => { amount.Text = Money.ToInput(q); Submit(); };
                    quick.Children.Add(b);
                }

            paidTitle.Visibility = payments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            paidList.Children.Clear();
            foreach (var p in payments.ToList())
            {
                var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 6, 6, 6), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1) };
                row.SetResourceReference(Border.BorderBrushProperty, "Brush.Line");
                var dock = new DockPanel();
                var remove = new Button { Content = "\uE711", Focusable = false, ToolTip = "Remove this payment", Width = 30, MinHeight = 30, Padding = new Thickness(0) };
                remove.SetResourceReference(FrameworkElement.StyleProperty, "Button.Subtle");
                remove.SetResourceReference(Control.FontFamilyProperty, "Font.Icons");
                remove.Click += (_, _) => { payments.Remove(p); Refresh(); amount.Focus(); };
                DockPanel.SetDock(remove, Dock.Right);
                dock.Children.Add(remove);
                var value = T(Money.Format(p.Amount, true), 15, null, FontWeights.SemiBold);
                value.Margin = new Thickness(0, 0, 10, 0);
                DockPanel.SetDock(value, Dock.Right);
                dock.Children.Add(value);
                dock.Children.Add(T(MethodName(p.Method) + (string.IsNullOrWhiteSpace(p.Reference) ? "" : $"  ({p.Reference})"), 14));
                row.Child = dock;
                paidList.Children.Add(row);
            }

            var (label, value2, bg, fg) = payments.Count == 0
                ? ("To pay", totals.Total, "Brush.SurfaceSubtle", "Brush.Text")
                : s.Complete
                    ? (s.Change > 0 ? "Change to give" : "Paid in full", s.Change, "Brush.SuccessSoft", "Brush.Success")
                    : ("Still to pay", s.Remaining, "Brush.WarningSoft", "Brush.Warning");
            statusLabel.Text = label;
            statusValue.Text = Money.Format(value2, true);
            status.SetResourceReference(Border.BackgroundProperty, bg);
            statusLabel.SetResourceReference(TextBlock.ForegroundProperty, fg);
            statusValue.SetResourceReference(TextBlock.ForegroundProperty, fg);
            splitHint.Visibility = payments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetMethod(PaymentMethod m)
        {
            method = m;
            // Card and bank are nearly always the exact amount: fill it in, ready to confirm or change.
            amount.Text = m == PaymentMethod.Cash || Remaining() <= 0 ? "" : Money.ToInput(Remaining());
            Refresh();
            amount.Focus();
            amount.SelectAll();
        }

        void Finish()
        {
            var s = Summary();
            if (s.Error is not null) { sheet.ShowError(s.Error, amount); return; }
            if (!s.Complete) { sheet.ShowError($"Still to pay {Money.Format(s.Remaining, true)}.", amount); return; }
            sheet.Close(payments.ToList());
        }

        void Submit()
        {
            sheet.ShowError(null);
            var text = amount.Text.Trim();
            if (text.Length == 0)
            {
                // Enter alone = the exact amount, but only as the first payment. After a part payment
                // the rest must be typed (or F12 = rest in cash), so a bill is never closed by accident.
                if (payments.Count > 0 && Remaining() > 0)
                {
                    sheet.ShowError($"Still to pay {Money.Format(Remaining(), true)}. Type the amount received, or press F12 to take the rest in cash.", amount);
                    return;
                }
                if (Remaining() > 0) payments.Add(new PaymentInput(method, Remaining(), reference.Text));
                Finish();
                return;
            }
            if (!Money.TryParse(text, out var value) || value <= 0) { sheet.ShowError("Type an amount like 5000 or 5000.50.", amount); return; }
            var s = Summary(value);
            if (s.Error is not null) { sheet.ShowError(s.Error, amount); return; }
            payments.Add(new PaymentInput(method, value, reference.Text));
            reference.Text = "";
            amount.Text = "";
            if (s.Complete) { Refresh(); Finish(); return; }
            // Part paid: say so clearly; the bill is not saved until everything is paid.
            var paidBy = method;
            method = PaymentMethod.Cash;
            Refresh();
            sheet.ShowError(paidBy == PaymentMethod.Cash
                ? $"Only {Money.Format(value, true)} received. Still to pay {Money.Format(s.Remaining, true)}: type more cash, or press K (card) or B (bank) for the rest."
                : $"{MethodName(paidBy)} {Money.Format(value, true)} added. Still to pay {Money.Format(s.Remaining, true)} in cash: type the cash received.", amount);
        }

        void ApplyDiscount()
        {
            var text = discount.Text.Trim().TrimEnd('%').Trim();
            if (discount.Text.Trim().EndsWith('%')) percentMode = true;
            if (!Discount.TryParse(text.Length == 0 ? "" : percentMode ? text + "%" : text, DiscountType.Amount, out var d, out var parseError))
            {
                Ds.SetError(discount, null);
                Ds.SetError(discount, parseError);
                return;
            }
            var (newTotals, error) = applyBillDiscount(d);
            if (error is not null)
            {
                Ds.SetError(discount, null);
                Ds.SetError(discount, error);
                return;
            }
            Ds.SetError(discount, null);
            discount.Text = text;
            totals = newTotals;
            payments.Clear();
            if (method != PaymentMethod.Cash) amount.Text = Money.ToInput(Remaining());
            Refresh();
        }

        foreach (Button chip in pctChips.Children)
            chip.Click += (_, _) => { discount.Text = ((int)chip.Tag).ToString(System.Globalization.CultureInfo.InvariantCulture); ApplyDiscount(); amount.Focus(); };
        removeDisc.Click += (_, _) => { discount.Text = ""; ApplyDiscount(); amount.Focus(); };
        rsButton.Checked += (_, _) => { if (!percentMode) return; percentMode = false; if (discount.Text.Trim().Length > 0) ApplyDiscount(); else Refresh(); discount.Focus(); };
        pctButton.Checked += (_, _) => { if (percentMode) return; percentMode = true; if (discount.Text.Trim().Length > 0) ApplyDiscount(); else Refresh(); discount.Focus(); };
        foreach (var (m, rb) in tileButtons) rb.Checked += (_, _) => { if (method != m) SetMethod(m); };
        discount.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            ApplyDiscount();
            amount.Focus();
            e.Handled = true;
        };
        var lastDiscount = discount.Text.Trim();
        discount.LostKeyboardFocus += (_, _) =>
        {
            if (discount.Text.Trim() == lastDiscount) return;
            lastDiscount = discount.Text.Trim();
            ApplyDiscount();
        };
        sheet.PreviewKeyDown += (_, e) =>
        {
            var inText = ReferenceEquals(Keyboard.FocusedElement, reference) || ReferenceEquals(Keyboard.FocusedElement, discount);
            switch (e.Key)
            {
                case Key.C when !inText: SetMethod(PaymentMethod.Cash); e.Handled = true; break;
                case Key.K when !inText: SetMethod(PaymentMethod.Card); e.Handled = true; break;
                case Key.B when !inText: SetMethod(PaymentMethod.BankTransfer); e.Handled = true; break;
                case Key.F12:
                    if (Remaining() > 0) payments.Add(new PaymentInput(PaymentMethod.Cash, Remaining()));
                    Finish();
                    e.Handled = true;
                    break;
            }
        };
        amount.Loaded += (_, _) => amount.Focus();
        Refresh();
        sheet.Button("Back to bill", () => sheet.Close(null), key: "Esc");
        sheet.Button("Pay and print", Submit, "Button.Primary", "Enter");
        sheet.OnEnter = () =>
        {
            if (ReferenceEquals(Keyboard.FocusedElement, discount)) return;
            Submit();
        };
        return await dialogs.ShowAsync(sheet) as IReadOnlyList<PaymentInput>;
    }
}
