using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VetPos.Data;

/// <summary>Used only by <c>dotnet ef migrations add</c>.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<VetPosDbContext>
{
    public VetPosDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<VetPosDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
