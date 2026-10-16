using System.Text.Json;

namespace TetNET.Game;

public enum GameAction { Left, Right, SoftDrop, HardDrop, RotateCw, RotateCcw, Hold, Pause }

public sealed class Bindings
{
    public Dictionary<GameAction, List<string>> Map = Defaults();

    public static Dictionary<GameAction, List<string>> Defaults() => new()
    {
        [GameAction.Left] = new() { "ArrowLeft" },
        [GameAction.Right] = new() { "ArrowRight" },
        [GameAction.SoftDrop] = new() { "ArrowDown" },
        [GameAction.HardDrop] = new() { "Space" },
        [GameAction.RotateCw] = new() { "ArrowUp", "KeyX" },
        [GameAction.RotateCcw] = new() { "KeyZ" },
        [GameAction.Hold] = new() { "KeyC", "ShiftLeft" },
        [GameAction.Pause] = new() { "Escape" },
    };

    public void Reset() { Map = Defaults(); }

    public GameAction? Find(string code)
    {
        foreach (var kv in Map) if (kv.Value.Contains(code)) return kv.Key;
        return null;
    }

    public void Set(GameAction a, string code)
    {
        foreach (var l in Map.Values) l.Remove(code);
        Map[a] = new() { code };
    }

    public string Json() => JsonSerializer.Serialize(Map.ToDictionary(k => k.Key.ToString(), k => k.Value));

    public void Load(string json)
    {
        var d = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
        if (d == null) return;
        var m = Defaults();
        foreach (var kv in d)
            if (Enum.TryParse<GameAction>(kv.Key, out var a)) m[a] = kv.Value;
        Map = m;
    }

    public static string Label(string c) => c switch
    {
        "ArrowLeft" => "LEFT", "ArrowRight" => "RIGHT", "ArrowUp" => "UP", "ArrowDown" => "DOWN",
        "Space" => "SPACE", "Escape" => "ESC", "Enter" => "ENTER", "Backspace" => "BKSP", "Tab" => "TAB",
        "ShiftLeft" or "ShiftRight" => "SHIFT", "ControlLeft" or "ControlRight" => "CTRL",
        _ when c.StartsWith("Key") => c[3..],
        _ when c.StartsWith("Digit") => c[5..],
        _ when c.StartsWith("Numpad") => "N" + c[6..],
        _ => c.ToUpperInvariant()
    };

    static string ShortLabel(string c) => c switch
    {
        "ArrowLeft" => "\u2190", "ArrowRight" => "\u2192", "ArrowUp" => "\u2191", "ArrowDown" => "\u2193",
        _ => Label(c)
    };

    public string Text(GameAction a)
    {
        var t = Map[a].Count == 0 ? "NONE" : string.Join(" / ", Map[a].Select(Label));
        return t.Length > 15 ? t[..15] : t;
    }

    public string Short(GameAction a) => Map[a].Count == 0 ? "?" : ShortLabel(Map[a][0]);
}
