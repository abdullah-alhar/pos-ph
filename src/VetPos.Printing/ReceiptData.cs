using VetPos.Core.Domain;
using VetPos.Core.Pricing;

namespace VetPos.Printing;

public sealed record ReceiptLine(string Name, string? Barcode, int Qty, decimal ListPrice, decimal UnitPrice, string? DiscountText, decimal DiscountAmount, decimal LineTotal)
{
    public bool PriceChanged => UnitPrice != ListPrice;
}

public sealed record ReceiptPayment(PaymentMethod Method, decimal Amount, decimal Tendered, string? Reference);

/// <summary>Everything a receipt can show, independent of the database (built from a saved bill).</summary>
public sealed record ReceiptData(
    string ShopName, string ShopAddress, string ShopPhone, string ShopEmail,
    int BillNumber, DateTime Date, string Cashier,
    IReadOnlyList<ReceiptLine> Lines, decimal Subtotal, decimal LineDiscountTotal, string? BillDiscountText, decimal BillDiscountAmount,
    decimal Total, IReadOnlyList<ReceiptPayment> Payments, decimal Change, string? Note, bool IsReprint, bool IsVoided)
{
    public decimal Saved => Lines.Sum(l => l.PriceChanged && l.UnitPrice < l.ListPrice ? (l.ListPrice - l.UnitPrice) * l.Qty : 0) + LineDiscountTotal + BillDiscountAmount;
    public int Units => Lines.Sum(l => l.Qty);

    /// <summary>A realistic bill for the editor preview when the shop has no sales yet.</summary>
    public static ReceiptData Sample(string shopName = "Happy Paws Vet Shop") => new(
        shopName, "No. 12, Galle Road, Colombo 03", "077 123 4567", "hello@happypaws.lk",
        1043, new DateTime(2026, 11, 13, 14, 5, 0), "Nimal",
        [
            new("Royal Canin Kitten 2 kg", "2000000000015", 1, 5450m, 5450m, null, 0, 5450m),
            new("Pedigree Adult Chicken & Veg 10 kg", "2000000000022", 1, 6200m, 6200m, null, 0, 6200m),
            new("Frontline Plus Dog M 1 pipette", "2000000000138", 2, 450m, 400m, "Rs 20.00 each", 40m, 760m),
            new("Whiskas Adult Ocean Fish 480 g", "2000000000053", 1, 1050m, 1050m, null, 0, 1050m),
        ],
        13500m, 40m, "Rs 100.00", 100m, 13360m,
        [new(PaymentMethod.Cash, 13360m, 14000m, null)], 640m, null, false, false);
}

public static class MoneyText
{
    public static string M(decimal v) => Money.Format(v);
}
