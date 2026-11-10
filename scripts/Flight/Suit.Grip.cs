using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Holding a thug. Physics off, placed off his hand each frame, with his anim tree swapped for one node.
internal sealed partial class Suit
{
    private const string GripTreeKey = "eclipse.griptree";

    private static readonly FName LeftHand = new("BONE_L_Hand");
    private static readonly FName RightHand = new("BONE_R_Hand");
    private static readonly FName[] NeckBones = { new("BONE_Neck"), new("BONE_Neck1"), new("BONE_Head") };

    // Thug side anims that read as being held, tried when the one in Rig isn't loaded.
    private static readonly string[] HeldThugAnims = { "Super_Stun_Hold_Idle", "Interrogation_Idle", "Grabbed_Idle" };

    private RPawnVillain? _gripped;
    private RBMAIController? _gripAi;
    private AnimSequence? _grippedAnim, _grippedIn;
    private double _gripSince;

    // The finisher, a heavy strike into the thug in his hand.
    private bool _finishing, _throwPunch;

    private SkeletalMeshComponent? _gripMesh;
    private AnimNode? _gripTree;
    private AnimNodeSequence? _gripNode;
    private int _gripSetsWas = -1;
    private nint _gripPlaying;

    // Where the thug sits in Batman's frame once settled. Snapped to the hand he chased it.
    private Vector3? _gripLock;
    private (bool Collide, bool Block, bool Ignore) _gripCollide = (true, true, false);
    private string _gripLockKey = "";
    private const double GripLockAfter = 0.3;
    private const double GripLockMost = 1.5;
    private const float GripEase = 18f;
    private const float GripStillBy = 1.5f;
    private const int GripStillFrames = 6;
    private const float GripSaneSide = 70f, GripSaneAhead = 90f;

    // A good lock for each thug model, kept for the session.
    private static readonly Dictionary<string, Vector3> _gripLocks = new();
    private Vector3? _gripEased, _gripHandWas;
    private double _gripEaseAt;
    private int _gripStill;

    private void Grip(RPawnVillain thug)
    {
        _gripped = thug;
        _gripSince = Clock.Now;
        _gripLock = null;
        _gripEased = null;
        _gripHandWas = null;
        _gripStill = 0;

        // No collision while held, or Batman's moves hit him and the game refused to place him.
        _gripCollide = (thug.bCollideActors, thug.bBlockActors, thug.bIgnoreEncroachers);
        thug.SetCollision(false, false, true);

        // His AI would go on picking attacks from Batman's hand.
        _gripAi = thug.Controller as RBMAIController;
        if (_gripAi is { IsValid: true } ai)
            ai.SetTickIsDisabled(true);
        else
            _gripAi = null;

        EndMove(thug);
        if (thug.Physics != Actor.EPhysics.PHYS_None)
            thug.SetPhysics(Actor.EPhysics.PHYS_None, false);
        thug.Velocity = Vector3.Zero;
        thug.Acceleration = Vector3.Zero;

        // His side of the grab, then the hold. Named apart from Batman's so each can be tuned alone.
        _grippedIn = ThugAnim(Rig.ThugGrabSide);
        _grippedAnim = ThugAnim(Rig.ThugHoldSide) ?? ThugAnim(HeldThugAnims);

        if ((_grippedIn ?? _grippedAnim) != null)
            TakeTree(thug);
    }

    // Swaps his anim tree for one anim node and adds the sets his held anims are in.
    private void TakeTree(RPawnVillain thug)
    {
        if (thug.Mesh is not { IsValid: true } mesh)
            return;

        _gripMesh = mesh;
        _gripTree = mesh.Animations;
        _gripSetsWas = mesh.AnimSets.Count;
        foreach (AnimSequence? seq in new[] { _grippedIn, _grippedAnim })
        {
            if (seq?.Outer is AnimSet { IsValid: true } set && !Contains(mesh.AnimSets, set))
                mesh.AnimSets.Push(set);
        }

        _gripNode = new AnimNodeSequence(mesh);
        mesh.SetAnimations(_gripNode);
        _gripPlaying = 0;
        Shared.Set(GripTreeKey, new long[] { mesh.Ptr, _gripTree?.Ptr ?? 0, _gripSetsWas });
    }

    private static bool Contains(TArray<AnimSet> sets, AnimSet set)
    {
        for (int i = 0; i < sets.Count; i++)
        {
            if (sets[i] is { } s && s.Ptr == set.Ptr)
                return true;
        }

        return false;
    }

    // Puts his own tree and sets back.
    private void GiveTree()
    {
        if (_gripMesh is { IsValid: true } mesh)
        {
            if (_gripTree is { IsValid: true } tree)
                mesh.SetAnimations(tree);
            if (_gripSetsWas >= 0 && mesh.AnimSets.Count > _gripSetsWas)
                mesh.AnimSets.Pop(mesh.AnimSets.Count - _gripSetsWas);
        }

        _gripMesh = null;
        _gripTree = null;
        _gripNode = null;
        _gripSetsWas = -1;
        Shared.Set(GripTreeKey, null);
    }

