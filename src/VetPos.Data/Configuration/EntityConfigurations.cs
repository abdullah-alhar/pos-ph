using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetPos.Core.Domain;

namespace VetPos.Data.Configuration;

internal sealed class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.Username).HasMaxLength(40).UseCollation("NOCASE");
        b.HasIndex(x => x.Username).IsUnique();
        b.Property(x => x.FullName).HasMaxLength(80);
        b.Property(x => x.PinHash).HasMaxLength(300);
    }
}

internal sealed class SupplierConfig : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).UseCollation("NOCASE");
        b.HasIndex(x => x.Name);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Note).HasMaxLength(500);
    }
}

internal sealed class CategoryConfig : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.Property(x => x.Name).HasMaxLength(60).UseCollation("NOCASE");
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class ProductConfig : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).UseCollation("NOCASE");
        b.HasIndex(x => x.Name);
        b.Property(x => x.Barcode).HasMaxLength(40);
        b.HasIndex(x => x.Barcode).IsUnique().HasFilter("\"Barcode\" IS NOT NULL");
        b.HasIndex(x => x.CategoryId);
        b.Property(x => x.GenericName).HasMaxLength(120).UseCollation("NOCASE");
        b.HasIndex(x => x.GenericName);
        b.Property(x => x.Strength).HasMaxLength(60);
        b.Property(x => x.DosageForm).HasMaxLength(60);
        b.Property(x => x.Species).HasMaxLength(120);
        b.HasMany(x => x.Barcodes).WithOne().HasForeignKey(x => x.ProductId);
        b.HasMany(x => x.Batches).WithOne(x => x.Product).HasForeignKey(x => x.ProductId);
        b.ToTable(t => t.HasCheckConstraint("CK_Product_Prices", "\"CostPrice\" >= 0 AND \"SellingPrice\" >= 0"));
    }
}

internal sealed class ProductBarcodeConfig : IEntityTypeConfiguration<ProductBarcode>
{
    public void Configure(EntityTypeBuilder<ProductBarcode> b)
    {
        b.Property(x => x.Code).HasMaxLength(40);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class GoodsReceiptConfig : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> b)
    {
        b.Property(x => x.Number).HasMaxLength(20);
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.SupplierInvoiceNo).HasMaxLength(60);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => new { x.SupplierId, x.SupplierInvoiceNo });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.GoodsReceiptId);
    }
}

internal sealed class GoodsReceiptLineConfig : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> b)
    {
        b.Property(x => x.BatchNo).HasMaxLength(60);
        b.ToTable(t => t.HasCheckConstraint("CK_GrnLine_Qty", "\"Qty\" > 0"));
    }
}

internal sealed class StockBatchConfig : IEntityTypeConfiguration<StockBatch>
{
    public void Configure(EntityTypeBuilder<StockBatch> b)
    {
        b.Property(x => x.BatchNo).HasMaxLength(60);
        b.HasIndex(x => new { x.ProductId, x.ExpiryDate });
        b.HasOne<GoodsReceiptLine>().WithMany().HasForeignKey(x => x.GoodsReceiptLineId);
        b.ToTable(t => t.HasCheckConstraint("CK_Batch_Qty", "\"QtyOnHand\" >= 0 AND \"QtyOnHand\" <= \"QtyReceived\""));
    }
}

internal sealed class StockMovementConfig : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.Property(x => x.Reference).HasMaxLength(40);
        b.Property(x => x.Note).HasMaxLength(300);
        b.HasIndex(x => new { x.ProductId, x.CreatedAt });
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
        b.HasOne<StockBatch>().WithMany().HasForeignKey(x => x.BatchId);
    }
}

