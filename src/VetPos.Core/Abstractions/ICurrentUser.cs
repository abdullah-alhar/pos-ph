namespace VetPos.Core.Abstractions;

/// <summary>The person signed in now; null before login and for background jobs.</summary>
public interface ICurrentUser
{
    int? UserId { get; }
}
