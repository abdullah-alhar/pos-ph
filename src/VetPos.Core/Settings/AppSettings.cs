namespace VetPos.Core.Settings;

/// <summary>Settings sections. Each is stored as one JSON row; new fields get these defaults.</summary>
public sealed record ShopSettings
{
    public const string Key = "shop";
    public string Name { get; init; } = "My Vet Shop";
    public string Address { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Email { get; init; } = "";
    public string Currency { get; init; } = "LKR";
}

public sealed record BillingSettings
{
    public const string Key = "billing";

    /// <summary>Owner choice: sell more than the stock shows (logged), or block it (default).</summary>
    public bool AllowSellAboveStock { get; init; }

    /// <summary>Print the receipt after every sale.</summary>
    public bool AutoPrint { get; init; } = true;

    /// <summary>Ask before removing a line from the bill.</summary>
    public bool ConfirmRemoveLine { get; init; } = true;

    /// <summary>Lock the screen after this many idle minutes; 0 = never.</summary>
    public int AutoLockMinutes { get; init; }
}

public enum PrinterMode
{
    None,
    Windows,
    Network,
    File,
}

public sealed record PrinterSettings
{
    public const string Key = "printer";
    public PrinterMode Mode { get; init; } = PrinterMode.None;
    public string WindowsPrinterName { get; init; } = "";
    public string NetworkHost { get; init; } = "";
    public int NetworkPort { get; init; } = 9100;

    /// <summary>48 for 80 mm paper (Font A), 32 for 58 mm.</summary>
    public int CharsPerLine { get; init; } = 48;

    public bool AutoCut { get; init; } = true;
    public int FeedLines { get; init; } = 4;
    public bool OpenDrawerOnCash { get; init; }
    public int Copies { get; init; } = 1;
}

public sealed record BackupSettings
{
    public const string Key = "backup";
    public bool DailyBackup { get; init; } = true;

    /// <summary>How many automatic / manual backups to keep.</summary>
    public int Keep { get; init; } = 30;

    /// <summary>Optional second copy (USB drive or another disk); empty = none.</summary>
    public string ExtraFolder { get; init; } = "";
}

public sealed record AiSettings
{
    public const string Key = "ai";
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "groq";
    public string Model { get; init; } = "";
    public string CustomUrl { get; init; } = "";

    /// <summary>The API key, encrypted with Windows DPAPI (base64). Never stored as plain text.</summary>
    public string EncryptedKey { get; init; } = "";

    /// <summary>"gsk_…9f3a": lets the owner recognise the saved key without showing it.</summary>
    public string KeyHint { get; init; } = "";
}

public sealed record AppearanceSettings
{
    public const string Key = "appearance";

    /// <summary>System, Light or Dark.</summary>
    public string Theme { get; init; } = "System";

    public bool SidebarCollapsed { get; init; }

    /// <summary>Colour theme: Teal, Blue, Green, Purple, Rose, Amber or Graphite.</summary>
    public string Accent { get; init; } = "Teal";

    /// <summary>Sidebar with full names (true) or the slim icon bar (false, the default).</summary>
    public bool SidebarExpanded { get; init; }
}

public sealed record UpdateSettings
{
    public const string Key = "update";

    /// <summary>
    /// Where published versions are read from. Stored encoded, not as plain text, so the address is
    /// not readable in the shipped program. Shops never see or set this.
    /// </summary>
    public static string DefaultSource { get; } =
        System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("aHR0cHM6Ly9naXRodWIuY29tL2FiZHVsbGFoLWFsaGFyL3Bvcy1waA=="));

    /// <summary>A folder or web address set by the installer; empty = the built-in source above.</summary>
    public string Source { get; init; } = "";

    public bool CheckOnStart { get; init; } = true;
}
