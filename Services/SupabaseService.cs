using TetNET.Models;

namespace TetNET.Services;

/// <summary>
/// Thin service wrapper around Supabase.Client that provides
/// full CRUD + soft-delete for leaderboard_scores, plus a read
/// for game_modes. All queries are issued once — never inside loops.
/// </summary>
public sealed class SupabaseService
{
    readonly Supabase.Client _db;

    public SupabaseService(Supabase.Client db) => _db = db;

    // ----------------------------------------------------------------
    // Game Modes – read-only from the app
    // ----------------------------------------------------------------

    /// <summary>Loads all game modes in a single query.</summary>
    public async Task<List<GameMode>> GetGameModesAsync()
    {
        var result = await _db
            .From<GameMode>()
            .Get();
        return result.Models;
    }

    // ----------------------------------------------------------------
    // Leaderboard – READ
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns up to 50 active (not soft-deleted) scores ordered by
    /// score descending.  One query, never called inside a loop.
    /// </summary>
    public async Task<List<LeaderboardScore>> GetTopScoresAsync()
    {
        var result = await _db
            .From<LeaderboardScore>()
            .Filter<string>("deleted_at", Supabase.Postgrest.Constants.Operator.Is, null)
            .Order("score", Supabase.Postgrest.Constants.Ordering.Descending)
            .Range(0, 49)
            .Get();
        return result.Models;
    }

    // ----------------------------------------------------------------
    // Leaderboard – CREATE
    // ----------------------------------------------------------------

    /// <summary>Inserts a new leaderboard entry and returns the saved row.</summary>
    public async Task<LeaderboardScore?> InsertScoreAsync(
        string playerName, long score, int linesCleared, int levelReached, long modeId)
    {
        var entry = new LeaderboardScore
        {
            PlayerName   = playerName,
            Score        = score,
            LinesCleared = linesCleared,
            LevelReached = levelReached,
            ModeId       = modeId,
        };
        var result = await _db.From<LeaderboardScore>().Insert(entry);
#pragma warning disable CS8603 // result.Model is nullable on the library's type; callers handle null
        return result.Model;
#pragma warning restore CS8603
    }

    // ----------------------------------------------------------------
    // Leaderboard – UPDATE (rename player)
    // ----------------------------------------------------------------

    /// <summary>Updates the player_name on an existing entry.</summary>
    public async Task UpdatePlayerNameAsync(long id, string newName)
    {
        await _db
            .From<LeaderboardScore>()
            .Where(r => r.Id == id)
            .Set(r => r.PlayerName, newName)
            .Update();
    }

    // ----------------------------------------------------------------
    // Leaderboard – SOFT DELETE
    // ----------------------------------------------------------------

    /// <summary>Soft-deletes an entry by stamping deleted_at = now().</summary>
    public async Task SoftDeleteAsync(long id)
    {
        await _db
            .From<LeaderboardScore>()
            .Where(r => r.Id == id)
            .Set(r => r.DeletedAt, DateTime.UtcNow)
            .Update();
    }
}
