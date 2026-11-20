using System.Diagnostics;
using System.Text.Json;
using Microsoft.JSInterop;
using TetNET.Models;
using TetNET.Services;

namespace TetNET.Game;

public enum Mode { Menu, Play, Board, Controls, Help }
public record ScoreEntry(string Name, int Score, int Lines);
public record Row(string Name, long Score, int Lines, bool Entry);

public sealed class AppState
{
    readonly IJSRuntime js;
    readonly Action changed;
    readonly SupabaseService? supa;

    public static readonly string[] MenuItems = { "PLAY", "HOW TO PLAY", "CONTROLS", "LEADERBOARD" };
    public static readonly (GameAction A, string Label)[] CtlRows =
    {
        (GameAction.Left, "MOVE LEFT"), (GameAction.Right, "MOVE RIGHT"), (GameAction.SoftDrop, "SOFT DROP"),
        (GameAction.HardDrop, "HARD DROP"), (GameAction.RotateCw, "ROTATE CW"), (GameAction.RotateCcw, "ROTATE CCW"),
        (GameAction.Hold, "HOLD"), (GameAction.Pause, "PAUSE"),
    };

    public Mode At = Mode.Menu;
    public int MenuIndex;
    public Engine? G;
    public bool Paused;
    public Bindings Keys = new();

    // Local cache (synced from DB; no hardcoded defaults)
    public List<ScoreEntry> Scores = new();

    // leaderboard – 3-letter name entry after game over
    public bool Entering;
    public int EntryRank, Slot;
    public char[] Name = { '_', '_', '_' };
    public string Banner = "";

    // leaderboard – Supabase rows (source of truth)
    public List<LeaderboardScore> DbScores = new();
    public List<GameMode> DbModes = new();
    public bool DbLoading;
    public string? DbError;

    // in-flight guard – disables OK button while saving to prevent duplicate POSTs
    public bool Saving;

    // controls
    public int CtlSel, Rebind = -1;

    // play input
    bool heldL, heldR, soft;
    int lastDir;
    double das; bool arr; int prevDir;
    DateTime goAt = DateTime.MaxValue;
    int loopGen;

    public AppState(IJSRuntime js, Action changed) { this.js = js; this.changed = changed; }
    public AppState(IJSRuntime js, Action changed, SupabaseService supa)
        : this(js, changed) { this.supa = supa; }

    public static string Pad(int v, int w)  => Math.Max(0, v).ToString().PadLeft(w, '0');
    public static string Pad(long v, int w) => Math.Max(0L, v).ToString().PadLeft(w, '0');

    // Title-screen HI-SCORE reads directly from the database list
    public long HiScore => DbScores.Count > 0 ? DbScores[0].Score : 0;

    // ---------- persistence ----------
    public async Task LoadAsync()
    {
        try
        {
            // Wipe any legacy localStorage scores so the DB is the sole source of truth
            await js.InvokeVoidAsync("tetnet.set", "tetnet.scores", "[]");
            Scores.Clear();

            var k = await js.InvokeAsync<string?>("tetnet.get", "tetnet.keys");
            if (!string.IsNullOrEmpty(k)) Keys.Load(k);
        }
        catch { }

        // Load DB on startup so HI-SCORE and top 5 are populated immediately
        await LoadDbAsync();
        changed();
    }

    void SaveKeys() => _ = js.InvokeVoidAsync("tetnet.set", "tetnet.keys", Keys.Json());

    // ---------- Supabase leaderboard ----------

    /// <summary>Fetches modes + active scores in two parallel queries; never called inside a loop.</summary>
    public async Task LoadDbAsync()
    {
        if (supa == null) return;
        DbLoading = true; DbError = null; changed();
        try
        {
            var modesTask  = supa.GetGameModesAsync();
            var scoresTask = supa.GetTopScoresAsync();
            await Task.WhenAll(modesTask, scoresTask);
            DbModes  = modesTask.Result;
            DbScores = scoresTask.Result;

            // Keep local cache in sync for reference (not used for display)
            Scores = DbScores.Select(s => new ScoreEntry(s.PlayerName, (int)s.Score, s.LinesCleared)).Take(5).ToList();

            // If name entry is in progress, recalculate the insertion rank
            if (Entering && G != null)
            {
                EntryRank = DbScores.FindIndex(e => G.Score > e.Score);
                if (EntryRank < 0) EntryRank = DbScores.Count;
            }
        }
        catch (Exception ex) { DbError = ex.Message; }
        finally { DbLoading = false; changed(); }
    }

