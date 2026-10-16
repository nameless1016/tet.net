namespace TetNET.Game;

public sealed class Piece
{
    public int Type, Rot, X = 3, Y = 1;
    public Piece(int type) { Type = type; }
}

/// <summary>Pure game logic: SRS rotation, 7-bag, hold, ghost, T-spin, B2B, combo.
/// Types: 0=I 1=O 2=T 3=S 4=Z 5=J 6=L. Grid is 10 x 22 (top 2 rows hidden).</summary>
public sealed class Engine
{
    public const int W = 10, H = 22, Hidden = 2;

    static readonly (int x, int y)[][] Base =
    {
        new[] { (0,1),(1,1),(2,1),(3,1) },
        new[] { (1,0),(2,0),(1,1),(2,1) },
        new[] { (1,0),(0,1),(1,1),(2,1) },
        new[] { (1,0),(2,0),(0,1),(1,1) },
        new[] { (0,0),(1,0),(1,1),(2,1) },
        new[] { (0,0),(0,1),(1,1),(2,1) },
        new[] { (2,0),(0,1),(1,1),(2,1) },
    };
    static readonly int[] Size = { 4, 4, 3, 3, 3, 3, 3 };
    static readonly (int x, int y)[][][] Shapes = BuildShapes();

    static (int x, int y)[][][] BuildShapes()
    {
        var all = new (int x, int y)[7][][];
        for (int t = 0; t < 7; t++)
        {
            all[t] = new (int x, int y)[4][];
            var cur = Base[t];
            int n = Size[t];
            for (int r = 0; r < 4; r++)
            {
                all[t][r] = cur;
                cur = cur.Select(c => (x: n - 1 - c.y, y: c.x)).ToArray();
            }
        }
        return all;
    }
    public static (int x, int y)[] Shape(int t, int r) => Shapes[t][r];

    // SRS kicks, key = from*4+to, values are (dx, dy) with y pointing UP (flipped when applied).
    static readonly Dictionary<int, (int x, int y)[]> KickJ = new()
    {
        [1]  = new[] { (0,0),(-1,0),(-1,1),(0,-2),(-1,-2) },
        [4]  = new[] { (0,0),(1,0),(1,-1),(0,2),(1,2) },
        [6]  = new[] { (0,0),(1,0),(1,-1),(0,2),(1,2) },
        [9]  = new[] { (0,0),(-1,0),(-1,1),(0,-2),(-1,-2) },
        [11] = new[] { (0,0),(1,0),(1,1),(0,-2),(1,-2) },
        [14] = new[] { (0,0),(-1,0),(-1,-1),(0,2),(-1,2) },
        [12] = new[] { (0,0),(-1,0),(-1,-1),(0,2),(-1,2) },
        [3]  = new[] { (0,0),(1,0),(1,1),(0,-2),(1,-2) },
    };
    static readonly Dictionary<int, (int x, int y)[]> KickI = new()
    {
        [1]  = new[] { (0,0),(-2,0),(1,0),(-2,-1),(1,2) },
        [4]  = new[] { (0,0),(2,0),(-1,0),(2,1),(-1,-2) },
        [6]  = new[] { (0,0),(-1,0),(2,0),(-1,2),(2,-1) },
        [9]  = new[] { (0,0),(1,0),(-2,0),(1,-2),(-2,1) },
        [11] = new[] { (0,0),(2,0),(-1,0),(2,1),(-1,-2) },
        [14] = new[] { (0,0),(-2,0),(1,0),(-2,-1),(1,2) },
        [12] = new[] { (0,0),(1,0),(-2,0),(1,-2),(-2,1) },
        [3]  = new[] { (0,0),(-1,0),(2,0),(-1,2),(2,-1) },
    };

    public readonly int[,] Grid = new int[H, W];
    public Piece Cur = null!;
    public int Hold = -1;
    public bool HoldUsed;
    public readonly Queue<int> Next = new();
    public int Score, Lines, Level = 1, Combo = -1;
    public bool B2B, GameOver;
    public string Toast = "", LastClear = "";
    public double ToastT;

    double dropAcc, lockT;
    int resets, lowest;
    bool lastRot;

    public Engine() { Spawn(TakeNext()); }

    int TakeNext()
    {
        while (Next.Count < 7)
            foreach (var b in Enumerable.Range(0, 7).OrderBy(_ => Random.Shared.Next()))
                Next.Enqueue(b);
        return Next.Dequeue();
    }

    void Spawn(int t)
    {
        Cur = new Piece(t);
        lastRot = false; lockT = 0; dropAcc = 0; resets = 0; lowest = Cur.Y;
        if (Hit(Cur.Type, Cur.Rot, Cur.X, Cur.Y)) GameOver = true;
    }

    bool Hit(int t, int r, int x, int y)
    {
        foreach (var c in Shapes[t][r])
        {
            int cx = c.x + x, cy = c.y + y;
            if (cx < 0 || cx >= W || cy < 0 || cy >= H) return true;
            if (Grid[cy, cx] != 0) return true;
        }
        return false;
    }

