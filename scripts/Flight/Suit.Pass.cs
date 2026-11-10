using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// A car moving away from him stops blocking him briefly so he follows it in.
internal sealed partial class Suit
{
    private const string PassKey = "eclipse.passing";
    private const double PassHold = 0.25;
    // How fast it has to be moving away already, as a share of his own speed.
    private const float PassFrom = 0.7f;

    private static readonly Dictionary<nint, (Actor Thing, double Until)> _passing = new();

    private static bool Passing(Actor actor) => _passing.ContainsKey(actor.Ptr);

    private static void Pass(Actor thing, float along, float speed)
    {
        if (along < speed * PassFrom)
            return;

        double until = Clock.Now + PassHold;
        if (_passing.ContainsKey(thing.Ptr))
        {
            _passing[thing.Ptr] = (thing, until);
            return;
        }

        if (!thing.bBlockActors)
            return;

        _passing[thing.Ptr] = (thing, until);
        thing.SetCollision(thing.bCollideActors, false, thing.bIgnoreEncroachers);
        SavePassing();
    }

    // What he's carrying never blocks him, or a car over his head stops him flying up.
    private static void KeepPassing(Actor thing)
    {
        bool known = _passing.ContainsKey(thing.Ptr);
        _passing[thing.Ptr] = (thing, Clock.Now + PassHold);
        if (known)
            return;

        if (thing.bBlockActors)
            thing.SetCollision(thing.bCollideActors, false, thing.bIgnoreEncroachers);
        SavePassing();
    }

    // A car bouncing back towards him blocks again at once, or it rolls through him.
    private static void StopPassing(Actor thing)
    {
        if (!_passing.Remove(thing.Ptr))
            return;

        thing.SetCollision(thing.bCollideActors, true, thing.bIgnoreEncroachers);
        SavePassing();
    }

    // Each frame. Anything he stopped shoving blocks again.
    private static void PassStep()
    {
        if (_passing.Count == 0)
            return;

        double now = Clock.Now;
        bool changed = false;
        foreach ((nint ptr, (Actor thing, double until)) in _passing.ToList())
        {
            if (now < until && thing.IsValid && !thing.bDeleteMe)
                continue;

            _passing.Remove(ptr);
            changed = true;
            if (thing.IsValid && !thing.bDeleteMe)
                thing.SetCollision(thing.bCollideActors, true, thing.bIgnoreEncroachers);
        }

        if (changed)
            SavePassing();
    }

    private static void EndPassing()
    {
        foreach ((Actor thing, double _) in _passing.Values)
        {
            if (thing.IsValid && !thing.bDeleteMe)
                thing.SetCollision(thing.bCollideActors, true, thing.bIgnoreEncroachers);
        }

        _passing.Clear();
        Shared.Set(PassKey, null);
    }

    private static void SavePassing() => Shared.Set(PassKey, _passing.Count == 0 ? null : _passing.Keys.Select(p => (long)p).ToArray());

    // A reload in the middle of a shove would leave that car never blocking anyone again.
    private static void RecoverPassing()
    {
        if (Shared.Get(PassKey) is not long[] { Length: > 0 } saved)
            return;

        Shared.Set(PassKey, null);
        var wanted = new HashSet<long>(saved);
        foreach (Actor thing in Game.FindObjects<Actor>())
        {
            if (thing is { IsValid: true, IsClassDefaultObject: false, bDeleteMe: false } && wanted.Contains((long)thing.Ptr))
                thing.SetCollision(thing.bCollideActors, true, thing.bIgnoreEncroachers);
        }
    }
}
