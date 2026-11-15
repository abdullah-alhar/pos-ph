using Serilog;
using Serilog.Events;
using VetPos.Core.Infrastructure;

namespace VetPos.App.Infrastructure;

public static class Logging
{
    /// <summary>Rolling daily log files in %LocalAppData%\VetPOS\logs, kept for 30 days.</summary>
    public static ILogger Create(AppPaths paths) => new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.File(
            System.IO.Path.Combine(paths.LogsDir, "vetpos-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            fileSizeLimitBytes: 20_000_000,
            rollOnFileSizeLimit: true,
            flushToDiskInterval: TimeSpan.FromSeconds(1),
            formatProvider: System.Globalization.CultureInfo.InvariantCulture,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
        .CreateLogger();
}
