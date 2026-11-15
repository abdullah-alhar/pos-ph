using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VetPos.App.DesignSystem;
using VetPos.App.Infrastructure;
using VetPos.App.DesignSystem.Controls;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;
using VetPos.Data.Services;

namespace VetPos.App.Features.Billing;

public enum PastBillAction { Print, Change }

public sealed record PastBillChoice(PastBillAction Action, int SaleId, int Number);

/// <summary>
/// Old bills (F7): find a bill by number or day, look at exactly what was sold, then print it again
/// or change it (the old bill is cancelled and its items open as a new bill).
/// </summary>
public static class PastBillsDialog
{
    public static async Task<PastBillChoice?> ShowAsync(IDialogService dialogs, SaleService sales, ShopClock clock)
    {
        var sheet = new DialogSheet(dialogs, "Past bills", "Find a bill, check it, print it again or change it.", 1040);
        var days = 0;
        SaleView? current = null;

        // ───── Top: search + day ─────
        var search = DialogSheet.Input(null, placeholder: "Bill number, e.g. 1024");
        Ds.SetIcon(search, "");
        var range = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        var rangeButtons = new List<RadioButton>();
        foreach (var (label, d) in new[] { ("Today", 0), ("Yesterday", 1), ("Last 7 days", 7), ("Last 30 days", 30) })
        {
            var rb = new RadioButton { Content = label, GroupName = "pastrange", Focusable = false, Tag = d, IsChecked = d == 0 };
            rb.SetResourceReference(FrameworkElement.StyleProperty, "Segment");
            range.Children.Add(rb);
            rangeButtons.Add(rb);
        }
        var rangeBox = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Child = range };
        rangeBox.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceSunken");
        var top = new DockPanel { Margin = new Thickness(0, 4, 0, 14) };
        DockPanel.SetDock(rangeBox, Dock.Right);
        top.Children.Add(rangeBox);
        top.Children.Add(search);
        sheet.Add(top);

