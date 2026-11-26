using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VetPos.App.DesignSystem.Controls;
using VetPos.App.Infrastructure;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;
using VetPos.Core.Stock;
using VetPos.Data.Services;

namespace VetPos.App.Features.Products;

public enum ProductFilter { All, LowStock, Inactive }

/// <summary>The product being edited in the side panel.</summary>
public sealed partial class ProductEditor : ObservableObject
{
    public int? Id { get; init; }
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _barcode = "";
    [ObservableProperty] private string _extraBarcodes = "";
    [ObservableProperty] private CategoryRow? _category;
    [ObservableProperty] private ProductUnit _unit = ProductUnit.Piece;
    [ObservableProperty] private string _costPrice = "0";
    [ObservableProperty] private string _sellingPrice = "0";
    [ObservableProperty] private string _reorderLevel = "0";
    [ObservableProperty] private bool _tracksExpiry;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isMedicine;
    [ObservableProperty] private string _genericName = "";
    [ObservableProperty] private string _strength = "";
    [ObservableProperty] private string _dosageForm = "";
    [ObservableProperty] private string _species = "";

    // New products only: what is on the shelf now, and its expiry date (saved as the first stock).
    [ObservableProperty] private string _openingQty = "";
    [ObservableProperty] private DateTime? _expiry;

    public static IReadOnlyList<string> DosageForms { get; } =
        ["Tablet", "Chewable tablet", "Capsule", "Injection", "Oral liquid", "Oral powder", "Spot-on", "Spray", "Ointment / cream", "Shampoo", "Drops", "Vaccine", "Bolus", "Premix (feed)"];


    public int StockQty { get; init; }
    public PriceChoice PriceChoice { get; init; }
    public ObservableCollection<BatchView> Batches { get; } = [];
    public bool IsNew => Id is null;
    public string Title => IsNew ? "New product" : Name;
    public string PriceChoiceText => PriceChoice switch
    {
        PriceChoice.OldFirst => "Two prices on hand: old price sells first",
        PriceChoice.AskAtBilling => "Two prices on hand: cashier chooses at billing",
        _ => "",
    };
}

public sealed record BatchView(BatchRow Row, string Status, string Tone, string ExpiryText, string ExpiryTone)
{
    public string Received => "received " + Row.ReceivedAt.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
}

public sealed partial class ProductsViewModel : PageViewModel
{
    private readonly CatalogService _catalog;
    private readonly StockEntryService _stock;
    private readonly ShopClock _clock;
    private readonly DispatcherTimer _searchTimer;


