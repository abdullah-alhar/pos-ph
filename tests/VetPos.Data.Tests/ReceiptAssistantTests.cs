using Microsoft.EntityFrameworkCore;
using VetPos.AI;
using VetPos.Core.Settings;
using VetPos.Data.Services;
using VetPos.Printing;

namespace VetPos.Data.Tests;

public class ReceiptAssistantTests
{
    [Fact]
    public void Tools_change_the_design()
    {
        var d = ReceiptTemplates.Classic();
        Assert.StartsWith("Changed", ReceiptAssistant.Apply(d, "update_piece", """{"target":"totals","big_total":false}"""), StringComparison.Ordinal);
        Assert.False(d.Pieces.Single(p => p.Type == PieceType.Totals).BigTotal);

        Assert.StartsWith("Added", ReceiptAssistant.Apply(d, "add_piece", """{"type":"qr","value":"https://wa.me/94771234567","position":"bottom"}"""), StringComparison.Ordinal);
        Assert.Equal(PieceType.Qr, d.Pieces[^1].Type);

        Assert.StartsWith("Added", ReceiptAssistant.Apply(d, "add_piece", """{"type":"text","text":"Free delivery in Kandy","position":"above","anchor":"totals","bold":true}"""), StringComparison.Ordinal);
        var i = d.Pieces.FindIndex(p => p.Text == "Free delivery in Kandy");
        Assert.Equal(PieceType.Totals, d.Pieces[i + 1].Type);

        Assert.StartsWith("Moved", ReceiptAssistant.Apply(d, "move_piece", """{"target":"logo","position":"bottom"}"""), StringComparison.Ordinal);
        Assert.Equal(PieceType.Logo, d.Pieces[^1].Type);

        Assert.StartsWith("Removed", ReceiptAssistant.Apply(d, "remove_piece", """{"target":"barcode"}"""), StringComparison.Ordinal);
        Assert.DoesNotContain(d.Pieces, p => p.Type == PieceType.Barcode);

        Assert.Equal("Used the Compact design", ReceiptAssistant.Apply(d, "use_template", """{"name":"compact"}"""));
    }

    [Fact]
    public void Bad_targets_and_locked_pieces_come_back_as_errors()
    {
        var d = ReceiptTemplates.Classic();
        Assert.StartsWith("error", ReceiptAssistant.Apply(d, "update_piece", """{"target":"divider","bold":true}"""), StringComparison.Ordinal); // several lines
        Assert.StartsWith("error", ReceiptAssistant.Apply(d, "remove_piece", """{"target":"nothing like this"}"""), StringComparison.Ordinal);
        d.Pieces.Single(p => p.Type == PieceType.Totals).Locked = true;
        Assert.StartsWith("error", ReceiptAssistant.Apply(d, "remove_piece", """{"target":"totals"}"""), StringComparison.Ordinal);
        Assert.StartsWith("error", ReceiptAssistant.Apply(d, "add_piece", """{"type":"rocket"}"""), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ask_applies_tool_calls_to_a_copy_and_logs()
    {
        var (test, owner, _, _, _) = await Shop.OpenAsync();
        await using var _ = test;
        await test.Get<SettingsService>().SaveAsync(AiSettings.Key, new AiSettings { Enabled = true, Provider = "groq", EncryptedKey = "enc:gsk_test_1234" }, owner);
        var fake = new FakeAi(FakeAi.ToolCall("update_piece", """{"target":"totals","big_total":false}"""), FakeAi.Answer("The total is now normal size."));
        var ai = new ReceiptAssistant(new AiClient(new HttpClient(fake)), test.Get<SettingsService>(), new PlainKeys(), test.Get<IDbContextFactory<VetPosDbContext>>());
        var original = ReceiptTemplates.Classic();

        var answer = await ai.AskAsync("make the total small", original, owner);

        Assert.Equal("The total is now normal size.", answer.Reply);
        Assert.Single(answer.Changes);
        Assert.False(answer.Design.Pieces.Single(p => p.Type == PieceType.Totals).BigTotal);
        Assert.True(original.Pieces.Single(p => p.Type == PieceType.Totals).BigTotal); // the editor's design is untouched
        await using var db = test.NewContext();
        Assert.StartsWith("[Bill editor]", (await db.AiLog.SingleAsync()).Question, StringComparison.Ordinal);
    }
}
