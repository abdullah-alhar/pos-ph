using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VetPos.App.DesignSystem.Controls;
using VetPos.App.Infrastructure;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;
using VetPos.Data.Services;

namespace VetPos.App.Features.StockEntry;

/// <summary>One line of the delivery being entered (text fields so the cashier can type freely).</summary>
public sealed partial class ReceiptLineRow : ObservableObject
{
    public required int ProductId { get; init; }
    public required string Name { get; init; }
    public required bool TracksExpiry { get; init; }
    public decimal OldPrice { get; init; }

    /// <summary>Expiry of the last delivery, filled in for this one. Shown so the cashier sees it can be changed.</summary>
    public DateTime? LastExpiry { get; init; }
    public string ExpiryHint => LastExpiry is null ? "No date = does not expire"
        : Expiry?.Date == LastExpiry.Value.Date ? "Same as last time. Change it if the new stock has a different date."
        : "Last time: " + LastExpiry.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LineTotal))] private string _qty = "1";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LineTotal), nameof(MarginText))] private string _cost = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MarginText))] private string _selling = "";
    [ObservableProperty] private string _batchNo = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ExpiryHint))] private DateTime? _expiry;
    [ObservableProperty] private string? _error;

    public decimal LineTotal => int.TryParse(Qty, out var q) && Money.TryParse(Cost, out var c) ? Money.Round(q * c) : 0;

    public string MarginText => Money.TryParse(Cost, out var c) && Money.TryParse(Selling, out var s) && s > 0
        ? $"{(s - c) / s * 100:0.#}% margin" + (s < c ? " · below cost!" : "")
        : "";

    public ReceiptLineInput? ToInput(out string? error)
    {
        error = null;
        if (!int.TryParse(Qty, NumberStyles.None, CultureInfo.InvariantCulture, out var q) || q < 1) error = "Quantity must be a whole number, 1 or more.";
        else if (!Money.TryParse(Cost, out _)) error = "Type the cost price.";
        else if (!Money.TryParse(Selling, out _)) error = "Type the selling price.";
        else if (Expiry is { } x && x.Date < DateTime.Today) error = "This expiry date has already passed. Pick the date printed on the pack.";
        Error = error;
        if (error is not null || !Money.TryParse(Cost, out var cost) || !Money.TryParse(Selling, out var selling)) return null;
        return new ReceiptLineInput(ProductId, q, cost, selling, BatchNo, Expiry is { } e ? DateOnly.FromDateTime(e) : null);
    }
}

public sealed partial class StockEntryViewModel : PageViewModel
{
    private readonly CatalogService _catalog;
    private readonly StockEntryService _entry;
    private readonly DispatcherTimer _searchTimer;