internal sealed class SaleConfig : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.HasIndex(x => x.ClientId).IsUnique();
        b.HasIndex(x => x.Number).IsUnique();
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => new { x.CashierId, x.CreatedAt });
        b.Property(x => x.Note).HasMaxLength(300);
        b.Property(x => x.VoidReason).HasMaxLength(300);
        b.HasOne(x => x.Cashier).WithMany().HasForeignKey(x => x.CashierId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.VoidedById);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SaleId);
        b.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.SaleId);
        b.ToTable(t => t.HasCheckConstraint("CK_Sale_Total", "\"Total\" >= 0"));
    }
}

internal sealed class SaleLineConfig : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> b)
    {
        b.Property(x => x.ProductName).HasMaxLength(120);
        b.Property(x => x.Barcode).HasMaxLength(40);
        b.HasIndex(x => x.ProductId);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
        b.HasMany(x => x.Batches).WithOne().HasForeignKey(x => x.SaleLineId);
        b.ToTable(t => t.HasCheckConstraint("CK_SaleLine", "\"Qty\" > 0 AND \"LineTotal\" >= 0 AND \"UnitPrice\" >= 0"));
    }
}

internal sealed class SaleLineBatchConfig : IEntityTypeConfiguration<SaleLineBatch>
{
    public void Configure(EntityTypeBuilder<SaleLineBatch> b)
    {
        b.HasIndex(x => x.BatchId);
        b.HasOne<StockBatch>().WithMany().HasForeignKey(x => x.BatchId);
    }
}

internal sealed class PaymentConfig : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.Property(x => x.Reference).HasMaxLength(60);
        b.ToTable(t => t.HasCheckConstraint("CK_Payment_Amount", "\"Amount\" > 0"));
    }
}

internal sealed class HeldBillConfig : IEntityTypeConfiguration<HeldBill>
{
    public void Configure(EntityTypeBuilder<HeldBill> b)
    {
        b.Property(x => x.Label).HasMaxLength(60);
        b.Property(x => x.CartJson).HasMaxLength(200_000);
    }
}

internal sealed class DraftBillConfig : IEntityTypeConfiguration<DraftBill>
{
    public void Configure(EntityTypeBuilder<DraftBill> b)
    {
        b.HasIndex(x => x.UserId).IsUnique();
        b.Property(x => x.CartJson).HasMaxLength(200_000);
    }
}

internal sealed class SettingConfig : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> b)
    {
        b.Property(x => x.Key).HasMaxLength(40);
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.Value).HasMaxLength(500_000);
    }
}

internal sealed class DocumentCounterConfig : IEntityTypeConfiguration<DocumentCounter>
{
    public void Configure(EntityTypeBuilder<DocumentCounter> b)
    {
        b.Property(x => x.Key).HasMaxLength(20);
        b.HasIndex(x => x.Key).IsUnique();
    }
}

internal sealed class AuditEntryConfig : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("AuditLog");
        b.Property(x => x.Action).HasMaxLength(40);
        b.Property(x => x.EntityType).HasMaxLength(40);
        b.Property(x => x.OldValue).HasMaxLength(4000);
        b.Property(x => x.NewValue).HasMaxLength(4000);
        b.Property(x => x.Summary).HasMaxLength(500);
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => new { x.Action, x.CreatedAt });
    }
}

internal sealed class AiLogEntryConfig : IEntityTypeConfiguration<AiLogEntry>
{
    public void Configure(EntityTypeBuilder<AiLogEntry> b)
    {
        b.ToTable("AiLog");
        b.Property(x => x.Question).HasMaxLength(4000);
        b.Property(x => x.ToolsJson).HasMaxLength(100_000);
        b.Property(x => x.Answer).HasMaxLength(20_000);
        b.Property(x => x.Provider).HasMaxLength(40);
        b.Property(x => x.Model).HasMaxLength(100);
        b.Property(x => x.Error).HasMaxLength(2000);
        b.HasIndex(x => x.CreatedAt);
    }
}

internal sealed class BackupRecordConfig : IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<BackupRecord> b)
    {
        b.ToTable("Backups");
        b.Property(x => x.FileName).HasMaxLength(200);
        b.Property(x => x.Error).HasMaxLength(2000);
    }
}
