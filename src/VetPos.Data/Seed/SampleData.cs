using VetPos.Core.Domain;

namespace VetPos.Data.Seed;

/// <summary>
/// Products for trying the app: pet food, farm feed, medicine and accessories, with batches that
/// cover the cases billing must handle — two prices on hand, low stock and out of stock. Barcodes use the in-store range (200…), so they never
/// clash with real product barcodes.
/// </summary>
internal static class SampleData
{
    private sealed record B(string? BatchNo, int? ExpiresInDays, int Qty, decimal Cost, decimal Price);

    private sealed record P(
        string Name, string Category, ProductUnit Unit, int Reorder, bool Expiry, B[] Batches,
        PriceChoice Choice = PriceChoice.OnePrice);

    private static readonly string[] CategoryNames = ["Pet food", "Farm & livestock feed", "Animal medicine", "Accessories"];

    private static readonly P[] Products =
    [
        // Pet food
        new("Royal Canin Kitten 2 kg", "Pet food", ProductUnit.Bag, 3, true,
            [new("RC-K2-0425", 120, 4, 4600m, 5450m), new("RC-K2-0926", 300, 6, 4850m, 5750m)], PriceChoice.AskAtBilling),
        new("Pedigree Adult Chicken & Vegetables 10 kg", "Pet food", ProductUnit.Bag, 3, true,
            [new("PD-10-118", 90, 2, 5300m, 6200m), new("PD-10-207", 240, 8, 5550m, 6500m)], PriceChoice.OldFirst),
        new("Pedigree Adult Chicken & Vegetables 3 kg", "Pet food", ProductUnit.Bag, 4, true,
            [new("PD-3-311", 200, 12, 1850m, 2250m)]),
        new("Whiskas Adult Ocean Fish 1.2 kg", "Pet food", ProductUnit.Pack, 5, true,
            [new("WK-12-552", 180, 15, 1900m, 2350m)]),
        new("Whiskas Adult Ocean Fish 480 g", "Pet food", ProductUnit.Pack, 6, true,
            [new("WK-48-118", 25, 9, 820m, 1050m)]),
        new("Me-O Kitten Tuna 400 g", "Pet food", ProductUnit.Pack, 6, true,
            [new("MEO-40-77", 160, 20, 690m, 890m)]),
        new("Drools Puppy Starter 3 kg", "Pet food", ProductUnit.Bag, 3, true,
            [new("DR-PS3-19", 210, 7, 3150m, 3850m)]),

        // Farm & livestock feed
        new("Layer Mash 50 kg", "Farm & livestock feed", ProductUnit.Bag, 10, true,
            [new("LM-2611-A", 60, 40, 8900m, 9800m)]),
        new("Broiler Starter Crumble 50 kg", "Farm & livestock feed", ProductUnit.Bag, 10, true,
            [new("BS-2611-C", 55, 25, 10200m, 11250m)]),
        new("Dairy Cattle Feed 50 kg", "Farm & livestock feed", ProductUnit.Bag, 8, true,
            [new("DC-2610-B", 75, 18, 6800m, 7600m)]),
        new("Pig Grower Feed 50 kg", "Farm & livestock feed", ProductUnit.Bag, 5, true,
            [new("PG-2609-A", 40, 6, 7900m, 8750m)]),
        new("Fish Feed Floating Pellets 1 kg", "Farm & livestock feed", ProductUnit.Pack, 10, true,
            [new("FF-1-930", 150, 30, 480m, 650m)]),

        // Animal medicine
        new("Frontline Plus Dog M (10–20 kg) 1 pipette", "Animal medicine", ProductUnit.Piece, 6, true,
            [new("FL-M-2231", 400, 24, 1150m, 1450m)]),
        new("Bravecto Chew Dog 10–20 kg", "Animal medicine", ProductUnit.Piece, 5, true,
            [new("BV-1020-88", 365, 2, 7400m, 8900m)]),
        new("Drontal Plus Dog Tablet (strip of 2)", "Animal medicine", ProductUnit.Strip, 10, true,
            [new("DT-2-4410", 20, 14, 380m, 520m)]),
        new("Ivermectin 1% Injection 50 ml", "Animal medicine", ProductUnit.Bottle, 4, true,
            [new("IV-50-301", 280, 10, 1250m, 1650m)]),
        new("Oxytetracycline 20% LA Injection 100 ml", "Animal medicine", ProductUnit.Bottle, 4, true,
            [new("OT-100-552", -10, 3, 1580m, 2100m), new("OT-100-781", 330, 8, 1620m, 2100m)]),
        new("Calcium Borogluconate 450 ml", "Animal medicine", ProductUnit.Bottle, 5, true,
            [new("CB-450-22", 210, 12, 640m, 880m)]),
        new("Vitamin AD3E Oral Solution 1 L", "Animal medicine", ProductUnit.Bottle, 3, true,
            [new("AD3E-1L-09", 15, 5, 2350m, 2950m)]),
        new("Nobivac DHPPi Dog Vaccine 1 dose", "Animal medicine", ProductUnit.Piece, 5, true,
            [new("NB-DHP-221", 150, 10, 980m, 1450m)]),

        // Accessories (no expiry)
        new("Nylon Dog Collar Medium", "Accessories", ProductUnit.Piece, 5, false,
            [new(null, null, 18, 290m, 550m)]),
        new("Nylon Dog Leash 1.5 m", "Accessories", ProductUnit.Piece, 4, false, []),
        new("Stainless Steel Pet Bowl Medium", "Accessories", ProductUnit.Piece, 4, false,
            [new(null, null, 11, 420m, 750m)]),
        new("Bentonite Cat Litter 5 kg", "Accessories", ProductUnit.Bag, 4, false,
            [new(null, null, 9, 1350m, 1850m)]),
        new("Pet Grooming Brush", "Accessories", ProductUnit.Piece, 3, false,
            [new(null, null, 3, 650m, 1200m)]),
    ];

