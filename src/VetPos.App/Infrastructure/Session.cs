using CommunityToolkit.Mvvm.ComponentModel;
using VetPos.Core.Abstractions;
using VetPos.Core.Rules;
using VetPos.Data.Services;

namespace VetPos.App.Infrastructure;

/// <summary>Who is signed in. The database stamps rows with this user; screens read the role.</summary>
public sealed partial class Session : ObservableObject, ICurrentUser
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn), nameof(IsOwner), nameof(Initials))]
    private UserInfo? _user;

    public int? UserId => User?.Id;
    public bool IsSignedIn => User is not null;
    public bool IsOwner => User?.Role == Core.Domain.UserRole.Owner;
    public Actor Actor => User?.ToActor() ?? throw new PosException("Please sign in again.");

    public string Initials => User is null ? "" : string.Concat(User.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
}
