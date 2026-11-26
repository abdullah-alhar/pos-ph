using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using VetPos.App.Features.Reports;
using VetPos.App.Features.Start;
using VetPos.App.Shell;
using VetPos.Core.Billing;
using VetPos.Core.Domain;
using VetPos.Core.Infrastructure;
using VetPos.Core.Pricing;
using VetPos.Core.Settings;
using VetPos.Data;
using VetPos.Data.Services;

namespace VetPos.App.Infrastructure;

internal static class StartupPreview
{
    public static AppPaths CreatePaths(string imagePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(imagePath))!;
        return new AppPaths(Path.Combine(directory, $"data-{Guid.NewGuid():N}"));
    }

    public static async Task CaptureScreensAsync(Window window, RootViewModel root, IServiceProvider services, string imagePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(imagePath))!;
        async Task CaptureAsync(string path)
        {
            await Task.Delay(600);
            await window.Dispatcher.InvokeAsync(() => Save(window, path), DispatcherPriority.ApplicationIdle);
        }

        if (root.Screen is not SetupViewModel) throw new InvalidOperationException("Preview did not reach first-run setup.");
        await CaptureAsync(imagePath);

        var auth = services.GetRequiredService<AuthService>();
        const string username = "preview-owner";
        const string pin = "1234";
        var owner = await auth.CreateFirstOwnerAsync(username, "Demo owner", pin);
        var actor = owner.ToActor();
        await services.GetRequiredService<SettingsService>().SaveAsync(ShopSettings.Key, new ShopSettings { Name = "VetPOS Demo Shop" }, actor);
        await services.GetRequiredService<DatabaseInitializer>().AddSampleDataIfEmptyAsync();
        const string productName = "Pedigree Adult Chicken & Vegetables 3 kg";
        var products = await services.GetRequiredService<CatalogService>().SearchAsync(productName, limit: 1);
        var product = products.Single();
        var line = new BillLine(product.Id, product.Name, product.Barcode, 1, product.SellingPrice, product.SellingPrice, null, null);
        var cart = new BillCart(Guid.NewGuid(), [line], null);
        await services.GetRequiredService<SaleService>().CheckoutAsync(new CheckoutRequest(cart,
            [new PaymentInput(PaymentMethod.Cash, cart.Totals().Total)]), actor);

        await root.ShowLoginAsync();
        if (root.Screen is not LoginViewModel) throw new InvalidOperationException("Preview did not reach sign-in.");
        await CaptureAsync(Path.Combine(directory, "login.png"));

        await root.OpenShellAsync(await auth.LoginAsync(username, pin));
        if (root.Screen is not ShellViewModel shell) throw new InvalidOperationException("Preview did not reach the till.");
        foreach (var item in shell.Items)
        {
            shell.Selected = item;
            if (!await shell.PageReady || shell.CurrentPage is PageViewModel { LoadError: not null })
                throw new InvalidOperationException($"Preview could not open {item.Title}.");

            var file = item.Title.ToLowerInvariant().Replace(' ', '-');
            await CaptureAsync(Path.Combine(directory, $"{file}.png"));
        }

        shell.Go<ReportsViewModel>();
        await shell.PageReady;
        var reports = shell.Page<ReportsViewModel>();
        reports.ProductSearch = product.Name;
        await reports.PickFirstProductCommand.ExecuteAsync(null);
        if (reports.Section != ReportSection.Today || reports.TodayProduct?.Id != product.Id
            || reports.Summary?.Sales != product.SellingPrice || reports.Chart.All(bar => bar.Value <= 0))
            throw new InvalidOperationException("Preview could not find the demo sale in Reports → Today.");
        await CaptureAsync(Path.Combine(directory, "today-product.png"));
    }

    private static void Save(Window window, string imagePath)
    {
        if (window.Content is not FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } content)
            throw new InvalidOperationException("The VetPOS window did not render.");

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),
            (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(imagePath);
        png.Save(file);
    }
}
