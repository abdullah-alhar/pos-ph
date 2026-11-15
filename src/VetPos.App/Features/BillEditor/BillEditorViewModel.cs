using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VetPos.App.DesignSystem.Controls;
using VetPos.App.Infrastructure;
using VetPos.Core.Settings;
using VetPos.Data.Services;
using VetPos.Printing;

namespace VetPos.App.Features.BillEditor;

/// <summary>One printed line in the live preview.</summary>
/// <summary>
/// One printed line in the preview, at the printer's real proportions: normal characters fill
/// 48 per 72 mm line; wide / large print twice as wide, tall / large twice as high.
/// </summary>
public sealed record PreviewLine(string Text, PieceAlign Align, bool Bold, TextSize Size, bool Underline, bool Invert, string Kind, string PieceId, bool IsSelected)
{
    public double ScaleX => Size is TextSize.Large or TextSize.Wide ? 2 : 1;
    public double ScaleY => Size is TextSize.Large or TextSize.Tall ? 2 : 1;
}

public sealed record PieceTypeOption(PieceType Type, string Label, string Hint)
{
    public string Icon => Type switch
    {
        PieceType.Text => "\uE8D2",
        PieceType.Pair => "\uE8A0",
        PieceType.Logo => "\uE722",
        PieceType.Divider => "\uE738",
        PieceType.Space => "\uE76F",
        PieceType.Items => "\uE8FD",
        PieceType.Totals => "\uE8EF",
        PieceType.Payments => "\uE8C7",
        PieceType.ItemCount => "\uE8CB",
        PieceType.Savings => "\uE8EC",
        PieceType.Barcode => "\uEE6F",
        PieceType.Qr => "\uED14",
        _ => "\uE70F",
    };
}

public sealed record EditorChat(bool FromUser, string Text);

public sealed record IssueView(ReceiptIssue Issue)
{
    public string Level => Issue.Level.ToString();
    public string Message => Issue.Message;
    public string? FixLabel => Issue.FixLabel;
}