    public ProductsViewModel(Ui ui, CatalogService catalog, StockEntryService stock, ShopClock clock) : base(ui)
    {
        _catalog = catalog;
        _stock = stock;
        _clock = clock;
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await Ui.Run(ReloadAsync); };
    }

    public ObservableCollection<ProductRow> Rows { get; } = [];
    public ObservableCollection<CategoryRow> Categories { get; } = [];
    public IReadOnlyList<ProductUnit> Units { get; } = Enum.GetValues<ProductUnit>();
    public bool IsOwner => Ui.Session.IsOwner;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private ProductFilter _filter;
    [ObservableProperty] private ProductRow? _selected;
    [ObservableProperty] private ProductEditor? _editor;
    [ObservableProperty] private int _lowCount;

    public bool IsEmpty => !IsBusy && Rows.Count == 0;

    public override async Task OnShownAsync()
    {
        if (Categories.Count == 0) await LoadCategoriesAsync();
        await ReloadAsync();
    }

    private async Task LoadCategoriesAsync()
    {
        Categories.Clear();
        foreach (var c in await _catalog.CategoriesAsync()) Categories.Add(c);
    }

    partial void OnSearchChanged(string value) { _searchTimer.Stop(); _searchTimer.Start(); }

    async partial void OnFilterChanged(ProductFilter value) => await ReloadAsync();

    private int _reloadVersion;

    private async Task ReloadAsync()
    {
        var version = ++_reloadVersion;
        var filter = Filter;
        var search = Search;
        IsBusy = true;
        LoadError = null;
        try
        {
            var result = await Task.Run(async () =>
            {
                if (filter == ProductFilter.LowStock)
                {
                    var low = await _catalog.LowStockAsync();
                    return (Count: low.Count, Rows: low.Where(r => r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList());
                }
                var count = await _catalog.CountLowStockAsync();
                var found = await _catalog.SearchAsync(search, includeInactive: filter == ProductFilter.Inactive, limit: 1000);
                return (Count: count, Rows: found.Where(r => filter != ProductFilter.Inactive || !r.IsActive).ToList());
            });
            if (version != _reloadVersion) return;
            LowCount = result.Count;
            Rows.Clear();
            foreach (var row in result.Rows) Rows.Add(row);
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(Ui.Logger, ex, "Products could not be loaded");
            if (version == _reloadVersion) LoadError = "Products could not be loaded. Try refreshing the list.";
        }
        finally
        {
            if (version == _reloadVersion) IsBusy = false;
        }
    }

    async partial void OnSelectedChanged(ProductRow? value)
    {
        if (value is not null) await OpenAsync(value.Id);
    }

    public async Task OpenAsync(int id)
    {
        await Ui.Run(async () =>
        {
            var p = await _catalog.GetAsync(id) ?? throw new PosException("That product no longer exists.");
            if (Categories.Count == 0) await LoadCategoriesAsync();
            var editor = new ProductEditor
            {
                Id = p.Id, Name = p.Name, Barcode = p.Barcode ?? "", ExtraBarcodes = string.Join(", ", p.ExtraBarcodes),
                Category = Categories.FirstOrDefault(c => c.Id == p.CategoryId), Unit = p.Unit,
                CostPrice = Money.ToInput(p.CostPrice), SellingPrice = Money.ToInput(p.SellingPrice), ReorderLevel = p.ReorderLevel.ToString(CultureInfo.InvariantCulture),
                TracksExpiry = p.TracksExpiry, IsActive = p.IsActive, StockQty = p.StockQty, PriceChoice = p.PriceChoice,
                IsMedicine = p.Medicine is not null, GenericName = p.Medicine?.GenericName ?? "", Strength = p.Medicine?.Strength ?? "",
                DosageForm = p.Medicine?.DosageForm ?? "", Species = p.Medicine?.Species ?? "",
            };
            var today = _clock.Today;
            foreach (var b in p.Batches.Where(b => b.QtyOnHand > 0))
            {
                var (expiryText, expiryTone) = Fefo.StatusOf(b.ExpiryDate, today) switch
                {
                    ExpiryStatus.Expired => ($"Expired {b.ExpiryDate:dd MMM yyyy}", "Badge.Danger"),
                    ExpiryStatus.ExpiringSoon => ($"Expires {b.ExpiryDate:dd MMM yyyy}", "Badge.Warning"),
                    ExpiryStatus.Ok => ($"Expires {b.ExpiryDate:dd MMM yyyy}", "Badge.Success"),
                    _ => ("No expiry date", "Badge.Outline"),
                };
                editor.Batches.Add(new BatchView(b, b.SellingPrice == p.SellingPrice ? "Current price" : "Older price",
                    b.SellingPrice == p.SellingPrice ? "Badge.Success" : "Badge.Warning", expiryText, expiryTone));
            }
            Editor = editor;
        });
    }

    /// <summary>The view puts the cursor in the name box when a form opens.</summary>
    public event EventHandler? FocusName;

    [RelayCommand]
    public async Task NewProduct()
    {
        if (Categories.Count == 0) await LoadCategoriesAsync();
        Selected = null;
        var last = Editor;
        Editor = new ProductEditor
        {
            Category = last?.Category ?? Categories.FirstOrDefault(),
            Unit = last?.IsNew == true ? last.Unit : ProductUnit.Piece,
            IsMedicine = last?.IsNew == true && last.IsMedicine,
        };
        FocusName?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Enter on the last field: "Save this product?" → Enter saves and opens a blank form for the next one.</summary>
    [RelayCommand]
    private async Task SaveConfirm()
    {
        if (Editor is not { } e) return;
        var price = Money.TryParse(e.SellingPrice, out var p) ? Money.Format(p, true) : e.SellingPrice;
        if (!await Ui.Dialogs.ConfirmAsync(e.IsNew ? "Add this product?" : "Save the changes?", $"{e.Name.Trim()} · {price}", e.IsNew ? "Add product" : "Save")) return;
        var wasNew = e.IsNew;
        if (await SaveCoreAsync() && wasNew)
        {
            await NewProduct();
            Ui.Toasts.Show("Saved. Type the next product.");
        }
    }

    [RelayCommand]
    private async Task GenerateBarcode()
    {
        if (Editor is not { } e) return;
        if (!string.IsNullOrWhiteSpace(e.Barcode) && !await Ui.Dialogs.ConfirmAsync("Replace the barcode?", $"This product already has {e.Barcode}.", "Make a new one")) return;
        await Ui.Run(async () => e.Barcode = await _catalog.NextInStoreBarcodeAsync());
    }

    /// <summary>Opens the AI chat ready to add products: type or paste them in any words, then Confirm the card.</summary>
    [RelayCommand]
    private void AddWithAi() => Ui.AskAi("Add these products: ");

    /// <summary>Remove the open product (owner). Used products are switched off, unused ones deleted.</summary>
    [RelayCommand]
    private async Task Remove()
    {
        if (Editor is not { Id: { } id } e) return;
        if (!Ui.Session.IsOwner) { Ui.Toasts.Show("Only the owner can remove products.", ToastKind.Info); return; }
        if (!await Ui.Dialogs.ConfirmAsync($"Remove {e.Name}?",
                "If it was never sold or stocked, it is deleted. If it has bills or stock history, it is taken off sale instead: old bills and reports keep it, and you can switch it on again under Switched off.",
                "Remove product", danger: true))
            return;
        await Ui.Run(async () =>
        {
            var deleted = await _catalog.RemoveAsync(id, Ui.Session.Actor);
            Ui.Toasts.Show(deleted ? $"{e.Name} deleted." : $"{e.Name} taken off sale (it has history). Find it under Switched off.");
            Editor = null;
            Selected = null;
            await ReloadAsync();
        });
    }

    [RelayCommand]
    private void CloseEditor()
    {
        Editor = null;
        Selected = null;
    }

    [RelayCommand]
    private Task Save() => SaveCoreAsync();

    private async Task<bool> SaveCoreAsync()
    {
        if (Editor is not { } e) return false;
        e.Error = null;
        if (!Money.TryParse(e.CostPrice, out var cost) || !Money.TryParse(e.SellingPrice, out var price)) { e.Error = "Type the prices like 450 or 450.50."; return false; }
        if (!int.TryParse(e.ReorderLevel, NumberStyles.None, CultureInfo.InvariantCulture, out var reorder)) { e.Error = "The reorder level is a whole number (0 = no warning)."; return false; }
        var opening = 0;
        if (e.IsNew)
        {
            if (e.OpeningQty.Trim().Length > 0 && !int.TryParse(e.OpeningQty.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out opening)) { e.Error = "Stock now is a whole number, like 12."; return false; }
            if (e.Expiry is { } x && x.Date < DateTime.Today) { e.Error = "This expiry date has already passed. Pick the date printed on the pack."; return false; }
            if (e.Expiry is not null && opening == 0) { e.Error = "Type how many you have now, so the expiry date is saved with them."; return false; }
        }
        var input = new ProductInput(e.Id, e.Name, e.Barcode, e.ExtraBarcodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            e.Category?.Id ?? 0, e.Unit, cost, price, reorder, e.TracksExpiry || e.Expiry is not null, e.IsActive,
            e.IsMedicine ? new MedicineInfo(e.GenericName, e.Strength, e.DosageForm, e.Species) : null);
        try
        {
            var id = await _catalog.SaveAsync(input, Ui.Session.Actor);
            if (opening > 0)
            {
                // The first stock, saved like a delivery without a supplier (so it has a batch and an expiry date).
                var line = new ReceiptLineInput(id, opening, cost, price, null, e.Expiry is { } exp ? DateOnly.FromDateTime(exp) : null);
                await _stock.SaveAsync(new ReceiptInput(null, null, _clock.Today, "Opening stock", [line]), new Dictionary<int, PriceDecision>(), Ui.Session.Actor);
            }
            Ui.Toasts.Show(opening > 0 ? $"Saved: {e.Name.Trim()} with {opening} in stock" : $"Saved: {e.Name.Trim()}");
            await ReloadAsync();
            await OpenAsync(id);
            return true;
        }
        catch (PosException ex)
        {
            e.Error = ex.Message;
            return false;
        }
    }

    [RelayCommand]
    private async Task AddCategory()
    {
        var name = await Ui.Dialogs.PromptAsync("New category", "Name", null, t => t.Length is > 0 and <= 60 ? null : "Enter a name (60 letters at most).", okText: "Add");
        if (name is null) return;
        await Ui.Run(async () =>
        {
            var id = await _catalog.SaveCategoryAsync(null, name, Ui.Session.Actor);
            await LoadCategoriesAsync();
            if (Editor is not null) Editor.Category = Categories.FirstOrDefault(c => c.Id == id);
        });
    }

    /// <summary>Change the expiry date of one batch (typed wrong, or the new stock has a different date).</summary>
    [RelayCommand]
    private async Task ChangeExpiry(BatchView? batch)
    {
        if (batch is null || Editor?.Id is not { } productId) return;
        var sheet = new DialogSheet(Ui.Dialogs, "Change expiry date", $"{batch.Row.QtyOnHand} units · {batch.Received}. Pick the date printed on the pack.", 420);
        var picker = sheet.Field("Expiry date", new System.Windows.Controls.DatePicker
        {
            SelectedDate = batch.Row.ExpiryDate?.ToDateTime(TimeOnly.MinValue), MinHeight = 46, SelectedDateFormat = System.Windows.Controls.DatePickerFormat.Long,
        }, "Leave it empty if this stock does not expire.");
        sheet.Buttons("Save date", async () =>
        {
            try
            {
                await _stock.SetBatchExpiryAsync(batch.Row.Id, picker.SelectedDate is { } d ? DateOnly.FromDateTime(d) : null, Ui.Session.Actor);
                sheet.Close(true);
            }
            catch (PosException ex) { sheet.ShowError(ex.Message); }
        });
        if (await Ui.Dialogs.ShowAsync(sheet) is true)
        {
            Ui.Toasts.Show("Expiry date changed.");
            await ReloadAsync();
            await OpenAsync(productId);
        }
    }

    /// <summary>Owner: take damaged / expired units off a batch, or correct a count. Logged with the reason.</summary>
    [RelayCommand]
    private async Task AdjustBatch(BatchView? batch)
    {
        if (batch is null || Editor?.Id is not { } productId) return;
        var sheet = new DialogSheet(Ui.Dialogs, "Change stock of this batch", $"Batch {batch.Row.BatchNo ?? "—"} · {batch.Received} · {batch.Row.QtyOnHand} on hand. Use a minus to take units off.", 420);
        var change = sheet.Field("Change (e.g. -2)", DialogSheet.Input("", number: true));
        var reason = sheet.Field("Reason", DialogSheet.Input("", placeholder: "e.g. damaged bag, counted wrong"));
        change.Loaded += (_, _) => { change.Focus(); change.SelectAll(); };
        sheet.Buttons("Save change", async () =>
        {
            if (!int.TryParse(change.Text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) || n == 0) { sheet.ShowError("Type a number like -2 or 5.", change); return; }
            try
            {
                await _stock.AdjustBatchAsync(batch.Row.Id, n, reason.Text, Ui.Session.Actor);
                sheet.Close(true);
            }
            catch (PosException ex) { sheet.ShowError(ex.Message); }
        });
        if (await Ui.Dialogs.ShowAsync(sheet) is true)
        {
            Ui.Toasts.Show("Stock changed and logged.");
            await ReloadAsync();
            await OpenAsync(productId);
        }
    }

}
