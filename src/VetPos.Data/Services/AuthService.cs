using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;
using VetPos.Core.Rules;
using VetPos.Core.Security;

namespace VetPos.Data.Services;

public sealed record UserInfo(int Id, string Username, string FullName, UserRole Role, bool IsActive, DateTime? LastLoginAt)
{
    public Actor ToActor() => new(Id, FullName, Role);
}

public sealed record UserInput(int? Id, string Username, string FullName, UserRole Role, string? Pin, bool IsActive);

/// <summary>
/// Users sign in with username + PIN. Five wrong PINs lock that username for two minutes.
/// The first start creates the owner account.
/// </summary>
public sealed class AuthService(IDbContextFactory<VetPosDbContext> dbFactory, ShopClock clock)
{
    private const int MaxFails = 5;
    private static readonly TimeSpan LockFor = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, (int Fails, DateTime Until)> _fails = new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> HasUsersAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AnyAsync(ct);
    }

    public async Task<UserInfo> CreateFirstOwnerAsync(string username, string fullName, string pin, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Users.AnyAsync(ct)) throw new PosException("The owner account already exists.");
        var user = NewUser(new UserInput(null, username, fullName, UserRole.Owner, pin, true));
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        Audit.Add(db, Info(user).ToActor(), AuditActions.UserSave, $"Owner account created: {user.FullName}", "User", user.Id);
        await db.SaveChangesAsync(ct);
        return Info(user);
    }

    public async Task<UserInfo> LoginAsync(string username, string pin, CancellationToken ct = default)
    {
        username = (username ?? "").Trim();
        if (_fails.TryGetValue(username, out var f) && f.Until > clock.UtcNow)
        {
            var wait = (int)Math.Ceiling((f.Until - clock.UtcNow).TotalSeconds);
            throw new PosException($"Too many wrong PINs. Try again in {wait} seconds.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username, ct);
        if (user is null || !user.IsActive || !PinHasher.Verify(pin ?? "", user.PinHash))
        {
            var fails = (_fails.TryGetValue(username, out var old) ? old.Fails : 0) + 1;
            _fails[username] = fails >= MaxFails ? (0, clock.UtcNow + LockFor) : (fails, DateTime.MinValue);
            Audit.Add(db, null, AuditActions.LoginFailed, $"Wrong username or PIN for \"{username}\"");
            await db.SaveChangesAsync(ct);
            throw new PosException(user is { IsActive: false } ? "This account is switched off. Ask the owner." : "Wrong username or PIN.");
        }

        _fails.TryRemove(username, out _);
        user.LastLoginAt = clock.UtcNow;
        Audit.Add(db, Info(user).ToActor(), AuditActions.Login, $"{user.FullName} signed in");
        await db.SaveChangesAsync(ct);
        return Info(user);
    }

    /// <summary>Checks a PIN against the user's own account (screen unlock).</summary>
    public async Task<bool> CheckPinAsync(int userId, string pin, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        return user is not null && PinHasher.Verify(pin, user.PinHash);
    }

    /// <summary>Finds the active owner whose PIN this is (approvals such as voiding a bill).</summary>
    public async Task<UserInfo?> FindOwnerByPinAsync(string pin, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var owners = await db.Users.AsNoTracking().Where(u => u.Role == UserRole.Owner && u.IsActive).ToListAsync(ct);
        return owners.Where(o => PinHasher.Verify(pin, o.PinHash)).Select(Info).FirstOrDefault();
    }

    public async Task<IReadOnlyList<UserInfo>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.Users.AsNoTracking().OrderByDescending(u => u.Role).ThenBy(u => u.FullName).ToListAsync(ct)).Select(Info).ToList();
    }

    public async Task<UserInfo> SaveAsync(UserInput input, Actor actor, CancellationToken ct = default)
    {
        actor.RequireOwner("add or change users");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        User user;
        if (input.Id is { } id)
        {
            user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new PosException("That user no longer exists.");
            var errors = Check(input, isNew: false);
            if (errors is not null) throw new PosException(errors);
            var losingOwner = user.Role == UserRole.Owner && (input.Role != UserRole.Owner || !input.IsActive);
            if (losingOwner && !await db.Users.AnyAsync(u => u.Id != id && u.Role == UserRole.Owner && u.IsActive, ct))
                throw new PosException("Keep at least one active owner, or nobody can change settings.");
            user.Username = input.Username.Trim();
            user.FullName = input.FullName.Trim();
            user.Role = input.Role;
            user.IsActive = input.IsActive;
            if (!string.IsNullOrEmpty(input.Pin)) user.PinHash = PinHasher.Hash(input.Pin);
        }
        else
        {
            user = NewUser(input);
            db.Users.Add(user);
        }
        if (await db.Users.AnyAsync(u => u.Username == user.Username && u.Id != user.Id, ct))
            throw new PosException($"The username \"{user.Username}\" is already used.");
        await db.SaveChangesAsync(ct);
        Audit.Add(db, actor, AuditActions.UserSave, $"User saved: {user.FullName} ({user.Role}{(user.IsActive ? "" : ", off")})", "User", user.Id);
        await db.SaveChangesAsync(ct);
        return Info(user);
    }

    public async Task ChangeOwnPinAsync(int userId, string currentPin, string newPin, CancellationToken ct = default)
    {
        if (PinHasher.CheckPin(newPin) is { } error) throw new PosException(error);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (!PinHasher.Verify(currentPin, user.PinHash)) throw new PosException("Your current PIN is not right.");
        user.PinHash = PinHasher.Hash(newPin);
        Audit.Add(db, Info(user).ToActor(), AuditActions.UserSave, $"{user.FullName} changed their PIN", "User", user.Id);
        await db.SaveChangesAsync(ct);
    }

    private static User NewUser(UserInput input)
    {
        if (Check(input, isNew: true) is { } error) throw new PosException(error);
        return new User
        {
            Username = input.Username.Trim(),
            FullName = input.FullName.Trim(),
            Role = input.Role,
            IsActive = input.IsActive,
            PinHash = PinHasher.Hash(input.Pin!),
        };
    }

    private static string? Check(UserInput input, bool isNew)
    {
        var username = input.Username?.Trim() ?? "";
        if (username.Length is < 2 or > 40 || !username.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
            return "A username is 2 to 40 letters or numbers (no spaces).";
        if (string.IsNullOrWhiteSpace(input.FullName) || input.FullName.Trim().Length > 80) return "Enter the person's name.";
        if (isNew || !string.IsNullOrEmpty(input.Pin)) return PinHasher.CheckPin(input.Pin);
        return null;
    }

    private static UserInfo Info(User u) => new(u.Id, u.Username, u.FullName, u.Role, u.IsActive, u.LastLoginAt);
}