    /// <summary>Medicine details for the sample medicines: generic name, strength, form and animals.</summary>
    private static readonly Dictionary<string, (string Generic, string Strength, string Form, string Species)> Medicines = new()
    {
        ["Frontline Plus Dog M (10–20 kg) 1 pipette"] = ("Fipronil + (S)-methoprene", "134 mg / 120.6 mg", "Spot-on", "Dogs 10–20 kg"),
        ["Bravecto Chew Dog 10–20 kg"] = ("Fluralaner", "500 mg", "Chewable tablet", "Dogs 10–20 kg"),
        ["Drontal Plus Dog Tablet (strip of 2)"] = ("Praziquantel + Pyrantel + Febantel", "50/144/150 mg", "Tablet", "Dogs"),
        ["Ivermectin 1% Injection 50 ml"] = ("Ivermectin", "1% w/v", "Injection", "Cattle, goats, pigs"),
        ["Oxytetracycline 20% LA Injection 100 ml"] = ("Oxytetracycline", "200 mg/ml", "Injection", "Cattle, goats, sheep"),
        ["Calcium Borogluconate 450 ml"] = ("Calcium borogluconate", "20% w/v", "Injection", "Cattle"),
        ["Vitamin AD3E Oral Solution 1 L"] = ("Vitamins A, D3, E", "Multi-vitamin", "Oral liquid", "Poultry, cattle"),
        ["Nobivac DHPPi Dog Vaccine 1 dose"] = ("Distemper, hepatitis, parvovirus, parainfluenza vaccine", "1 dose", "Vaccine (injection)", "Dogs"),
    };

    public static async Task AddAsync(VetPosDbContext db, DateOnly today, CancellationToken ct)
    {
        var categories = CategoryNames.Select((name, i) => new Category { Name = name, SortOrder = i }).ToList();
        db.Categories.AddRange(categories);
        db.Suppliers.Add(new Supplier { Name = "Sample Pet Supplies (Pvt) Ltd", Phone = "011 234 5678", Address = "Colombo 10" });

        var products = new List<Product>();
        for (var i = 0; i < Products.Length; i++)
        {
            var p = Products[i];
            var newest = p.Batches.LastOrDefault();
            var product = new Product
            {
                Name = p.Name,
                Barcode = InStoreEan13(i + 1),
                Category = categories.Single(c => c.Name == p.Category),
                Unit = p.Unit,
                CostPrice = newest?.Cost ?? 350m,
                SellingPrice = newest?.Price ?? 650m,
                ReorderLevel = p.Reorder,
                TracksExpiry = false,
                PriceChoice = p.Choice,
            };
            if (Medicines.TryGetValue(p.Name, out var m))
            {
                product.IsMedicine = true;
                (product.GenericName, product.Strength, product.DosageForm, product.Species) = (m.Generic, m.Strength, m.Form, m.Species);
            }
            foreach (var b in p.Batches)
            {
                product.Batches.Add(new StockBatch
                {
                    BatchNo = b.BatchNo,
                    ExpiryDate = null, // expiry dates are not used
                    CostPrice = b.Cost,
                    SellingPrice = b.Price,
                    QtyReceived = b.Qty,
                    QtyOnHand = b.Qty,
                });
            }
            product.StockQty = p.Batches.Sum(b => b.Qty);
            products.Add(product);
        }
        db.Products.AddRange(products);
        await db.SaveChangesAsync(ct);

        foreach (var product in products)
        {
            var running = 0;
            foreach (var batch in product.Batches)
            {
                running += batch.QtyOnHand;
                db.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    BatchId = batch.Id,
                    Change = batch.QtyOnHand,
                    QtyAfter = running,
                    Reason = StockReason.Opening,
                    Note = "Sample opening stock",
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>EAN-13 in the in-store range: 200 + 9-digit number + check digit.</summary>
    internal static string InStoreEan13(int n)
    {
        var body = "200" + n.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (body[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return body + ((10 - sum % 10) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
