using VetPos.Core.Domain;
using VetPos.Core.Rules;

namespace VetPos.Data.Services;

/// <summary>Audit actions. The audit log is append-only (protected by database triggers).</summary>
public static class AuditActions
{
    public const string PriceChange = "PRICE_CHANGE";
    public const string LineDiscount = "LINE_DISCOUNT";
    public const string BillDiscount = "BILL_DISCOUNT";
    public const string SaleCreate = "SALE_CREATE";
    public const string SaleVoid = "SALE_VOID";
    public const string SaleAboveStock = "SALE_ABOVE_STOCK";
    public const string ProductPrice = "PRODUCT_PRICE";
    public const string ProductSave = "PRODUCT_SAVE";
    public const string ProductRemove = "PRODUCT_REMOVE";
    public const string BatchPrice = "BATCH_PRICE";
    public const string StockReceive = "STOCK_RECEIVE";
    public const string StockAdjust = "STOCK_ADJUST";
    public const string UserSave = "USER_SAVE";
    public const string Login = "LOGIN";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string SettingsChange = "SETTINGS_CHANGE";
    public const string Backup = "BACKUP";
    public const string Restore = "RESTORE";
    public const string HoldBill = "BILL_HOLD";
}

internal static class Audit
{
    public static void Add(VetPosDbContext db, Actor? actor, string action, string summary,
        string? entityType = null, int? entityId = null, string? oldValue = null, string? newValue = null, int? approvedById = null) =>
        db.AuditLog.Add(new AuditEntry
        {
            Action = action,
            Summary = Trim(summary, 500)!,
            EntityType = entityType,
            EntityId = entityId,
            OldValue = Trim(oldValue, 4000),
            NewValue = Trim(newValue, 4000),
            ApprovedById = approvedById,
            CreatedById = actor?.Id,
        });

    private static string? Trim(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
