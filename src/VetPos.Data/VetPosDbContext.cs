using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;
using VetPos.Data.Configuration;

namespace VetPos.Data;

public class VetPosDbContext(DbContextOptions<VetPosDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<StockBatch> StockBatches => Set<StockBatch>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<SaleLineBatch> SaleLineBatches => Set<SaleLineBatch>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<HeldBill> HeldBills => Set<HeldBill>();
    public DbSet<DraftBill> DraftBills => Set<DraftBill>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<DocumentCounter> DocumentCounters => Set<DocumentCounter>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();
    public DbSet<AiLogEntry> AiLog => Set<AiLogEntry>();
    public DbSet<BackupRecord> Backups => Set<BackupRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HaveConversion<MoneyConverter>();
        configurationBuilder.Properties<string>().HaveMaxLength(200);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VetPosDbContext).Assembly);

        // History is never deleted through relationships: bills are voided, products deactivated.
        foreach (var fk in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
