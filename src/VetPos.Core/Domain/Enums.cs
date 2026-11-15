namespace VetPos.Core.Domain;

public enum UserRole
{
    Cashier = 0,
    Owner = 1,
}

/// <summary>How a product is counted. Quantities are always whole numbers of this unit.</summary>
public enum ProductUnit
{
    Piece = 0,
    Pack = 1,
    Bag = 2,
    Kg = 3,
    Bottle = 4,
    Box = 5,
    Tin = 6,
    Sachet = 7,
    Tube = 8,
    Strip = 9,
}

/// <summary>
/// What billing does when the product's stock is on hand at more than one selling price.
/// Chosen on stock entry when a delivery comes in at a new price.
/// </summary>
public enum PriceChoice
{
    /// <summary>All stock sells at one price.</summary>
    OnePrice = 0,

    /// <summary>Sell the old-price stock first (FEFO), then the new price. No question.</summary>
    OldFirst = 1,

    /// <summary>The cashier picks the price each time the item is added.</summary>
    AskAtBilling = 2,
}

public enum DiscountType
{
    /// <summary>Rupees off each unit (5 × Rs 100 off = Rs 500 off the line).</summary>
    PerUnit = 0,

    /// <summary>Rupees off the whole line or the whole bill.</summary>
    Amount = 1,

    /// <summary>Percent off.</summary>
    Percent = 2,
}

public enum SaleStatus
{
    Completed = 0,
    Voided = 1,
}

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    BankTransfer = 2,
}

public enum StockReason
{
    Opening = 0,
    Receipt = 1,
    Sale = 2,
    SaleVoid = 3,
    Adjustment = 4,
}

public enum BackupKind
{
    Auto = 0,
    Manual = 1,
    PreRestore = 2,
    PreUpdate = 3,
}
