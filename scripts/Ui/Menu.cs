namespace Trinity.Eclipse;

internal enum Nav
{
    Up,
    Down,
    Left,
    Right,
    Accept,
    Back,
    Alt,
    Extra,
    PagePrev,
    PageNext,
}

// The Eclipse menu. It opens on the hero screen, where you pick who Batman plays as, and each hero has his own
// pages drawn in his own look. Keys and the pad both become the same moves through it.
internal static class Menu
{
    public const float RepeatDelay = 0.38f;
    public const float RepeatRate = 0.06f;
    private const float ConfirmSeconds = 6f;

    private static readonly Keys[] RepeatKeys = { Keys.Up, Keys.Down, Keys.Left, Keys.Right };
    private static readonly Nav[] RepeatNav = { Nav.Up, Nav.Down, Nav.Left, Nav.Right };
    private static readonly PadButton[] RepeatPad = { PadButton.Up, PadButton.Down, PadButton.Left, PadButton.Right };
    private static readonly float[] _keyHeld = { -1f, -1f, -1f, -1f };
    private static readonly float[] _padHeld = { -1f, -1f, -1f, -1f };
    private static readonly Dictionary<Page, int> _rowMemory = new();
    private static readonly Dictionary<Hero, int> _pageMemory = new();
    private static nint _inputPc;
    private static float _confirmLeft;

    public static bool IsOpen { get; private set; }

    // On the hero screen, or in a hero's pages.
    public static bool Roster { get; private set; } = true;

    // The hero the roster points at, or whose pages are open.
    public static Hero Hero { get; private set; }

    public static int PageIndex { get; private set; }

    public static int RowIndex { get; private set; }

    public static Option? Binding { get; private set; }

    public static string BindNote { get; private set; } = "";

    public static Command? Confirming { get; private set; }

    public static float ConfirmLeft => Math.Clamp(_confirmLeft / ConfirmSeconds, 0f, 1f);

    public static Option? Flashed { get; private set; }

    public static double FlashAt { get; private set; }

    public static double OpenedAt { get; private set; }

    public static double PageChangedAt { get; private set; }

    public static double ScreenChangedAt { get; private set; }

    public static double HeroChangedAt { get; private set; }

    public static int Moved { get; private set; }

    public static List<Page> Pages => Heroes.Pages(Hero);

    public static Page Current => Pages[Math.Clamp(PageIndex, 0, Pages.Count - 1)];

    public static Option? Selected => Current.Options.Count == 0 ? null : Current.Options[Math.Clamp(RowIndex, 0, Current.Options.Count - 1)];

    public static void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    public static void Open()
    {
        if (IsOpen || !World.InGame || World.Paused)
            return;
        if (!Shared.ScreenFree)
        {
            Readout.Toast("ECLIPSE", "ANOTHER MENU IS OPEN", false);
            return;
        }

        IsOpen = true;
        Roster = true;
        Hero = Heroes.Active;
        Binding = null;
        Confirming = null;
        BindNote = "";
        OpenedAt = ScreenChangedAt = HeroChangedAt = PageChangedAt = Gfx.Time;
        Array.Fill(_keyHeld, -1f);
        Array.Fill(_padHeld, -1f);
        Shared.MenuOwner = Shared.OwnerName;
        HoldInput();
    }

    public static void Close()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        if (!Roster)
        {
            _rowMemory[Current] = RowIndex;
            _pageMemory[Hero] = PageIndex;
        }

