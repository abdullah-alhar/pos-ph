using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using VetPos.Core.Domain;
using VetPos.Core.Infrastructure;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;
using VetPos.Core.Settings;
using VetPos.Data.Services;
using VetPos.Printing;

namespace VetPos.App.Infrastructure;

/// <summary>
/// Prints receipts on the 80 mm thermal printer: through the Windows spooler (RAW, works with the
/// USB printer's driver), straight to a network printer (port 9100), or to a text file for testing.
/// A printing problem never loses the bill — it is already saved and can be printed again.
/// </summary>
public sealed partial class PrintService(SettingsService settings, SaleService sales, LogoService logo, AppPaths paths)
{
    public async Task<ReceiptDesign> DesignAsync() => ReceiptDesign.FromJson(await settings.GetRawAsync(ReceiptDesign.SettingsKey));

    public async Task<ReceiptData> DataForAsync(SaleView sale, bool reprint)
    {
        var shop = await settings.GetAsync<ShopSettings>(ShopSettings.Key);
        return new ReceiptData(shop.Name, shop.Address, shop.Phone, shop.Email, sale.Number, sale.CreatedAtLocal, sale.Cashier,
            sale.Lines.Select(l => new ReceiptLine(l.Name, l.Barcode, l.Qty, l.ListPrice, l.UnitPrice,
                l.DiscountType is { } t ? new Discount(t, l.DiscountValue).Describe() : null, l.DiscountAmount, l.Gross - l.DiscountAmount)).ToList(),
            sale.Subtotal, sale.LineDiscountTotal, sale.BillDiscountType is { } bt ? new Discount(bt, sale.BillDiscountValue).Describe() : null,
            sale.BillDiscountAmount, sale.Total,
            sale.Payments.Select(p => new ReceiptPayment(p.Method, p.Amount, p.Tendered, p.Reference)).ToList(),
            sale.Change, sale.Note, reprint, sale.Status == SaleStatus.Voided);
    }

    public async Task<ReceiptDoc> RenderAsync(ReceiptDesign design, ReceiptData data)
    {
        var p = await settings.GetAsync<PrinterSettings>(PrinterSettings.Key);
        return ReceiptRenderer.Render(design, data, p.CharsPerLine, logo.HasLogo, p.FeedLines, p.AutoCut);
    }

    /// <summary>Prints a saved bill. Returns false when no printer is set up.</summary>
    public async Task<bool> PrintSaleAsync(int saleId, bool reprint)
    {
        var p = await settings.GetAsync<PrinterSettings>(PrinterSettings.Key);
        if (p.Mode == PrinterMode.None) return false;
        var sale = await sales.GetAsync(saleId) ?? throw new PosException("That bill no longer exists.");
        var doc = await RenderAsync(await DesignAsync(), await DataForAsync(sale, reprint));
        var drawer = !reprint && p.OpenDrawerOnCash && sale.Payments.Any(x => x.Method == PaymentMethod.Cash);
        var copies = reprint ? 1 : Math.Clamp(p.Copies, 1, 3);
        for (var i = 0; i < copies; i++)
            await SendAsync(p, EscPos.Encode(doc, new EscPosOptions(logo.Printable(), drawer && i == 0)), doc, $"Bill {sale.Number}");
        return true;
    }

    public async Task TestAsync(PrinterSettings p)
    {
        var doc = ReceiptRenderer.Render(await DesignAsync(), ReceiptData.Sample((await settings.GetAsync<ShopSettings>(ShopSettings.Key)).Name) with { IsReprint = true },
            p.CharsPerLine, logo.HasLogo, p.FeedLines, p.AutoCut);
        await SendAsync(p, EscPos.Encode(doc, new EscPosOptions(logo.Printable())), doc, "VetPOS test print");
    }

    public async Task OpenDrawerAsync()
    {
        var p = await settings.GetAsync<PrinterSettings>(PrinterSettings.Key);
        if (p.Mode is PrinterMode.Windows or PrinterMode.Network) await SendAsync(p, EscPos.DrawerKick(), null, "Open drawer");
    }

    private async Task SendAsync(PrinterSettings p, byte[] bytes, ReceiptDoc? doc, string jobName)
    {
        try
        {
            switch (p.Mode)
            {
                case PrinterMode.Windows:
                    if (string.IsNullOrWhiteSpace(p.WindowsPrinterName)) throw new PosException("Choose the receipt printer in Settings → Printer.");
                    await Task.Run(() => RawPrinter.Send(p.WindowsPrinterName, bytes, jobName));
                    break;
                case PrinterMode.Network:
                    using (var client = new TcpClient())
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await client.ConnectAsync(p.NetworkHost, p.NetworkPort, cts.Token);
                        await client.GetStream().WriteAsync(bytes, cts.Token);
                    }
                    break;
                case PrinterMode.File:
                    var dir = Path.Combine(paths.Root, "printed");
                    Directory.CreateDirectory(dir);
                    var file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{jobName.Replace(' ', '_')}.txt");
                    await File.WriteAllTextAsync(file, doc is null ? jobName : ReceiptRenderer.ToText(doc));
                    break;
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or Win32PrinterException)
        {
            throw new PosException("The printer did not answer. Check it is on, has paper and is connected. " + ex.Message);
        }
    }

    public static IReadOnlyList<string> InstalledPrinters()
    {
        try
        {
            return System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().Order().ToList();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return [];
        }
    }
}

public sealed class Win32PrinterException(string message) : Exception(message);

/// <summary>Sends raw ESC/POS bytes through the Windows print spooler.</summary>
internal static partial class RawPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? DocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int StartDocPrinter(IntPtr handle, int level, [In] DocInfo info);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, byte[] bytes, int count, out int written);

    public static void Send(string printer, byte[] data, string jobName)
    {
        if (!OpenPrinter(printer, out var h, IntPtr.Zero)) throw new Win32PrinterException($"Printer \"{printer}\" was not found.");
        try
        {
            if (StartDocPrinter(h, 1, new DocInfo { DocName = jobName, DataType = "RAW" }) == 0) throw new Win32PrinterException("The printer did not accept the job.");
            StartPagePrinter(h);
            var ok = WritePrinter(h, data, data.Length, out var written);
            EndPagePrinter(h);
            EndDocPrinter(h);
            if (!ok || written != data.Length) throw new Win32PrinterException("Could not send the receipt to the printer.");
        }
        finally
        {
            ClosePrinter(h);
        }
    }
}
