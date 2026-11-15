using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VetPos.Data.Services;

namespace VetPos.App.Infrastructure;

/// <summary>Makes today's automatic backup soon after start, then checks every hour (a shop PC may run for days).</summary>
public sealed class BackupScheduler(BackupService backups, ILogger<BackupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await backups.EnsureDailyAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Automatic backup failed");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
