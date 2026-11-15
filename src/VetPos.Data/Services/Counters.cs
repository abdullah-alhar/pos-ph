using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;

namespace VetPos.Data.Services;

/// <summary>Running numbers (bill 1001, 1002 …; GRN-00001 …). Call inside the save transaction.</summary>
internal static class Counters
{
    public const string Bill = "bill";
    public const string GoodsReceipt = "grn";

    public static async Task<int> NextAsync(VetPosDbContext db, string key, int start, CancellationToken ct)
    {
        var counter = await db.DocumentCounters.SingleOrDefaultAsync(c => c.Key == key, ct);
        if (counter is null)
        {
            counter = new DocumentCounter { Key = key, Value = start - 1 };
            db.DocumentCounters.Add(counter);
        }
        counter.Value++;
        return counter.Value;
    }
}
