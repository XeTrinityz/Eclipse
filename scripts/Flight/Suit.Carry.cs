using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Carried things are placed on his hands each frame. A spring left them lagging and tumbling.
internal sealed partial class Suit
{
    // How far his hands sink into what's resting on them, and how fast it settles into them.
    private const float CarryGrip = 20f;
    private const float CarryEase = 9f;
    private const float CarryThrowEase = 30f;
    // Bigger than this and the bounds have taken in loose parts lying away from it.
    private static readonly Vector3 CarryHalfMost = new(350f, 350f, 150f);

    private Vector3 _carryPivot, _carryHalf;
    private float _carryYaw;
    private Vector3 _carryAt, _carryFwd, _carryUp;
    private double _carryEaseAt;

    // Each body of a car in its own frame. A wreck's wheels are separate bodies and have to move too.
    private readonly List<(FName Bone, Vector3 At, Quaternion Turn)> _carryBodies = new();

    private static readonly string[] LetGoWords = { "throw", "release", "drop", "launch", "detach", "impact" };
    private static readonly Dictionary<nint, float> _letGo = new();

    // Taken at pickup. Its middle, size and turn from his, eased into his hands from there.
    private void StartCarry(Actor thing, Kind kind)
    {
        (Vector3 centre, Vector3 half) = HoldBox(thing);
        Geo.Axes(thing.Rotation, out Vector3 x, out Vector3 y, out Vector3 z);
        Vector3 off = centre - thing.Location;
        _carryPivot = new Vector3(Vector3.Dot(off, x), Vector3.Dot(off, y), Vector3.Dot(off, z));
        _carryHalf = half;

        // A car goes up sideways, across his shoulders, whichever side is nearer.
        float yaw = Geo.WrapDegrees(thing.Rotation.Yaw - _faceYaw);
        if (kind is Kind.Car or Kind.Parked || thing is Vehicle)
            yaw = yaw >= 0f ? 90f : -90f;
        _carryYaw = yaw;

        Vector3 fwd = Geo.Facing(_faceYaw), right = Geo.Facing(_faceYaw + 90f);
        Vector3 Local(Vector3 v) => new(Vector3.Dot(v, fwd), Vector3.Dot(v, right), v.Z);
        if (World.Player is { } pawn)
            _carryAt = Local(centre - pawn.Location);
        _carryFwd = Local(x);
        _carryUp = Local(z);
        _carryEaseAt = Clock.Now;

        _carryBodies.Clear();
        if (Part(thing) is SkeletalMeshComponent { IsValid: true } mesh)
            Safe.Run("carry bodies", () => TakeBodies(mesh, thing.Location, Turn(x, y, z)));
    }

    private void TakeBodies(SkeletalMeshComponent mesh, Vector3 from, Quaternion turn)
    {
        if (mesh.PhysicsAsset is not { IsValid: true } asset || mesh.PhysicsAssetInstance is not { IsValid: true } inst)
            return;

        Quaternion back = Quaternion.Inverse(turn);
        int i = 0;
        foreach (RB_BodySetup setup in asset.BodySetup)
        {
            if (i < inst.Bodies.Count && inst.Bodies[i] is { IsValid: true } body && body.IsValidBodyInstance() && body.IsFixed())
                body.SetFixed(false);
            i++;
            if (setup is not { IsValid: true } || mesh.MatchRefBone(setup.BoneName) < 0)
                continue;

            Vector3 at = Vector3.Transform(mesh.GetBoneLocation(setup.BoneName, 0) - from, back);
            GameObject.FQuat bq = mesh.GetBoneQuaternion(setup.BoneName, 0);
            Quaternion q = back * new Quaternion(bq.X, bq.Y, bq.Z, bq.W);
            _carryBodies.Add((setup.BoneName, at, q));
        }

        mesh.WakeRigidBody();
    }

