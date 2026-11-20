using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace TetNET.Models;

[Table("game_modes")]
public class GameMode : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;
}
