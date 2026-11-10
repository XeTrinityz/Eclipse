using BmSDK;
using BmSDK.BmGame;

namespace Trinity.Eclipse;

// Game speed shared by everything that slows it. The slowest ask wins.
internal static class Pace
{
    private const string BaseKey = "eclipse.pace";

    private static readonly Dictionary<string, (float Speed, double Until)> _asks = new(StringComparer.Ordinal);
    private static float _base = 1f;
    private static float _applied = -1f;
    private static bool _slowed;

    public static void Hold(string who, float speed, double seconds = double.PositiveInfinity)
    {
        _asks[who] = (Math.Clamp(speed, 0.01f, 1f), Clock.Now + seconds);
        Apply();
    }

    // The game speed right now, whoever set it.
    public static float Speed => Current();

    public static void Drop(string who)
    {
        if (_asks.Remove(who))
            Apply();
    }

    // Every tick, so timed asks run out in real seconds however slow the game is.
    public static void Step()
    {
        if (_asks.Count > 0 || _slowed)
            Apply();
    }

    // Puts the old speed back and forgets every ask.
    public static void Reset()
    {
        _asks.Clear();
        Apply();
    }

    // A new world comes up at its own speed, so nothing is put back.
    public static void Forget()
    {
        _asks.Clear();
        _slowed = false;
        _applied = -1f;
        Shared.Set(BaseKey, null);
    }

    // Scripts reloaded while slowed. The speed from before is still saved.
    public static void Recover()
    {
        if (Shared.Get(BaseKey) is not float was)
            return;

        Shared.Set(BaseKey, null);
        Set(was);
    }

    private static void Apply()
    {
        double now = Clock.Now;
        List<string>? done = null;
        foreach (KeyValuePair<string, (float Speed, double Until)> ask in _asks)
        {
            if (ask.Value.Until <= now)
                (done ??= new List<string>()).Add(ask.Key);
        }

        if (done != null)
        {
            foreach (string who in done)
                _asks.Remove(who);
        }

        if (_asks.Count == 0)
        {
            if (!_slowed)
                return;

            _slowed = false;
            _applied = -1f;
            Shared.Set(BaseKey, null);
            Set(_base);
            return;
        }

        float want = _asks.Values.Min(a => a.Speed);
        if (!_slowed)
        {
            float was = Current();
            _base = was > 0.01f ? was : 1f;
            _slowed = true;
            Shared.Set(BaseKey, _base);
        }

        if (MathF.Abs(want - _applied) < 0.0001f)
            return;

        _applied = want;
        Set(want);
    }

    private static float Current() =>
        Game.TryGetGameInfo(out RGameInfo? info) && info is { IsValid: true } ? info.DebugGameSpeed : 1f;

    private static void Set(float speed)
    {
        if (Game.TryGetGameInfo(out RGameInfo? info) && info is { IsValid: true })
            info.SetDebugGameSpeed(speed);
    }
}
