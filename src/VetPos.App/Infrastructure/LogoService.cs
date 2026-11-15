using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VetPos.Core.Infrastructure;
using VetPos.Printing;

namespace VetPos.App.Infrastructure;

/// <summary>The shop logo: kept in the data folder, shown in the app, and turned into dots for the printer.</summary>
public sealed class LogoService(AppPaths paths)
{
    private LogoBitmap? _printable;

    public string LogoFile => Path.Combine(paths.AssetsDir, "logo.png");
    public string IconFile => Path.Combine(paths.AssetsDir, "logo.ico");
    public bool HasLogo => File.Exists(LogoFile);

    public event EventHandler? Changed;

    /// <summary>
    /// The logo as an image for the screen. Read into memory first, so the file is never locked
    /// and a new logo always shows (no stale image from WPF's cache).
    /// </summary>
    public ImageSource? Load()
    {
        if (!HasLogo) return null;
        try
        {
            // Decoded from bytes in memory: the file is never locked and a new logo always shows.
            // (No IgnoreImageCache here: with a stream source it makes WPF itself throw.)
            using var stream = new MemoryStream(File.ReadAllBytes(LogoFile));
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = stream;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex)
        {
            // A logo problem must never stop VetPOS (it shows on the sign-in screen and the menu).
            Serilog.Log.Warning(ex, "The logo could not be shown");
            return null;
        }
    }

    /// <summary>
    /// Saves a picture as the shop logo. Only the pixels are kept (as 32-bit PNG, at most 600 px):
    /// photo metadata, colour profiles and print colours (CMYK) are dropped, because the PNG writer
    /// refuses some of them. Written to a temporary file first, so a failure never leaves half a logo.
    /// </summary>
    public void SetLogo(string sourceFile)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(sourceFile);
            if (!info.Exists) throw new VetPos.Core.Rules.PosException("That picture is no longer there. Choose it again.");
            if (info.Length > 20 * 1024 * 1024) throw new VetPos.Core.Rules.PosException("That picture is too big (over 20 MB). Use a smaller PNG or JPG.");
            bytes = File.ReadAllBytes(sourceFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VetPos.Core.Rules.PosException("That picture could not be read. Close any program that has it open, or copy it to the Desktop first.");
        }

        BitmapSource pixels;
        try
        {
            var frame = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            var scale = Math.Min(1.0, 600.0 / Math.Max(frame.PixelWidth, frame.PixelHeight));
            BitmapSource sized = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
            // Bgra32 keeps transparency and turns CMYK / grey / 16-bit pictures into normal colours.
            var converted = new FormatConvertedBitmap(sized, PixelFormats.Bgra32, null, 0);
            var copy = new WriteableBitmap(converted); // plain pixels: no metadata comes along
            copy.Freeze();
            pixels = copy;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException
                                   or System.Runtime.InteropServices.COMException or OverflowException)
        {
            Serilog.Log.Warning(ex, "Logo picture could not be decoded: {File}", Path.GetFileName(sourceFile));
            throw new VetPos.Core.Rules.PosException("That picture could not be opened. Save it as PNG or JPG (for example with Paint) and try again.");
        }

        var temp = LogoFile + ".new";
        try
        {
            Directory.CreateDirectory(paths.AssetsDir);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(pixels));
            using (var fs = File.Create(temp)) encoder.Save(fs);
            File.Move(temp, LogoFile, overwrite: true);
            ShopIcon.WriteIco(pixels, IconFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException
                                   or System.Runtime.InteropServices.COMException)
        {
            Serilog.Log.Warning(ex, "Logo could not be saved");
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw new VetPos.Core.Rules.PosException("The logo could not be saved in VetPOS's data folder. Restart VetPOS and try again.");
        }
        _printable = null;
        Changed?.Invoke(this, EventArgs.Empty);
        RefreshShortcuts();
    }

    /// <summary>Desktop / Start menu shortcuts show the shop's logo (or the VetPOS icon when there is none).</summary>
    public void RefreshShortcuts()
    {
        var ico = HasLogo && File.Exists(IconFile) ? IconFile : null;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ShopIcon.UpdateShortcuts(ico), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Makes logo.ico for a logo saved by an older version (before icons existed).</summary>
    public void EnsureIcon()
    {
        if (!HasLogo || File.Exists(IconFile)) return;
        try
        {
            if (Load() is BitmapSource src) ShopIcon.WriteIco(src, IconFile);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not make the logo icon");
        }
    }

    public void RemoveLogo()
    {
        try
        {
            if (HasLogo) File.Delete(LogoFile);
            if (File.Exists(IconFile)) File.Delete(IconFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VetPos.Core.Rules.PosException("The logo could not be removed right now. Try again in a moment.");
        }
        _printable = null;
        Changed?.Invoke(this, EventArgs.Empty);
        RefreshShortcuts();
    }

    /// <summary>The logo as black/white dots, at most 384 dots wide (fits 80 mm paper).</summary>
    public LogoBitmap? Printable()
    {
        if (_printable is not null || !HasLogo) return _printable;
        if (Load() is not BitmapSource src) return null; // damaged logo file: print without it
        var scale = Math.Min(1.0, 384.0 / src.PixelWidth);
        BitmapSource scaled = new TransformedBitmap(src, new ScaleTransform(scale, scale));
        var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = Math.Min(bgra.PixelHeight, 300);
        var pixels = new byte[w * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, w * 4, 0);
        var bpr = (w + 7) / 8;
        var data = new byte[bpr * h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 4;
            var a = pixels[i + 3] / 255.0;
            var lum = (0.114 * pixels[i] + 0.587 * pixels[i + 1] + 0.299 * pixels[i + 2]) * a + 255 * (1 - a);
            if (lum < 140) data[y * bpr + x / 8] |= (byte)(0x80 >> (x % 8));
        }
        return _printable = new LogoBitmap(w, h, data);
    }
}