    // A turn whose X, Y and Z axes are these.
    private static Quaternion Turn(Vector3 x, Vector3 y, Vector3 z) =>
        Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f)));

    private static Rotator Rot(Quaternion q) =>
        Geo.FromAxes(Vector3.Transform(Vector3.UnitX, q), Vector3.Transform(Vector3.UnitY, q), Vector3.Transform(Vector3.UnitZ, q));

    private void CarryStep(RPawnPlayer pawn, Actor thing, PrimitiveComponent part)
    {
        Vector3 fwd = Geo.Facing(_faceYaw), right = Geo.Facing(_faceYaw + 90f);
        Vector3 ToWorld(Vector3 l) => fwd * l.X + right * l.Y + Vector3.UnitZ * l.Z;
        float height = pawn.GetCollisionHeight();

        Vector3 hands = pawn.Location + Vector3.UnitZ * (height * 0.8f);
        if (pawn.Mesh is { IsValid: true } mesh && HandBone(mesh, false) is { } r && HandBone(mesh, true) is { } l)
        {
            pawn.ForceUpdateComponents(false, false);
            mesh.ForceSkelUpdate();
            hands = (mesh.GetBoneLocation(r, 0) + mesh.GetBoneLocation(l, 0)) * 0.5f;
        }

        if (_throwing)
        {
            Vector3 rel = hands - pawn.Location;
            TrackThrowHands(new Vector3(Vector3.Dot(rel, fwd), Vector3.Dot(rel, right), rel.Z));
        }

        // It rests on his hands.
        Vector3 centre = hands + Vector3.UnitZ * MathF.Max(0f, _carryHalf.Z - CarryGrip);

        Vector3 want = centre - pawn.Location;
        want = new Vector3(Vector3.Dot(want, fwd), Vector3.Dot(want, right), want.Z);
        double now = Clock.Now;
        float dt = (float)Math.Clamp(now - _carryEaseAt, 0.0, 0.1);
        _carryEaseAt = now;
        float k = Ease.Follow(_throwing ? CarryThrowEase : CarryEase, dt);
        _carryAt = Vector3.Lerp(_carryAt, want, k);

        // Turned towards its yaw from his and levelled, at the same ease.
        Vector3 fx = Vector3.Normalize(Vector3.Lerp(_carryFwd, Geo.Facing(_carryYaw), k));
        Vector3 fz = Vector3.Lerp(_carryUp, Vector3.UnitZ, k);
        fz -= fx * Vector3.Dot(fz, fx);
        fz = fz.LengthSquared() > 1e-6f ? Vector3.Normalize(fz) : Vector3.UnitZ;
        _carryFwd = fx;
        _carryUp = fz;

        Vector3 x = ToWorld(fx), z = ToWorld(fz), y = Vector3.Cross(z, x);
        Vector3 at = pawn.Location + ToWorld(_carryAt);
        Vector3 origin = at - (x * _carryPivot.X + y * _carryPivot.Y + z * _carryPivot.Z);

        if (Vector3.Distance(HoldBox(thing).Centre, at) > HoldLost)
        {
            _held = null;
            return;
        }

        part.WakeRigidBody();
        if (_carryBodies.Count > 0)
        {
            Quaternion turn = Turn(x, y, z);
            foreach ((FName bone, Vector3 off, Quaternion q) in _carryBodies)
            {
                part.SetRBPosition(origin + Vector3.Transform(off, turn), bone);
                part.SetRBRotation(Rot(turn * q), bone);
            }
        }
        else
        {
            part.SetRBPosition(origin, default);
            part.SetRBRotation(Geo.FromAxes(x, y, z), default);
        }
        // Moving with him between frames, so it doesn't sag or jump when let go.
        part.SetRBLinearVelocity(_fm.Velocity, false);
        part.SetRBAngularVelocity(Vector3.Zero, false);
    }

    // Middle and half size. Cars with stray bounds use their origin instead.
    private static (Vector3 Centre, Vector3 Half) HoldBox(Actor thing)
    {
        (Vector3 centre, Vector3 half) = Box(thing);
        if (half.X <= CarryHalfMost.X && half.Y <= CarryHalfMost.Y && half.Z <= CarryHalfMost.Z)
            return (centre, half);
        return (thing.Location, Vector3.Min(half, CarryHalfMost));
    }

    // Where the anim lets go of what's in hand, as a fraction. -1 when it has none.
    private static float LetGoPoint(AnimSequence seq)
    {
        if (_letGo.TryGetValue(seq.Ptr, out float known))
            return known;

        float named = -1f, prop = -1f;
        foreach (AnimSequence.FAnimNotifyEvent e in seq.Notifies)
        {
            if (e.Notify is not { IsValid: true } notify)
                continue;

            string name = notify is AnimNotify_Script script ? script.NotifyName.ToString() : "";
            if (e.Time < 0.05f)
                continue;
            if (named < 0f && LetGoWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                named = e.Time;
            if (notify is RAnimNotify_PickupProp)
                prop = e.Time;
        }

        float time = named >= 0f ? named : prop;
        float at = time >= 0f ? Math.Clamp(time / MathF.Max(seq.SequenceLength, 0.01f), 0f, 1f) : -1f;
        _letGo[seq.Ptr] = at;
        return at;
    }
}
