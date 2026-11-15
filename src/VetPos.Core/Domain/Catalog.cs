namespace VetPos.Core.Domain;

public class Category : Entity
{
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

public class Product : Entity
{
    /// <summary>Full name including the pack size, e.g. "Royal Canin Kitten 2 kg".</summary>
    public string Name { get; set; } = "";

    /// <summary>The main barcode; more can be added in <see cref="Barcodes"/>.</summary>
    public string? Barcode { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public ProductUnit Unit { get; set; }

    /// <summary>Cost of the newest delivery, for display. Profit uses each batch's own cost.</summary>
    public decimal CostPrice { get; set; }

    /// <summary>Price for new stock and for items without batches.</summary>
    public decimal SellingPrice { get; set; }

    /// <summary>Warn when stock falls to this level or below; 0 = no warning.</summary>
    public int ReorderLevel { get; set; }

    /// <summary>Medicine and food have an expiry date; collars and bowls do not.</summary>
    public bool TracksExpiry { get; set; }

    public PriceChoice PriceChoice { get; set; }

    // ── Medicine (vet medicines, vaccines, dewormers …) ──
    public bool IsMedicine { get; set; }

    /// <summary>Active ingredient, e.g. "Ivermectin". Searchable at billing.</summary>
    public string? GenericName { get; set; }

    /// <summary>e.g. "1% w/v", "250 mg".</summary>
    public string? Strength { get; set; }

    /// <summary>Tablet, injection, oral liquid, spot-on, powder …</summary>
    public string? DosageForm { get; set; }

    /// <summary>Which animals it is for, e.g. "Dogs, cats" or "Cattle, goats".</summary>
    public string? Species { get; set; }

    /// <summary>
    /// Units on hand across all batches. Kept in step with <see cref="StockBatch.QtyOnHand"/> in the
    /// same transaction, so the product list never has to add up batches.
    /// </summary>
    public int StockQty { get; set; }

    public bool IsActive { get; set; } = true;
    public List<ProductBarcode> Barcodes { get; set; } = [];
    public List<StockBatch> Batches { get; set; } = [];
}

/// <summary>Another barcode printed on the same item (new pack design, maker's code …).</summary>
public class ProductBarcode : Entity
{
    public string Code { get; set; } = "";
    public int ProductId { get; set; }
}
