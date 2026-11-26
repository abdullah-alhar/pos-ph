using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;
using VetPos.Core.Rules;
using VetPos.Data;
using VetPos.Data.Services;
using VetPos.Printing;

namespace VetPos.AI;

public sealed record ReceiptAnswer(ReceiptDesign Design, string Reply, IReadOnlyList<string> Changes);

/// <summary>
/// The AI in the bill editor: "make the total big", "add our WhatsApp QR at the bottom". The AI
/// changes a working copy of the design through tools; the person sees it at once, can undo, and
/// saves it themselves. Nothing is stored by the AI.
/// </summary>
public sealed class ReceiptAssistant(AiClient client, SettingsService settings, IAiKeyProtector keys, IDbContextFactory<VetPosDbContext> dbFactory)
{
    private const int MaxRounds = 5;

    public async Task<ReceiptAnswer> AskAsync(string request, ReceiptDesign current, Actor actor, CancellationToken ct = default)
    {
        request = (request ?? "").Trim();
        if (request.Length == 0) throw new PosException("Type what to change, e.g. \"make the total big\".");
        var design = current.Clone();
        var changes = new List<string>();
        var watch = Stopwatch.StartNew();
        string? reply = null, error = null;
        AiEndpoint? endpoint = null;
        try
        {
            endpoint = await AiEndpoint.ResolveAsync(settings, keys, ct);
            var messages = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = SystemPrompt() },
                new JsonObject { ["role"] = "user", ["content"] = $"Current receipt design:\n{Describe(design)}\n\nRequest: {request}" },
            };
            var definitions = Tools();
            for (var round = 0; round < MaxRounds; round++)
            {
                var answer = await client.ChatAsync(endpoint.BaseUrl, endpoint.Key, endpoint.Model, messages, definitions, allowTools: round < MaxRounds - 1, ct);
                if (answer.ToolCalls.Count == 0)
                {
                    reply = string.IsNullOrWhiteSpace(answer.Text) ? (changes.Count > 0 ? "Done." : "I could not change that. Try saying it another way.") : answer.Text.Trim();
                    break;
                }
                messages.Add(answer.ToAssistantMessage());
                for (var i = 0; i < answer.ToolCalls.Count; i++)
                {
                    var call = answer.ToolCalls[i];
                    if (i >= 12)
                    {
                        messages.Add(AiClient.ToolResult(call, "Too many changes at once. Try again if needed."));
                        continue;
                    }
                    var result = Apply(design, call.Name, call.ArgumentsJson);
                    if (!result.StartsWith("error", StringComparison.Ordinal)) changes.Add(result);
                    messages.Add(AiClient.ToolResult(call, result + "\n\nDesign now:\n" + Describe(design)));
                }
            }
            reply ??= changes.Count > 0 ? "Done." : "That needed too many steps. Please ask one change at a time.";
            return new ReceiptAnswer(design, reply, changes);
        }
        catch (AiUnavailableException ex)
        {
            error = ex.Message;
            throw;
        }
        finally
        {
            await LogAsync(request, changes, reply, endpoint, (int)watch.ElapsedMilliseconds, error, actor);
        }
    }

    // ───────── Tools ─────────

    private static JsonObject Fn(string name, string description, JsonObject props, params string[] required) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = name, ["description"] = description,
            ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = props, ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()) },
        },
    };

    private static JsonObject Str(string d) => new() { ["type"] = "string", ["description"] = d };
    private static JsonObject Bool(string d) => new() { ["type"] = "boolean", ["description"] = d };
    private static JsonObject Enum(string d, params string[] values) => new() { ["type"] = "string", ["description"] = d, ["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()) };

    private static JsonObject Options() => new()
    {
        ["text"] = Str("Text (left text for pair, label for signature). May contain fields like {bill_no}."),
        ["right"] = Str("Right text of a pair"),
        ["align"] = Enum("Position", "left", "center", "right"),
        ["bold"] = Bool("Bold"),
        ["size"] = Enum("Text size", "normal", "tall", "wide", "large"),
        ["underline"] = Bool("Underline"),
        ["invert"] = Bool("White letters on black"),
        ["style"] = Enum("Line style", "dashed", "double", "dotted", "stars"),
        ["lines"] = new JsonObject { ["type"] = "integer", ["description"] = "Empty lines, 1–5" },
        ["layout"] = Enum("Items layout", "two-line", "one-line"),
        ["show_discounts"] = Bool("Items: show discounts and price changes"),
        ["show_barcode"] = Bool("Items: show barcodes"),
        ["numbered"] = Bool("Items: number them"),
        ["big_total"] = Bool("Totals: big TOTAL"),
        ["value"] = Str("QR code link or text"),
        ["qr_size"] = Enum("QR size", "small", "medium", "large"),
        ["when"] = Enum("Print only when", "always", "cash", "card", "bank", "discount", "price-changed", "reprint", "not-reprint"),
        ["hidden"] = Bool("Hide the piece (kept but not printed)"),
    };

    private static JsonArray Tools()
    {
        var add = Options();
        add["type"] = Enum("Kind of piece", "text", "pair", "logo", "divider", "space", "items", "totals", "payments", "itemcount", "savings", "barcode", "qr", "signature");
        add["position"] = Enum("Where", "top", "bottom", "above", "below");
        add["anchor"] = Str("For above/below: the piece id, kind or words it contains");
        var update = Options();
        update["target"] = Str("The piece: its id, its kind (e.g. totals, logo) or words it contains");
        return
        [
            Fn("add_piece", "Add a new piece to the receipt.", add, "type"),
            Fn("update_piece", "Change options of an existing piece.", update, "target"),
            Fn("remove_piece", "Remove a piece.", new JsonObject { ["target"] = Str("The piece: id, kind or words") }, "target"),
            Fn("move_piece", "Move a piece up, down, to the top/bottom, or above/below another piece.",
                new JsonObject { ["target"] = Str("The piece to move"), ["position"] = Enum("Where", "top", "bottom", "up", "down", "above", "below"), ["anchor"] = Str("For above/below") }, "target", "position"),
            Fn("use_template", "Replace the whole design with a ready-made one.", new JsonObject { ["name"] = Enum("Template", ReceiptTemplates.All.Select(t => t.Name).ToArray()) }, "name"),
            Fn("save_paper", "Make the receipt shorter: no empty lines, one line per item, no double lines.", []),
            Fn("set_header_footer", "Change the shop's header text ({header}) or footer text ({footer}).",
                new JsonObject { ["header"] = Str("Header text"), ["footer"] = Str("Footer text") }),
        ];
    }

    /// <summary>Applies one tool call to the working design. Returns a change description, or "error: …" for the AI to read.</summary>
    internal static string Apply(ReceiptDesign d, string name, string argsJson)
    {
        JsonObject a;
        try { a = JsonNode.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson) as JsonObject ?? []; }
        catch (JsonException) { return "error: arguments are not valid JSON"; }
        string? S(string k) => a[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

        switch (name)
        {
            case "add_piece":
            {
                if (!TryType(S("type"), out var type)) return "error: unknown piece type";
                var piece = new ReceiptPiece { Type = type };
                if (type == PieceType.Text && S("text") is null) piece.Text = "Text";
                var err = SetOptions(piece, a);
                if (err is not null) return err;
                var at = Position(d, S("position") ?? "bottom", S("anchor"), out var posErr);
                if (posErr is not null) return posErr;
                d.Pieces.Insert(at, piece);
                return $"Added {ReceiptFields.Describe(piece)}";
            }
            case "update_piece":
            {
                var piece = Find(d, S("target"), out var err);
                if (piece is null) return err!;
                if (piece.Locked) return "error: that piece is locked by the owner";
                err = SetOptions(piece, a);
                return err ?? $"Changed {ReceiptFields.Describe(piece)}";
            }
            case "remove_piece":
            {
                var piece = Find(d, S("target"), out var err);
                if (piece is null) return err!;
                if (piece.Locked) return "error: that piece is locked by the owner";
                d.Pieces.Remove(piece);
                return $"Removed {ReceiptFields.Describe(piece)}";
            }
            case "move_piece":
            {
                var piece = Find(d, S("target"), out var err);
                if (piece is null) return err!;
                if (piece.Locked) return "error: that piece is locked by the owner";
                var i = d.Pieces.IndexOf(piece);
                var pos = S("position") ?? "";
                d.Pieces.RemoveAt(i);
                int at;
                switch (pos)
                {
                    case "up": at = Math.Max(0, i - 1); break;
                    case "down": at = Math.Min(d.Pieces.Count, i + 1); break;
                    default:
                        at = Position(d, pos, S("anchor"), out var posErr);
                        if (posErr is not null) { d.Pieces.Insert(i, piece); return posErr; }
                        break;
                }
                d.Pieces.Insert(at, piece);
                return $"Moved {ReceiptFields.Describe(piece)} {pos}";
            }
            case "use_template":
            {
                var t = ReceiptTemplates.All.FirstOrDefault(x => string.Equals(x.Name, S("name"), StringComparison.OrdinalIgnoreCase));
                if (t.Build is null) return "error: no template with that name";
                var built = t.Build();
                d.Pieces = built.Pieces;
                return $"Used the {t.Name} design";
            }
            case "save_paper":
                d.Pieces = ReceiptCheck.SavePaper(d).Pieces;
                return "Saved paper";
            case "set_header_footer":
                if (S("header") is { } h) d.HeaderText = h;
                if (S("footer") is { } f) d.FooterText = f;
                return "Changed the header/footer text";
            default:
                return $"error: there is no tool called {name}";
        }
    }

    private static bool TryType(string? text, out PieceType type)
    {
        type = default;
        var t = (text ?? "").Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        return System.Enum.TryParse(t, ignoreCase: true, out type)
            || (t.Equals("line", StringComparison.OrdinalIgnoreCase) && (type = PieceType.Divider) == PieceType.Divider)
            || (t.Equals("total", StringComparison.OrdinalIgnoreCase) && (type = PieceType.Totals) == PieceType.Totals);
    }

    private static ReceiptPiece? Find(ReceiptDesign d, string? target, out string? error)
    {
        error = null;
        var t = (target ?? "").Trim();
        if (t.Length == 0) { error = "error: say which piece"; return null; }
        var byId = d.Pieces.FirstOrDefault(p => p.Id == t);
        if (byId is not null) return byId;
        if (TryType(t, out var type))
        {
            var ofType = d.Pieces.Where(p => p.Type == type).ToList();
            if (ofType.Count == 1) return ofType[0];
            if (ofType.Count > 1) { error = $"error: there are {ofType.Count} {type} pieces; use the id"; return null; }
        }
        var byText = d.Pieces.Where(p => ReceiptFields.Describe(p).Contains(t, StringComparison.OrdinalIgnoreCase)
                                         || p.Text.Contains(t, StringComparison.OrdinalIgnoreCase) || p.Right.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byText.Count == 1) return byText[0];
        error = byText.Count == 0 ? $"error: no piece matches \"{t}\"" : $"error: {byText.Count} pieces match \"{t}\"; use the id";
        return null;
    }

    private static int Position(ReceiptDesign d, string position, string? anchor, out string? error)
    {
        error = null;
        switch (position)
        {
            case "top": return 0;
            case "bottom": return d.Pieces.Count;
            case "above":
            case "below":
                var a = Find(d, anchor, out error);
                if (a is null) return 0;
                return d.Pieces.IndexOf(a) + (position == "below" ? 1 : 0);
            default:
                error = "error: position must be top, bottom, above or below";
                return 0;
        }
    }

    private static string? SetOptions(ReceiptPiece p, JsonObject a)
    {
        foreach (var (key, node) in a)
        {
            if (node is not JsonValue v) continue;
            bool B() => v.TryGetValue<bool>(out var b) ? b : string.Equals(v.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            var s = v.ToString();
            switch (key)
            {
                case "text": p.Text = s; break;
                case "right": p.Right = s; break;
                case "align": if (!System.Enum.TryParse<PieceAlign>(s, true, out var al)) return "error: align is left, center or right"; p.Align = al; break;
                case "bold": p.Bold = B(); break;
                case "size": if (!System.Enum.TryParse<TextSize>(s, true, out var sz)) return "error: size is normal, tall, wide or large"; p.Size = sz; break;
                case "underline": p.Underline = B(); break;
                case "invert": p.Invert = B(); break;
                case "style":
                    p.Style = s.ToLowerInvariant() switch { "double" => DividerStyle.DoubleLine, "dotted" => DividerStyle.Dotted, "stars" => DividerStyle.Stars, _ => DividerStyle.Dashed };
                    break;
                case "lines": p.Lines = Math.Clamp(v.TryGetValue<int>(out var n) ? n : int.TryParse(s, CultureInfo.InvariantCulture, out n) ? n : 1, 1, 5); break;
                case "layout": p.Layout = s.Contains("one", StringComparison.OrdinalIgnoreCase) ? ItemLayout.OneLine : ItemLayout.TwoLine; break;
                case "show_discounts": p.ShowDiscounts = B(); break;
                case "show_barcode": p.ShowBarcode = B(); break;
                case "numbered": p.Numbered = B(); break;
                case "big_total": p.BigTotal = B(); break;
                case "value": p.Value = s; break;
                case "qr_size": if (!System.Enum.TryParse<QrSize>(s, true, out var q)) return "error: qr_size is small, medium or large"; p.QrSize = q; break;
                case "when":
                    if (!System.Enum.TryParse<PrintWhen>(s.Replace("-", "", StringComparison.Ordinal), true, out var w)) return "error: unknown 'when'";
                    p.When = w;
                    break;
                case "hidden": p.Hidden = B(); break;
            }
        }
        return null;
    }

    /// <summary>The design as numbered lines the AI can read.</summary>
    internal static string Describe(ReceiptDesign d)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < d.Pieces.Count; i++)
        {
            var p = d.Pieces[i];
            sb.Append(CultureInfo.InvariantCulture, $"{i + 1}. id={p.Id} {p.Type.ToString().ToLowerInvariant()}");
            if (p.Type is PieceType.Text or PieceType.Pair or PieceType.Signature) sb.Append(CultureInfo.InvariantCulture, $" text=\"{p.Text}\"");
            if (p.Type == PieceType.Pair) sb.Append(CultureInfo.InvariantCulture, $" right=\"{p.Right}\"");
            if (p.Type == PieceType.Text) sb.Append(CultureInfo.InvariantCulture, $" align={p.Align.ToString().ToLowerInvariant()} size={p.Size.ToString().ToLowerInvariant()}{(p.Bold ? " bold" : "")}{(p.Invert ? " invert" : "")}");
            if (p.Type == PieceType.Qr) sb.Append(CultureInfo.InvariantCulture, $" value=\"{p.Value}\"");
            if (p.Type == PieceType.Items) sb.Append(CultureInfo.InvariantCulture, $" layout={(p.Layout == ItemLayout.OneLine ? "one-line" : "two-line")}");
            if (p.Type == PieceType.Totals) sb.Append(p.BigTotal ? " big_total" : "");
            if (p.When != PrintWhen.Always) sb.Append(CultureInfo.InvariantCulture, $" when={p.When.ToString().ToLowerInvariant()}");
            if (p.Hidden) sb.Append(" hidden");
            if (p.Locked) sb.Append(" locked");
            sb.AppendLine();
        }
        sb.Append(CultureInfo.InvariantCulture, $"header=\"{d.HeaderText}\" footer=\"{d.FooterText}\"");
        return sb.ToString();
    }

    private static string SystemPrompt() => $"""
        You edit the receipt design of a veterinary shop's point-of-sale. The receipt prints top to bottom on an 80 mm thermal printer and is a list of pieces.
        Use the tools to make the change the person asks for, then reply with one or two short, friendly sentences saying what you changed.
        Fields that can go in any text: {string.Join(", ", ReceiptFields.All.Keys.Select(k => "{" + k + "}"))}.
        Target pieces by id when you can. Never remove the items or totals pieces unless asked clearly. Locked pieces cannot be changed.
        If the request is unclear, ask a short question instead of guessing. Reply in the language of the request.
        """;

    private async Task LogAsync(string request, List<string> changes, string? reply, AiEndpoint? endpoint, int ms, string? error, Actor actor)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            db.AiLog.Add(new AiLogEntry
            {
                Question = "[Bill editor] " + (request.Length > 3900 ? request[..3900] : request),
                ToolsJson = JsonSerializer.Serialize(changes),
                Answer = reply,
                Provider = endpoint?.ProviderId,
                Model = endpoint?.Model,
                DurationMs = ms,
                Error = error,
                CreatedById = actor.Id,
            });
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Logging must never break the editor.
        }
    }
}
