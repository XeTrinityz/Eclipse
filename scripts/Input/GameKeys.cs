namespace Trinity.Eclipse;

// Keeps keys and pad inputs from the game while the mod uses them. Each part of the mod holds its own set
// by name, and the game gets everything back the moment nothing holds it.
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

    // Every button and axis of the pad, by the name the game's binds use. MENU is left out so pause still works.
    public static readonly string[] PadNames =
    {
        "XboxTypeS_A", "XboxTypeS_B", "XboxTypeS_X", "XboxTypeS_Y",
        "XboxTypeS_LeftShoulder", "XboxTypeS_RightShoulder", "XboxTypeS_LeftTrigger", "XboxTypeS_RightTrigger",
        "XboxTypeS_LeftThumbstick", "XboxTypeS_RightThumbstick", "XboxTypeS_Back",
        "XboxTypeS_DPad_Up", "XboxTypeS_DPad_Down", "XboxTypeS_DPad_Left", "XboxTypeS_DPad_Right",
        "XboxTypeS_LeftX", "XboxTypeS_LeftY", "XboxTypeS_RightX", "XboxTypeS_RightY",
        "XboxTypeS_LeftTriggerAxis", "XboxTypeS_RightTriggerAxis",
    };

    private static readonly Dictionary<string, (Keys[] Keys, string[] Names)> _held = new(StringComparer.Ordinal);
    private static readonly HashSet<Keys> _keys = new();
    private static readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private static readonly HashSet<Keys> _stale = new();
    private static bool _idle;
    private static Every _check;

    public static string PadName(PadButton b) => b switch
    {
        PadButton.A => "XboxTypeS_A",
        PadButton.B => "XboxTypeS_B",
        PadButton.X => "XboxTypeS_X",
        PadButton.Y => "XboxTypeS_Y",
        PadButton.LB => "XboxTypeS_LeftShoulder",
        PadButton.RB => "XboxTypeS_RightShoulder",
        PadButton.LT => "XboxTypeS_LeftTrigger",
        PadButton.RT => "XboxTypeS_RightTrigger",
        PadButton.LS => "XboxTypeS_LeftThumbstick",
        PadButton.RS => "XboxTypeS_RightThumbstick",
        PadButton.Back => "XboxTypeS_Back",
        PadButton.Start => "XboxTypeS_Start",
        PadButton.Up => "XboxTypeS_DPad_Up",
        PadButton.Down => "XboxTypeS_DPad_Down",
        PadButton.Left => "XboxTypeS_DPad_Left",
        _ => "XboxTypeS_DPad_Right",
    };

    public static void Hold(string owner, Keys[] keys) => Hold(owner, keys, Array.Empty<string>());

    public static void Hold(string owner, Keys[] keys, string[] names)
    {
        _held[owner] = (keys, names);
        Apply();
    }

    // Holds or lets go, and only touches the game when that changes.
    public static void Hold(string owner, bool on, Keys[] keys, string[] names)
    {
        if (!on)
        {
            Release(owner);
            return;
        }

        if (!_held.TryGetValue(owner, out var had) || !had.Keys.SequenceEqual(keys) || !had.Names.SequenceEqual(names))
            Hold(owner, keys, names);
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

    // A key that was pressed in the pause menu belongs to the menu until it's let go.
    public static bool Down(Keys key) => key != Keys.None && !_idle && Keyboard.IsDown(key) && !_stale.Contains(key);

    // Runs every draw, so it keeps going behind the pause menu.
    public static void Frame()
    {
        if (_stale.Count > 0)
            _stale.RemoveWhere(static key => !Keyboard.IsDown(key));

        // The pause menu and the map need the mouse, the keys and the pad. Nothing is kept from the game there.
        bool idle = World.Paused || !World.InGame;
        if (idle != _idle)
        {
            _idle = idle;
            Apply();
            if (!idle)
            {
                foreach (Keys key in _keys)
                {
                    if (Keyboard.IsDown(key))
                        _stale.Add(key);
                }
            }
        }
        else if (!idle && _check.Elapsed(0.5))
        {
            Reassert();
        }
    }

    // The game, or another mod letting go of all its keys, can take a key back, so they're taken again now and then.
    public static void Reassert()
    {
        foreach (Keys key in _keys)
        {
            if (!Keyboard.IsSwallowed(key))
                Keyboard.Swallow(key);
        }

        foreach (string name in _names)
        {
            if (!Keyboard.IsSwallowed(name))
                Keyboard.Swallow(name);
        }
    }

    private static void Apply()
    {
        var keys = new HashSet<Keys>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (!_idle)
        {
            foreach ((Keys[] k, string[] n) in _held.Values)
            {
                keys.UnionWith(k.Where(x => x != Keys.None));
                names.UnionWith(n);
            }
        }

        foreach (Keys key in _keys)
        {
            if (!keys.Contains(key))
                Keyboard.Unswallow(key);
        }

        foreach (string name in _names)
        {
            if (!names.Contains(name))
                Keyboard.Unswallow(name);
        }

        foreach (Keys key in keys)
            Keyboard.Swallow(key);
        foreach (string name in names)
            Keyboard.Swallow(name);

        _keys.Clear();
        _keys.UnionWith(keys);
        _names.Clear();
        _names.UnionWith(names);
    }
}
