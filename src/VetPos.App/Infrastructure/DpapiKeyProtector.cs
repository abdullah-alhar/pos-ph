using System.Security.Cryptography;
using System.Text;
using VetPos.AI;

namespace VetPos.App.Infrastructure;

/// <summary>The AI key is encrypted with Windows DPAPI for the signed-in Windows user. Never stored as plain text.</summary>
public sealed class DpapiKeyProtector : IAiKeyProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VetPOS.ai-key.v1");

    public string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    public string? Unprotect(string protectedValue)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null; // made by another Windows user or damaged: the owner enters the key again
        }
    }
}
