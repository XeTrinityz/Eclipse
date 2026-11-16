using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// An effect kept on one of his bones and switched on and off. The game's effect pool is often nearly full in
// the city, and anything borrowed from it was skipped, so these are our own components like Batwing's jets.
internal sealed class WornFx
{
    private readonly string _path;
    private readonly FName _bone;
    private ParticleSystemComponent? _fx;
    private SkeletalMeshComponent? _mesh;
    private bool _on;

    // The bone is a name, since these are made with the suit, before its static names are set.
    public WornFx(string path, string bone)
    {
        _path = path;
        _bone = new FName(bone);
    }

    public bool On => _on;

    public string Template => _path[(_path.LastIndexOf('.') + 1)..];

    public FName Bone => _bone;

    public void Set(Pawn? pawn, bool on)
    {
        if (on && !Opt.FlightFx.On)
            on = false;
        if (pawn?.Mesh is not SkeletalMeshComponent { IsValid: true } mesh)
        {
            Drop();
            return;
        }

        if (_fx is not { IsValid: true } || _mesh is not { IsValid: true } || _mesh.Ptr != mesh.Ptr)
        {
            if (!on)
                return;
            Drop();
            Make(pawn, mesh);
            if (_fx == null)
                return;
        }

        if (on == _on)
            return;
        _on = on;
        ParticleSystemComponent fx = _fx!;
        Safe.Run("worn effect", () =>
        {
            if (on)
                fx.ActivateSystem(true, false);
            else
                fx.DeactivateSystem();
        });
    }

    // Starts it over even if it's on, for a crackle that comes in pulses.
    public void Pulse(Pawn? pawn)
    {
        Set(pawn, false);
        Set(pawn, true);
    }

    public void Drop()
    {
        ParticleSystemComponent? fx = _fx;
        SkeletalMeshComponent? mesh = _mesh;
        _fx = null;
        _mesh = null;
        _on = false;
        if (fx is not { IsValid: true })
            return;
        Safe.Run("worn effect off", () =>
        {
            fx.DeactivateSystem();
            if (mesh is { IsValid: true })
                mesh.DetachComponent(fx);
        });
    }

    private void Make(Pawn pawn, SkeletalMeshComponent mesh)
    {
        if (Fx.System(_path) is not { } template)
            return;

        Safe.Run("worn effect", () =>
        {
            Sweep(mesh, template.Name.ToString(), _bone);
            var c = new ParticleSystemComponent(pawn) { bAutoActivate = false };
            c.SetTemplate(template);
            mesh.AttachComponent(c, _bone, Vector3.Zero, default, Vector3.One);
            _fx = c;
            _mesh = mesh;
        });
    }

    // Takes off copies left on that bone by an earlier load of the scripts. No bone means any bone.
    public static void Sweep(SkeletalMeshComponent mesh, string template, FName? bone)
    {
        string? boneName = bone?.ToString();
        var stale = new List<ActorComponent>();
        foreach (SkeletalMeshComponent.FAttachment a in mesh.Attachments)
        {
            if (a.Component is ParticleSystemComponent { IsValid: true } ps && ps.Template?.Name.ToString() == template
                && (boneName == null || a.BoneName.ToString() == boneName))
                stale.Add(ps);
        }

        foreach (ActorComponent c in stale)
            mesh.DetachComponent(c);
    }
}
