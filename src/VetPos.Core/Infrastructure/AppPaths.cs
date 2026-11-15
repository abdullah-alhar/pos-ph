namespace VetPos.Core.Infrastructure;

/// <summary>
/// Where the app keeps its files: <c>%LocalAppData%\VetPOS</c> (database, backups, logs).
/// The <c>VETPOS_DATA</c> environment variable points it somewhere else (tests, a second till).
/// </summary>
public sealed class AppPaths
{
    public const string DataEnvVar = "VETPOS_DATA";

    public AppPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }
    public string DatabaseFile => Path.Combine(Root, "vetpos.db");
    public string BackupsDir => Path.Combine(Root, "backups");
    public string LogsDir => Path.Combine(Root, "logs");
    public string AssetsDir => Path.Combine(Root, "assets");

    public static AppPaths Default()
    {
        var custom = Environment.GetEnvironmentVariable(DataEnvVar);
        return new AppPaths(!string.IsNullOrWhiteSpace(custom)
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VetPOS"));
    }

    /// <summary>Makes the folders if they are missing.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BackupsDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(AssetsDir);
    }
}
