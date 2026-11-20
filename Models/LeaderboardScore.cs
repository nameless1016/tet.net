using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace TetNET.Models;

[Table("leaderboard_scores")]
public class LeaderboardScore : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("player_name")]
    public string PlayerName { get; set; } = string.Empty;

    [Column("score")]
    public long Score { get; set; }

    [Column("lines_cleared")]
    public int LinesCleared { get; set; }

    [Column("level_reached")]
    public int LevelReached { get; set; }

    [Column("mode_id")]
    public long ModeId { get; set; }

    /// <summary>
    /// Soft-delete sentinel. NULL = active; non-NULL = deleted.
    /// </summary>
    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