    /// <summary>
    /// Called when the player confirms their 3-letter name after game over.
    /// Inserts the score into Supabase and refreshes the leaderboard.
    /// </summary>
    public async Task ConfirmEntryDbAsync()
    {
        if (!Entering || G == null || supa == null || Saving) return;
        var nm = new string(Name).Replace("_", "").Trim();
        if (nm.Length == 0) nm = "AAA";

        Entering = false;
        Saving = true; changed();
        try
        {
            long modeId = DbModes.Count > 0 ? DbModes[0].Id : 1;
            await supa.InsertScoreAsync(nm, G.Score, G.Lines, G.Level, modeId);
            await LoadDbAsync();
        }
        catch (Exception ex) { DbError = ex.Message; }
        finally { Saving = false; changed(); }
    }

    // ---------- navigation ----------
    public void GoMenu() { At = Mode.Menu; loopGen++; Paused = false; Entering = false; Saving = false; changed(); }

    public void Hover(int i) { MenuIndex = i; }

    public void Activate(int i)
    {
        MenuIndex = i;
        switch (i)
        {
            case 0: StartGame(); break;
            case 1: At = Mode.Help; break;
            case 2: At = Mode.Controls; CtlSel = 0; Rebind = -1; break;
            case 3: OpenBoard(false); break;
        }
        changed();
    }

    public void StartGame()
    {
        G = new Engine();
        Paused = false; heldL = heldR = soft = false; das = 0; arr = false; prevDir = 0;
        goAt = DateTime.MaxValue;
        At = Mode.Play;
        _ = Loop();
    }

    public void OpenBoard(bool fromGame)
    {
        At = Mode.Board; loopGen++;
        Entering = false; Banner = "";
        if (fromGame && G != null)
        {
            // Always prompt for initials — every game is recorded to the database.
            // No top-5 comparison; every non-zero score gets a name and is submitted.
            Banner = "GAME OVER";
            if (G.Score > 0)
            {
                Entering = true;
                // Show "NEW HIGH SCORE!" banner if the player beats the current top-5
                bool isTopFive = DbScores.Count < 5
                    || G.Score > DbScores[Math.Min(4, DbScores.Count - 1)].Score;
                if (isTopFive) Banner = "NEW HIGH SCORE!";
                EntryRank = DbScores.FindIndex(e => G.Score > e.Score);
                if (EntryRank < 0) EntryRank = DbScores.Count;
                Name = new[] { '_', '_', '_' }; Slot = 0;
            }
        }
        _ = LoadDbAsync();
        changed();
    }

    /// <summary>
    /// Returns up to 5 rows for the retro SVG board, sourced from the database.
    /// Injects the player's live name-entry row at the correct rank during game-over submission.
    /// </summary>
    public List<Row> Rows()
    {
        var rows = DbScores.Select(e => new Row(e.PlayerName, e.Score, e.LinesCleared, false)).ToList();
        if (Entering && G != null)
        {
            int idx = Math.Clamp(EntryRank, 0, rows.Count);
            rows.Insert(idx, new Row(new string(Name), G.Score, G.Lines, true));
        }
        return rows.Take(5).ToList();
    }

    // Called by keyboard Enter press on the leaderboard screen
    public void ConfirmEntry()
    {
        if (!Entering || G == null) return;
        _ = ConfirmEntryDbAsync();
    }

    public void ResetKeys() { Keys.Reset(); SaveKeys(); changed(); }
    public void BeginRebind(int row) { CtlSel = row; Rebind = row; changed(); }

    // ---------- game loop ----------
    async Task Loop()
    {
        int gen = ++loopGen;
        long last = Stopwatch.GetTimestamp();
        while (gen == loopGen && At == Mode.Play)
        {
            await Task.Delay(16);
            long now = Stopwatch.GetTimestamp();
            double dt = Math.Min(0.1, (now - last) / (double)Stopwatch.Frequency);
            last = now;
            if (G != null && !Paused && !G.GameOver) Step(dt);
            changed();
        }
    }

    void Step(double dt)
    {
        var g = G!;
        int dir = heldL && heldR ? lastDir : heldL ? -1 : heldR ? 1 : 0;
        if (dir != prevDir) { das = 0; arr = false; prevDir = dir; }
        if (dir != 0)
        {
            das += dt;
            if (!arr && das >= 0.16) { arr = true; das = 0.04; }
            if (arr) while (das >= 0.04) { das -= 0.04; g.Move(dir); }
        }
        g.Tick(dt, soft);
        if (g.GameOver && goAt == DateTime.MaxValue) goAt = DateTime.UtcNow;
    }

    // ---------- input ----------
    public void Blur()
    {
        heldL = heldR = soft = false;
        if (At == Mode.Play && G is { GameOver: false }) Paused = true;
        changed();
    }

