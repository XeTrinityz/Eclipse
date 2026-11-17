using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Holding a thug. He's knocked into a ragdoll and carried upside down by the leg on a physics handle off his hand.
internal sealed partial class Suit
{
    private const string GripKey = "eclipse.griphandle";

    private static readonly FName LeftHand = new("BONE_L_Hand");
    private static readonly FName RightHand = new("BONE_R_Hand");
    private static readonly FName Pelvis = new("Bip01_Pelvis");
    private static readonly FName[] LegBones = { new("Bip01_R_Foot"), new("Bip01_R_Calf"), new("Bip01_R_Thigh"), new("Bip01_L_Foot") };
    private static readonly FName[] NeckBones = { new("BONE_Neck"), new("BONE_Neck1"), new("BONE_Head") };
    private static readonly FName ThugNeck = new("Bip01_Neck"), ThugHead = new("Bip01_Head"), Chest = new("Bip01_Spine1");
    private static readonly FName Middle = new("Bip01_Spine2");

    // How fast the hold point travels from where he was taken to his hand, so a far pull isn't a snap.
    private const float GripPull = 2000f;
    private const double GripWaitMost = 1.5;
    // The stun he's left with when let go, and when thrown or hit, so he's clear of the hand before he gets up.
    private const float GripDropStun = 1f, GripThrowStun = 2.5f;

    private readonly Tether _tether = new();
    private readonly Limp _limp = new();
    private RPawnVillain? _gripped;
    private FName _gripBone;
    private Vector3 _gripPoint, _gripOffset;
    private double _gripSince, _gripAt;
    private bool _gripTaken;

    // The finisher, a heavy strike into the thug in his hand.
    private bool _finishing, _throwPunch;

    // The hold in use is kept for the whole hold, so changing the option mid hold doesn't mix the two.
    private bool _gripByAnim;

    private void Grip(RPawnVillain thug)
    {
        _gripByAnim = Opt.HoldStyle.Key == "animation";
        if (_gripByAnim)
        {
            GripAnim(thug);
            return;
        }

        _gripped = thug;
        _gripSince = Clock.Now;
        _gripTaken = false;

        EndMove(thug);
        if (!thug.IsRagdoll())
            thug.GenericGoRagdoll();
    }

    // Scripts reloaded while holding a thug. Only the shared state knows whose handle to take off.
    private static void RecoverGrip()
    {
        RecoverGripAnim();
        if (Shared.Get(GripKey) is long[] { Length: 2 } saved && FromPtr<RB_Handle>((nint)saved[0]) is { IsValid: true } handle)
        {
            if (handle.GrabbedComponent is { IsValid: true })
                handle.ReleaseComponent();
            if (FromPtr<Actor>((nint)saved[1]) is { IsValid: true } owner)
                owner.DetachComponent(handle);
            Log.Info("Scripts reloaded while holding a thug, let him go.");
        }

        Shared.Set(GripKey, null);
    }

    // A new world freed the thug and the handle, so they're only forgotten.
    private void ForgetGrip()
    {
        _tether.Forget();
        _limp.Forget();
        _gripped = null;
        _gripTaken = false;
        _gripByAnim = false;
        _gripMesh = null;
        _gripTree = null;
        _gripNode = null;
        _gripAi = null;
        Shared.Set(GripKey, null);
        Shared.Set(GripTreeKey, null);
    }

    // Each carried frame. False once he's lost, got himself up or couldn't be taken hold of.
    private bool GripStep(RPawnPlayer pawn, RPawnVillain thug)
    {
        if (_gripByAnim)
        {
            // Knocked down while in hand, he's let go rather than carried as a ragdoll.
            if (thug.IsRagdoll())
                return false;

            AnimStep(pawn, thug);
            return true;
        }

        // Still going down. The ragdoll can take a tick or two to start.
        if (!thug.IsRagdoll())
        {
            if (!_gripTaken && Clock.Now - _gripSince < GripWaitMost)
                return true;

            Log.Info(_gripTaken ? "Let go of a thug, he got himself up." : "Let go of a thug, he didn't go down.");
            return false;
        }

        if (thug.Mesh is not { IsValid: true } body)
            return false;

        if (!_gripTaken)
        {
            if (!TakeThug(pawn, body))
                return false;
            _gripTaken = true;
        }
        else if (!_tether.Holding)
        {
            Log.Info("Let go of a thug, the handle lost him.");
            return false;
        }

        // A finisher or throw winds his hand back, which dragged the thug into him. He's kept where he was.
        Vector3 want = _finishing ? pawn.Location + _gripOffset : HandPoint(pawn, thug);
        if (!_finishing)
            _gripOffset = want - pawn.Location;
        double now = Clock.Now;
        float dt = (float)Math.Clamp(now - _gripAt, 0.0, 0.1);
        _gripAt = now;
        Vector3 toGo = want - _gripPoint;
        float reach = GripPull * dt;
        _gripPoint = toGo.Length() <= reach ? want : _gripPoint + Vector3.Normalize(toGo) * reach;
        _tether.Pull(_gripPoint);
        _limp.Keep(thug);
        body.WakeRigidBody(_gripBone);
        return true;
    }

    // The bones for the part the player picked, best first.
    private static IEnumerable<FName> PartBones(SkeletalMeshComponent body) => Opt.HoldPart.Key switch
    {
        "chest" => new[] { Middle, Chest },
        "hips" => new[] { Pelvis },
        "leg" => LegBones,
        _ => NeckBone(body) is { } neck ? new[] { ThugNeck, neck, ThugHead } : new[] { ThugNeck, ThugHead },
    };

