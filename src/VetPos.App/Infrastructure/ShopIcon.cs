using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VetPos.App.Infrastructure;

/// <summary>
/// The shop's logo as a Windows icon: written to <c>assets\logo.ico</c> and put on the VetPOS
/// shortcuts (Desktop, Start menu), so the shop sees its own logo where it opens VetPOS.
/// Removing the logo puts the VetPOS icon back. Problems are only logged: icons are never worth
/// stopping the till for.
/// </summary>
public static class ShopIcon
{
    private static readonly int[] Sizes = [256, 64, 48, 32, 16];

    /// <summary>Square PNG pictures in one .ico file (Windows Vista and later read PNG icons).</summary>
    public static void WriteIco(BitmapSource logo, string path)
    {
        var images = Sizes.Select(size => (size, png: RenderPng(logo, size))).ToList();
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0);              // reserved
        w.Write((short)1);              // type: icon
        w.Write((short)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);           // palette
            w.Write((byte)0);           // reserved
            w.Write((short)1);          // planes
            w.Write((short)32);         // bits per pixel
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images) w.Write(png);
    }

    /// <summary>The logo centred on a square, transparent canvas (keeps its shape).</summary>
    private static byte[] RenderPng(BitmapSource logo, int size)
    {
        var scale = Math.Min((double)size / logo.PixelWidth, (double)size / logo.PixelHeight);
        var w = logo.PixelWidth * scale;
        var h = logo.PixelHeight * scale;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            dc.DrawImage(logo, new Rect((size - w) / 2, (size - h) / 2, w, h));
        }
        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Points every VetPOS shortcut at <paramref name="icoPath"/> (null = the program's own icon).</summary>
    public static void UpdateShortcuts(string? icoPath)
    {
        var exe = Environment.ProcessPath;
        if (exe is null || !OperatingSystem.IsWindows()) return;
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;
        dynamic? shell = null;
        var changed = false;
        try
        {
            shell = Activator.CreateInstance(shellType);
            foreach (var folder in folders.Where(Directory.Exists).Distinct())
            foreach (var link in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories))
            {
                try
                {
                    dynamic sc = shell!.CreateShortcut(link);
                    string target = sc.TargetPath ?? "";
                    // Velopack shortcuts point at VetPOS.exe in the install folder (current\ or the stub).
                    if (!target.EndsWith("VetPOS.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    var icon = (icoPath ?? target) + ",0";
                    if (string.Equals((string)(sc.IconLocation ?? ""), icon, StringComparison.OrdinalIgnoreCase)) continue;
                    sc.IconLocation = icon;
                    sc.Save();
                    changed = true;
                }
                catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    Serilog.Log.Debug(ex, "Shortcut skipped: {Link}", link);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException)
        {
            Serilog.Log.Warning(ex, "Could not update the VetPOS shortcuts");
        }
        finally
        {
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
        if (changed) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED: refresh icons
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