    public void KeyUp(string code)
    {
        if (At != Mode.Play) return;
        switch (Keys.Find(code))
        {
            case GameAction.Left:     heldL = false; if (heldR) lastDir = 1;  break;
            case GameAction.Right:    heldR = false; if (heldL) lastDir = -1; break;
            case GameAction.SoftDrop: soft = false; break;
        }
    }

    public void KeyDown(string code, bool repeat)
    {
        if (repeat && (At == Mode.Play || Rebind >= 0 || code == "Enter" || code == "Space")) return;
        switch (At)
        {
            case Mode.Menu:     MenuKey(code); break;
            case Mode.Help:     if (code is "Enter" or "Escape" or "Backspace" or "Space") GoMenu(); break;
            case Mode.Board:    BoardKey(code); break;
            case Mode.Controls: ControlsKey(code); break;
            case Mode.Play:     PlayKey(code); break;
        }
        changed();
    }

    void MenuKey(string code)
    {
        switch (code)
        {
            case "ArrowUp":   MenuIndex = (MenuIndex + 3) % 4; break;
            case "ArrowDown": MenuIndex = (MenuIndex + 1) % 4; break;
            case "Enter": case "Space": Activate(MenuIndex); break;
        }
    }

    void BoardKey(string code)
    {
        if (!Entering)
        {
            if (code is "Enter" or "Escape" or "Backspace" or "Space") GoMenu();
            return;
        }
        // Escape always exits (skips saving) even during name entry
        if (code == "Escape") { Entering = false; GoMenu(); return; }
        switch (code)
        {
            case "ArrowUp":    Cycle(true);  break;
            case "ArrowDown":  Cycle(false); break;
            case "ArrowLeft":  Slot = Math.Max(0, Slot - 1); break;
            case "ArrowRight": Slot = Math.Min(2, Slot + 1); break;
            case "Enter":      ConfirmEntry(); break;
            case "Backspace":  Name[Slot] = '_'; Slot = Math.Max(0, Slot - 1); break;
            default:
                if (code.Length == 4 && code.StartsWith("Key"))
                { Name[Slot] = code[3]; Slot = Math.Min(2, Slot + 1); }
                break;
        }
    }

    void Cycle(bool up)
    {
        char c = Name[Slot];
        if (c == '_') c = up ? 'A' : 'Z';
        else if (up) c = c == 'Z' ? 'A' : (char)(c + 1);
        else c = c == 'A' ? 'Z' : (char)(c - 1);
        Name[Slot] = c;
    }

    void ControlsKey(string code)
    {
        if (Rebind >= 0)
        {
            Keys.Set(CtlRows[Rebind].A, code);
            Rebind = -1; SaveKeys();
            return;
        }
        switch (code)
        {
            case "ArrowUp":   CtlSel = Math.Max(0, CtlSel - 1); break;
            case "ArrowDown": CtlSel = Math.Min(9, CtlSel + 1); break;
            case "ArrowLeft":  if (CtlSel >= 8) CtlSel = 8; break;
            case "ArrowRight": if (CtlSel >= 8) CtlSel = 9; break;
            case "Escape": GoMenu(); break;
            case "Enter": case "Space":
                if (CtlSel < 8) Rebind = CtlSel;
                else if (CtlSel == 8) ResetKeys();
                else GoMenu();
                break;
        }
    }

    void PlayKey(string code)
    {
        var g = G;
        if (g == null) return;
        if (g.GameOver)
        {
            // goAt may still be MaxValue if game ended via HardDrop (outside Step) — set it now
            if (goAt == DateTime.MaxValue) goAt = DateTime.UtcNow;
            if (code == "Enter" && (DateTime.UtcNow - goAt).TotalSeconds > 0.7) OpenBoard(true);
            return;
        }
        var act = Keys.Find(code);
        if (Paused)
        {
            if (act == GameAction.Pause) Paused = false;
            else if (code == "KeyQ") GoMenu();
            return;
        }
        switch (act)
        {
            case GameAction.Left:      heldL = true; lastDir = -1; g.Move(-1); break;
            case GameAction.Right:     heldR = true; lastDir = 1;  g.Move(1);  break;
            case GameAction.SoftDrop:  soft = true;   break;
            case GameAction.HardDrop:  g.HardDrop();  break;
            case GameAction.RotateCw:  g.Rotate(true);  break;
            case GameAction.RotateCcw: g.Rotate(false); break;
            case GameAction.Hold:      g.DoHold();    break;
            case GameAction.Pause:     Paused = true; heldL = heldR = soft = false; break;
        }
        // Catch game-overs triggered directly by player input (e.g. HardDrop lock-out)
        if (g.GameOver && goAt == DateTime.MaxValue) goAt = DateTime.UtcNow;
    }
}
