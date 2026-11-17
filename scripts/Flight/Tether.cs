using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// One physics handle on a pawn. It takes hold of one bone of a ragdoll and pulls it toward the point it is given
// each tick. Ported from Knightfall's Grip. The handle is ours and comes off again.
internal sealed class Tether
{
    // The engine's own spring is far too soft to carry a ragdoll.
    private const float Stiffness = 8000f;
    private const float Damping = 400f;

    private RB_Handle? _handle;
    private Actor? _owner;

    public bool Holding => _owner is { IsValid: true } && _handle is { IsValid: true } handle && handle.GrabbedComponent is { IsValid: true };

    public nint HandlePtr => _handle?.Ptr ?? 0;

    public nint OwnerPtr => _owner?.Ptr ?? 0;

    public bool Take(Actor owner, PrimitiveComponent part, FName bone, Vector3 spot)
    {
        RB_Handle handle = Handle(owner);
        if (handle.GrabbedComponent is { IsValid: true })
            handle.ReleaseComponent();

        part.WakeRigidBody(bone);
        handle.GrabComponent(part, bone, spot, false);
        return Holding;
    }

    public void Pull(Vector3 point)
    {
        if (Holding)
            _handle!.SetLocation(point);
    }

    // Lets go and takes the handle off its pawn.
    public void Remove()
    {
        if (_handle is { IsValid: true } handle)
        {
            if (handle.GrabbedComponent is { IsValid: true })
                handle.ReleaseComponent();
            if (_owner is { IsValid: true } owner)
                owner.DetachComponent(handle);
        }

        _handle = null;
        _owner = null;
    }

    // A new world freed both, so nothing is called on them.
    public void Forget()
    {
        _handle = null;
        _owner = null;
    }

    private RB_Handle Handle(Actor owner)
    {
        if (_handle is { IsValid: true } kept && ReferenceEquals(_owner, owner))
            return kept;

        Remove();
        RB_Handle handle = Tracked(new RB_Handle(owner));
        owner.AttachComponent(handle);
        handle.LinearStiffness = Stiffness;
        handle.LinearDamping = Damping;
        _handle = handle;
        _owner = owner;
        return handle;
    }

    // A wrapper from `new` is never told when its object is freed, so a reload could leave it reading as valid.
    // Looking the object up by its path gives the wrapper the SDK keeps track of.
    private static T Tracked<T>(T made) where T : GameObject, IGameObject
    {
        if (Game.FindObject<T>(made.GetPathName()) is { IsValid: true } tracked && tracked.Ptr == made.Ptr)
            return tracked;

        Log.Info("A new " + typeof(T).Name + " couldn't be looked up again, so it can't be told when it is destroyed.");
        return made;
    }
}

// Keeps a ragdolled thug down while he's carried. The game counts a ragdoll's stun time down and stands him up
// at zero, or sooner once the body has been still for a second, and a carried body is held still. So the time is
// topped up each tick and the stillness rule is off.
internal sealed class Limp
{
    private const float KeepDown = 5f;

    private SkeletalMeshComponent? _mesh;
    private RRagdollProcessor? _ragdoll;
    private bool _calmWas, _floorWas;
    private float _stunWas;

    // Calming off for a body about to be pulled.
    public void Loosen(SkeletalMeshComponent mesh)
    {
        if (ReferenceEquals(_mesh, mesh))
            return;

        _mesh = mesh;
        _calmWas = mesh.bDebugDisableRagdollCalmingMeasures;
        mesh.bDebugDisableRagdollCalmingMeasures = true;
        mesh.ResetRagdollCalmingMeasures();
    }

    // Every tick while carried. The ragdoll's own state only starts a tick or two after the knock-down, so its
    // part waits for that.
    public void Keep(RPawnVillain thug)
    {
        if (thug.Mesh is { IsValid: true } mesh)
            Loosen(mesh);

        if (thug.RagdollProcessor is not { IsValid: true } ragdoll || ragdoll.CurrState != RRagdollProcessor.RagdollState.RAG_Ragdoll)
            return;

        if (!ReferenceEquals(_ragdoll, ragdoll))
        {
            _ragdoll = ragdoll;
            _stunWas = ragdoll.StunTime;
            _floorWas = ragdoll.bGetUpWhenHitFloor;
        }

        ragdoll.bGetUpWhenHitFloor = false;
        ragdoll.StunTime = KeepDown;
    }

    // A new world freed what was kept, so it is only forgotten.
    public void Forget()
    {
        _mesh = null;
        _ragdoll = null;
    }

    // Let go. He keeps at least the stun time given, or what he had when picked up if that was longer, and the
    // game stands him up from there in its own way.
    public void Free(float left)
    {
        if (_mesh is { IsValid: true } mesh)
            mesh.bDebugDisableRagdollCalmingMeasures = _calmWas;

        if (_ragdoll is { IsValid: true } ragdoll)
        {
            ragdoll.bGetUpWhenHitFloor = _floorWas;
            if (ragdoll.CurrState == RRagdollProcessor.RagdollState.RAG_Ragdoll)
                ragdoll.StunTime = MathF.Max(_stunWas, left);
        }

        Forget();
    }
}
