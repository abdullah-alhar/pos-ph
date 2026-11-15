using CommunityToolkit.Mvvm.ComponentModel;
using VetPos.Core.Billing;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;

namespace VetPos.App.Features.Billing;

/// <summary>One line on the till, as the screen shows it.</summary>
public sealed partial class CartLine : ObservableObject
{
    public required int ProductId { get; init; }
    public required string Name { get; init; }
    public string? Barcode { get; init; }
    public required decimal ListPrice { get; init; }

    /// <summary>Batch price group (only batches at this price are sold); null = FEFO across all.</summary>
    public decimal? PriceGroup { get; init; }

    /// <summary>Amber note: "Expires 03 Dec" when the batch that sells first expires within 30 days.</summary>
    public string? ExpiryNote { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Gross), nameof(DiscountAmount), nameof(Total), nameof(DiscountText))]
    private int _qty = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Gross), nameof(DiscountAmount), nameof(Total), nameof(PriceChanged), nameof(PriceChangeText), nameof(DiscountText))]
    private decimal _unitPrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscountAmount), nameof(Total), nameof(DiscountText), nameof(HasDiscount))]
    private Discount? _discount;

    [ObservableProperty] private int _number;
    [ObservableProperty] private bool _flash;

    public decimal Gross => Money.Round(UnitPrice * Qty);
    public decimal DiscountAmount => CartCalculator.LineDiscountAmount(UnitPrice, Qty, Discount);
    public decimal Total => Gross - DiscountAmount;
    public bool PriceChanged => UnitPrice != ListPrice;
    public bool HasDiscount => DiscountAmount > 0;
    public string PriceChangeText => $"Price changed {Money.Format(ListPrice)} → {Money.Format(UnitPrice)}";
    public string? DiscountText => Discount is null ? null : Discount.Type == DiscountType.PerUnit
        ? $"{Money.Format(Discount.Value, true)} off each (−{Money.Format(DiscountAmount)})"
        : $"{Discount.Describe()} off (−{Money.Format(DiscountAmount)})";

    public BillLine ToBillLine() => new(ProductId, Name, Barcode, Qty, ListPrice, UnitPrice, Discount, PriceGroup);

    public static CartLine From(BillLine l) => new()
    {
        ProductId = l.ProductId, Name = l.Name, Barcode = l.Barcode, ListPrice = l.ListPrice, PriceGroup = l.PriceGroup,
        Qty = l.Qty, UnitPrice = l.UnitPrice, Discount = l.Discount,
    };
}