    public StockEntryViewModel(Ui ui, CatalogService catalog, StockEntryService entry) : base(ui)
    {
        _catalog = catalog;
        _entry = entry;
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await Ui.Run(SearchNowAsync); };
        Lines.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) foreach (ReceiptLineRow r in e.NewItems) r.PropertyChanged += OnRowChanged;
            Refresh();
        };
    }

    public ObservableCollection<SupplierRow> Suppliers { get; } = [];
    public ObservableCollection<ReceiptLineRow> Lines { get; } = [];
    public ObservableCollection<ProductRow> Suggestions { get; } = [];
    public ObservableCollection<ReceiptRow> Recent { get; } = [];

    [ObservableProperty] private SupplierRow? _supplier;
    [ObservableProperty] private string _invoiceNo = "";
    [ObservableProperty] private DateTime _invoiceDate = DateTime.Today;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private ProductRow? _suggestion;
    [ObservableProperty] private bool _showSuggestions;
    [ObservableProperty] private string? _searchError;
    [ObservableProperty] private ReceiptLineRow? _selectedLine;

    public decimal Total => Lines.Sum(l => l.LineTotal);
    public int Units => Lines.Sum(l => int.TryParse(l.Qty, out var q) ? q : 0);
    public bool HasLines => Lines.Count > 0;

    public event EventHandler<ReceiptLineRow>? FocusLine;
    public event EventHandler? FocusSearch;

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReceiptLineRow.LineTotal) or nameof(ReceiptLineRow.Qty)) Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Units));
        OnPropertyChanged(nameof(HasLines));
    }

    public override Task OnShownAsync() => LoadAsync(async () =>
    {
        var keep = Supplier?.Id;
        Suppliers.Clear();
        foreach (var s in await _catalog.SuppliersAsync()) Suppliers.Add(s);
        Supplier = Suppliers.FirstOrDefault(s => s.Id == keep) ?? Supplier;
        Recent.Clear();
        foreach (var r in await _entry.ListAsync(30)) Recent.Add(r);
    });

    partial void OnSearchChanged(string value)
    {
        SearchError = null;
        _searchTimer.Stop();
        if (value.Trim().Length == 0) { ShowSuggestions = false; return; }
        _searchTimer.Start();
    }

    private async Task SearchNowAsync()
    {
        var rows = await _catalog.SearchAsync(Search, limit: 20);
        Suggestions.Clear();
        foreach (var r in rows) Suggestions.Add(r);
        Suggestion = Suggestions.FirstOrDefault();
        ShowSuggestions = Suggestions.Count > 0;
    }

    public void MoveSuggestion(int delta)
    {
        if (Suggestions.Count == 0) return;
        var i = Suggestion is null ? 0 : Suggestions.IndexOf(Suggestion);
        Suggestion = Suggestions[Math.Clamp(i + delta, 0, Suggestions.Count - 1)];
    }

    [RelayCommand]
    private async Task SubmitSearch()
    {
        var text = Search.Trim();
        if (text.Length == 0) return;
        await Ui.Run(async () =>
        {
            var id = ShowSuggestions ? Suggestion?.Id : null;
            id ??= await _catalog.FindIdByBarcodeAsync(text);
            if (id is null)
            {
                var rows = await _catalog.SearchAsync(text, limit: 20);
                if (rows.Count == 1) id = rows[0].Id;
            }
            if (id is null) { SearchError = $"No product matches “{text}”. Add it in Products first."; return; }
            await AddLineAsync(id.Value);
        });
    }

    public async Task AddLineAsync(int productId)
    {
        var p = await _catalog.GetAsync(productId) ?? throw new PosException("That product no longer exists.");
        // The newest delivery's expiry date is filled in; the cashier changes it when the new stock differs.
        var last = p.Batches.Where(b => b.ExpiryDate is not null).OrderByDescending(b => b.ReceivedAt).ThenByDescending(b => b.Id).FirstOrDefault()?.ExpiryDate;
        var lastDate = last?.ToDateTime(TimeOnly.MinValue);
        var row = new ReceiptLineRow
        {
            ProductId = p.Id, Name = p.Name, TracksExpiry = p.TracksExpiry, OldPrice = p.SellingPrice,
            Cost = Money.ToInput(p.CostPrice), Selling = Money.ToInput(p.SellingPrice),
            LastExpiry = lastDate, Expiry = lastDate >= DateTime.Today ? lastDate : null,
        };
        Lines.Add(row);
        SelectedLine = row;
        Search = "";
        ShowSuggestions = false;
        FocusLine?.Invoke(this, row);
    }

    [RelayCommand]
    private void RemoveLine(ReceiptLineRow? row)
    {
        row ??= SelectedLine;
        if (row is null) return;
        Lines.Remove(row);
        FocusSearch?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearSupplier() => Supplier = null;

    [RelayCommand]
    private async Task NewSupplier()
    {
        var sheet = new DialogSheet(Ui.Dialogs, "New supplier");
        var name = sheet.Field("Name", DialogSheet.Input());
        var phone = sheet.Field("Phone", DialogSheet.Input());
        var address = sheet.Field("Address", DialogSheet.Input());
        name.Loaded += (_, _) => name.Focus();
        sheet.Buttons("Add supplier", async () =>
        {
            try
            {
                var id = await _catalog.SaveSupplierAsync(new SupplierRow(0, name.Text, phone.Text, address.Text, true), Ui.Session.Actor);
                sheet.Close(id);
            }
            catch (PosException ex) { sheet.ShowError(ex.Message, name); }
        });
        if (await Ui.Dialogs.ShowAsync(sheet) is int newId)
        {
            await OnShownAsync();
            Supplier = Suppliers.FirstOrDefault(s => s.Id == newId);
            Ui.Toasts.Show("Supplier added.");
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (Lines.Count == 0) { Ui.Toasts.Show("Add at least one item.", ToastKind.Warning); return; }
        var inputs = new List<ReceiptLineInput>();
        foreach (var row in Lines)
        {
            var input = row.ToInput(out var error);
            if (input is null) { Ui.Toasts.Show($"{row.Name}: {error}", ToastKind.Error); SelectedLine = row; FocusLine?.Invoke(this, row); return; }
            inputs.Add(input);
        }

        await Ui.Run(async () =>
        {
            var questions = await _entry.PriceQuestionsAsync(inputs);
            var decisions = new Dictionary<int, PriceDecision>();
            foreach (var q in questions)
            {
                var old = string.Join(" / ", q.CurrentPrices.Select(p => Money.Format(p, true)));
                var choice = await Ui.Dialogs.ChooseAsync($"New price for {q.ProductName}",
                    $"{q.QtyOnHand} are still on the shelf at {old}. The new delivery sells at {Money.Format(q.NewPrice, true)}. What should billing do?",
                    [
                        ($"Change all stock to {Money.Format(q.NewPrice, true)}", "Old and new stock sell at the new price from now."),
                        ("Keep the old price until the old stock is sold", "Billing sells the old stock first at its old price, then the new price, by itself."),
                        ("Ask at billing", "When this item is scanned, the cashier picks the price each time."),
                    ], "Use this");
                if (choice is null) return;
                decisions[q.ProductId] = (PriceDecision)choice.Value;
            }
            var saved = await _entry.SaveAsync(new ReceiptInput(Supplier?.Id, InvoiceNo, DateOnly.FromDateTime(InvoiceDate), Note, inputs), decisions, Ui.Session.Actor);
            Ui.Toasts.Show($"{saved.Number} saved: {Units} units in stock, {Money.Format(saved.Total, true)}.");
            Lines.Clear();
            InvoiceNo = "";
            Note = "";
            InvoiceDate = DateTime.Today;
            await OnShownAsync();
            FocusSearch?.Invoke(this, EventArgs.Empty);
        });
    }
}
