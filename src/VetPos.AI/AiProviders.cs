namespace VetPos.AI;

public sealed record AiProvider(string Id, string Name, string BaseUrl, string DefaultModel, string KeyHelp, bool NeedsKey = true,
    string KeyUrl = "", IReadOnlyList<string>? Steps = null, string Free = "");

/// <summary>
/// Services that speak the OpenAI-compatible chat API with tool calling (same list as the old
/// ShopPOS). Swapping provider = choosing another entry in Settings → AI.
/// </summary>
public static class AiProviders
{
    public static readonly IReadOnlyList<AiProvider> All =
    [
        new("groq", "Groq (free, fast)", "https://api.groq.com/openai/v1", "openai/gpt-oss-120b", "console.groq.com → API Keys → Create API Key",
            KeyUrl: "https://console.groq.com/keys", Steps: ["Open console.groq.com and sign in with Google or an email (no card needed).", "Open “API Keys” and press “Create API Key”.", "Copy the key (it starts with gsk_), paste it below and press Save."], Free: "Free, no card. Very fast; enough for a shop."),
        new("gemini", "Google Gemini (free)", "https://generativelanguage.googleapis.com/v1beta", GeminiChat.DefaultModel, "aistudio.google.com → Get API key",
            KeyUrl: "https://aistudio.google.com/apikey", Steps: ["Open aistudio.google.com and sign in with a Google account.", "Press “Get API key”, then “Create API key”.", "Copy the key, paste it below and press Save."], Free: "Free with a Google account."),
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", "openrouter/free", "openrouter.ai → Keys → Create key",
            KeyUrl: "https://openrouter.ai/keys", Steps: ["Open openrouter.ai and sign up (no card for free models).", "Open “Keys” and press “Create key”.", "Copy the key (sk-or-…), paste it below and press Save."], Free: "Free models, small daily limit."),
        new("mistral", "Mistral", "https://api.mistral.ai/openai/v1", "mistral-small-latest", "console.mistral.ai → API Keys",
            KeyUrl: "https://console.mistral.ai/api-keys", Steps: ["Open console.mistral.ai, sign up and choose the free “Experiment” plan.", "Open “API Keys” and create a key.", "Copy the key, paste it below and press Save."], Free: "Free plan after phone check."),
        new("cerebras", "Cerebras", "https://api.cerebras.ai/v1", "gpt-oss-120b", "cloud.cerebras.ai → API Keys",
            KeyUrl: "https://cloud.cerebras.ai", Steps: ["Open cloud.cerebras.ai and sign up.", "Open “API Keys” and create a key.", "Copy the key, paste it below and press Save."], Free: "Usage depends on account credits and limits."),
        new("github", "GitHub Models (retired)", "", "", "Choose another AI provider in Settings."),
        new("ollama", "On this computer (Ollama)", "http://localhost:11434/v1", "qwen3:4b", "No key needed. Install Ollama and run: ollama pull qwen3:4b", NeedsKey: false,
            KeyUrl: "https://ollama.com/download", Steps: ["Install Ollama on this PC from ollama.com.", "Open Command Prompt and run: ollama pull qwen3:4b", "Choose this option and press Save (no key needed)."], Free: "Free, runs on this PC (needs a strong PC)."),
        new("custom", "Other OpenAI-compatible service", "", "", "The service's own key (if it needs one)", NeedsKey: false,
            KeyUrl: "", Steps: ["Any OpenAI-compatible service works: type its address (ending in /v1) and model.", "Paste its key if it needs one and press Save."], Free: "Depends on the service."),
    ];

    public static AiProvider Get(string? id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];

    /// <summary>"gsk_…9f3a": enough to recognise a saved key without showing it.</summary>
    public static string KeyHint(string key)
    {
        var k = key.Trim();
        if (k.Length < 8) return "…";
        var dash = k.IndexOfAny(['_', '-']);
        var prefix = dash is > 0 and < 7 ? k[..(dash + 1)] : "";
        return $"{prefix}…{k[^4..]}";
    }

    /// <summary>https anywhere; plain http only on this computer or the shop network (keys must not cross the internet unencrypted).</summary>
    public static bool IsSafeUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return false;
        if (u.Scheme == Uri.UriSchemeHttps) return true;
        if (u.Scheme != Uri.UriSchemeHttp) return false;
        if (u.IsLoopback) return true;
        return System.Net.IPAddress.TryParse(u.Host, out var ip) && ip.GetAddressBytes() is [10, ..] or [192, 168, ..] or [172, >= 16 and <= 31, ..];
    }
}

/// <summary>Encrypts the AI key at rest (Windows DPAPI in the app).</summary>
public interface IAiKeyProtector
{
    string Protect(string plain);
    string? Unprotect(string protectedValue);
}
