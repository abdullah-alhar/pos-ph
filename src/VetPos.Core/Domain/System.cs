namespace VetPos.Core.Domain;

/// <summary>One section of settings stored as JSON (shop, receipt, printer, backup, ai …).</summary>
public class Setting : Entity
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "{}";
}

/// <summary>Running numbers for bills and deliveries.</summary>
public class DocumentCounter : Entity
{
    public string Key { get; set; } = "";
    public int Value { get; set; }
}

/// <summary>Price changes, discounts, voids, stock edits … Rows are never changed or deleted.</summary>
public class AuditEntry : Entity
{
    /// <summary>Short code such as PRICE_CHANGE, LINE_DISCOUNT, SALE_VOID.</summary>
    public string Action { get; set; } = "";

    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    /// <summary>One sentence for the report: "Bill 1042: price of Frontline M 450.00 → 400.00".</summary>
    public string Summary { get; set; } = "";

    public int? ApprovedById { get; set; }
}

/// <summary>Every question asked to the AI assistant, the tools it used and its answer.</summary>
public class AiLogEntry : Entity
{
    public string Question { get; set; } = "";
    public string ToolsJson { get; set; } = "[]";
    public string? Answer { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int DurationMs { get; set; }
    public string? Error { get; set; }
}

public class BackupRecord : Entity
{
    public string FileName { get; set; } = "";
    public BackupKind Kind { get; set; }
    public long SizeBytes { get; set; }
    public bool Verified { get; set; }
    public string? Error { get; set; }
}