public sealed partial class BillEditorViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly SaleService _sales;
    private readonly PrintService _printer;
    private readonly LogoService _logo;
    private readonly DispatcherTimer _snapshotTimer;
    private readonly List<string> _history = [];
    private int _historyAt = -1;
    private string _savedJson = "";
    private ReceiptData _data = ReceiptData.Sample();

    private readonly VetPos.AI.ReceiptAssistant _ai;

    public BillEditorViewModel(Ui ui, SettingsService settings, SaleService sales, PrintService printer, LogoService logo, VetPos.AI.ReceiptAssistant ai) : base(ui)
    {
        _ai = ai;
        _settings = settings;
        _sales = sales;
        _printer = printer;
        _logo = logo;
        _snapshotTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _snapshotTimer.Tick += (_, _) => { _snapshotTimer.Stop(); Snapshot(); };
        _logo.Changed += (_, _) => { LogoImage = _logo.Load(); Render(); };
    }

    public ReceiptDesign Design { get; private set; } = ReceiptTemplates.Classic();
    public ObservableCollection<ReceiptPiece> Pieces { get; } = [];
    public ObservableCollection<PreviewLine> Preview { get; } = [];
    public ObservableCollection<IssueView> Issues { get; } = [];
    public IReadOnlyList<PieceTypeOption> PieceTypes { get; } = ReceiptFields.PieceInfo.Select(p => new PieceTypeOption(p.Key, p.Value.Label, p.Value.Hint)).ToList();
    public IReadOnlyList<string> Templates { get; } = ReceiptTemplates.All.Select(t => t.Name).ToList();
    public IReadOnlyList<KeyValuePair<string, string>> Fields { get; } = ReceiptFields.All.ToList();
    public IReadOnlyList<PieceAlign> Aligns { get; } = Enum.GetValues<PieceAlign>();
    public IReadOnlyList<TextSize> Sizes { get; } = Enum.GetValues<TextSize>();
    public IReadOnlyList<DividerStyle> DividerStyles { get; } = Enum.GetValues<DividerStyle>();
    public IReadOnlyList<PrintWhen> Whens { get; } = Enum.GetValues<PrintWhen>();
    public IReadOnlyList<ItemLayout> Layouts { get; } = Enum.GetValues<ItemLayout>();
    public IReadOnlyList<QrSize> QrSizes { get; } = Enum.GetValues<QrSize>();

    [ObservableProperty] private ReceiptPiece? _selected;
    [ObservableProperty] private string _shopName = "";
    [ObservableProperty] private string _shopAddress = "";
    [ObservableProperty] private string _shopPhone = "";
    [ObservableProperty] private string _shopEmail = "";
    [ObservableProperty] private string _headerText = "";
    [ObservableProperty] private string _footerText = "";
    [ObservableProperty] private bool _narrow;
    [ObservableProperty] private bool _useLatestBill;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private ImageSource? _logoImage;
    [ObservableProperty] private string _paperLength = "";
    [ObservableProperty] private string _aiRequest = "";
    [ObservableProperty] private bool _aiBusy;
    [ObservableProperty] private string? _aiReply;
    [ObservableProperty] private double _zoom = 1.25;
    [ObservableProperty] private bool _printerView;
    [ObservableProperty] private string _printerText = "";
    [ObservableProperty] private int _seriousIssues;

    public ObservableCollection<EditorChat> AiChat { get; } = [];
    public string ZoomText => $"{Zoom * 100:0}%";

    partial void OnZoomChanged(double value) => OnPropertyChanged(nameof(ZoomText));

    [RelayCommand]
    private void ZoomBy(string step) => Zoom = Math.Clamp(Math.Round(Zoom + double.Parse(step, System.Globalization.CultureInfo.InvariantCulture), 2), 0.75, 2.5);

    [RelayCommand]
    private void ClearSelection() => Selected = null;

    public IReadOnlyList<string> AiExamples { get; } =
        ["Make the total big", "Add our WhatsApp 0771234567 as a QR at the bottom", "Put the logo on the left", "Save paper", "Use the modern design"];

    /// <summary>The AI changes a copy of the design; it shows at once and Ctrl+Z undoes it. Nothing is saved until Save.</summary>
    [RelayCommand]
    private async Task AskAi(string? request)
    {
        request = (request ?? AiRequest).Trim();
        if (request.Length == 0 || AiBusy) return;
        AiBusy = true;
        AiReply = null;
        AiChat.Add(new EditorChat(true, request));
        try
        {
            Snapshot();
            Design.Pieces = [.. Pieces];
            Design.HeaderText = HeaderText;
            Design.FooterText = FooterText;
            var answer = await Task.Run(() => _ai.AskAsync(request, Design, Ui.Session.Actor));
            if (answer.Changes.Count > 0)
            {
                Load(answer.Design.ToJson());
                Commit();
            }
            AiReply = answer.Reply + (answer.Changes.Count > 0 ? "  (Ctrl+Z to undo · Save to keep)" : "");
            AiChat.Add(new EditorChat(false, AiReply));
            AiRequest = "";
        }
        catch (VetPos.AI.AiUnavailableException ex)
        {
            AiReply = ex.Message;
            AiChat.Add(new EditorChat(false, ex.Message));
        }
        catch (Core.Rules.PosException ex)
        {
            AiReply = ex.Message;
            AiChat.Add(new EditorChat(false, ex.Message));
        }
        catch (Exception ex)
        {
            var reference = ErrorHandling.NewReference();
            Microsoft.Extensions.Logging.LoggerExtensions.LogError(Ui.Logger, ex, "Receipt AI failed {Reference}", reference);
            AiReply = $"Something went wrong ({reference}).";
        }
        finally
        {
            AiBusy = false;
        }
    }

    public bool CanUndo => _historyAt > 0;
    public bool CanRedo => _historyAt < _history.Count - 1;

    public override Task OnShownAsync() => LoadAsync(async () =>
    {
        if (_history.Count > 0) return; // keep unsaved work when coming back to the page
        var shop = await _settings.GetAsync<ShopSettings>(ShopSettings.Key);
        ShopName = shop.Name;
        ShopAddress = shop.Address;
        ShopPhone = shop.Phone;
        ShopEmail = shop.Email;
        Load((await _printer.DesignAsync()).ToJson());
        _savedJson = CurrentJson();
        IsDirty = false;
        Narrow = (await _settings.GetAsync<PrinterSettings>(PrinterSettings.Key)).CharsPerLine < 40;
        LogoImage = _logo.Load();
        Snapshot();
    });

    private void Load(string json)
    {
        Design = ReceiptDesign.FromJson(json);
        var keep = Selected?.Id;
        Pieces.Clear();
        foreach (var p in Design.Pieces) Pieces.Add(p);
        HeaderText = Design.HeaderText;
        FooterText = Design.FooterText;
        Selected = Pieces.FirstOrDefault(p => p.Id == keep) ?? Pieces.FirstOrDefault();
        Render();
    }

    private string CurrentJson()
    {
        Design.Pieces = [.. Pieces];
        Design.HeaderText = HeaderText;
        Design.FooterText = FooterText;
        return Design.ToJson() + "|" + ShopName + "|" + ShopAddress + "|" + ShopPhone + "|" + ShopEmail;
    }

    /// <summary>Called by the view whenever any option changes.</summary>
    public void Changed()
    {
        Render();
        _snapshotTimer.Stop();
        _snapshotTimer.Start();
    }

    partial void OnSelectedChanged(ReceiptPiece? value) => Render();
    partial void OnHeaderTextChanged(string value) => Changed();
    partial void OnFooterTextChanged(string value) => Changed();
    partial void OnShopNameChanged(string value) => Changed();
    partial void OnShopAddressChanged(string value) => Changed();
    partial void OnShopPhoneChanged(string value) => Changed();
    partial void OnShopEmailChanged(string value) => Changed();
    partial void OnNarrowChanged(bool value) => Render();

    async partial void OnUseLatestBillChanged(bool value)
    {
        _data = ReceiptData.Sample();
        if (value)
        {
            var id = await _sales.LastSaleIdAsync(Ui.Session.Actor.Id);
            var sale = id is null ? null : await _sales.GetAsync(id.Value);
            if (sale is null) Ui.Toasts.Show("There is no bill yet; showing a sample.", ToastKind.Info);
            else _data = await _printer.DataForAsync(sale, false);
        }
        Render();
    }

    private void Render()
    {
        if (Pieces.Count == 0 && Design.Pieces.Count == 0) return;
        Design.Pieces = [.. Pieces];
        Design.HeaderText = HeaderText;
        Design.FooterText = FooterText;
        var data = _data with { ShopName = ShopName, ShopAddress = ShopAddress, ShopPhone = ShopPhone, ShopEmail = ShopEmail };
        var doc = ReceiptRenderer.Render(Design, data, Narrow ? 32 : 48, _logo.HasLogo, 2, cut: false);
        Preview.Clear();
        foreach (var op in doc.Ops)
        {
            var sel = op switch { TextOp t => t.PieceId, LogoOp l => l.PieceId, BarcodeOp b => b.PieceId, QrOp q => q.PieceId, _ => "" } == Selected?.Id;
            Preview.Add(op switch
            {
                TextOp t => new PreviewLine(t.Text, t.Align, t.Bold, t.Size, t.Underline, t.Invert, "text", t.PieceId, sel),
                LogoOp l => new PreviewLine("", l.Align, false, TextSize.Normal, false, false, "logo", l.PieceId, sel),
                BarcodeOp b => new PreviewLine(b.Value, PieceAlign.Center, false, TextSize.Normal, false, false, "barcode", b.PieceId, sel),
                QrOp q => new PreviewLine(q.Value, PieceAlign.Center, false, TextSize.Normal, false, false, "qr", q.PieceId, sel),
                FeedOp f => new PreviewLine(string.Join("\n", Enumerable.Repeat(" ", Math.Max(1, f.Lines))), PieceAlign.Left, false, TextSize.Normal, false, false, "text", "", false),
                _ => new PreviewLine("", PieceAlign.Left, false, TextSize.Normal, false, false, "text", "", false),
            });
        }
        PaperLength = $"About {doc.EstimatedLengthMm / 10.0:0.#} cm of paper";
        PrinterText = ReceiptRenderer.ToText(doc);
        Issues.Clear();
        foreach (var i in ReceiptCheck.Check(Design, Narrow ? 32 : 48)) Issues.Add(new IssueView(i));
        SeriousIssues = Issues.Count(i => i.Issue.Level == IssueLevel.Error);
        IsDirty = CurrentJson() != _savedJson;
    }

    private void Snapshot()
    {
        var json = Design.ToJson();
        if (_historyAt >= 0 && _history[_historyAt] == json) return;
        _history.RemoveRange(_historyAt + 1, _history.Count - _historyAt - 1);
        _history.Add(json);
        if (_history.Count > 100) _history.RemoveAt(0);
        _historyAt = _history.Count - 1;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    [RelayCommand]
    private void Undo()
    {
        Snapshot();
        if (!CanUndo) return;
        _historyAt--;
        Load(_history[_historyAt]);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    [RelayCommand]
    private void Redo()
    {
        if (!CanRedo) return;
        _historyAt++;
        Load(_history[_historyAt]);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void Commit()
    {
        Render();
        Snapshot();
    }

    [RelayCommand]
    private void Add(PieceType type)
    {
        var piece = type switch
        {
            PieceType.Text => new ReceiptPiece { Type = type, Text = "New text" },
            PieceType.Pair => new ReceiptPiece { Type = type, Text = "Bill: {bill_no}", Right = "{date}" },
            PieceType.Qr => new ReceiptPiece { Type = type, Value = "https://wa.me/94771234567" },
            PieceType.Signature => new ReceiptPiece { Type = type, Text = "Customer signature" },
            _ => new ReceiptPiece { Type = type },
        };
        var at = Selected is null ? Pieces.Count : Pieces.IndexOf(Selected) + 1;
        Pieces.Insert(at, piece);
        Selected = piece;
        Commit();
    }

    [RelayCommand]
    private void Move(string direction)
    {
        if (Selected is not { Locked: false } p) return;
        var i = Pieces.IndexOf(p);
        var j = direction == "up" ? i - 1 : i + 1;
        if (j < 0 || j >= Pieces.Count) return;
        Pieces.Move(i, j);
        Selected = p;
        Commit();
    }

    [RelayCommand]
    private void Remove()
    {
        if (Selected is not { Locked: false } p) return;
        var i = Pieces.IndexOf(p);
        Pieces.Remove(p);
        Selected = Pieces.Count == 0 ? null : Pieces[Math.Min(i, Pieces.Count - 1)];
        Commit();
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (Selected is not { } p) return;
        var copy = p.Clone();
        Pieces.Insert(Pieces.IndexOf(p) + 1, copy);
        Selected = copy;
        Commit();
    }

    [RelayCommand]
    private void ToggleHidden(ReceiptPiece? piece)
    {
        piece ??= Selected;
        if (piece is null) return;
        piece.Hidden = !piece.Hidden;
        var i = Pieces.IndexOf(piece);
        Pieces[i] = piece; // refresh the row
        Selected = piece;
        Commit();
    }

    [RelayCommand]
    private void InsertField(string field)
    {
        if (Selected is null || Selected.Type is not (PieceType.Text or PieceType.Pair or PieceType.Signature)) return;
        Selected.Text += "{" + field + "}";
        var p = Selected;
        Selected = null;
        Selected = p;
        Commit();
    }

    [RelayCommand]
    private async Task ApplyTemplate(string name)
    {
        if (!await Ui.Dialogs.ConfirmAsync($"Use the {name} design?", "Your pieces are replaced. You can undo with Ctrl+Z.", "Use design")) return;
        var t = ReceiptTemplates.All.First(x => x.Name == name).Build();
        t.HeaderText = HeaderText;
        t.FooterText = FooterText;
        Load(t.ToJson());
        Commit();
    }

    [RelayCommand]
    private void SavePaper()
    {
        Load(ReceiptCheck.SavePaper(Design).ToJson());
        Commit();
        Ui.Toasts.Show("Spaces and double lines removed; items on one line.", ToastKind.Info);
    }

    [RelayCommand]
    private void Fix(IssueView? issue)
    {
        if (issue?.Issue.Fix is not { } fix) return;
        Load(fix(Design).ToJson());
        Commit();
    }

    [RelayCommand]
    private async Task ChooseLogo()
    {
        var dialog = new OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp", Title = "Choose the shop logo" };
        if (dialog.ShowDialog() != true) return;
        await Ui.Run(() =>
        {
            _logo.SetLogo(dialog.FileName);
            LogoImage = _logo.Load();
            if (!Pieces.Any(p => p.Type == PieceType.Logo)) Pieces.Insert(0, new ReceiptPiece { Type = PieceType.Logo });
            foreach (var p in Pieces.Where(p => p.Type == PieceType.Logo)) p.Hidden = false;
            Commit();
            Ui.Toasts.Show(LogoImage is null ? "The logo was saved but could not be shown." : "Logo saved. It prints in black and white on the receipt.");
            return Task.CompletedTask;
        });
    }

    [RelayCommand]
    private Task RemoveLogo() => Ui.Run(() =>
    {
        _logo.RemoveLogo();
        LogoImage = null;
        Render();
        return Task.CompletedTask;
    });

    [RelayCommand]
    private async Task Save()
    {
        Design.Pieces = [.. Pieces];
        Design.HeaderText = HeaderText;
        Design.FooterText = FooterText;
        if (ReceiptCheck.Check(Design).Any(i => i.Level == IssueLevel.Error)
            && !await Ui.Dialogs.ConfirmAsync("The design has a problem", "The check found a real problem (see the list). Save anyway?", "Save anyway"))
            return;
        await Ui.Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(ShopName)) throw new Core.Rules.PosException("Enter the shop name.");
            var shop = await _settings.GetAsync<ShopSettings>(ShopSettings.Key);
            await _settings.SaveAsync(ShopSettings.Key, shop with { Name = ShopName.Trim(), Address = ShopAddress.Trim(), Phone = ShopPhone.Trim(), Email = ShopEmail.Trim() }, Ui.Session.Actor);
            await _settings.SaveRawAsync(ReceiptDesign.SettingsKey, Design.ToJson(), Ui.Session.Actor);
            _savedJson = CurrentJson();
            IsDirty = false;
            Ui.Toasts.Show("Receipt design saved. New bills print like this.");
        });
    }

    [RelayCommand]
    private async Task TestPrint()
    {
        await Ui.Run(async () =>
        {
            var p = await _settings.GetAsync<PrinterSettings>(PrinterSettings.Key);
            if (p.Mode == PrinterMode.None) { Ui.Toasts.Show("Set up the printer first (Settings → Printer).", ToastKind.Info); return; }
            await Save();
            await _printer.TestAsync(p);
            Ui.Toasts.Show("Test receipt sent to the printer.");
        });
    }
}