        Binding = null;
        Confirming = null;
        if (Shared.MenuOwner == Shared.OwnerName)
            Shared.MenuOwner = null;
        ReleaseInput();
    }

    private static void HoldInput()
    {
        Safe.Run("menu movement", () =>
        {
            if (World.Pc is { } pc)
            {
                pc.IgnoreMoveInput(true);
                _inputPc = pc.Ptr;
            }
        });
        Safe.Run("menu keys", () => GameKeys.Hold("menu", GameKeys.MenuKeys, GameKeys.PadNames));
    }

    private static void ReleaseInput()
    {
        Safe.Run("menu keys", () => GameKeys.Release("menu"));
        Safe.Run("menu movement", () =>
        {
            if (_inputPc != 0 && World.Pc is { } pc && pc.Ptr == _inputPc)
                pc.IgnoreMoveInput(false);
            _inputPc = 0;
        });
    }

    public static void Tick(float dt)
    {
        if (!IsOpen)
            return;

        if (Shared.MenuOwner != Shared.OwnerName || !World.InGame)
        {
            Close();
            return;
        }

        if (Confirming != null)
        {
            _confirmLeft -= dt;
            if (_confirmLeft <= 0f)
                Confirming = null;
        }

        if (Binding != null)
        {
            PadBind();
            return;
        }

        PadFrame();
        if (!IsOpen)
            return;

        for (int i = 0; i < RepeatKeys.Length; i++)
            Repeat(_keyHeld, i, Native.GameInFront() && Keyboard.IsDown(RepeatKeys[i]), dt, false);
    }

    private static void PadFrame()
    {
        if (!Pad.Connected)
            return;

        if (Pad.Pressed(PadButton.A))
            Do(Nav.Accept);
        else if (Pad.Pressed(PadButton.B))
            Do(Nav.Back);
        else if (Pad.Pressed(PadButton.X))
            Do(Nav.Alt);
        else if (Pad.Pressed(PadButton.Y))
            Do(Nav.Extra);
        else if (Pad.Pressed(PadButton.LB))
            Do(Nav.PagePrev);
        else if (Pad.Pressed(PadButton.RB))
            Do(Nav.PageNext);

        for (int i = 0; i < RepeatPad.Length && IsOpen; i++)
        {
            bool down = Pad.MenuDown(RepeatPad[i]);
            if (down && _padHeld[i] < 0f)
            {
                _padHeld[i] = 0f;
                Do(RepeatNav[i]);
                continue;
            }

            Repeat(_padHeld, i, down, Gfx.Dt, true);
        }
    }

    private static void Repeat(float[] held, int i, bool down, float dt, bool pad)
    {
        if (!down)
        {
            held[i] = -1f;
            return;
        }

        if (held[i] < 0f)
        {
            // A key's first press comes through OnKeyDown, so only the repeats are sent from here.
            held[i] = 0f;
            return;
        }

        held[i] += dt;
        if (held[i] < RepeatDelay)
            return;

        held[i] -= RepeatRate;
        Do(RepeatNav[i], repeat: true);
    }

    // A key press while the menu is open, from OnKeyDown.
    public static void Press(Keys key)
    {
        if (!IsOpen)
            return;

        if (Binding != null)
        {
            KeyBind(key);
            return;
        }

        if (key == Opt.MenuKey.Key)
        {
            Close();
            return;
        }

        Nav? nav = key switch
        {
            Keys.Up => Nav.Up,
            Keys.Down => Nav.Down,
            Keys.Left => Nav.Left,
            Keys.Right => Nav.Right,
            Keys.Enter => Nav.Accept,
            Keys.Back => Nav.Back,
            Keys.Delete => Nav.Alt,
            Keys.Tab => Nav.Extra,
            Keys.PageUp => Nav.PagePrev,
            Keys.PageDown => Nav.PageNext,
            _ => null,
        };
        if (nav is { } n)
            Do(n);
    }

    private static void Do(Nav nav, bool repeat = false)
    {
        if (nav != Nav.Accept)
            Confirming = null;

        if (Roster)
            RosterDo(nav, repeat);
        else
            PagesDo(nav, repeat);
    }

    private static void RosterDo(Nav nav, bool repeat)
    {
        switch (nav)
        {
            case Nav.Left:
            case Nav.Up:
            case Nav.PagePrev:
                PointAt(-1);
                break;
            case Nav.Right:
            case Nav.Down:
            case Nav.PageNext:
                PointAt(1);
                break;
            case Nav.Accept when !repeat:
                Heroes.Become(Hero);
                Close();
                break;
            case Nav.Extra when !repeat:
                OpenPages(Hero);
                break;
            case Nav.Alt when !repeat:
                Opt.Powers.On = !Opt.Powers.On;
                Changed(Opt.Powers);
                Readout.Toast("ECLIPSE", Opt.Powers.On ? "POWERS ON" : "PLAIN BATMAN", Opt.Powers.On);
                break;
            case Nav.Back when !repeat:
                Close();
                break;
        }
    }

    private static void PointAt(int dir)
    {
        int n = Heroes.All.Length;
        Hero = Heroes.All[((int)Hero + dir + n) % n];
        HeroChangedAt = Gfx.Time;
        Moved = dir;
    }

    private static void OpenPages(Hero hero)
    {
        Roster = false;
        Hero = hero;
        PageIndex = _pageMemory.TryGetValue(hero, out int page) ? Math.Clamp(page, 0, Pages.Count - 1) : 0;
        RowIndex = RowFor(Current);
        ScreenChangedAt = PageChangedAt = Gfx.Time;
    }

    private static void ToRoster()
    {
        _rowMemory[Current] = RowIndex;
        _pageMemory[Hero] = PageIndex;
        Roster = true;
        ScreenChangedAt = HeroChangedAt = Gfx.Time;
    }

    private static int RowFor(Page page) => _rowMemory.TryGetValue(page, out int row) ? Math.Clamp(row, 0, Math.Max(0, page.Options.Count - 1)) : 0;

    private static void PagesDo(Nav nav, bool repeat)
    {
        switch (nav)
        {
            case Nav.Up:
                MoveRow(-1);
                break;
            case Nav.Down:
                MoveRow(1);
                break;
            case Nav.Left:
                if (Selected is { } l && l.Step(-1))
                    Changed(l);
                break;
            case Nav.Right:
                if (Selected is { } r && r.Step(1))
                    Changed(r);
                break;
            case Nav.PagePrev:
                TurnPage(-1);
                break;
            case Nav.PageNext:
                TurnPage(1);
                break;
            case Nav.Accept when !repeat:
                if (Selected is { } o)
                    Activate(o);
                break;
            case Nav.Alt when !repeat:
                if (Selected is { } d && d is not Command && !d.IsDefault)
                {
                    d.Reset();
                    Changed(d);
                }

                break;
            case Nav.Extra when !repeat:
                ToRoster();
                break;
            case Nav.Back when !repeat:
                Close();
                break;
        }
    }

    private static void TurnPage(int dir)
    {
        _rowMemory[Current] = RowIndex;
        PageIndex = (PageIndex + dir + Pages.Count) % Pages.Count;
        RowIndex = RowFor(Current);
        PageChangedAt = Gfx.Time;
        Moved = dir;
    }

    private static void MoveRow(int dir)
    {
        int n = Current.Options.Count;
        if (n == 0)
            return;
        RowIndex = (RowIndex + dir + n) % n;
    }

    private static void Activate(Option o)
    {
        switch (o)
        {
            case Command cmd:
                if (cmd.Confirm && Confirming != cmd)
                {
                    Confirming = cmd;
                    _confirmLeft = ConfirmSeconds;
                    return;
                }

                Confirming = null;
                Safe.Run(cmd.Id, () => cmd.Press());
                Flash(cmd);
                break;
            case Bind:
            case Control:
                Binding = o;
                BindNote = "";
                break;
            default:
                if (o.Press())
                    Changed(o);
                break;
        }
    }

    // Waiting for a key. Backspace gives up.
    private static void KeyBind(Keys key)
    {
        if (Binding == null)
            return;
        if (key == Keys.Back)
        {
            Binding = null;
            return;
        }

        if (Binding is Bind bind)
        {
            if (Array.IndexOf(GameKeys.MenuKeys, key) >= 0 || Array.IndexOf(GameKeys.FlightKeys, key) >= 0)
            {
                BindNote = "THE GAME OR THIS MENU NEEDS " + Bind.KeyName(key);
                return;
            }

            if (bind.Take(key))
                Changed(bind);
            Binding = null;
            return;
        }

        if (Binding is not Control c)
            return;
        if (c.KeyFixed)
        {
            BindNote = "THIS KEY CAN'T CHANGE. PRESS A PAD BUTTON";
            return;
        }

        if (key == Opt.MenuKey.Key || Array.IndexOf(GameKeys.MenuKeys, key) >= 0 || Array.IndexOf(GameKeys.FlightKeys, key) >= 0)
        {
            BindNote = "THE GAME OR THIS MENU NEEDS " + Bind.KeyName(key);
            return;
        }

        // Another of this hero's controls on that key gets this one's old key.
        foreach (Control other in Opt.Binds[c.Hero].Values)
        {
            if (other != c && !other.KeyFixed && other.Key == key)
            {
                other.Key = c.Key;
                Changed(other);
            }
        }

        c.Key = key;
        Changed(c);
        Binding = null;
    }

    // Waiting for a pad button. VIEW gives up.
    private static void PadBind()
    {
        if (!Pad.Connected || Binding is not Control c)
            return;
        if (Pad.Pressed(PadButton.Back))
        {
            Binding = null;
            return;
        }

        foreach (PadButton b in Pad.NewPresses())
        {
            if (!c.Fits(b))
            {
                BindNote = Glyphs.Name(b) + " ISN'T FREE FOR THIS";
                continue;
            }

            // A clash with another control used the same way swaps the two.
            foreach (Control other in Opt.Binds[c.Hero].Values)
            {
                if (other != c && other.Shares(c) && other.Button == b)
                {
                    other.Button = c.Button;
                    Changed(other);
                }
            }

            c.Button = b;
            Changed(c);
            Binding = null;
            return;
        }
    }

    private static void Changed(Option o)
    {
        Safe.Run(o.Id + " change", () => o.Changed?.Invoke());
        Settings.MarkDirty();
        Flash(o);
    }

    private static void Flash(Option o)
    {
        Flashed = o;
        FlashAt = Gfx.Time;
    }
}
