# TET.NET - Blazor Edition

Game Boy style Tetris, Blazor WebAssembly (.NET 8). No server code, runs fully in the browser.

## Run
    dotnet run
then open the URL it prints (usually http://localhost:5000).

## Publish as static site
    dotnet publish -c Release
Serve `bin/Release/net8.0/publish/wwwroot` from any static host.

## Supabase Setup

1. Create a Supabase project at <https://supabase.com>.
2. Run `database/schema.sql` in the **SQL Editor** to create `game_modes` and `leaderboard_scores`.
3. Copy your **Project URL** and **anon public key** from *Settings → API* into `wwwroot/appsettings.json`:

```json
{
  "Supabase": {
    "Url": "https://<your-ref>.supabase.co",
    "Key": "<your-anon-public-key>"
  }
}
```

> **Never commit real keys.** Add `wwwroot/appsettings.json` to `.gitignore` or use environment-specific overrides.

## Leaderboard & Soft Delete

The `leaderboard_scores` table uses a **soft-delete** pattern via the `deleted_at` column (nullable `timestamptz`).
Instead of physically removing a row, the app stamps `deleted_at = now()`, and all active-score queries filter on `deleted_at IS NULL`.
This design is well-suited to Tet.NET because it preserves a full audit trail of every score ever submitted — useful for detecting cheating or score disputes — while keeping the active leaderboard fast through a partial index on `(score DESC)`.
Score history also enables future features such as personal bests, replay timelines, or score rollback, without any schema migration.

Notes: the leaderboard is strictly dependent on the Supabase database, and local storage mirrors active database scores for offline cache. Key bindings are saved in browser localStorage.
The "Press Start 2P" font loads from Google Fonts (falls back to a monospace font offline).
