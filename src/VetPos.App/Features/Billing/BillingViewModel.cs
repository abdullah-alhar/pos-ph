using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VetPos.App.DesignSystem.Controls;
using VetPos.App.Infrastructure;
using VetPos.Core.Billing;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;
using VetPos.Core.Settings;
using VetPos.Core.Stock;
using VetPos.Data.Services;

namespace VetPos.App.Features.Billing;

/// <summary>The till. Everything works by keyboard; the cursor always goes back to the scan box.</summary>
public sealed partial class BillingViewModel : PageViewModel
{
    private readonly CatalogService _catalog;
    private readonly SaleService _sales;
    private readonly BillStore _store;
    private readonly SettingsService _settings;
    private readonly PrintService _printer;
    private readonly AuthService _auth;
    private readonly ShopClock _clock;
    private readonly DispatcherTimer _searchTimer;
    private readonly DispatcherTimer _draftTimer;
    private bool _draftChecked;
    private Guid _clientId = Guid.NewGuid();

    public BillingViewModel(Ui ui, CatalogService catalog, SaleService sales, BillStore store, SettingsService settings, PrintService printer, AuthService auth, ShopClock clock) : base(ui)
    {
        _auth = auth;
        _clock = clock;
        _catalog = catalog;
        _sales = sales;
        _store = store;
        _settings = settings;
        _printer = printer;
        Lines.CollectionChanged += OnLinesChanged;
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await Ui.Run(SearchNowAsync); };
        _draftTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _draftTimer.Tick += async (_, _) => { _draftTimer.Stop(); await ErrorHandling.Quietly(SaveDraftNowAsync, "bill draft"); };
    }

    public ObservableCollection<CartLine> Lines { get; } = [];

    /// <summary>Set while an old bill is being changed: "Changing bill 1024 (was Rs 5,400.00)".</summary>
    [ObservableProperty] private string? _changingBill;
    private int? _replacesNumber;
    public ObservableCollection<ProductRow> Suggestions { get; } = [];

    [ObservableProperty] private CartLine? _selected;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private ProductRow? _suggestion;
    [ObservableProperty] private bool _showSuggestions;
    [ObservableProperty] private string? _searchError;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowSuccess))] private CheckoutResult? _lastSale;
    [ObservableProperty] private int _heldCount;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasBillDiscount), nameof(BillDiscountLabel))] private Discount? _billDiscount;
    [ObservableProperty] private CartTotals _totals = CartTotals.Empty;

    public bool HasLines => Lines.Count > 0;
    public bool ShowSuccess => LastSale is not null && !HasLines;
    public bool HasBillDiscount => Totals.BillDiscountAmount > 0;
    public bool HasLineDiscounts => Totals.LineDiscountTotal > 0;
    public string BillDiscountLabel => BillDiscount?.Type == DiscountType.Percent ? $"Bill discount ({BillDiscount.Describe()})" : "Bill discount";
    public string CountText => $"{Lines.Count} item{(Lines.Count == 1 ? "" : "s")} · {Lines.Sum(l => l.Qty)} unit{(Lines.Sum(l => l.Qty) == 1 ? "" : "s")}";
    public string PayText => HasLines ? $"Pay  {Money.Format(Totals.Total, true)}" : "Pay";

    /// <summary>The view puts the cursor back in the scan box after every action.</summary>
    public event EventHandler? FocusScan;

    private void Focus() => FocusScan?.Invoke(this, EventArgs.Empty);

    public override async Task OnShownAsync()
    {
        HeldCount = await _store.HeldCountAsync();
        if (!_draftChecked)
        {
            _draftChecked = true;
            var draft = await _store.LoadDraftAsync(Ui.Session.Actor.Id);
            if (draft is not null && Lines.Count == 0)
            {
                var t = draft.Totals();
                if (await Ui.Dialogs.ConfirmAsync("Restore the unfinished bill?",
                        $"A bill with {t.UnitCount} item(s), {Money.Format(t.Total, true)}, was not finished (the app closed or the power went off).",
                        "Restore bill", "Start new"))
                    LoadCart(draft);
                else
                    await _store.ClearDraftAsync(Ui.Session.Actor.Id);
            }
        }
        Focus();
    }

    // ───────── Totals & draft ─────────

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (CartLine l in e.NewItems) l.PropertyChanged += OnLineChanged;
        if (e.OldItems is not null) foreach (CartLine l in e.OldItems) l.PropertyChanged -= OnLineChanged;
        for (var i = 0; i < Lines.Count; i++) Lines[i].Number = i + 1;
        if (Lines.Count > 0) LastSale = null;
        Recalculate();
    }

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CartLine.Total) or nameof(CartLine.Qty)) Recalculate();
    }

    partial void OnBillDiscountChanged(Discount? value) => Recalculate();

    private void Recalculate()
    {
        try
        {
            Totals = Cart().Totals();
        }
        catch (PosException ex)
        {
            // A bill discount bigger than the bill after a line was removed: drop it and say so.
            BillDiscount = null;
            Ui.Toasts.Show(ex.Message + " The bill discount was removed.", ToastKind.Warning);
            return;
        }
        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(ShowSuccess));
        OnPropertyChanged(nameof(HasBillDiscount));
        OnPropertyChanged(nameof(HasLineDiscounts));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(PayText));
        _draftTimer.Stop();
        _draftTimer.Start();
    }

    private BillCart Cart() => new(_clientId, Lines.Select(l => l.ToBillLine()).ToList(), BillDiscount);

    public async Task SaveDraftNowAsync()
    {
        if (!Ui.Session.IsSignedIn) return;
        try
        {
            await _store.SaveDraftAsync(Ui.Session.Actor.Id, Cart());
        }
        catch (Exception ex)
        {
            Ui.Logger.LogError(ex, "Could not save the bill draft");
        }
    }

    private void LoadCart(BillCart cart)
    {
        Lines.Clear();
        _clientId = cart.ClientId;
        BillDiscount = null;
        foreach (var l in cart.Lines) Lines.Add(CartLine.From(l));
        BillDiscount = cart.BillDiscount;
        Selected = Lines.LastOrDefault();
    }

    private void ClearBill()
    {
        _clientId = Guid.NewGuid();
        _replacesNumber = null;
        ChangingBill = null;
        BillDiscount = null;
        Lines.Clear();
        Selected = null;
        Search = "";
        ShowSuggestions = false;
    }

    // ───────── Scan & search ─────────

    partial void OnSearchChanged(string value)
    {
        SearchError = null;
        _searchTimer.Stop();
        var (_, text) = SplitQty(value);
        // A scanner types digits very fast then Enter: no list for long numbers.
        if (text.Length == 0)
        {
            ShowSuggestions = false;
            return;
        }
        _searchTimer.Start();
    }

    private async Task SearchNowAsync()
    {
        var (_, text) = SplitQty(Search);
        if (text.Length == 0) return;
        var rows = await _catalog.SearchAsync(text, limit: 30);
        Suggestions.Clear();
        foreach (var r in rows) Suggestions.Add(r);
        Suggestion = Suggestions.FirstOrDefault();
        ShowSuggestions = Suggestions.Count > 0;
    }

    public void MoveSuggestion(int delta)
    {
        if (!ShowSuggestions || Suggestions.Count == 0) return;
        var i = Suggestion is null ? 0 : Suggestions.IndexOf(Suggestion);
        Suggestion = Suggestions[Math.Clamp(i + delta, 0, Suggestions.Count - 1)];
    }

    public void MoveSelection(int delta)
    {
        if (Lines.Count == 0) return;
        var i = Selected is null ? Lines.Count : Lines.IndexOf(Selected);
        Selected = Lines[Math.Clamp(i + delta, 0, Lines.Count - 1)];
    }

    /// <summary>"3*kitten" → (3, "kitten"); "kitten" → (null, "kitten").</summary>
    public static (int? Qty, string Text) SplitQty(string input)
    {
        var s = (input ?? "").Trim();
        var star = s.IndexOf('*', StringComparison.Ordinal);
        if (star > 0 && int.TryParse(s[..star], NumberStyles.None, CultureInfo.InvariantCulture, out var q) && q > 0)
            return (q, s[(star + 1)..].Trim());
        return (null, s);
    }

    /// <summary>Enter in the scan box.</summary>
    [RelayCommand]
    private async Task Submit()
    {
        var (qty, text) = SplitQty(Search);
        if (text.Length == 0)
        {
            if (qty is { } q && Selected is not null) { await SetQtyAsync(Selected, q); Search = ""; }
            return;
        }
        await Ui.Run(async () =>
        {
            int? id = null;
            if (ShowSuggestions && Suggestion is not null) id = Suggestion.Id;
            id ??= await _catalog.FindIdByBarcodeAsync(text);
            if (id is null)
            {
                var rows = await _catalog.SearchAsync(text, limit: 30);
                if (rows.Count == 1) id = rows[0].Id;
                else if (rows.Count > 1)
                {
                    Suggestions.Clear();
                    foreach (var r in rows) Suggestions.Add(r);
                    Suggestion = rows[0];
                    ShowSuggestions = true;
                    return;
                }
            }
            if (id is null)
            {
                SearchError = $"Nothing found for “{text}”. Check the name or scan again.";
                return;
            }
            if (await AddAsync(id.Value, qty ?? 1))
            {
                Search = "";
                ShowSuggestions = false;
            }
        });
        Focus();
    }

    public async Task PickSuggestionAsync(ProductRow row)
    {
        var (qty, _) = SplitQty(Search);
        if (await AddAsync(row.Id, qty ?? 1))
        {
            Search = "";
            ShowSuggestions = false;
        }
        Focus();
    }

    private async Task<bool> AddAsync(int productId, int qty)
    {
        var item = await _sales.GetBillingItemAsync(productId) ?? throw new PosException("That product is switched off or was removed.");
        var allowAbove = (await _settings.GetAsync<BillingSettings>(BillingSettings.Key)).AllowSellAboveStock;
        var today = item.Today;

        // How the price is chosen (see the stock-entry price question).
        var options = item.PriceOptions;
        List<(decimal? Group, decimal Price, int Qty)> parts;
        if (item.HasSeveralPrices && item.PriceChoice == PriceChoice.AskAtBilling)
        {
            var choice = await Ui.Dialogs.ChooseAsync($"Which price? — {item.Name}",
                "This item is in stock at more than one price. The first one is the older stock.",
                options.Select(o => ($"{Money.Format(o.Price, true)}", $"{o.QtyAvailable} in stock")).ToList(),
                "Add at this price");
            if (choice is null) return false;
            parts = [(options[choice.Value].Price, options[choice.Value].Price, qty)];
        }
        else if (item.HasSeveralPrices)
        {
            // Old price first: fill from the batch that sells first, then the next price.
            parts = [];
            var left = qty;
            foreach (var o in options)
            {
                if (left == 0) break;
                var onBill = Lines.Where(l => l.ProductId == productId && l.PriceGroup == o.Price).Sum(l => l.Qty);
                var free = Math.Max(0, o.QtyAvailable - onBill);
                var take = Math.Min(left, free);
                if (take > 0) { parts.Add((o.Price, o.Price, take)); left -= take; }
            }
            if (left > 0) parts.Add((options[^1].Price, options[^1].Price, left)); // checked below
            if (parts.Select(p => p.Price).Distinct().Count() > 1)
                Ui.Toasts.Show($"{item.Name}: " + string.Join(", ", parts.Select(p => $"{p.Qty} at {Money.Format(p.Price, true)}")), ToastKind.Info);
        }
        else
        {
            parts = [(null, item.DefaultPrice, qty)];
        }

        foreach (var (group, price, n) in parts)
        {
            var onBill = Lines.Where(l => l.ProductId == productId && l.PriceGroup == group).Sum(l => l.Qty);
            var available = Fefo.SellableQty(item.Batches, today, group);
            if (onBill + n > available)
            {
                var at = group is { } g ? $" at {Money.Format(g, true)}" : "";
                var message = available - onBill <= 0 ? $"No stock left{at} for {item.Name}." : $"Only {available - onBill} {item.Name} left{at}.";
                if (!allowAbove) throw new PosException(message);
                Ui.Toasts.Show(message + " Selling anyway (allowed in Settings).", ToastKind.Warning);
            }
            var existing = Lines.FirstOrDefault(l => l.ProductId == productId && l.PriceGroup == group && !l.PriceChanged && l.Discount is null);
            if (existing is not null)
            {
                existing.Qty += n;
                Pulse(existing);
            }
            else
            {
                var line = new CartLine
                {
                    ProductId = item.Id, Name = item.Name, Barcode = item.Barcode, ListPrice = price, UnitPrice = price, PriceGroup = group, Qty = n,
                };
                Lines.Add(line);
                Pulse(line);
            }
        }
        return true;
    }

    private void Pulse(CartLine line)
    {
        line.Flash = false;
        line.Flash = true;
        Selected = line;
    }

    // ───────── Line actions ─────────

    [RelayCommand]
    private async Task ChangeQty(string delta)
    {
        if (Selected is not { } line) return;
        var next = line.Qty + int.Parse(delta, CultureInfo.InvariantCulture);
        if (next < 1) return;
        await SetQtyAsync(line, next);
    }

    private async Task SetQtyAsync(CartLine line, int qty)
    {
        await Ui.Run(async () =>
        {
            if (qty > CartCalculator.MaxQty) throw new PosException($"Quantity cannot be more than {CartCalculator.MaxQty:N0}.");
            if (qty > line.Qty)
            {
                var item = await _sales.GetBillingItemAsync(line.ProductId);
                var available = item is null ? 0 : Fefo.SellableQty(item.Batches, item.Today, line.PriceGroup);
                var others = Lines.Where(l => l != line && l.ProductId == line.ProductId && l.PriceGroup == line.PriceGroup).Sum(l => l.Qty);
                if (others + qty > available)
                {
                    var msg = $"Only {Math.Max(0, available - others)} {line.Name} in stock.";
                    if (!(await _settings.GetAsync<BillingSettings>(BillingSettings.Key)).AllowSellAboveStock) throw new PosException(msg);
                    Ui.Toasts.Show(msg + " Selling anyway (allowed in Settings).", ToastKind.Warning);
                }
            }
            if (line.Discount is not null && CartCalculator.CheckLineDiscount(line.UnitPrice, qty, line.Discount) is { } err)
                throw new PosException(err + " Change the discount first.");
            line.Qty = qty;
        });
        Focus();
    }

    [RelayCommand]
    private async Task EditQty()
    {
        if (Selected is not { } line) { Focus(); return; }
        var text = await Ui.Dialogs.PromptAsync($"Quantity — {line.Name}", "Quantity", line.Qty.ToString(CultureInfo.InvariantCulture),
            t => int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var q) && q >= 1 ? null : "Type a whole number, 1 or more.",
            okText: "Set quantity", number: true);
        if (text is not null) await SetQtyAsync(line, int.Parse(text, CultureInfo.InvariantCulture));
        Focus();
    }

    /// <summary>F4: price for this bill only (the product's saved price does not change). Logged at save.</summary>
    [RelayCommand]
    private async Task EditPrice()
    {
        if (Selected is not { } line) { Focus(); return; }
        var text = await Ui.Dialogs.PromptAsync($"Price — {line.Name}", "Price for this bill (Rs)", Money.ToInput(line.UnitPrice),
            t =>
            {
                if (!Money.TryParse(t, out var p)) return "Type a price like 450 or 450.50.";
                return line.Discount is not null && CartCalculator.CheckLineDiscount(p, line.Qty, line.Discount) is { } e ? e + " Remove the discount first." : null;
            },
            message: $"Shelf price {Money.Format(line.ListPrice, true)}. This changes the price on this bill only; it is saved with your name.",
            okText: "Change price", number: true);
        if (text is not null && Money.TryParse(text, out var price))
        {
            line.UnitPrice = price;
            if (price != line.ListPrice) Ui.Toasts.Show($"Price changed to {Money.Format(price, true)} for this bill.", ToastKind.Info);
        }
        Focus();
    }

    /// <summary>Price typed straight into the bill line (click the price). Returns false when it was not accepted.</summary>
    public bool SetPriceFromText(CartLine line, string text)
    {
        if (!Money.TryParse(text, out var price))
        {
            Ui.Toasts.Show("Type a price like 450 or 450.50.", ToastKind.Error);
            return false;
        }
        if (line.Discount is not null && CartCalculator.CheckLineDiscount(price, line.Qty, line.Discount) is { } e)
        {
            Ui.Toasts.Show(e + " Remove the discount first.", ToastKind.Error);
            return false;
        }
        if (price == line.UnitPrice) return true;
        line.UnitPrice = price;
        Ui.Toasts.Show(price == line.ListPrice ? "Back to the shelf price." : $"Price changed to {Money.Format(price, true)} for this bill.", ToastKind.Info);
        return true;
    }

    [RelayCommand]
    private async Task LineDiscount()
    {
        if (Selected is not { } line) { Focus(); return; }
        var result = await DiscountDialog.ShowAsync(Ui.Dialogs, $"Discount — {line.Name}", line.Gross, line.UnitPrice, line.Qty, line.Discount, isBill: false);
        if (result is { } r) line.Discount = r.Discount;
        Focus();
    }

    [RelayCommand]
    private async Task BillDiscountEdit()
    {
        if (!HasLines) { Focus(); return; }
        var result = await DiscountDialog.ShowAsync(Ui.Dialogs, "Bill discount", Totals.Subtotal - Totals.LineDiscountTotal, 0, 1, BillDiscount, isBill: true);
        if (result is { } r) BillDiscount = r.Discount;
        Focus();
    }

    [RelayCommand]
    private async Task RemoveLine()
    {
        if (Selected is not { } line) { Focus(); return; }
        var confirm = (await _settings.GetAsync<BillingSettings>(BillingSettings.Key)).ConfirmRemoveLine;
        if (!confirm || await Ui.Dialogs.ConfirmAsync("Remove this item?", $"{line.Qty} × {line.Name}", "Remove", danger: true))
        {
            var i = Lines.IndexOf(line);
            Lines.Remove(line);
            Selected = Lines.Count == 0 ? null : Lines[Math.Min(i, Lines.Count - 1)];
        }
        Focus();
    }

    [RelayCommand]
    public async Task NewBill()
    {
        if (HasLines && !await Ui.Dialogs.ConfirmAsync("Start a new bill?", "The items on this bill will be cleared.", "Clear bill", danger: true))
        {
            Focus();
            return;
        }
        ClearBill();
        LastSale = null;
        Focus();
    }

    // ───────── Hold / recall ─────────

    [RelayCommand]
    private async Task Hold()
    {
        if (!HasLines) { Ui.Toasts.Show("There is nothing to hold.", ToastKind.Info); Focus(); return; }
        var label = await Ui.Dialogs.PromptAsync("Hold this bill", "Name or note (optional)", null, okText: "Hold bill",
            message: "The customer can come back; press F9 to bring the bill back.");
        if (label is null) { Focus(); return; }
        if (await Ui.Run(() => _store.HoldAsync(Cart(), label, Ui.Session.Actor)))
        {
            ClearBill();
            await _store.ClearDraftAsync(Ui.Session.Actor.Id);
            HeldCount = await _store.HeldCountAsync();
            Ui.Toasts.Show("Bill held. Press F9 to bring it back.", ToastKind.Info);
        }
        Focus();
    }

    [RelayCommand]
    public async Task Recall()
    {
        await Ui.Run(async () =>
        {
            var held = await _store.HeldAsync();
            if (held.Count == 0) { Ui.Toasts.Show("There are no held bills.", ToastKind.Info); return; }
            if (HasLines) throw new PosException("Finish or hold this bill first, then recall the other one.");
            var choice = await Ui.Dialogs.ChooseAsync("Held bills", "Pick the bill to bring back to the till.",
                held.Select(h => ($"{(h.Label ?? "Bill")} · {Money.Format(h.Total, true)}", $"{h.ItemCount} item(s) · held {h.CreatedAtLocal:HH:mm} by {h.HeldBy}")).ToList(),
                "Recall bill");
            if (choice is null) return;
            LoadCart(await _store.RecallAsync(held[choice.Value].Id));
            HeldCount = await _store.HeldCountAsync();
        });
        Focus();
    }

    // ───────── Pay ─────────

    [RelayCommand]
    private async Task Pay()
    {
        if (!HasLines) { Ui.Toasts.Show("Add an item to the bill first.", ToastKind.Info); Focus(); return; }
        var payments = await PaymentDialog.ShowAsync(Ui.Dialogs, Totals, BillDiscount, Lines.Count, d =>
        {
            var error = CartCalculator.CheckBillDiscount(Totals.Subtotal - Totals.LineDiscountTotal, d);
            if (error is not null) return (Totals, error);
            BillDiscount = d;
            return (Totals, null);
        });
        if (payments is null) { Focus(); return; }

        CheckoutResult? result = null;
        await Ui.Run(async () => result = await _sales.CheckoutAsync(new CheckoutRequest(Cart(), payments, _replacesNumber is { } n ? $"Replaces bill {n}" : null), Ui.Session.Actor));
        if (result is null) { Focus(); return; }

        ClearBill();
        LastSale = result;
        Ui.Toasts.Show(result.Change > 0 ? $"Bill #{result.Number} saved · change {Money.Format(result.Change, true)}" : $"Bill #{result.Number} saved");
        foreach (var w in result.Warnings) Ui.Toasts.Show(w, ToastKind.Warning);
        Focus();

        if ((await _settings.GetAsync<BillingSettings>(BillingSettings.Key)).AutoPrint) await PrintAsync(result.SaleId, reprint: false);
    }

    private async Task PrintAsync(int saleId, bool reprint)
    {
        try
        {
            if (!await _printer.PrintSaleAsync(saleId, reprint) && reprint)
                Ui.Toasts.Show("No receipt printer is set up (Settings → Printer).", ToastKind.Info);
        }
        catch (PosException ex)
        {
            Ui.Toasts.Show("The bill is saved, but it did not print. " + ex.Message + " Press F10 to print again.", ToastKind.Warning);
        }
        catch (Exception ex)
        {
            Ui.Logger.LogError(ex, "Printing failed");
            Ui.Toasts.Show("The bill is saved, but it did not print. Press F10 to try again.", ToastKind.Warning);
        }
    }

    /// <summary>F7: find an old bill, print it again, or change it.</summary>
    [RelayCommand]
    private async Task PastBills()
    {
        var choice = await PastBillsDialog.ShowAsync(Ui.Dialogs, _sales, _clock);
        if (choice is null) { Focus(); return; }
        if (choice.Action == PastBillAction.Print)
        {
            await PrintAsync(choice.SaleId, reprint: true);
            Ui.Toasts.Show($"Bill {choice.Number} sent to the printer.");
            Focus();
            return;
        }
        await ChangeBillAsync(choice.SaleId);
        Focus();
    }

    /// <summary>
    /// Changing a saved bill: the old bill is cancelled (kept in the records, stock goes back) and its
    /// items open here as a new bill. Paying saves the new bill with "Replaces bill N".
    /// </summary>
    private async Task ChangeBillAsync(int saleId)
    {
        if (HasLines)
        {
            await Ui.Dialogs.AlertAsync("Finish this bill first", "Pay or hold (F8) the bill on screen, then open the old bill again.");
            return;
        }
        var old = await _sales.GetAsync(saleId);
        if (old is null || old.Status == SaleStatus.Voided) { Ui.Toasts.Show("That bill is already cancelled.", ToastKind.Info); return; }
        if (!await Ui.Dialogs.ConfirmAsync($"Change bill {old.Number}?",
                $"Bill {old.Number} ({Money.Format(old.Total, true)}) is cancelled and kept in the records. Its items open as a new bill so you can fix them and take payment again. The customer has already paid {Money.Format(old.Total, true)}: take or give back only the difference.",
                "Change bill"))
            return;
        await Ui.Run(async () =>
        {
            Core.Rules.Actor? approver = null;
            if (!Ui.Session.Actor.IsOwner)
            {
                var pin = await Ui.Dialogs.PinAsync("Owner approval", "Changing a saved bill needs the owner's PIN.");
                if (pin is null) return;
                approver = (await _auth.FindOwnerByPinAsync(pin))?.ToActor() ?? throw new PosException("Wrong owner PIN.");
            }
            await _sales.VoidAsync(old.Id, "Changed: replaced by a new bill", Ui.Session.Actor, approver);
            ClearBill();
            LastSale = null;
            foreach (var l in old.Lines.Where(l => l.ProductId > 0))
                Lines.Add(new CartLine
                {
                    ProductId = l.ProductId, Name = l.Name, Barcode = l.Barcode, ListPrice = l.ListPrice,
                    Qty = l.Qty, UnitPrice = l.UnitPrice,
                    Discount = l.DiscountType is { } t && l.DiscountValue > 0 ? new Discount(t, l.DiscountValue) : null,
                });
            BillDiscount = old.BillDiscountType is { } bt && old.BillDiscountValue > 0 ? new Discount(bt, old.BillDiscountValue) : null;
            Selected = Lines.LastOrDefault();
            _replacesNumber = old.Number;
            ChangingBill = $"Changing bill {old.Number}. The customer already paid {Money.Format(old.Total, true)}.";
            Ui.Toasts.Show($"Bill {old.Number} is open. Change it, then press F12.");
        });
    }

    [RelayCommand]
    public async Task ReprintLast()
    {
        var id = LastSale?.SaleId ?? await _sales.LastSaleIdAsync(Ui.Session.Actor.Id);
        if (id is null) { Ui.Toasts.Show("There is no bill to print yet.", ToastKind.Info); return; }
        await PrintAsync(id.Value, reprint: true);
        Focus();
    }
}