    // Scripts reloaded while holding a thug. Only the shared state knows whose tree to put back.
    private static void RecoverGrip()
    {
        if (Shared.Get(GripTreeKey) is long[] { Length: 3 } saved && FromPtr<SkeletalMeshComponent>((nint)saved[0]) is { IsValid: true } mesh)
        {
            if (FromPtr<AnimNode>((nint)saved[1]) is { IsValid: true } tree)
                mesh.SetAnimations(tree);
            if (saved[2] >= 0 && mesh.AnimSets.Count > saved[2])
                mesh.AnimSets.Pop(mesh.AnimSets.Count - (int)saved[2]);
            Log.Info("Scripts reloaded while holding a thug, put his own anims back.");
        }

        Shared.Set(GripTreeKey, null);
    }

    // Plays the grab, then loops the hold, by setting the node's place in the anim each frame.
    private void DriveThug()
    {
        if (_gripNode is not { IsValid: true } node || _gripMesh is not { IsValid: true } mesh)
            return;

        float t = (float)(Clock.Now - _gripSince);
        AnimSequence? seq;
        float at;
        if (_grippedIn is { IsValid: true } lead && t < lead.SequenceLength)
        {
            seq = lead;
            at = t;
        }
        else if (_grippedAnim is { IsValid: true } hold)
        {
            seq = hold;
            float into = _grippedIn is { IsValid: true } l ? t - l.SequenceLength : t;
            at = into % MathF.Max(hold.SequenceLength, 0.01f);
        }
        else
        {
            return;
        }

        if (_gripPlaying != seq.Ptr)
        {
            node.SetAnim(seq.SequenceName);
            _gripPlaying = seq.Ptr;
        }

        node.SetPosition(at, false);
        mesh.ForceSkelUpdate();
    }

    // Each carried frame. Plays his anim, then puts his neck on the hand using the Rig offsets.
    private void GripStep(RPawnPlayer pawn, RPawnVillain thug)
    {
        DriveThug();

        // Offset from Batman's facing, not the camera, so the grip doesn't swing as you look round.
        Vector3 fwd = Geo.Facing(_faceYaw);
        Vector3 right = Geo.Facing(_faceYaw + 90f);
        Vector3 nudge = fwd * (Rig.GripAhead * Metre) + right * (Rig.GripSide * Metre) - Vector3.UnitZ * (Rig.GripDrop * Metre);
        var wantRot = new Rotator(Rig.GripThugPitch, _faceYaw + Rig.GripYaw, 0f);

        if (thug.Physics != Actor.EPhysics.PHYS_None)
            thug.SetPhysics(Actor.EPhysics.PHYS_None, false);
        thug.Velocity = Vector3.Zero;
        thug.Acceleration = Vector3.Zero;
        EndMove(thug);

        // Per thug model too, since big thugs and soldiers hang differently.
        string model = thug.Mesh is { SkeletalMesh: { IsValid: true } sm } ? sm.Name.ToString() : "";
        string key = model;
        // A throw moves him with the hand. The finisher keeps him in place so the fist meets him.
        if (key != _gripLockKey || _throwing)
        {
            _gripLock = null;
            _gripLockKey = key;
        }

        // Reuse a lock from an earlier grab of this model so every hold sits the same.
        float lead = _grippedIn is { IsValid: true } l ? l.SequenceLength : 0f;
        if (_gripLock == null && !_throwing && Clock.Now - _gripSince > lead && _gripLocks.TryGetValue(key, out Vector3 known))
            _gripLock = known;

        if (_gripLock is { } locked)
        {
            Place(thug, pawn.Location + fwd * locked.X + right * locked.Y + Vector3.UnitZ * locked.Z, wantRot);
            return;
        }

        if (pawn.Mesh is { IsValid: true } hand && HandBone(hand, Rig.GripLeftHand) is { } handBone
            && thug.Mesh is { IsValid: true } body && NeckBone(body) is { } neck)
        {
            // Update his mesh and bones first, or the hand trails his movement.
            pawn.ForceUpdateComponents(false, false);
            hand.ForceSkelUpdate();
            // Measure the neck in his own frame, or it swings away from the hand.
            Vector3 handAt = hand.GetBoneLocation(handBone, 0);
            Vector3 neckOff = Turned(body.GetBoneLocation(neck, 0) - thug.Location, thug.Rotation, wantRot);
            Vector3 spot = handAt - neckOff + nudge;

            // Eased towards the hand in Batman's frame, so a blending hand doesn't shake him.
            Vector3 local = new(Vector3.Dot(spot - pawn.Location, fwd), Vector3.Dot(spot - pawn.Location, right), spot.Z - pawn.Location.Z);
            double now = Clock.Now;
            float dt = (float)Math.Clamp(now - _gripEaseAt, 0.0, 0.1);
            _gripEaseAt = now;
            _gripEased = _gripEased is { } was && !_throwing ? Vector3.Lerp(was, local, Ease.Follow(GripEase, dt)) : local;
            Vector3 eased = _gripEased.Value;
            Place(thug, pawn.Location + fwd * eased.X + right * eased.Y + Vector3.UnitZ * eased.Z, wantRot);

            // Lock once the grab has played and the hand is still, or after a while. A throw never locks.
            Vector3 handLocal = new(Vector3.Dot(handAt - pawn.Location, fwd), Vector3.Dot(handAt - pawn.Location, right), handAt.Z - pawn.Location.Z);
            _gripStill = _gripHandWas is { } h && Vector3.Distance(h, handLocal) < GripStillBy ? _gripStill + 1 : 0;
            _gripHandWas = handLocal;
            double since = now - _gripSince;
            if (!_throwing && since > lead + GripLockAfter && (_gripStill >= GripStillFrames || since > lead + GripLockMost))
            {
                // A spot far off to the side is a bad grab. Use it for this hold but don't keep it.
                bool sane = MathF.Abs(local.Y) < GripSaneSide && local.X < GripSaneAhead;
                _gripLock = local;
                if (_gripStill >= GripStillFrames && sane)
                    _gripLocks[key] = local;
            }
        }
        else
        {
            Place(thug, pawn.Location + fwd * (pawn.GetCollisionRadius() + thug.GetCollisionRadius()) + nudge, wantRot);
        }
    }

