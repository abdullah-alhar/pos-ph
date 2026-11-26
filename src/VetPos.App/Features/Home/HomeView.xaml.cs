using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VetPos.App.DesignSystem;
using VetPos.App.Shell;

namespace VetPos.App.Features.Home;

public partial class HomeView : UserControl
{
    // ---------- Attached property: one-line help under each card title ----------

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.RegisterAttached("Description", typeof(string), typeof(HomeView),
            new PropertyMetadata(string.Empty));

    public static string GetDescription(DependencyObject o) => (string)o.GetValue(DescriptionProperty);
    public static void SetDescription(DependencyObject o, string v) => o.SetValue(DescriptionProperty, v);

    // ---------- Items from the shell (same list the sidebar uses) ----------

    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(ObservableCollection<NavItem>),
            typeof(HomeView), new PropertyMetadata(null, OnItemsChanged));

    public ObservableCollection<NavItem>? Items
    {
        get => (ObservableCollection<NavItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    // The cards on this page. This is OUR OWN copy of the list (without the Home card),
    // so the sidebar is never affected.
    private readonly ObservableCollection<NavItem> _cards = new();

    private static bool IsCardItem(NavItem n) =>
        !string.Equals(n.Title, "Home", StringComparison.OrdinalIgnoreCase);

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (HomeView)d;
        if (e.OldValue is ObservableCollection<NavItem> old) old.CollectionChanged -= view.OnNavItemsChanged;
        if (e.NewValue is ObservableCollection<NavItem> now) now.CollectionChanged += view.OnNavItemsChanged;
        view.RebuildCards();
    }

    private void OnNavItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildCards();

    private void RebuildCards()
    {
        _cards.Clear();
        if (Items is null) return;
        foreach (var n in Items)
            if (IsCardItem(n)) _cards.Add(n);
    }

    // ---------- Number of cards per row (changes with window width) ----------

    public static readonly DependencyProperty ColumnCountProperty =
        DependencyProperty.Register(nameof(ColumnCount), typeof(int),
            typeof(HomeView), new PropertyMetadata(4));

    public int ColumnCount
    {
        get => (int)GetValue(ColumnCountProperty);
        set => SetValue(ColumnCountProperty, value);
    }

    public HomeView()
    {
        InitializeComponent();
        CardList.ItemsSource = _cards;

        SizeChanged += (_, e) =>
            ColumnCount = e.NewSize.Width >= 1000 ? 4 : e.NewSize.Width >= 700 ? 3 : 2;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Same short page fade-in the rest of the app uses.
        Motion.FadeScaleIn(this);
    }

    // ---------- Card entrance: one soft fade, one card after another ----------

    private void Card_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button card) return;

        // Respect the Windows "show animations" setting.
        if (!SystemParameters.ClientAreaAnimation)
        {
            card.Opacity = 1;
            return;
        }

        int index = card.DataContext is NavItem nav ? Math.Max(0, _cards.IndexOf(nav)) : 0;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            BeginTime = TimeSpan.FromMilliseconds(40 * index),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        card.BeginAnimation(OpacityProperty, fade);

        // Put keyboard focus on the first card so the keyboard works straight away.
        if (index == 0)
            card.Dispatcher.BeginInvoke(() => card.Focus(), DispatcherPriority.Input);
    }

    // ---------- Quick start buttons (Start a new bill / Add stock / Find a product) ----------

    private void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string title }) return;

        var item = Items?.FirstOrDefault(n =>
            string.Equals(n.Title, title, StringComparison.OrdinalIgnoreCase));

        if (item is not null && DataContext is HomeViewModel vm && vm.OpenCommand.CanExecute(item))
            vm.OpenCommand.Execute(item);
    }

    // ---------- Arrow keys move between cards ----------

    // The list container is a ContentPresenter; the focusable element is the Button inside it.
    private Button? CardAt(int index) =>
        CardList.ItemContainerGenerator.ContainerFromIndex(index) is ContentPresenter cp
        && VisualTreeHelper.GetChildrenCount(cp) > 0
            ? VisualTreeHelper.GetChild(cp, 0) as Button
            : null;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;

        int count = _cards.Count;
        if (count == 0) return;

        int idx = -1;
        for (int i = 0; i < count; i++)
            if (ReferenceEquals(CardAt(i), Keyboard.FocusedElement)) { idx = i; break; }
        if (idx < 0) return;

        int cols = Math.Max(1, ColumnCount);
        int next = e.Key switch
        {
            Key.Right => (idx + 1) % count,
            Key.Left  => (idx - 1 + count) % count,
            Key.Down  => Math.Min(idx + cols, count - 1),
            Key.Up    => Math.Max(idx - cols, 0),
            _         => idx,
        };

        if (next != idx && CardAt(next) is { } target)
        {
            target.Focus();
            e.Handled = true;
        }
    }
}

/// <summary>Gives each section card a short plain-language help line.</summary>
public sealed class NavDescriptionConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Billing"]      = "Make a sale and print the bill",
        ["Stock in"]     = "Add new stock when goods arrive",
        ["Products"]     = "Food, medicine, extras and prices",
        ["Labels"]       = "Print price and shelf labels",
        ["Reports"]      = "Sales, profit and stock reports",
        ["Bill editor"]  = "Fix or reprint an old bill",
        ["Receipt"]      = "Fix or reprint an old bill",
        ["Settings"]     = "Shop details, printer and backups",
        ["AI Assistant"] = "Ask questions about your shop",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string title && Map.TryGetValue(title, out var text) ? text : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
