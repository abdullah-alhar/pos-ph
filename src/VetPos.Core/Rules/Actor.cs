using VetPos.Core.Domain;

namespace VetPos.Core.Rules;

/// <summary>The signed-in person doing something (for permissions and the audit log).</summary>
public sealed record Actor(int Id, string FullName, UserRole Role)
{
    public bool IsOwner => Role == UserRole.Owner;

    public void RequireOwner(string what)
    {
        if (!IsOwner) throw new PosException($"Only the owner can {what}.");
    }
}
