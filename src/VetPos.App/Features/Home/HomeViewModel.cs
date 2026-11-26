using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VetPos.App.Infrastructure;
using VetPos.App.Shell;
using VetPos.Data.Services;

namespace VetPos.App.Features.Home;

public sealed partial class HomeViewModel(Ui ui, ReportService reports, CatalogService catalog, ShopClock clock, UpdateService updates)
    : PageViewModel(ui)
{
    public UpdateService Updates { get; } = updates;

    public string TodayText => clock.Today.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);

    [ObservableProperty] private string? _billsToday;
    [ObservableProperty] private string? _salesToday;
    [ObservableProperty] private string? _lowStock;

    public override async Task OnShownAsync()
    {
        if (IsBusy) return;
        await LoadAsync(async () =>
        {
            var today = clock.Today;
            var result = await Task.Run(async () =>
            {
                var sales = await reports.OverviewAsync(today);
                return (sales.Bills, sales.Sales, LowStock: await catalog.CountLowStockAsync());
            });

            BillsToday = result.Bills.ToString(CultureInfo.CurrentCulture);
            SalesToday = $"Rs {result.Sales:N2}";
            LowStock = result.LowStock.ToString(CultureInfo.CurrentCulture);
        });
    }

    public event EventHandler<NavItem>? CardActivated;

    [RelayCommand]
    private void Open(NavItem item) => CardActivated?.Invoke(this, item);

    [RelayCommand]
    private Task CheckUpdates() => Updates.CheckAsync(quiet: false);
}
