using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VetPos.App.DesignSystem.Controls;
using VetPos.App.Infrastructure;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;
using VetPos.Core.Settings;
using VetPos.Data.Services;

namespace VetPos.App.Features.Reports;

public enum ReportSection { Today, Sales, Profit, Stock, LowStockExpiry, Changes, Bills, Activity }

public sealed record Tile(string Label, string Value, string? Hint = null, string? Trend = null, bool TrendUp = true);

/// <summary>The period buttons on the Sales report.</summary>
public enum SalesPeriod { Day, Week, Month, Year, Custom }

public sealed partial class ReportsViewModel : PageViewModel
{
    private readonly ReportService _reports;
    private readonly CatalogService _catalog;
    private readonly SaleService _sales;
    private readonly AuthService _auth;
    private readonly PrintService _printer;
    private readonly SettingsService _settings;
    private readonly ShopClock _clock;

    public ReportsViewModel(Ui ui, ReportService reports, CatalogService catalog, SaleService sales, AuthService auth, PrintService printer, SettingsService settings, ShopClock clock) : base(ui)
    {
        _reports = reports;
        _catalog = catalog;
        _sales = sales;
        _auth = auth;
        _printer = printer;
        _settings = settings;
        _clock = clock;
        From = To = clock.Today.ToDateTime(TimeOnly.MinValue);
        PeriodTitle = "Today";
    }

