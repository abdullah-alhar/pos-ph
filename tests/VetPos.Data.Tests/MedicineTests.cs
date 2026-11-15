using VetPos.Core.Domain;
using VetPos.Data.Services;

namespace VetPos.Data.Tests;

public class MedicineTests
{
    [Fact]
    public async Task Medicine_details_are_saved_and_found_by_generic_name()
    {
        var (test, owner, _, _, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var catalog = test.Get<CatalogService>();
        var id = await catalog.SaveAsync(new ProductInput(null, "Ivomec 1% 50 ml", null, [], cat, ProductUnit.Bottle, 1200m, 1650m, 2, false, true,
            new MedicineInfo("Ivermectin", "1% w/v", "Injection", "Cattle")), owner);

        var d = await catalog.GetAsync(id);
        Assert.Equal("Ivermectin", d!.Medicine!.GenericName);
        Assert.Equal("Injection", d.Medicine.DosageForm);
        Assert.False(d.TracksExpiry); // the shop does not use expiry dates
        var row = Assert.Single(await catalog.SearchAsync("ivermectin"));
        Assert.True(row.IsMedicine);
    }

    [Fact]
    public async Task Generated_barcodes_are_valid_and_unused()
    {
        var (test, owner, _, _, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var catalog = test.Get<CatalogService>();
        var code = await catalog.NextInStoreBarcodeAsync();
        Assert.True(VetPos.Printing.Barcodes.IsEan13(code));
        await catalog.SaveAsync(new ProductInput(null, "Bowl", code, [], cat, ProductUnit.Piece, 1, 2, 0, false, true), owner);
        Assert.NotEqual(code, await catalog.NextInStoreBarcodeAsync());
    }
}
