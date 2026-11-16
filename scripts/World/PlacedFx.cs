using System.Numerics;
using BmSDK;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// An effect we own, fired at a point in the world when it's wanted. Mid fight the game's effect pool is often
// full, and one-off effects borrowed from it were skipped, so these are kept on the player like the trails.
internal sealed class PlacedFx
{
    private readonly string _path;
    private ParticleSystemComponent? _fx;
    private Actor? _owner;

    public PlacedFx(string path) => _path = path;

    public string Template => _path[(_path.LastIndexOf('.') + 1)..];

    // Started over at this point, facing this way. Null when the effect isn't loaded or effects are off.
    public ParticleSystemComponent? At(Actor owner, Vector3 at, Rotator turn)
    {
        if (!Opt.FlightFx.On || !Ready(owner))
            return null;

        ParticleSystemComponent fx = _fx!;
        Safe.Run("placed effect", () =>
        {
            fx.DeactivateSystem();
            fx.KillParticlesForced();
            fx.SetTranslation(at);
            fx.SetRotation(turn);
            fx.ActivateSystem(true, false);
        });
        return fx;
    }

    public void Off()
    {
        if (_fx is { IsValid: true } fx)
            Safe.Run("placed effect off", fx.DeactivateSystem);
    }

    public void Drop()
    {
        ParticleSystemComponent? fx = _fx;
        Actor? owner = _owner;
        _fx = null;
        _owner = null;
        if (fx is not { IsValid: true })
            return;
        Safe.Run("placed effect off", () =>
        {
            fx.DeactivateSystem();
            if (owner is { IsValid: true })
                owner.DetachComponent(fx);
        });
    }

    private bool Ready(Actor owner)
    {
        if (_fx is { IsValid: true } && _owner is { IsValid: true } && _owner.Ptr == owner.Ptr)
            return true;
        Drop();
        if (!owner.IsValid || Fx.System(_path) is not { } template)
            return false;

        Safe.Run("placed effect", () =>
        {
            var c = new ParticleSystemComponent(owner) { bAutoActivate = false };
            c.SetTemplate(template);
            owner.AttachComponent(c);
            c.SetAbsolute(true, true, true);
            _fx = c;
            _owner = owner;
        });
        return _fx != null;
    }

    // Takes off copies an earlier load of the scripts left on him.
    public static void Sweep(Actor owner, string template)
    {
        var stale = new List<ActorComponent>();
        foreach (ActorComponent? c in owner.Components)
        {
            if (c is ParticleSystemComponent { IsValid: true } ps && ps.Template?.Name.ToString() == template)
                stale.Add(ps);
        }

        foreach (ActorComponent c in stale)
            owner.DetachComponent(c);
    }
}
