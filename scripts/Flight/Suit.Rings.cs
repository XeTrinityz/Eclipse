using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Superman's one-off effects. The game's pool is full in the city, so each one is a component he owns,
// reused in turn, the way the Flash's rings are.
internal sealed partial class Suit
{
    private const int OwnedSlots = 3;

    private readonly Dictionary<string, PlacedFx[]> _owned = new();
    private readonly Dictionary<string, int> _ownedNext = new();

    // Starts the effect at this point, turned this way. False when it isn't loaded or effects are off.
    private bool Own(string path, Vector3 at, Rotator turn, int count = OwnedSlots)
    {
        if (World.Player is not RPawnPlayer { IsValid: true } pawn)
            return false;

        if (!_owned.TryGetValue(path, out PlacedFx[]? slots))
            _owned[path] = slots = Enumerable.Range(0, count).Select(_ => new PlacedFx(path)).ToArray();

        int next = _ownedNext.GetValueOrDefault(path);
        _ownedNext[path] = next + 1;
        return slots[next % slots.Length].At(pawn, at, turn) != null;
    }

    private void DropOwned()
    {
        EndBoost();
        foreach (PlacedFx[] slots in _owned.Values)
        {
            foreach (PlacedFx slot in slots)
                slot.Drop();
        }

        _owned.Clear();
        _ownedNext.Clear();
    }

    // Scripts reloaded. Components the last load left on him are taken off.
    private static void RecoverOwned()
    {
        if (World.Player is not { IsValid: true } player)
            return;

        foreach (string path in new[] { Fx.Dust, Fx.Gust, Fx.FloorSlam, Fx.LaserBeam, Fx.BeamSparks, Fx.GroundFire, Fx.Ignite, Fx.Fireball })
            PlacedFx.Sweep(player, path[(path.LastIndexOf('.') + 1)..]);
    }
}
