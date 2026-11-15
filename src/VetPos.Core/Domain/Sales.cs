namespace VetPos.Core.Domain;

public class Sale : Entity
{
    /// <summary>
    /// Made by the billing screen when the bill starts. Saving the same bill twice (double key
    /// press, retry after an error) returns the first sale instead of making a second one.
    /// </summary>
    public Guid ClientId { get; set; }

    /// <summary>Running bill number shown to people: 1042.</summary>
    public int Number { get; set; }

    public SaleStatus Status { get; set; }
    public int CashierId { get; set; }
    public User? Cashier { get; set; }

    /// <summary>Σ qty × unit price, before any discount.</summary>
    public decimal Subtotal { get; set; }

    public decimal LineDiscountTotal { get; set; }
    public DiscountType? BillDiscountType { get; set; }

    /// <summary>What was typed: rupees, or percent (12.5 = 12.5 %).</summary>
    public decimal BillDiscountValue { get; set; }

    public decimal BillDiscountAmount { get; set; }
    public decimal Total { get; set; }

    /// <summary>Money kept for the bill (equals Total for a completed sale).</summary>
    public decimal Paid { get; set; }

    public decimal Change { get; set; }

    /// <summary>What the goods cost the shop, from the batches they came from.</summary>
    public decimal CostTotal { get; set; }

    public string? Note { get; set; }
    public DateTime? VoidedAt { get; set; }
    public int? VoidedById { get; set; }
    public string? VoidReason { get; set; }
    public List<SaleLine> Lines { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}

public class SaleLine : Entity
{
    public int SaleId { get; set; }
    public int LineNo { get; set; }
    public int ProductId { get; set; }

    /// <summary>Name at the time of sale (the product may be renamed later).</summary>
    public string ProductName { get; set; } = "";

    public string? Barcode { get; set; }
    public int Qty { get; set; }

    /// <summary>The batch / shelf price before any change on this bill.</summary>
    public decimal ListPrice { get; set; }

    /// <summary>The price charged; differs from ListPrice when staff changed it (F4).</summary>
    public decimal UnitPrice { get; set; }

    public DiscountType? DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>This line's part of the bill discount (for exact per-item profit).</summary>
    public decimal BillDiscountShare { get; set; }

    public decimal LineTotal { get; set; }
    public decimal CostTotal { get; set; }
    public List<SaleLineBatch> Batches { get; set; } = [];
}

/// <summary>Which batch the units of a bill line came from. Rows are never changed or deleted.</summary>
public class SaleLineBatch : Entity
{
    public int SaleLineId { get; set; }
    public int BatchId { get; set; }
    public int Qty { get; set; }
    public decimal UnitCost { get; set; }
}

/// <summary>Money received for a bill. Rows are never changed or deleted.</summary>
public class Payment : Entity
{
    public int SaleId { get; set; }
    public PaymentMethod Method { get; set; }

    /// <summary>Applied to the bill (cash after change).</summary>
    public decimal Amount { get; set; }

    /// <summary>Cash handed over before change; equals Amount for card and bank.</summary>
    public decimal Tendered { get; set; }

    public string? Reference { get; set; }
}

/// <summary>A bill put aside while the customer fetches something.</summary>
public class HeldBill : Entity
{
    public string? Label { get; set; }
    public string CartJson { get; set; } = "";
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
}

/// <summary>
/// The bill being made right now, saved after every change so a power cut loses nothing.
/// One row per signed-in user.
/// </summary>
public class DraftBill : Entity
{
    public int UserId { get; set; }
    public string CartJson { get; set; } = "";
}