    // Lets go of him. His anims, collision and physics go back as they were.
    private void Ungrip()
    {
        GiveTree();
        if (_gripped is { IsValid: true, bDeleteMe: false } thug)
        {
            thug.SetCollision(_gripCollide.Collide, _gripCollide.Block, _gripCollide.Ignore);
            if (thug.Physics == Actor.EPhysics.PHYS_None)
                thug.SetPhysics(Actor.EPhysics.PHYS_Falling, false);
        }

        if (_gripAi is { IsValid: true } ai)
            ai.SetTickIsDisabled(false);
        _gripAi = null;
        _gripped = null;
        _finishing = false;
        _grippedAnim = null;
        _grippedIn = null;
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
        Ungrip();
    }

    // A combat move in progress is ended the way the game ends one.
    private static void EndMove(RPawnVillain thug)
    {
        if (thug.CombatMove is not { IsValid: true } move)
            return;

        Safe.Run("end combat move", () => move.ExitMoveForPawn(thug));
    }

    // A teleport the game refuses, for something in the way, is done as a move instead.
    private static void Place(RPawnVillain thug, Vector3 spot, Rotator rot)
    {
        if (!thug.SetLocation(spot))
            thug.Move(spot - thug.Location);

        thug.SetRotation(rot);
    }

    // An offset in one rotation's frame, given in another's.
    private static Vector3 Turned(Vector3 offset, Rotator from, Rotator to)
    {
        Geo.Axes(from, out Vector3 fx, out Vector3 fy, out Vector3 fz);
        Geo.Axes(to, out Vector3 tx, out Vector3 ty, out Vector3 tz);
        return tx * Vector3.Dot(offset, fx) + ty * Vector3.Dot(offset, fy) + tz * Vector3.Dot(offset, fz);
    }

    // Thug skeletons use other bone names, so the bone is found by part of its name.
    private static readonly Dictionary<(nint, string), FName?> _bones = new();

    private static FName? NeckBone(SkeletalMeshComponent body) => FindBone(body, "neck", NeckBones, "Neck", "Head");

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

    private static readonly Dictionary<string, AnimSequence> _thugAnims = new(StringComparer.OrdinalIgnoreCase);

    // The thug's side of a paired anim, found by its shared name and kept once found.
    private static AnimSequence? ThugAnim(params string[] names)
    {
        foreach (string name in names)
        {
            if (_thugAnims.TryGetValue(name, out AnimSequence? known) && known.IsValid)
                return known;

            AnimSequence? found = null;
            int best = -1;
            foreach (AnimSequence seq in Game.FindObjects<AnimSequence>())
            {
                if (!seq.IsValid || seq.IsClassDefaultObject || !string.Equals(seq.SequenceName.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    continue;

                string path = seq.GetPathName(null);
                string set = SetName(path);
                if (set.StartsWith("BM_", StringComparison.OrdinalIgnoreCase))
                    continue;
                int score = (set.Contains("_BM", StringComparison.OrdinalIgnoreCase) ? 2 : 0) + (set.StartsWith("TG", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                if (score > best)
                {
                    best = score;
                    found = seq;
                }
            }

            if (found == null)
                continue;

            _thugAnims[name] = found;
            return found;
        }

        return null;
    }
}