    // By the part picked, or his middle if his ragdoll has no body there. A bone with no body is taken by the handle but
    // never pulled, so he'd just fall.
    private bool TakeThug(RPawnPlayer pawn, SkeletalMeshComponent body)
    {
        // Calming goes off before the pull starts.
        _limp.Loosen(body);
        foreach (FName bone in PartBones(body).Append(Middle).Append(Pelvis))
        {
            if (body.FindBodyInstanceNamed(bone) is not { IsValid: true })
                continue;

            Vector3 spot = body.GetBoneLocation(bone, 0);
            if (!_tether.Take(pawn, body, bone, spot))
                continue;

            _gripBone = bone;
            _gripPoint = spot;
            _gripAt = Clock.Now;
            Shared.Set(GripKey, new long[] { _tether.HandlePtr, _tether.OwnerPtr });
            Log.Info("Holding a thug by " + bone + ".");
            return true;
        }

        _tether.Remove();
        _limp.Free(0f);
        Log.Info("Couldn't take hold of a thug, let him go.");
        return false;
    }

    // Where his leg goes. Off Batman's facing, not the camera, so the hold doesn't swing as you look round.
    private Vector3 HandPoint(RPawnPlayer pawn, RPawnVillain thug)
    {
        Vector3 fwd = Geo.Facing(_faceYaw);
        Vector3 right = Geo.Facing(_faceYaw + 90f);
        Vector3 nudge = fwd * (Rig.GripAhead * Metre) + right * (Rig.GripSide * Metre) - Vector3.UnitZ * (Rig.GripDrop * Metre);
        if (pawn.Mesh is { IsValid: true } hand && HandBone(hand, Rig.GripLeftHand) is { } handBone)
        {
            // His bones are updated first, or the hand trails his movement. Transform only, a full update
            // re-attaches the handle on him and that lets go of the thug.
            pawn.ForceUpdateComponents(false, true);
            hand.ForceSkelUpdate();
            return hand.GetBoneLocation(handBone, 0) + nudge;
        }

        return pawn.Location + fwd * (pawn.GetCollisionRadius() + thug.GetCollisionRadius()) + nudge;
    }

    // Lets go of him. He keeps the stun given so the game stands him up in its own way.
    private void Ungrip(float left = GripDropStun)
    {
        if (_gripByAnim)
        {
            _gripByAnim = false;
            UngripAnim();
            return;
        }

        _tether.Remove();
        _limp.Free(left);
        Shared.Set(GripKey, null);
        _gripped = null;
        _gripTaken = false;
        _finishing = false;
    }

    // The finisher. A heavy strike into the held thug, who is let go as it lands.
    private void StartFinish(RPawnPlayer pawn, bool throwing = false)
    {
        if (_gripped is not { IsValid: true } thug)
            return;

        _finishing = true;
        _throwPunch = throwing;
        StartSwing(pawn, thug, Kind.Thug, heavy: !throwing, HeavyHard + 1f);
        _throwPunch = false;
        // He's already in hand, so no lunge at him.
        if (_swing is { } swing)
        {
            swing.Held = true;
            swing.Lunge = 0f;
            swing.Dir = Look();
            _fm.Velocity = Vector3.Zero;
        }
    }

    // Each frame of the finisher. He's let go when the fist lands, see Land.
    private void FinishStep(RPawnPlayer pawn, RPlayerController pc)
    {
        if (_swing is { } swing)
        {
            SwingStep(pawn, pc, swing);
            return;
        }

        _finishing = false;
    }

    // Called as a fist lands. A held thug is let go first so the hit can launch him.
    private void LetGoFor(Actor thing)
    {
        if (_held is not { } held || held.Ptr != thing.Ptr)
            return;

        _held = null;
        Ungrip(GripThrowStun);
    }

    // A combat move in progress is ended the way the game ends one.
    private static void EndMove(RPawnVillain thug)
    {
        if (thug.CombatMove is not { IsValid: true } move)
            return;

        Safe.Run("end combat move", () => move.ExitMoveForPawn(thug));
    }

    private static FName? NeckBone(SkeletalMeshComponent body) => FindBone(body, "neck", NeckBones, "Neck", "Head");

    // Thug skeletons use other bone names, so the bone is found by part of its name.
    private static readonly Dictionary<(nint, string), FName?> _bones = new();

    private static FName? HandBone(SkeletalMeshComponent body, bool left) =>
        FindBone(body, left ? "left hand" : "right hand", new[] { left ? LeftHand : RightHand }, left ? "L_Hand" : "R_Hand", left ? "LeftHand" : "RightHand", left ? "Hand_L" : "Hand_R");

    private static FName? FindBone(SkeletalMeshComponent body, string what, FName[] known, params string[] words)
    {
        nint skel = body.SkeletalMesh is { IsValid: true } m ? m.Ptr : body.Ptr;
        if (_bones.TryGetValue((skel, what), out FName? cached))
            return cached;

        FName? found = known.Cast<FName?>().FirstOrDefault(b => body.MatchRefBone(b!.Value) >= 0);
        for (int i = 0; i < 400 && found == null; i++)
        {
            FName name = body.GetBoneName(i);
            if (name.Index == 0)
                break;
            string text = name.ToString();
            if (words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)) && !text.Contains("Twist", StringComparison.OrdinalIgnoreCase))
                found = name;
        }

        _bones[(skel, what)] = found;
        return found;
    }
}
