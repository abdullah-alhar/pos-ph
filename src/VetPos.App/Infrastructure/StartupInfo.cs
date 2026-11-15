using VetPos.Data;

namespace VetPos.App.Infrastructure;

/// <summary>What happened when the app opened the database (shown on the Design page).</summary>
public sealed class StartupInfo
{
    public DatabaseStartResult? Database { get; set; }
}
