using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// What the Flash's HUD reads: how long each move has left to cool down, and who his moves are on.
internal sealed partial class Suit
{
    public double FlurryWait => Wait(_flurryReady);

    public double SlowWait => Wait(_slowReady);

    public double TornadoWait => Wait(_tornadoReady);

    public double RewindWait => Wait(_rewindReady);

    public int TornadoLifting => _tornado ? _tornadoLifting : 0;

    public int KeptThugs => _kept.Count;

    public IEnumerable<(RPawnVillain Thug, int Hits)> KeptMarks =>
        _kept.Values.Where(k => k.Thug is { IsValid: true, bDeleteMe: false }).Select(k => (k.Thug, k.Hits));

    // Whoever a bolt let go now would hit.
    public Actor? BoltAim => _bolt >= 0f && _boltAim is { IsValid: true, bDeleteMe: false } aim ? aim : null;

    private static double Wait(double readyAt) => Math.Max(0.0, readyAt - Clock.Now);
}
