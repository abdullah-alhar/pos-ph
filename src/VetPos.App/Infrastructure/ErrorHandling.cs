using System.IO;
using System.Windows;
using System.Windows.Threading;
using Serilog;

namespace VetPos.App.Infrastructure;

/// <summary>
/// The till must never close because of a bug. Every unexpected error is written to the log with
/// a short reference number, and the cashier gets a calm message with that number. Bills are
/// saved in a transaction and the bill in progress is kept as a draft, so nothing is lost.
/// </summary>
public static class ErrorHandling
{
    private static int _showing;

    /// <summary>Set once the app is up: errors then show as a toast instead of a blocking box.</summary>
    public static DesignSystem.Controls.IToastService? Toasts { get; set; }

    /// <summary>
    /// Shows any error the right way: a cashier-fixable problem (PosException) in its own words, AI
    /// problems as a warning, known Windows / disk / printer / network problems in plain words, and
    /// anything else as a short message with a reference number (the details go to the log).
    /// </summary>
    public static void Show(Exception ex, string? reference = null)
    {
        var e = ex is AggregateException { InnerExceptions.Count: 1 } a ? a.InnerExceptions[0] : ex;
        if (e is VetPos.Core.Rules.PosException pos)
        {
            Toast(pos.Message, DesignSystem.Controls.ToastKind.Error);
            return;
        }
        if (e is VetPos.AI.AiUnavailableException ai)
        {
            Toast(ai.Message, DesignSystem.Controls.ToastKind.Warning);
            return;
        }
        reference ??= NewReference();
        Log.Error(e, "Error shown to the user {Reference}", reference);
        var text = Describe(e) is { } known
            ? $"{known} (ref {reference})"
            : $"That did not work, but your data is safe. Try again; if it repeats, tell support: {reference}.";
        if (Toasts is null) ShowFriendly(reference, Describe(e));
        else Toast(text, DesignSystem.Controls.ToastKind.Error);
    }

    /// <summary>For background timers (notices, idle lock, drafts): log, never interrupt the cashier.</summary>
    public static async Task Quietly(Func<Task> work, string what)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Background task failed: {What}", what);
        }
    }

    private static void Toast(string message, DesignSystem.Controls.ToastKind kind)
    {
        if (Toasts is null)
        {
            MessageBox.Show(message, "VetPOS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Toasts.Show(message, kind);
    }

    public static void Register(Application app)
    {
        app.DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTask;
    }

    /// <summary>
    /// Known problems in plain words with what to do, or null for a real bug. Used by every screen
    /// (Ui.Run) and by the last-chance handler.
    /// </summary>
    public static string? Describe(Exception ex)
    {
        var e = ex is AggregateException { InnerExceptions.Count: 1 } a ? a.InnerExceptions[0] : ex;
        if (IsBlockedByWindows(ex)) return BlockedMessage;
        switch (e)
        {
            case Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 5 or 6 }:
                return "The shop's data is busy for a moment (a backup or another VetPOS window). Try again in a few seconds.";
            case Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 13 }:
            case IOException io when (io.HResult & 0xFFFF) is 0x27 or 0x70:
                return "This computer's disk is full. Delete some files or empty the Recycle Bin, then try again. Nothing was lost.";
            case Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 8 }:
                return "VetPOS is not allowed to write its data (read-only). Restart the computer; if it stays, call support.";
            case Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException:
                return "Someone changed this at the same time. Close it, open it again and repeat your change.";
            case Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 } }:
                return "This would create a duplicate (for example a barcode or name that is already used). Change it and save again.";
            case UnauthorizedAccessException:
                return "Windows blocked VetPOS from opening that file or folder. Choose another folder (for example Documents) and try again.";
            case FileNotFoundException or DirectoryNotFoundException:
                return "That file or folder is no longer there. Check the USB drive or folder and try again.";
            case IOException when e.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase):
                return "That file is open in another program (for example Excel). Close it and try again.";
            case System.Net.Http.HttpRequestException or TaskCanceledException or TimeoutException:
                return "No answer from the internet. Check the connection and try again. Billing works without internet.";
            case System.Printing.PrintQueueException or System.Printing.PrintSystemException:
                return "The printer did not answer. Check that it is switched on, connected and has paper, then print again (F10).";
            case OutOfMemoryException or InsufficientExecutionStackException:
                return "The computer is low on memory. Close other programs, then try again.";
            default:
                return null;
        }
    }

    public const string BlockedMessage =
        "Windows blocked part of VetPOS (Smart App Control or the antivirus).\n\n" +
        "Open Windows Security → App & browser control → Protection history, choose the VetPOS item and allow it, " +
        "or ask the person who installed VetPOS for the signed version.";

    /// <summary>
    /// Smart App Control / Windows Defender Application Control / antivirus refused to load one of
    /// VetPOS's files (the database engine, PDF export, …).
    /// </summary>
    public static bool IsBlockedByWindows(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            // 0x800711C7 = blocked by code integrity policy (Smart App Control / WDAC),
            // 0x800700E1 = virus or unwanted software (Defender), 0x800704EC = blocked by group policy.
            if ((uint)e.HResult is 0x800711C7 or 0x800700E1 or 0x800704EC) return true;
            if (e is DllNotFoundException or BadImageFormatException && e.Message.Contains("e_sqlite3", StringComparison.OrdinalIgnoreCase)) return true;
            if (e.Message.Contains("Application Control policy", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("blocked this file", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Short id the cashier can read out on the phone: "E-7K2Q".</summary>
    public static string NewReference() =>
        "E-" + Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("+", "", StringComparison.Ordinal).Replace("/", "", StringComparison.Ordinal)[..4].ToUpperInvariant();

    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (e.Exception is VetPos.Core.Rules.PosException or VetPos.AI.AiUnavailableException)
        {
            Show(e.Exception);
            return;
        }
        var reference = NewReference();
        Log.Error(e.Exception, "Unhandled UI error {Reference}", reference);
        if (Toasts is not null) Show(e.Exception, reference);
        else ShowFriendly(reference, Describe(e.Exception));
    }

    private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        // A crash on a background thread cannot be stopped; make sure it is on disk.
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled background error (terminating: {Terminating})", e.IsTerminating);
        Log.CloseAndFlush();
    }

    private static void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved background task error {Reference}", NewReference());
        e.SetObserved();
    }

    private static void ShowFriendly(string reference, string? known)
    {
        // Never stack dialogs if errors repeat (e.g. inside a render loop).
        if (Interlocked.Exchange(ref _showing, 1) == 1) return;
        try
        {
            MessageBox.Show(
                (known ?? "Something did not work, but your bills are safe and you can keep working.") +
                "\n\nIf it happens again, tell support this number: " + reference,
                "VetPOS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Interlocked.Exchange(ref _showing, 0);
        }
    }
}
