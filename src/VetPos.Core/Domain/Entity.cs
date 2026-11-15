namespace VetPos.Core.Domain;

/// <summary>
/// Every table has these four columns. The database context fills them in on save, so no
/// service has to remember to.
/// </summary>
public abstract class Entity
{
    public int Id { get; set; }

    /// <summary>When the row was first saved (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When the row was last changed (UTC).</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>The user who created the row; null for rows made by the system (seed, backups).</summary>
    public int? CreatedById { get; set; }
}