    public bool IsOwner => Ui.Session.IsOwner;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UsesDates), nameof(SectionTitle), nameof(ShowDatePickers))] private ReportSection _section;
    [ObservableProperty] private DateTime _from;
    [ObservableProperty] private DateTime _to;
    [ObservableProperty] private string _billSearch = "";
    [ObservableProperty] private SalesSummary? _summary;
    [ObservableProperty] private BillRow? _selectedBill;
    [ObservableProperty] private SaleView? _bill;

    public ObservableCollection<Tile> Tiles { get; } = [];
    public ObservableCollection<PeriodRow> Days { get; } = [];
    public ObservableCollection<ChartBar> Chart { get; } = [];

    [ObservableProperty] private SalesPeriod _period = SalesPeriod.Day;
    [ObservableProperty] private string _periodTitle = "";
    [ObservableProperty] private string _chartTitle = "";
    public bool IsCustomPeriod => Period == SalesPeriod.Custom;

    // ── One product's sales (search box on the Sales report) ──
    public ObservableCollection<ProductRow> ProductSuggestions { get; } = [];
    [ObservableProperty] private string _productSearch = "";
    [ObservableProperty] private bool _showProductSuggestions;
    [ObservableProperty] private ProductRow? _pickedSuggestion;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSalesProduct))] private ProductRow? _salesProduct;
    [ObservableProperty] private ProductSalesRow? _openProductRow;
    public bool HasSalesProduct => SalesProduct is not null;

    async partial void OnProductSearchChanged(string value)
    {
        var text = value.Trim();
        if (text.Length == 0) { ShowProductSuggestions = false; ProductSuggestions.Clear(); return; }
        var rows = await _catalog.SearchAsync(text, includeInactive: true, limit: 8);
        if (text != ProductSearch.Trim()) return; // typed on meanwhile
        ProductSuggestions.Clear();
        foreach (var r in rows) ProductSuggestions.Add(r);
        ShowProductSuggestions = ProductSuggestions.Count > 0;
    }

    async partial void OnPickedSuggestionChanged(ProductRow? value)
    {
        if (value is null) return;
        await ShowProductSalesAsync(value);
    }

    /// <summary>Click a product in Today / Profit: open its sales graph.</summary>
    async partial void OnOpenProductRowChanged(ProductSalesRow? value)
    {
        if (value is null) return;
        var row = (await _catalog.SearchAsync(value.Name, includeInactive: true, limit: 50)).FirstOrDefault(r => r.Id == value.ProductId)
                  ?? new ProductRow(value.ProductId, value.Name, null, "", default, 0, 0, 0, 0, 0, 0, null, false, true);
        OpenProductRow = null;
        await ShowProductSalesAsync(row);
    }

    private async Task ShowProductSalesAsync(ProductRow product)
    {
        SalesProduct = product;
        ProductSearch = "";
        ShowProductSuggestions = false;
        PickedSuggestion = null;
        if (Section != ReportSection.Sales)
        {
            // Keep the dates the person was looking at.
            if (Section == ReportSection.Today)
            {
                _settingRange = true;
                From = To = _clock.Today.ToDateTime(TimeOnly.MinValue);
                _settingRange = false;
            }
            if (F == T) Period = SalesPeriod.Day;
            else { _settingRange = true; Period = SalesPeriod.Custom; _settingRange = false; }
            Section = ReportSection.Sales;
        }
        else await RefreshAsync();
    }

    /// <summary>Enter in the search box: take the first suggestion.</summary>
    [RelayCommand]
    private async Task PickFirstProduct()
    {
        if (ProductSuggestions.FirstOrDefault() is { } first) await ShowProductSalesAsync(first);
    }

    /// <summary>Back to the whole shop's sales.</summary>
    [RelayCommand]
    private async Task ClearProduct()
    {
        SalesProduct = null;
        ProductSearch = "";
        await RefreshAsync();
    }
    public bool ShowDatePickers => UsesDates && !(Section == ReportSection.Sales && !IsCustomPeriod);
    public ObservableCollection<ProductSalesRow> ProductRows { get; } = [];
    public ObservableCollection<StockRow> StockRows { get; } = [];
    public ObservableCollection<ProductRow> LowRows { get; } = [];
    public ObservableCollection<ExpiryRow> ExpiryRows { get; } = [];
    public ObservableCollection<ChangeLogRow> Changes { get; } = [];
    public ObservableCollection<BillRow> Bills { get; } = [];

    public bool UsesDates => Section is not (ReportSection.Today or ReportSection.Stock or ReportSection.LowStockExpiry);
    public string SectionTitle => Section switch
    {
        ReportSection.Today => "Today's sales",
        ReportSection.Sales => "How much was sold: pick a day, week, month or year",
        ReportSection.Profit => "Profit",
        ReportSection.Stock => "Stock on hand",
        ReportSection.LowStockExpiry => "Low stock, and stock that expires in the next 60 days",
        ReportSection.Changes => "Discounts and price changes",
        ReportSection.Activity => "Activity log: everything that happened, and who did it",
        _ => "Bills",
    };

    private DateOnly F => DateOnly.FromDateTime(Section == ReportSection.Today ? DateTime.Today : From);
    private DateOnly T => DateOnly.FromDateTime(Section == ReportSection.Today ? DateTime.Today : To);
    private string RangeText => F == T ? F.ToString("dddd, dd MMMM yyyy", CultureInfo.InvariantCulture) : $"{F:dd MMM yyyy} – {T:dd MMM yyyy}";

    public override Task OnShownAsync() => RefreshAsync();

    async partial void OnSectionChanged(ReportSection value)
    {
        OnPropertyChanged(nameof(ShowDatePickers));
        if (value == ReportSection.Sales && Period != SalesPeriod.Custom) await SetPeriodAsync(DateOnly.FromDateTime(To));
        else await RefreshAsync();
    }
    private bool _settingRange;

    async partial void OnFromChanged(DateTime value)
    {
        if (_settingRange) return;
        if (value > To) To = value; else await RefreshAsync();
    }

    async partial void OnToChanged(DateTime value)
    {
        if (_settingRange) return;
        if (value < From) From = value; else await RefreshAsync();
    }

    async partial void OnSelectedBillChanged(BillRow? value)
    {
        Bill = value is null ? null : await _sales.GetAsync(value.Id);
    }

    async partial void OnPeriodChanged(SalesPeriod value)
    {
        OnPropertyChanged(nameof(IsCustomPeriod));
        OnPropertyChanged(nameof(ShowDatePickers));
        if (_settingRange) return;
        if (value == SalesPeriod.Custom) { await RefreshAsync(); return; }
        await SetPeriodAsync(DateOnly.FromDateTime(To));
    }

    /// <summary>Sets From / To to the day, week, month or year that contains <paramref name="anchor"/>.</summary>
    private async Task SetPeriodAsync(DateOnly anchor)
    {
        var today = _clock.Today;
        if (anchor > today) anchor = today;
        (DateOnly f, DateOnly t) = Period switch
        {
            SalesPeriod.Week => (anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7)), anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7) + 6)),
            SalesPeriod.Month => (new DateOnly(anchor.Year, anchor.Month, 1), new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(1).AddDays(-1)),
            SalesPeriod.Year => (new DateOnly(anchor.Year, 1, 1), new DateOnly(anchor.Year, 12, 31)),
            _ => (anchor, anchor),
        };
        if (t > today) t = today;
        _settingRange = true;
        From = f.ToDateTime(TimeOnly.MinValue);
        To = t.ToDateTime(TimeOnly.MinValue);
        _settingRange = false;
        await RefreshAsync();
    }

    /// <summary>◀ / ▶ : the day, week, month or year before or after.</summary>
    [RelayCommand]
    private Task Step(string direction)
    {
        var back = direction == "back";
        var f = DateOnly.FromDateTime(From);
        var anchor = Period switch
        {
            SalesPeriod.Week => f.AddDays(back ? -7 : 7),
            SalesPeriod.Month => f.AddMonths(back ? -1 : 1),
            SalesPeriod.Year => f.AddYears(back ? -1 : 1),
            SalesPeriod.Custom => f.AddDays((back ? -1 : 1) * (DateOnly.FromDateTime(To).DayNumber - f.DayNumber + 1)),
            _ => f.AddDays(back ? -1 : 1),
        };
        if (Period == SalesPeriod.Custom)
        {
            var len = DateOnly.FromDateTime(To).DayNumber - f.DayNumber;
            if (!back && anchor > _clock.Today) return Task.CompletedTask;
            _settingRange = true;
            From = anchor.ToDateTime(TimeOnly.MinValue);
            To = anchor.AddDays(len).ToDateTime(TimeOnly.MinValue);
            _settingRange = false;
            return RefreshAsync();
        }
        if (!back && anchor > _clock.Today) return Task.CompletedTask;
        return SetPeriodAsync(anchor);
    }

    private string PeriodName => Period switch
    {
        SalesPeriod.Day => F == _clock.Today ? "Today" : F == _clock.Today.AddDays(-1) ? "Yesterday" : F.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture),
        SalesPeriod.Week => $"Week of {F:d MMM} - {T:d MMM yyyy}",
        SalesPeriod.Month => F.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
        SalesPeriod.Year => F.ToString("yyyy", CultureInfo.InvariantCulture),
        _ => Period == SalesPeriod.Custom && F == T ? F.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture) : $"{F:d MMM yyyy} - {T:d MMM yyyy}",
    };

    private string CompareName => Period switch
    {
        SalesPeriod.Day => "the day before",
        SalesPeriod.Week => "last week",
        SalesPeriod.Month => "last month",
        SalesPeriod.Year => "last year",
        _ => "the period before",
    };

    [RelayCommand]
    private async Task Preset(string which)
    {
        var today = _clock.Today;
        (DateOnly f, DateOnly t) = which switch
        {
            "yesterday" => (today.AddDays(-1), today.AddDays(-1)),
            "week" => (today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today),
            "month" => (new DateOnly(today.Year, today.Month, 1), today),
            "lastmonth" => (new DateOnly(today.Year, today.Month, 1).AddMonths(-1), new DateOnly(today.Year, today.Month, 1).AddDays(-1)),
            _ => (today, today),
        };
        _settingRange = true;
        From = f.ToDateTime(TimeOnly.MinValue);
        To = t.ToDateTime(TimeOnly.MinValue);
        _settingRange = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    private Task RefreshAsync() => LoadAsync(async () =>
    {
        Tiles.Clear();
        switch (Section)
        {
            case ReportSection.Today:
            case ReportSection.Sales:
            case ReportSection.Profit:
            {
                var productId = Section == ReportSection.Sales ? SalesProduct?.Id : null;
                var s = await _reports.SummaryAsync(F, T, productId);
                Summary = s;
                string? trend = null;
                var up = true;
                if (Section == ReportSection.Sales)
                {
                    // Compare with the same length of time just before (a partly finished period is compared to the same days).
                    var len = T.DayNumber - F.DayNumber;
                    var (pf, pt) = Period switch
                    {
                        SalesPeriod.Month => (F.AddMonths(-1), F.AddMonths(-1).AddDays(len)),
                        SalesPeriod.Year => (F.AddYears(-1), F.AddYears(-1).AddDays(len)),
                        _ => (F.AddDays(-(len + 1)), F.AddDays(-1)),
                    };
                    var prev = await _reports.SummaryAsync(pf, pt, productId);
                    if (prev.Sales > 0)
                    {
                        var change = Math.Round((s.Sales - prev.Sales) / prev.Sales * 100);
                        up = change >= 0;
                        trend = $"{(up ? "▲" : "▼")} {Math.Abs(change)}% than {CompareName} ({Money.Format(prev.Sales, true)})";
                    }
                    else if (s.Sales > 0) trend = $"No sales {CompareName}";
                }
                Tiles.Add(new("Sales", Money.Format(s.Sales, true), $"{s.Bills} bill(s)", trend, up));
                if (productId is null) Tiles.Add(new("Average bill", Money.Format(s.AverageBill, true)));
                else Tiles.Add(new("Units sold", s.Units.ToString("N0", CultureInfo.InvariantCulture), $"{SalesProduct!.StockQty} left in stock"));
                Tiles.Add(new("Discounts given", Money.Format(s.Discounts, true)));
                if (IsOwner) Tiles.Add(new("Profit", Money.Format(s.Profit, true), $"{s.MarginPercent}% margin"));
                if (s.VoidedBills > 0) Tiles.Add(new("Voided bills", s.VoidedBills.ToString(CultureInfo.InvariantCulture), "not counted"));
                Days.Clear();
                Chart.Clear();
                if (Section == ReportSection.Sales)
                {
                    PeriodTitle = PeriodName;
                    var grouping = ReportService.GroupingFor(F, T);
                    ChartTitle = (SalesProduct is { } sp ? sp.Name + ": sales" : "Sales") + grouping switch
                    {
                        SalesGrouping.Hour => " by hour",
                        SalesGrouping.Day => " by day",
                        _ => " by month",
                    };
                    var rows = await _reports.SalesByPeriodAsync(F, T, grouping, productId);
                    var now = DateTime.Now;
                    foreach (var r in rows)
                    {
                        Chart.Add(new ChartBar(r.Label, r.Sales, productId is null ? $"{r.LongLabel}:  {Money.Format(r.Sales, true)}  ·  {r.Bills} bill(s)" : $"{r.LongLabel}:  {r.Units} sold  ·  {Money.Format(r.Sales, true)}",
                            Highlight: grouping == SalesGrouping.Day ? r.Start.Date == now.Date : grouping == SalesGrouping.Hour && r.Start.Date == now.Date && r.Start.Hour == now.Hour));
                        if (r.Bills > 0 || grouping != SalesGrouping.Hour) Days.Add(r);
                    }
                }
                ProductRows.Clear();
                if (Section != ReportSection.Sales) foreach (var p in await _reports.ByProductAsync(F, T, Section == ReportSection.Today ? 10 : null)) ProductRows.Add(p);
                break;
            }
            case ReportSection.Stock:
            {
                StockRows.Clear();
                var rows = await _reports.StockAsync();
                foreach (var r in rows) StockRows.Add(r);
                Tiles.Add(new("Products", rows.Count.ToString(CultureInfo.InvariantCulture)));
                Tiles.Add(new("Units on hand", rows.Sum(r => r.Qty).ToString("N0", CultureInfo.InvariantCulture)));
                if (IsOwner) Tiles.Add(new("Value at cost", Money.Format(rows.Sum(r => r.ValueAtCost), true)));
                Tiles.Add(new("Value at selling price", Money.Format(rows.Sum(r => r.ValueAtPrice), true)));
                break;
            }
            case ReportSection.LowStockExpiry:
            {
                LowRows.Clear();
                foreach (var r in await _catalog.LowStockAsync()) LowRows.Add(r);
                Tiles.Add(new("Low stock", LowRows.Count(r => r.StockQty > 0).ToString(CultureInfo.InvariantCulture), "at or below reorder level"));
                Tiles.Add(new("Out of stock", LowRows.Count(r => r.StockQty <= 0).ToString(CultureInfo.InvariantCulture)));
                ExpiryRows.Clear();
                foreach (var r in await _catalog.ExpiringAsync(60)) ExpiryRows.Add(r);
                Tiles.Add(new("Already expired", ExpiryRows.Where(r => r.DaysLeft < 0).Sum(r => r.QtyOnHand).ToString(CultureInfo.InvariantCulture), "units: take them off the shelf"));
                Tiles.Add(new("Expires in 60 days", ExpiryRows.Where(r => r.DaysLeft >= 0).Sum(r => r.QtyOnHand).ToString(CultureInfo.InvariantCulture), "units: sell these first"));
                break;
            }
            case ReportSection.Changes:
            {
                Changes.Clear();
                var rows = await _reports.ChangesAsync(F, T);
                foreach (var r in rows) Changes.Add(r);
                Tiles.Add(new("Price changes on bills", rows.Count(r => r.Action == "Price changed on bill").ToString(CultureInfo.InvariantCulture)));
                Tiles.Add(new("Discounts", rows.Count(r => r.Action is "Item discount" or "Bill discount").ToString(CultureInfo.InvariantCulture)));
                Tiles.Add(new("Voids", rows.Count(r => r.Action == "Bill voided").ToString(CultureInfo.InvariantCulture)));
                break;
            }
            case ReportSection.Bills:
            {
                await LoadBillsAsync();
                break;
            }
            case ReportSection.Activity:
            {
                Changes.Clear();
                foreach (var r in await _reports.ActivityAsync(F, T, BillSearch)) Changes.Add(r);
                Tiles.Add(new("Events", Changes.Count.ToString(CultureInfo.InvariantCulture)));
                Tiles.Add(new("Sign-ins", Changes.Count(c => c.Action == "Signed in").ToString(CultureInfo.InvariantCulture)));
                Tiles.Add(new("Wrong PINs", Changes.Count(c => c.Action == "Wrong PIN").ToString(CultureInfo.InvariantCulture)));
                break;
            }
        }
    });

    partial void OnBillSearchChanged(string value) => _ = Section == ReportSection.Activity ? RefreshAsync() : LoadBillsAsync();

    private async Task LoadBillsAsync()
    {
        var keep = SelectedBill?.Id;
        Bills.Clear();
        foreach (var b in await _sales.ListAsync(F, T, BillSearch)) Bills.Add(b);
        SelectedBill = Bills.FirstOrDefault(b => b.Id == keep);
    }

    [RelayCommand]
    private async Task Reprint()
    {
        if (Bill is null) return;
        await Ui.Run(async () =>
        {
            if (await _printer.PrintSaleAsync(Bill.Id, reprint: true)) Ui.Toasts.Show($"Bill #{Bill.Number} sent to the printer.");
            else Ui.Toasts.Show("No receipt printer is set up (Settings → Printer).", ToastKind.Info);
        });
    }

    /// <summary>Void: a cashier needs the owner's PIN; the bill stays in the records marked Voided.</summary>
    [RelayCommand]
    private async Task Void()
    {
        if (Bill is not { Status: SaleStatus.Completed } bill) return;
        var reason = await Ui.Dialogs.PromptAsync($"Void bill #{bill.Number}?", "Reason", null,
            t => t.Length >= 3 ? null : "Write why (e.g. \"customer returned all items\").",
            message: $"The bill ({Money.Format(bill.Total, true)}) stays in the records marked VOIDED and its items go back into stock. Give the money back to the customer.",
            okText: "Void bill");
        if (reason is null) return;
        await Ui.Run(async () =>
        {
            Core.Rules.Actor? approver = null;
            if (!IsOwner)
            {
                var pin = await Ui.Dialogs.PinAsync("Owner approval", "Voiding a bill needs the owner's PIN.");
                if (pin is null) return;
                approver = (await _auth.FindOwnerByPinAsync(pin))?.ToActor() ?? throw new Core.Rules.PosException("Wrong owner PIN.");
            }
            await _sales.VoidAsync(bill.Id, reason, Ui.Session.Actor, approver);
            Ui.Toasts.Show($"Bill #{bill.Number} voided. Stock is back.");
            await LoadBillsAsync();
            Bill = await _sales.GetAsync(bill.Id);
        });
    }

    // ───────── Export ─────────

    [RelayCommand]
    private async Task Export(string kind)
    {
        var table = BuildTable();
        var shop = (await _settings.GetAsync<ShopSettings>(ShopSettings.Key)).Name;
        var dialog = new SaveFileDialog
        {
            FileName = ReportExporter.SafeFileName($"{table.Title} {F:yyyy-MM-dd}{(F == T ? "" : $" to {T:yyyy-MM-dd}")}"),
            Filter = kind == "pdf" ? "PDF file|*.pdf" : "Excel file|*.xlsx",
            DefaultExt = kind == "pdf" ? ".pdf" : ".xlsx",
        };
        if (dialog.ShowDialog() != true) return;
        await Ui.Run(async () =>
        {
            await Task.Run(() =>
            {
                if (kind == "pdf") ReportExporter.Pdf(table, shop, dialog.FileName);
                else ReportExporter.Excel(table, shop, dialog.FileName);
            });
            Ui.Toasts.Show($"Saved {System.IO.Path.GetFileName(dialog.FileName)}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        });
    }

    private ExportTable BuildTable()
    {
        static string M(decimal v) => Money.Format(v);
        var summary = Tiles.Select(t => (t.Label, t.Value)).ToList();
        string period = UsesDates || Section == ReportSection.Today ? RangeText : $"As of {DateTime.Now:dd MMM yyyy HH:mm}";
        switch (Section)
        {
            case ReportSection.Sales:
                return new ExportTable(ChartTitle, period, summary, ["When", SalesProduct is null ? "Bills" : "Units", "Sales", "Discounts", .. IsOwner ? new[] { "Profit" } : []], [false, true, true, true, true],
                    Days.Select(d => (IReadOnlyList<string>)[d.LongLabel, (SalesProduct is null ? d.Bills : d.Units).ToString(CultureInfo.InvariantCulture), M(d.Sales), M(d.Discounts), .. IsOwner ? new[] { M(d.Profit) } : []]).ToList());
            case ReportSection.Stock:
                return new ExportTable("Stock on hand", period, summary, ["Product", "Category", "Qty", .. IsOwner ? new[] { "Value at cost" } : [], "Value at price"], [false, false, true, true, true],
                    StockRows.Select(r => (IReadOnlyList<string>)[r.Name, r.Category, r.Qty.ToString(CultureInfo.InvariantCulture), .. IsOwner ? new[] { M(r.ValueAtCost) } : [], M(r.ValueAtPrice)]).ToList());
            case ReportSection.LowStockExpiry:
                return new ExportTable("Low stock and expiry", period, summary, ["Product", "Category", "Reorder at", "Qty", "Status"], [false, false, true, true, false],
                    [
                        .. LowRows.Select(r => (IReadOnlyList<string>)[r.Name, r.Category, r.ReorderLevel.ToString(CultureInfo.InvariantCulture), r.StockQty.ToString(CultureInfo.InvariantCulture), r.StockQty <= 0 ? "Out of stock" : "Low stock"]),
                        .. ExpiryRows.Select(r => (IReadOnlyList<string>)[r.ProductName, "", "", r.QtyOnHand.ToString(CultureInfo.InvariantCulture),
                            (r.DaysLeft < 0 ? "Expired " : "Expires ") + r.ExpiryDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)]),
                    ]);
            case ReportSection.Activity:
                return new ExportTable("Activity log", period, summary, ["When", "What", "Who", "Details"], [false, false, false, false],
                    Changes.Select(c => (IReadOnlyList<string>)[c.AtLocal.ToString("dd MMM HH:mm", CultureInfo.InvariantCulture), c.Action, c.User, c.Summary]).ToList());
            case ReportSection.Changes:
                return new ExportTable("Discounts and price changes", period, summary, ["When", "What", "Who", "Details"], [false, false, false, false],
                    Changes.Select(c => (IReadOnlyList<string>)[c.AtLocal.ToString("dd MMM HH:mm", CultureInfo.InvariantCulture), c.Action, c.User, c.Summary]).ToList());
            case ReportSection.Bills:
                return new ExportTable("Bills", period, [], ["Bill", "Time", "Cashier", "Paid by", "Units", "Total", "Status"], [false, false, false, false, true, true, false],
                    Bills.Select(b => (IReadOnlyList<string>)[b.Number.ToString(CultureInfo.InvariantCulture), b.CreatedAtLocal.ToString("dd MMM HH:mm", CultureInfo.InvariantCulture), b.Cashier, b.Methods, b.Units.ToString(CultureInfo.InvariantCulture), M(b.Total), b.Status.ToString()]).ToList());
            default:
                var rows = ProductRows.Select(p => (IReadOnlyList<string>)[p.Name, p.Units.ToString(CultureInfo.InvariantCulture), M(p.Sales), .. IsOwner ? new[] { M(p.Cost), M(p.Profit), $"{p.MarginPercent}%" } : []]).ToList();
                if (Summary is { } s && Section == ReportSection.Today)
                {
                    rows.Insert(0, ["— By payment —", "", ""]);
                    rows.InsertRange(1, s.ByPayment.Select(p => (IReadOnlyList<string>)[p.Label, p.Bills.ToString(CultureInfo.InvariantCulture), M(p.Amount)]));
                    rows.Insert(1 + s.ByPayment.Count, ["— By cashier —", "", ""]);
                    rows.InsertRange(2 + s.ByPayment.Count, s.ByCashier.Select(p => (IReadOnlyList<string>)[p.Label, p.Bills.ToString(CultureInfo.InvariantCulture), M(p.Amount)]));
                    rows.Insert(2 + s.ByPayment.Count + s.ByCashier.Count, ["— Products —", "", ""]);
                }
                return new ExportTable(SectionTitle, period, summary, ["Product", "Units / bills", "Sales", .. IsOwner ? new[] { "Cost", "Profit", "Margin" } : []],
                    [false, true, true, true, true, true], rows);
        }
    }
}
