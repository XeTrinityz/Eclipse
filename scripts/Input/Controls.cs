using System.Numerics;

namespace Trinity.Eclipse;

internal enum Act
{
    Swap,
    Fly,
    Heat,
    HeatAir,
    Punch,
    Grab,
    Boost,
    Burst,
    Drop,
    Dash,
    Roll,
    Run,
    Skid,
    Lightning,
    Blitz,
    Tornado,
    Flurry,
    Slow,
    Rewind,
}

// Every control the mod reads, by what it does rather than which key or button it's on. Presses are counted
// as they happen, so whatever reads them later never misses one or sees one twice.
//
// While a hero is on, Batman's gadgets are off (NoGadgets), which frees RB, the triggers and the d-pad on foot
// for the hero's powers. A quick tap of MENU still pauses, through the game's own command, since the game never
// sees that button while Eclipse holds it for the menu.
internal static class Controls
{
    private const float MenuHoldSeconds = 0.4f;
    private const float Deadzone = 0.24f;

    private const string FootHold = "foot pad", StartHold = "menu button", FlightHold = "flight pad", RunHold = "run pad";
    private const string FootKeys = "foot keys", RunKeys = "run keys";

    private static readonly string[] StartName = { "XboxTypeS_Start" };

    // Everything but the right stick, which still turns the camera, and MENU and VIEW, which stay the game's.
    private static readonly string[] FlightNames =
    {
        "XboxTypeS_A", "XboxTypeS_B", "XboxTypeS_X", "XboxTypeS_Y",
        "XboxTypeS_LeftShoulder", "XboxTypeS_RightShoulder", "XboxTypeS_LeftTrigger", "XboxTypeS_RightTrigger",
        "XboxTypeS_LeftThumbstick", "XboxTypeS_RightThumbstick",
        "XboxTypeS_DPad_Up", "XboxTypeS_DPad_Down", "XboxTypeS_DPad_Left", "XboxTypeS_DPad_Right",
        "XboxTypeS_LeftX", "XboxTypeS_LeftY", "XboxTypeS_LeftTriggerAxis", "XboxTypeS_RightTriggerAxis",
    };

    private static readonly int Count = Enum.GetValues<Act>().Length;
    private static readonly int[] _presses = new int[Count];
    private static double _padAt = -1.0, _keysAt;

    private static float _startHeld = -1f;
    private static bool _startWait;

    // The last thing used was the pad, so prompts show pad buttons.
    public static bool PadActive => Pad.Connected && _padAt > _keysAt;

    // The hero's controls count only while nothing else has the input.
    public static bool Listening => World.InGame && !World.Paused && Shared.ScreenFree && !Menu.IsOpen && Native.GameInFront();

    public static Control? Of(Act a) => Opt.Binds.TryGetValue(Heroes.Active, out var map) && map.TryGetValue(a, out Control? c) ? c : null;

    public static Keys Key(Act a) => Of(a) is { KeyFixed: false } c ? c.Key : Keys.None;

    public static PadButton Button(Act a) => Of(a)?.Button ?? PadButton.None;

    // Not in the Batmobile, a cutscene or knocked down.
    private static bool OnFootOrFlying =>
        World.Pawn is BmSDK.BmGame.RPawnPlayer { Health: > 0, bHidden: false } p && p.DrivenVehicle is not { IsValid: true }
        && World.Pc is { } pc && !Hooks.InCinematic(pc);

    private static bool PowersUsable => Opt.Powers.On && Pad.Connected && Listening && OnFootOrFlying;

    // Whether a button used this way counts right now.
    private static bool Counts(Use use) => use switch
    {
        Use.Foot => !Suit.I.Flying,
        Use.Any => true,
        Use.Flight => Suit.I.Flying,
        _ => Suit.I.Running,
    };

    public static void KeyboardUsed() => _keysAt = Clock.Now;

    public static void KeyPressed(Keys key)
    {
        KeyboardUsed();
        if (!Listening || !Opt.Powers.On || !Opt.Binds.TryGetValue(Heroes.Active, out var map))
            return;

        foreach ((Act act, Control c) in map)
        {
            if (!c.KeyFixed && c.Key == key)
                _presses[(int)act]++;
        }
    }

    // A press since the last time this was asked. Each control has one reader.
    public static bool Take(Act a)
    {
        if (_presses[(int)a] == 0)
            return false;
        _presses[(int)a] = 0;
        return true;
    }

    // Forgets presses from before now, like the one that started the flight.
    public static void Clear(Act a) => _presses[(int)a] = 0;

    // Held on its key, or on its pad button where that button counts right now.
    public static bool Down(Act a)
    {
        if (!Listening || Of(a) is not { } c)
            return false;
        if (!c.KeyFixed && GameKeys.Down(c.Key))
            return true;
        return Pad.Down(c.Button) && Counts(c.Use);
    }

