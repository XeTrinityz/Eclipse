using BmSDK.Engine;

namespace Trinity.Eclipse;

internal static class App
{
    public const string Version = "2.2.0";
    private const float MaxStep = 0.25f;

    private static FrameTimer _tick, _draw;
    private static bool _started;
    private static bool _inWorld;

    public static string Dir { get; private set; } = "";

    public static void Start(string dir)
    {
        if (_started)
            return;
        _started = true;

        Dir = dir;
        Log.Open(dir);
        Opt.Build();
        Settings.Load(first: true);
        Opt.ApplyAll();
        // Scripts reloaded with the menu open, or slowed down. Neither is open or slowed any more.
        if (Shared.MenuOwner == Shared.OwnerName)
            Shared.MenuOwner = null;
        Safe.Run("game speed recover", Pace.Recover);
        Safe.Run("recover", Suit.I.Recover);
        Safe.Run("heavy body recover", Body.Recover);
        Safe.Run("gadgets recover", NoGadgets.Recover);
        Safe.Run("looks recover", Looks.Recover);
        Log.Info("Loaded as " + Heroes.Name(Heroes.Active) + ", menu key " + Bind.KeyName(Opt.MenuKey.Key) + ".");
    }

    public static void Stop()
    {
        if (!_started)
            return;
        _started = false;

        Safe.Run("menu close", Menu.Close);
        Safe.Run("shutdown", Suit.I.Shutdown);
        Safe.Run("gadgets back", NoGadgets.Release);
        Safe.Run("looks back", Looks.Release);
        Safe.Run("heavy body off", Body.Forget);
        Safe.Run("camera restore", Hooks.Shutdown);
        Controls.Shutdown();
        Rumble.Release();
        Shapes.Release();
        Settings.Flush();
        GameKeys.ReleaseAll();
        World.Forget();
    }

    public static void EnterWorld()
    {
        World.Forget();
        NoGadgets.Forget();
        Looks.Forget();
        Safe.Run("world change", Suit.I.WorldChanged);
        Safe.Run("heavy body off", Body.Forget);
    }

    public static void EnterMenu()
    {
        Safe.Run("menu close", Menu.Close);
        World.Forget();
        NoGadgets.Forget();
        Looks.Forget();
        Safe.Run("heavy body off", Body.Forget);
    }

    public static void SdkTick()
    {
        Fallback.NoteSdkTick();
        Tick();
    }

    public static void SdkKey(Keys key)
    {
        if (_started)
            KeyInput.Pressed(key);
    }

    public static void Tick()
    {
        if (!_started)
            return;

        float dt = _tick.Step(MaxStep);
        World.Refresh();
        Settings.Tick(dt);

        if (World.InGame)
        {
            _inWorld = true;
            Safe.Run("gadgets", NoGadgets.Step);
            Safe.Run("looks", Looks.Step);
            Safe.Run("hero keys", HeroKeys.Step);
            Safe.Run("suit tick", () => Suit.I.Tick(dt));

            // The camera update places it. This catches frames that don't run it, like in the Batmobile.
            Safe.Run("heavy body", Body.Follow);
        }
        else if (World.InFrontend && _inWorld)
        {
            _inWorld = false;
            Safe.Run("leave game", Suit.I.WorldChanged);
        }
    }

    public static void Frame(Canvas canvas)
    {
        if (!_started)
            return;

        World.Refresh();
        Safe.Run("controls", () => Controls.Frame(_draw.Step(MaxStep)));
        Safe.Run("fallback", Fallback.Frame);
        Safe.Run("overlay", () => Overlay.Draw(canvas));
    }
}

internal static class Fallback
{
    private const double QuietSeconds = 0.5;

    private static long _lastSdkTick = System.Diagnostics.Stopwatch.GetTimestamp();
    private static bool _logged;

    public static bool Active => System.Diagnostics.Stopwatch.GetElapsedTime(_lastSdkTick).TotalSeconds > QuietSeconds;

    public static void NoteSdkTick() => _lastSdkTick = System.Diagnostics.Stopwatch.GetTimestamp();

    public static void Frame()
    {
        if (!Active || World.Paused)
            return;

        if (!_logged)
        {
            _logged = true;
            Log.Info("OnTick stopped firing, running from OnDraw instead.");
        }

        App.Tick();
    }
}