        // ───── Left: bills · Right: the bill ─────
        var list = new ListBox { Height = 440, Focusable = true };
        list.SetResourceReference(FrameworkElement.StyleProperty, "Table");
        list.ItemTemplate = BillTemplate();
        var listCard = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = list, Margin = new Thickness(0, 0, 16, 0), ClipToBounds = true };
        listCard.SetResourceReference(Border.BorderBrushProperty, "Brush.Line");

        var detail = new StackPanel();
        var detailScroll = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 440, Focusable = false };
        var detailCard = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(20, 16, 20, 16), Child = detailScroll };
        detailCard.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceSubtle");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(detailCard, 1);
        grid.Children.Add(listCard);
        grid.Children.Add(detailCard);
        sheet.Add(grid);

        sheet.Button("Close", () => sheet.Close(null), key: "Esc");
        var changeButton = sheet.Button("Change this bill", () => { if (current is { } c && c.Status != SaleStatus.Voided) sheet.Close(new PastBillChoice(PastBillAction.Change, c.Id, c.Number)); }, key: "F2");
        var printButton = sheet.Button("Print again", () => { if (current is { } c) sheet.Close(new PastBillChoice(PastBillAction.Print, c.Id, c.Number)); }, "Button.Primary", "Enter");
        changeButton.IsEnabled = printButton.IsEnabled = false;

        static TextBlock T(string text, double size = 14, string? brush = null, FontWeight? weight = null, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, HorizontalAlignment = align, TextWrapping = TextWrapping.Wrap };
            if (brush is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            return t;
        }

        void ShowDetail(SaleView? s)
        {
            current = s;
            detail.Children.Clear();
            if (s is null)
            {
                detail.Children.Add(T("Choose a bill on the left to see it here.", 15, "Brush.TextSecondary"));
                return;
            }
            var head = new DockPanel();
            var status = new Border { VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = s.Status == SaleStatus.Voided ? "Cancelled" : "Paid" } };
            status.SetResourceReference(FrameworkElement.StyleProperty, s.Status == SaleStatus.Voided ? "Badge.Danger" : "Badge.Success");
            DockPanel.SetDock(status, Dock.Right);
            head.Children.Add(status);
            head.Children.Add(T($"Bill {s.Number}", 22, null, FontWeights.SemiBold));
            detail.Children.Add(head);
            detail.Children.Add(T($"{s.CreatedAtLocal.ToString("dddd d MMM yyyy, h:mm tt", CultureInfo.InvariantCulture)}, by {s.Cashier}", 13, "Brush.TextSecondary"));
            if (s.Status == SaleStatus.Voided)
            {
                var v = T($"Cancelled {s.VoidedAtLocal?.ToString("d MMM, h:mm tt", CultureInfo.InvariantCulture)} by {s.VoidedBy}: {s.VoidReason}", 13, "Brush.Danger");
                v.Margin = new Thickness(0, 6, 0, 0);
                detail.Children.Add(v);
            }
            if (!string.IsNullOrWhiteSpace(s.Note))
            {
                var n = T(s.Note, 13, "Brush.TextSecondary");
                n.Margin = new Thickness(0, 4, 0, 0);
                detail.Children.Add(n);
            }

            var lines = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            lines.ColumnDefinitions.Add(new ColumnDefinition());
            lines.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var r = 0;
            foreach (var l in s.Lines)
            {
                lines.RowDefinitions.Add(new RowDefinition());
                var name = new StackPanel { Margin = new Thickness(0, 0, 12, 10) };
                name.Children.Add(T(l.Name, 15, null, FontWeights.Medium));
                var sub = $"{l.Qty} × {Money.Format(l.UnitPrice, true)}";
                if (l.PriceChanged) sub += $"  (shelf {Money.Format(l.ListPrice, true)})";
                if (l.DiscountAmount > 0) sub += $"  ·  discount − {Money.Format(l.DiscountAmount, true)}";
                name.Children.Add(T(sub, 13, "Brush.TextSecondary"));
                var total = T(Money.Format(l.LineTotal, true), 15, null, FontWeights.SemiBold, HorizontalAlignment.Right);
                Grid.SetRow(name, r); Grid.SetRow(total, r); Grid.SetColumn(total, 1);
                lines.Children.Add(name); lines.Children.Add(total);
                r++;
            }
            detail.Children.Add(lines);
            var rule = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 12) };
            rule.SetResourceReference(Border.BackgroundProperty, "Brush.Line");
            detail.Children.Add(rule);

            void Sum(string label, string value, bool strong = false)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                g.Children.Add(T(label, strong ? 16 : 14, strong ? null : "Brush.TextSecondary", strong ? FontWeights.SemiBold : FontWeights.Normal));
                g.Children.Add(T(value, strong ? 22 : 14, null, strong ? FontWeights.Bold : FontWeights.Normal, HorizontalAlignment.Right));
                detail.Children.Add(g);
            }
            Sum("Subtotal", Money.Format(s.Subtotal, true));
            if (s.LineDiscountTotal > 0) Sum("Item discounts", "− " + Money.Format(s.LineDiscountTotal, true));
            if (s.BillDiscountAmount > 0) Sum("Bill discount", "− " + Money.Format(s.BillDiscountAmount, true));
            Sum("Total", Money.Format(s.Total, true), strong: true);
            foreach (var p in s.Payments)
                Sum($"Paid by {SaleService.MethodName(p.Method).ToLowerInvariant()}{(string.IsNullOrWhiteSpace(p.Reference) ? "" : $" ({p.Reference})")}",
                    Money.Format(p.Method == PaymentMethod.Cash && p.Tendered > 0 ? p.Tendered : p.Amount, true));
            if (s.Change > 0) Sum("Change given", Money.Format(s.Change, true));
            printButton.IsEnabled = true;
            changeButton.IsEnabled = s.Status != SaleStatus.Voided;
        }

        async Task LoadAsync()
        {
            var text = search.Text.Trim().TrimStart('#');
            var to = clock.Today.AddDays(days == 1 ? -1 : 0);
            var from = days switch { 0 => clock.Today, 1 => clock.Today.AddDays(-1), _ => clock.Today.AddDays(-days + 1) };
            var rows = await sales.ListAsync(from, to, text.Length > 0 ? text : null);
            list.ItemsSource = rows;
            list.SelectedIndex = rows.Count > 0 ? 0 : -1;
            if (rows.Count == 0)
            {
                ShowDetail(null);
                detail.Children.Clear();
                detail.Children.Add(T(text.Length > 0 ? $"No bill number {text}." : "No bills for these days.", 15, "Brush.TextSecondary"));
                printButton.IsEnabled = changeButton.IsEnabled = false;
            }
        }

        async Task SafeLoad()
        {
            try { await LoadAsync(); }
            catch (Exception ex) { ErrorHandling.Show(ex); }
        }

        list.SelectionChanged += async (_, _) => { try { ShowDetail(list.SelectedItem is BillRow b ? await sales.GetAsync(b.Id) : null); } catch (Exception ex) { ErrorHandling.Show(ex); } };
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += async (_, _) => { timer.Stop(); await SafeLoad(); };
        search.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        foreach (var rb in rangeButtons)
            rb.Checked += async (_, _) => { days = (int)rb.Tag; await SafeLoad(); };
        search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Down or Key.Up && list.Items.Count > 0)
            {
                list.SelectedIndex = Math.Clamp(list.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, list.Items.Count - 1);
                list.ScrollIntoView(list.SelectedItem);
                e.Handled = true;
            }
        };

        sheet.OnEnter = () => { if (current is { } c) sheet.Close(new PastBillChoice(PastBillAction.Print, c.Id, c.Number)); };
        sheet.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F2 && current is { Status: not SaleStatus.Voided } c)
            {
                sheet.Close(new PastBillChoice(PastBillAction.Change, c.Id, c.Number));
                e.Handled = true;
            }
        };
        search.Loaded += async (_, _) => { search.Focus(); await SafeLoad(); };
        return await dialogs.ShowAsync(sheet) as PastBillChoice;
    }

    private static DataTemplate BillTemplate()
    {
        const string xaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid Margin="0,2">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition />
                  <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <StackPanel>
                  <TextBlock FontSize="15" FontWeight="SemiBold"><Run Text="Bill " /><Run Text="{Binding Number, Mode=OneWay}" /></TextBlock>
                  <TextBlock FontSize="12" Opacity="0.7"><Run Text="{Binding CreatedAtLocal, Mode=OneWay, StringFormat='d MMM, h:mm tt'}" /><Run Text=" · " /><Run Text="{Binding Methods, Mode=OneWay}" /></TextBlock>
                </StackPanel>
                <StackPanel Grid.Column="1" HorizontalAlignment="Right">
                  <TextBlock Text="{Binding Total, StringFormat='Rs {0:N2}'}" FontSize="15" FontWeight="SemiBold" HorizontalAlignment="Right" />
                  <TextBlock x:Name="State" Text="Cancelled" FontSize="12" Foreground="#C0392B" HorizontalAlignment="Right" Visibility="Collapsed" />
                </StackPanel>
              </Grid>
              <DataTemplate.Triggers>
                <DataTrigger Binding="{Binding Status}" Value="Voided">
                  <Setter TargetName="State" Property="Visibility" Value="Visible" />
                </DataTrigger>
              </DataTemplate.Triggers>
            </DataTemplate>
            """;
        return (DataTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
    }
}
