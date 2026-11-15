using System.Text.Json;
using System.Text.Json.Serialization;
using VetPos.Core.Pricing;

namespace VetPos.Core.Billing;

/// <summary>
/// One line on the till. <see cref="PriceGroup"/> is the batch selling price chosen in the
/// "which price?" picker (only batches at that price are sold); null = FEFO across all.
/// </summary>
public sealed record BillLine(
    int ProductId,
    string Name,
    string? Barcode,
    int Qty,
    decimal ListPrice,
    decimal UnitPrice,
    Discount? Discount,
    decimal? PriceGroup)
{
    public bool PriceChanged => UnitPrice != ListPrice;
}

/// <summary>The bill on the till: saved as a draft after every change, and for held bills.</summary>
public sealed record BillCart(Guid ClientId, IReadOnlyList<BillLine> Lines, Discount? BillDiscount)
{
    public static BillCart New() => new(Guid.NewGuid(), [], null);

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static BillCart? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<BillCart>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public CartTotals Totals() => CartCalculator.Calculate(
        Lines.Select((l, i) => new CartLineInput(i.ToString(System.Globalization.CultureInfo.InvariantCulture), l.UnitPrice, l.Qty, l.Discount)).ToList(),
        BillDiscount);
}
