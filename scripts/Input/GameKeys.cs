namespace Trinity.Eclipse;

// Keys kept from the game. A key goes back once no owner wants it.
internal static class GameKeys
{
    // The keys flight and the super run use. None of them can be bound to anything else.
    public static readonly Keys[] FlightKeys =
    {
        Keys.W, Keys.A, Keys.S, Keys.D, Keys.Space, Keys.ControlKey, Keys.ShiftKey,
    };

    // Taken while flying or running. Movement keys stay with the game so a run out of flight works.
    public static readonly Keys[] ButtonKeys = { Keys.Space, Keys.ControlKey, Keys.ShiftKey };

    public static readonly Keys[] MenuKeys =
    {
        Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Enter, Keys.Back, Keys.Tab, Keys.PageUp, Keys.PageDown, Keys.Delete, Keys.Home, Keys.End,
    };

    private static readonly Dictionary<string, Keys[]> _held = new(StringComparer.Ordinal);
    private static readonly HashSet<Keys> _keys = new();

    public static void Hold(string owner, Keys[] keys)
    {
        _held[owner] = keys;
        Apply();
    }

    public static void Release(string owner)
    {
        if (_held.Remove(owner))
            Apply();
    }

    public static void ReleaseAll()
    {
        _held.Clear();
        Apply();
    }

    // The game can take a key back on its own, so they're taken again now and then.
    public static void Reassert()
    {
        foreach (Keys key in _keys)
        {
            if (!Keyboard.IsSwallowed(key))
                Keyboard.Swallow(key);
        }
    }

    private static void Apply()
    {
        var keys = new HashSet<Keys>();
        foreach (Keys[] k in _held.Values)
            keys.UnionWith(k);

        foreach (Keys key in _keys)
        {
            if (!keys.Contains(key))
                Keyboard.Unswallow(key);
        }

        foreach (Keys key in keys)
            Keyboard.Swallow(key);

        _keys.Clear();
        _keys.UnionWith(keys);
    }
}