    public static Vector2 LeftStick => Listening ? Shape(Pad.LeftRaw) : Vector2.Zero;

    public static float RightTrigger => Listening ? Pad.RightTrigger : 0f;

    public static float LeftTrigger => Listening ? Pad.LeftTrigger : 0f;

    // A round deadzone, so diagonals stay smooth, with the rest of the throw stretched back to full.
    private static Vector2 Shape(Vector2 raw)
    {
        float size = raw.Length();
        if (size <= Deadzone)
            return Vector2.Zero;

        float t = Math.Min(1f, (size - Deadzone) / (1f - Deadzone));
        return raw / size * t;
    }

    public static void Frame(float dt)
    {
        Pad.Poll();
        if (Pad.Busy)
            _padAt = Clock.Now;

        GameKeys.Frame();
        StartButton(dt);
        HoldsChanged();

        if (!Listening || !Opt.Powers.On || !Opt.Binds.TryGetValue(Heroes.Active, out var map))
            return;

        foreach (PadButton pressed in Pad.NewPresses())
        {
            foreach ((Act act, Control c) in map)
            {
                if (c.Button == pressed && Counts(c.Use))
                    _presses[(int)act]++;
            }
        }
    }

    // Hold MENU to open Eclipse. A tap is the game's pause.
    private static void StartButton(float dt)
    {
        bool on = Opt.PadMenu.Index == 0 && Pad.Connected;
        bool down = on && Pad.Down(PadButton.Start);
        if (Menu.IsOpen)
        {
            if (on && Pad.Pressed(PadButton.Start) && !_startWait)
            {
                Menu.Close();
                _startWait = true;
            }

            _startHeld = -1f;
            if (!down)
                _startWait = false;
            return;
        }

        if (!down)
        {
            if (_startHeld >= 0f && !_startWait && StartOurs)
                Safe.Run("pause", () => World.Pc?.UserPause());
            _startHeld = -1f;
            _startWait = false;
            return;
        }

        if (_startWait || !StartOurs)
        {
            _startHeld = -1f;
            return;
        }

        _startHeld = _startHeld < 0f ? 0f : _startHeld + dt;
        if (_startHeld < MenuHoldSeconds)
            return;

        _startHeld = -1f;
        _startWait = true;
        _padAt = Clock.Now;
        Menu.Open();
    }

    private static bool StartOurs => Opt.PadMenu.Index == 0 && Pad.Connected && World.InGame && !World.Paused && Shared.ScreenFree;

    // Which pad inputs the game can't see right now. Runs every draw, and when a setting changes.
    public static void HoldsChanged()
    {
        bool menu = Menu.IsOpen;
        GameKeys.Hold(StartHold, (StartOurs || menu) && Opt.PadMenu.Index == 0, Array.Empty<Keys>(), StartName);
        GameKeys.Hold(FootHold, PowersUsable && !Suit.I.Flying, Array.Empty<Keys>(), Names(Use.Foot, Use.Any));
        GameKeys.Hold(FlightHold, Pad.Connected && Suit.I.Flying && !menu, Array.Empty<Keys>(), FlightNames);
        GameKeys.Hold(RunHold, Pad.Connected && Suit.I.Running && !menu, Array.Empty<Keys>(), Names(Use.Run, Use.Run));

        // The hero's own keys too, or a power key that's also one of Batman's (Q and middle mouse are his cape stun)
        // does both.
        bool keys = Opt.Powers.On && Listening && OnFootOrFlying;
        GameKeys.Hold(FootKeys, keys && !Suit.I.Flying, KeysOf(Use.Foot, Use.Any), Array.Empty<string>());
        GameKeys.Hold(RunKeys, keys && Suit.I.Running, KeysOf(Use.Run, Use.Run), Array.Empty<string>());
    }

    private static Keys[] KeysOf(Use a, Use b)
    {
        if (!Opt.Binds.TryGetValue(Heroes.Active, out var map))
            return Array.Empty<Keys>();
        return map.Values.Where(c => (c.Use == a || c.Use == b) && !c.KeyFixed && c.Key != Keys.None)
            .Select(c => c.Key).Distinct().ToArray();
    }

    // The buttons the hero has on these uses, by the names the game's binds use.
    private static string[] Names(Use a, Use b)
    {
        if (!Opt.Binds.TryGetValue(Heroes.Active, out var map))
            return Array.Empty<string>();
        return map.Values.Where(c => (c.Use == a || c.Use == b) && c.Button != PadButton.None)
            .Select(c => GameKeys.PadName(c.Button)).Distinct().ToArray();
    }

    public static void Shutdown()
    {
        GameKeys.Release(StartHold);
        GameKeys.Release(FootHold);
        GameKeys.Release(FlightHold);
        GameKeys.Release(RunHold);
        GameKeys.Release(FootKeys);
        GameKeys.Release(RunKeys);
        _startHeld = -1f;
    }
}
