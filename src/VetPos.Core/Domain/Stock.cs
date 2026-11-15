namespace VetPos.Core.Domain;

/// <summary>Goods received from a supplier (one supplier invoice).</summary>
public class GoodsReceipt : Entity
{
    /// <summary>Our own number, e.g. "GRN-00012".</summary>
    public string Number { get; set; } = "";

    /// <summary>Optional: stock can also be added without a supplier (opening stock, a gift, own repack).</summary>
    public int? SupplierId { get; set; }

    public Supplier? Supplier { get; set; }
    public string? SupplierInvoiceNo { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public decimal Total { get; set; }
    public string? Note { get; set; }
    public List<GoodsReceiptLine> Lines { get; set; } = [];
}

public class GoodsReceiptLine : Entity
{
    public int GoodsReceiptId { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Qty { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public string? BatchNo { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal LineTotal { get; set; }
}

/// <summary>
/// One lot of a product with its own expiry, cost and selling price. Billing sells from the batch
/// that expires first (FEFO) and never from an expired one.
/// </summary>
public class StockBatch : Entity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>Null for opening stock entered without a delivery.</summary>
    public int? GoodsReceiptLineId { get; set; }

    public string? BatchNo { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public int QtyReceived { get; set; }
    public int QtyOnHand { get; set; }
}

/// <summary>Every change to stock, ever. Rows are never changed or deleted.</summary>
public class StockMovement : Entity
{
    public int ProductId { get; set; }
    public int? BatchId { get; set; }

    /// <summary>Signed: + in, − out.</summary>
    public int Change { get; set; }

    /// <summary>Product stock after this change.</summary>
    public int QtyAfter { get; set; }

    public StockReason Reason { get; set; }

    /// <summary>The bill or delivery number this came from.</summary>
    public string? Reference { get; set; }

    public string? Note { get; set; }
}
