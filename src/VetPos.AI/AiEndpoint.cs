using VetPos.Core.Settings;
using VetPos.Data.Services;

namespace VetPos.AI;

/// <summary>Where to send AI requests, from Settings → AI (shared by the assistants).</summary>
public sealed record AiEndpoint(string ProviderId, string BaseUrl, string Model, string? Key)
{
    /// <summary>The endpoint, or throws <see cref="AiUnavailableException"/> with a reason the owner understands.</summary>
    public static async Task<AiEndpoint> ResolveAsync(SettingsService settings, IAiKeyProtector keys, CancellationToken ct = default)
    {
        var s = await settings.GetAsync<AiSettings>(AiSettings.Key, ct);
        if (!s.Enabled) throw new AiUnavailableException("The AI assistant is switched off. The owner can switch it on in Settings → AI.");
        var provider = AiProviders.Get(s.Provider);
        if (provider.Id == "custom" && !AiProviders.IsSafeUrl(s.CustomUrl)) throw new AiUnavailableException("The AI service address is not set (Settings → AI).");
        var key = string.IsNullOrEmpty(s.EncryptedKey) ? null : keys.Unprotect(s.EncryptedKey);
        if (provider.NeedsKey && string.IsNullOrEmpty(key)) throw new AiUnavailableException($"No key is saved for {provider.Name} (Settings → AI).");
        return new AiEndpoint(provider.Id, provider.Id == "custom" ? s.CustomUrl.Trim() : provider.BaseUrl,
            string.IsNullOrWhiteSpace(s.Model) ? provider.DefaultModel : s.Model.Trim(), key);
    }
}
