namespace VetPos.Core.Domain;

public class User : Entity
{
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public UserRole Role { get; set; }

    /// <summary>Salted PBKDF2 hash of the PIN; never the PIN itself.</summary>
    public string PinHash { get; set; } = "";

    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
}

public class Supplier : Entity
{
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
}