    bool Filled(int x, int y) => x < 0 || x >= W || y >= H || (y >= 0 && Grid[y, x] != 0);
    bool Grounded() => Hit(Cur.Type, Cur.Rot, Cur.X, Cur.Y + 1);

    public int GhostY()
    {
        int y = Cur.Y;
        while (!Hit(Cur.Type, Cur.Rot, Cur.X, y + 1)) y++;
        return y;
    }

    void Touch() { if (Grounded() && resets < 15) { resets++; lockT = 0; } }

    public bool Move(int dx)
    {
        if (GameOver || Hit(Cur.Type, Cur.Rot, Cur.X + dx, Cur.Y)) return false;
        Cur.X += dx; lastRot = false; Touch();
        return true;
    }

    public void Rotate(bool cw)
    {
        if (GameOver || Cur.Type == 1) return;
        int from = Cur.Rot, to = (from + (cw ? 1 : 3)) % 4;
        var kicks = (Cur.Type == 0 ? KickI : KickJ)[from * 4 + to];
        foreach (var k in kicks)
        {
            int nx = Cur.X + k.x, ny = Cur.Y - k.y;
            if (!Hit(Cur.Type, to, nx, ny))
            {
                Cur.Rot = to; Cur.X = nx; Cur.Y = ny; lastRot = true;
                Touch();
                return;
            }
        }
    }

    public void DoHold()
    {
        if (GameOver || HoldUsed) return;
        int t = Cur.Type;
        if (Hold < 0) { Hold = t; Spawn(TakeNext()); }
        else { int h = Hold; Hold = t; Spawn(h); }
        HoldUsed = true;
    }

    public void HardDrop()
    {
        if (GameOver) return;
        int n = 0;
        while (!Hit(Cur.Type, Cur.Rot, Cur.X, Cur.Y + 1)) { Cur.Y++; n++; }
        if (n > 0) lastRot = false;
        Score += 2 * n;
        Lock();
    }

    double GravityInterval()
    {
        int l = Math.Min(Level, 20);
        return Math.Max(0.02, Math.Pow(0.8 - (l - 1) * 0.007, l - 1));
    }

    public void Tick(double dt, bool soft)
    {
        if (GameOver) return;
        if (ToastT > 0) ToastT -= dt;
        double gi = GravityInterval();
        if (soft) gi = Math.Min(gi, 0.04);

        if (Grounded())
        {
            dropAcc = 0;
            lockT += dt;
            if (lockT >= 0.5) Lock();
            return;
        }
        lockT = 0;
        dropAcc += dt;
        while (dropAcc >= gi)
        {
            dropAcc -= gi;
            if (Hit(Cur.Type, Cur.Rot, Cur.X, Cur.Y + 1)) break;
            Cur.Y++; lastRot = false;
            if (soft) Score++;
            if (Cur.Y > lowest) { lowest = Cur.Y; resets = 0; }
        }
    }

    void Lock()
    {
        bool tspin = false;
        if (Cur.Type == 2 && lastRot)
        {
            int corners = 0;
            foreach (var d in new[] { (0, 0), (2, 0), (0, 2), (2, 2) })
                if (Filled(Cur.X + d.Item1, Cur.Y + d.Item2)) corners++;
            tspin = corners >= 3;
        }

        bool lockOut = true;
        foreach (var c in Shapes[Cur.Type][Cur.Rot])
        {
            int x = c.x + Cur.X, y = c.y + Cur.Y;
            Grid[y, x] = Cur.Type + 1;
            if (y >= Hidden) lockOut = false;
        }

        int n = 0;
        for (int y = H - 1; y >= 0; y--)
        {
            bool full = true;
            for (int x = 0; x < W; x++) if (Grid[y, x] == 0) { full = false; break; }
            if (!full) continue;
            n++;
            for (int yy = y; yy > 0; yy--)
                for (int x = 0; x < W; x++) Grid[yy, x] = Grid[yy - 1, x];
            for (int x = 0; x < W; x++) Grid[0, x] = 0;
            y++; // re-check this row
        }

        if (n > 0)
        {
            int pts; string name;
            if (tspin)
            {
                pts = n switch { 1 => 800, 2 => 1200, _ => 1600 };
                name = "T-SPIN " + n;
            }
            else
            {
                pts = n switch { 1 => 100, 2 => 300, 3 => 500, _ => 800 };
                name = n switch { 1 => "SINGLE", 2 => "DOUBLE", 3 => "TRIPLE", _ => "TETRIS" };
            }
            bool difficult = tspin || n == 4;
            if (difficult && B2B) pts = pts * 3 / 2;
            B2B = difficult;
            Combo++;
            pts *= Level;
            Score += pts + (Combo > 0 ? 50 * Combo * Level : 0);
            Lines += n;
            Level = Lines / 10 + 1;
            Toast = $"{name} +{pts}"; ToastT = 1.4;
            LastClear = name;
        }
        else Combo = -1;

        HoldUsed = false;
        if (lockOut) { GameOver = true; return; }
        Spawn(TakeNext());
    }
}
