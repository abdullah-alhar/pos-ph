using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VetPos.App.DesignSystem.Controls;
using VetPos.App.Infrastructure;
using VetPos.Core.Rules;
using VetPos.Core.Settings;
using VetPos.Data.Services;

namespace VetPos.App.Features.Labels;

public sealed partial class LabelItem : ObservableObject
{
    public required int ProductId { get; init; }
    public required string Name { get; init; }
    public required decimal Price { get; init; }
    [ObservableProperty] private string? _barcode;
    [ObservableProperty] private string _copies = "1";
    public int CopyCount => int.TryParse(Copies, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 0, 500) : 0;
}

/// <summary>Barcode / price labels on A4 sticker sheets or a label-printer roll.</summary>
public sealed partial class LabelsViewModel : PageViewModel
{
    private readonly CatalogService _catalog;
    private readonly SaleService _sales;
    private readonly SettingsService _settings;

    public LabelsViewModel(Ui ui, CatalogService catalog, SaleService sales, SettingsService settings) : base(ui)
    {
        _catalog = catalog;
        _sales = sales;
        _settings = settings;
        Layout = LabelLayout.All[0];
        Items.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) foreach (LabelItem i in e.NewItems) i.PropertyChanged += (_, _) => Refresh();
            Refresh();
        };
    }

    public ObservableCollection<LabelItem> Items { get; } = [];
    public ObservableCollection<ProductRow> Suggestions { get; } = [];
    public IReadOnlyList<LabelLayout> Layouts { get; } = LabelLayout.All;

    [ObservableProperty] private LabelLayout _layout;
    [ObservableProperty] private bool _showShop = true;
    [ObservableProperty] private bool _showName = true;
    [ObservableProperty] private bool _showPrice = true;
    [ObservableProperty] private bool _showBarcode = true;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private ProductRow? _suggestion;
    [ObservableProperty] private bool _showSuggestions;
    [ObservableProperty] private FrameworkElement? _preview;
    [ObservableProperty] private string _summary = "";
    private string _shopName = "";

    public bool HasItems => Items.Count > 0;

    public override async Task OnShownAsync()
    {
        _shopName = (await _settings.GetAsync<ShopSettings>(ShopSettings.Key)).Name;
        Refresh();
    }

    partial void OnLayoutChanged(LabelLayout value) => Refresh();
    partial void OnShowShopChanged(bool value) => Refresh();
    partial void OnShowNameChanged(bool value) => Refresh();
    partial void OnShowPriceChanged(bool value) => Refresh();
    partial void OnShowBarcodeChanged(bool value) => Refresh();

    async partial void OnSearchChanged(string value)
    {
        var text = value.Trim();
        if (text.Length == 0) { ShowSuggestions = false; return; }
        var rows = await _catalog.SearchAsync(text, limit: 20);
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
            if (id is null) throw new PosException($"No product matches “{text}”.");
            await AddAsync(id.Value);
        });
    }

    public async Task AddAsync(int productId)
    {
        var p = await _catalog.GetAsync(productId) ?? throw new PosException("That product no longer exists.");
        var price = (await _sales.GetBillingItemAsync(productId))?.DefaultPrice ?? p.SellingPrice;
        var existing = Items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is not null) existing.Copies = (existing.CopyCount + 1).ToString(CultureInfo.InvariantCulture);
        else Items.Add(new LabelItem { ProductId = p.Id, Name = p.Name, Price = price, Barcode = p.Barcode, Copies = Math.Max(1, p.StockQty).ToString(CultureInfo.InvariantCulture) });
        Search = "";
        ShowSuggestions = false;
    }

    [RelayCommand]
    private void Remove(LabelItem? item)
    {
        if (item is not null) Items.Remove(item);
    }

    [RelayCommand]
    private void ClearAll() => Items.Clear();

    [RelayCommand]
    private Task GiveBarcode(LabelItem? item) => Ui.Run(async () =>
    {
        if (item is null) return;
        item.Barcode = await _catalog.AssignInStoreBarcodeAsync(item.ProductId, Ui.Session.Actor);
        Ui.Toasts.Show($"{item.Name} now has barcode {item.Barcode}. It scans at billing.");
        Refresh();
    });

    private List<LabelContent> AllLabels() =>
        Items.SelectMany(i => Enumerable.Repeat(new LabelContent(_shopName, i.Name, i.Price, i.Barcode), i.CopyCount)).ToList();

    private LabelOptions Options => new(ShowShop, ShowName, ShowPrice, ShowBarcode);

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasItems));
        var labels = AllLabels();
        Summary = LabelRenderer.Count(labels.Count, Layout);
        var sample = labels.Count > 0 ? labels : [new LabelContent(_shopName, "Royal Canin Kitten 2 kg", 5450m, "2000000000015")];
        Preview = LabelRenderer.Page(sample.Take(Layout.PerPage).ToList(), Layout, Options);
    }

    [RelayCommand]
    private Task Print() => Ui.Run(() =>
    {
        var labels = AllLabels();
        if (labels.Count == 0) throw new PosException("Add products and the number of labels first.");
        if (ShowBarcode && Items.Any(i => i.CopyCount > 0 && string.IsNullOrWhiteSpace(i.Barcode)))
            Ui.Toasts.Show("Some products have no barcode; their labels print without one (use “Give barcode”).", ToastKind.Warning);
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;
        var doc = LabelRenderer.Document(labels, Layout, Options);
        dialog.PrintDocument(doc.DocumentPaginator, "VetPOS labels");
        Ui.Toasts.Show($"{labels.Count} label(s) sent to {dialog.PrintQueue.Name}.");
        return Task.CompletedTask;
    });
}
