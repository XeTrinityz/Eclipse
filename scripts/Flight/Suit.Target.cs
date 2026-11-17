using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.BmScript;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Targeting in flight. The thing nearest the crosshair is bracketed, others in reach get a faint mark.
internal sealed partial class Suit
{
    private const float TargetReach = 6000f;
    private const float PropReach = 2500f;
    private const float TargetCone = 10f;
    // Keeps the current target unless another scores this much better, so it doesn't flicker.
    private const float TargetKeep = 2.5f;
    // Thugs are what he's there for, so they win over a car about as close to the crosshair.
    private const float ThugWeight = 0.6f;
    private const float MarkCone = 55f;
    // Things the aim line passes through beat everything else, nearest first.
    private const float OnItSlack = 15f;
    private const float OffItScore = 5f;
    private const double TargetScanSeconds = 0.25;
    // Things that stay put are looked for this often, or sooner once he moves this far.
    private const double StillScanSeconds = 1.0;
    private const float StillScanMoved = 1000f;
    private const int SightChecks = 3;

    internal enum Kind
    {
        Thug,
        Car,
        Parked,
        Prop,
        Thing,
        Drone,
    }

    internal readonly record struct Mark(Actor Thing, Kind Kind, Vector3 Centre, Vector3 Half, float Distance);

    private readonly List<(Actor Thing, Kind Kind)> _near = new();
    private readonly List<(Actor Thing, Kind Kind)> _still = new();
    private Every _stillScan;
    private Vector3 _stillAt;
    private readonly List<Mark> _marks = new();
    private readonly List<(Mark Mark, float Score, bool OnIt)> _ranked = new();
    private Every _targetScan;
    private Mark? _aim;

    public Mark? Aim => _flying ? _aim : null;

    public IReadOnlyList<Mark> Marks => _flying ? _marks : Array.Empty<Mark>();

    private void ResetTargets()
    {
        _near.Clear();
        _still.Clear();
        _marks.Clear();
        _aim = null;
        _targetScan.Force();
        _stillScan.Force();
    }

    private void TargetStep(RPawnPlayer pawn)
    {
        if (Hooks.PlayerCam() is not { } cam)
        {
            _aim = null;
            _marks.Clear();
            return;
        }

        if (_targetScan.Elapsed(TargetScanSeconds))
            ScanTargets(pawn);

        BmSDK.GameObject.FTPOV view = ViewPov(cam);
        Vector3 eye = view.Location;
        Vector3 look = Geo.Dir(view.Rotation.Yaw, Geo.WrapDegrees(view.Rotation.Pitch));
        _marks.Clear();
        _ranked.Clear();
        float kept = float.MaxValue;
        bool keptOnIt = false;
        Mark? keep = null;
        // Props and loose objects are only for the shockwave. Only enemies and vehicles are targets.
        foreach ((Actor thing, Kind kind) in _near)
        {
            if (kind is Kind.Prop or Kind.Thing || !Usable(thing, kind) || IsHeld(thing))
                continue;

            (Vector3 centre, Vector3 half) = HoldBox(thing);
            float dist = Vector3.Distance(centre, pawn.Location);
            if (dist > TargetReach)
                continue;

            Vector3 to = centre - eye;
            float len = to.Length();
            if (len < 1f)
                continue;

            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(to / len, look), -1f, 1f)) * Geo.Deg;
            if (angle > MarkCone)
                continue;

            var mark = new Mark(thing, kind, centre, half, dist);
            _marks.Add(mark);

            // Scored mostly by angle to its middle, then distance, with thugs first.
            float edge = MathF.Atan(MathF.Max(half.X, half.Y) / len) * Geo.Deg;
            if (angle - edge > TargetCone)
                continue;

            bool onIt = RayEnter(eye, look, centre, half + new Vector3(OnItSlack), out _);
            float score;
            if (onIt)
            {
                score = len / TargetReach;
            }
            else
            {
                score = MathF.Max(0f, angle - edge * 0.3f) + dist / TargetReach * 4f;
                if (kind == Kind.Thug)
                    score *= ThugWeight;
                score += OffItScore;
            }

            _ranked.Add((mark, score, onIt));
            if (_aim is { } was && was.Thing.Ptr == thing.Ptr)
            {
                kept = score;
                keptOnIt = onIt;
                keep = mark;
            }
        }

        _ranked.Sort((a, b) => a.Score.CompareTo(b.Score));
        Mark? best = null;
        float bestScore = float.MaxValue;
        bool bestOnIt = false;
        int checks = 0;
        foreach ((Mark mark, float score, bool onIt) in _ranked)
        {
            if (checks++ >= SightChecks)
                break;
            if (!InSight(pawn, eye, mark))
                continue;

            best = mark;
            bestScore = score;
            bestOnIt = onIt;
            break;
        }

        // Keep the bracket against a slightly better score, unless the dot is on the other one.
        if (keep is { } k && kept < bestScore + TargetKeep && (keptOnIt || !bestOnIt) && InSight(pawn, eye, k))
            best = k;
        _aim = best;
    }

    // Where a line from the eye first enters the box in front of it, if it does.
    private static bool RayEnter(Vector3 eye, Vector3 look, Vector3 centre, Vector3 half, out float t)
    {
        float near = 0f, far = float.MaxValue;
        t = 0f;
        for (int i = 0; i < 3; i++)
        {
            float o = i == 0 ? eye.X : i == 1 ? eye.Y : eye.Z;
            float d = i == 0 ? look.X : i == 1 ? look.Y : look.Z;
            float c = i == 0 ? centre.X : i == 1 ? centre.Y : centre.Z;
            float h = i == 0 ? half.X : i == 1 ? half.Y : half.Z;
            if (MathF.Abs(d) < 1e-6f)
            {
                if (o < c - h || o > c + h)
                    return false;
                continue;
            }

            float a = (c - h - o) / d, b = (c + h - o) / d;
            near = MathF.Max(near, MathF.Min(a, b));
            far = MathF.Min(far, MathF.Max(a, b));
            if (near > far)
                return false;
        }

        t = near;
        return true;
    }

    // Each kind is a pass over everything in memory, so static ones are looked for less often.
    private void ScanTargets(RPawnPlayer pawn)
    {
        Vector3 at = pawn.Location;
        float reach = TargetReach * TargetReach;
        if (_stillScan.Elapsed(StillScanSeconds) || Vector3.DistanceSquared(at, _stillAt) > StillScanMoved * StillScanMoved)
            ScanStill(at, reach);

        _near.Clear();
        foreach (RPawnVillain thug in Game.FindObjects<RPawnVillain>())
        {
            if (thug is { IsValid: true, IsClassDefaultObject: false } && Vector3.DistanceSquared(thug.Location, at) < reach)
                _near.Add((thug, Kind.Thug));
        }

        foreach (RVehicle car in Game.FindObjects<RVehicle>())
        {
            if (car is { IsValid: true, IsClassDefaultObject: false } and not RVehicleBatmobileBase && Vector3.DistanceSquared(car.Location, at) < reach)
                _near.Add((car, car is RVehicleFlying ? Kind.Drone : Kind.Car));
        }

        // Only the small ones. The big helicopters are story pieces.
        foreach (RHelicopterBase heli in Game.FindObjects<RHelicopterBase>())
        {
            if (heli is RPredatorDrone or RPredatorDroneMini && heli is { IsValid: true, IsClassDefaultObject: false }
                && Vector3.DistanceSquared(heli.Location, at) < reach)
                _near.Add((heli, Kind.Drone));
        }

        _near.AddRange(_still);
    }

    private void ScanStill(Vector3 at, float reach)
    {
        _still.Clear();
        _stillAt = at;
        float propReach = PropReach * PropReach;
        foreach (SkeletalMeshActor parked in Game.FindObjects<SkeletalMeshActor>())
        {
            if (parked is RAbandonedVehicle or RDeadVehicle && parked is { IsValid: true, IsClassDefaultObject: false }
                && Vector3.DistanceSquared(parked.Location, at) < reach)
                _still.Add((parked, Kind.Parked));
        }

        foreach (KActor thing in Game.FindObjects<KActor>())
        {
            if (thing is { IsValid: true, IsClassDefaultObject: false } && Vector3.DistanceSquared(thing.Location, at) < reach)
                _still.Add((thing, Kind.Thing));
        }

        foreach (RDestructibleProp prop in Game.FindObjects<RDestructibleProp>())
        {
            if (prop is { IsValid: true, IsClassDefaultObject: false } && Vector3.DistanceSquared(prop.Location, at) < propReach)
                _still.Add((prop, Kind.Prop));
        }

        foreach (RSentryGunBase sentry in Game.FindObjects<RSentryGunBase>())
        {
            if (sentry is { IsValid: true, IsClassDefaultObject: false } && Vector3.DistanceSquared(sentry.Location, at) < reach)
                _still.Add((sentry, Kind.Drone));
        }

        foreach (RTurretBase turret in Game.FindObjects<RTurretBase>())
        {
            if (turret is { IsValid: true, IsClassDefaultObject: false } && Vector3.DistanceSquared(turret.Location, at) < reach)
                _still.Add((turret, Kind.Drone));
        }
    }

    // Still there and worth hitting. A downed thug can't be punched or grabbed, so he'd only block the next one.
    private static bool Usable(Actor thing, Kind kind)
    {
        if (!thing.IsValid || thing.bDeleteMe || thing.bHidden)
            return false;

        return kind switch
        {
            Kind.Thug => thing is RPawnVillain thug && ThugUp(thug),
            Kind.Car => thing is RVehicle { Health: > 0 },
            Kind.Parked => thing is not RAbandonedVehicle { Health: <= 0 },
            Kind.Prop => thing is RDestructibleProp { Health: > 0 } prop && prop.bBlockActors,
            Kind.Drone => DroneUp(thing),
            _ => true,
        };
    }

    private static bool Foe(RPawnVillain thug) =>
        thug.CombatPawnType is not (RPawnCombat.ECombatPawnType.ECPT_Player or RPawnCombat.ECombatPawnType.ECPT_Friendly);

    private static bool Punchable(Mark mark) => mark.Kind != Kind.Thug || ThugUp((RPawnVillain)mark.Thing);

    // Only grabbed on his feet. Grabbing a ragdoll broke both of them.
    internal static bool Grabbable(Mark mark) =>
        mark.Kind is Kind.Car or Kind.Parked or Kind.Thing || (mark.Kind == Kind.Thug && mark.Thing is RPawnVillain thug && ThugUp(thug) && !thug.IsRagdoll());

    // Middle and half size. His cylinder while standing, his body's bounds as a ragdoll.
    internal static (Vector3 Centre, Vector3 Half) Box(Actor thing)
    {
        if (thing is Pawn { CylinderComponent: { IsValid: true } cyl } pawn and not Vehicle && !(pawn is RPawnVillain v && v.IsRagdoll()))
            return (pawn.Location, new Vector3(cyl.CollisionRadius, cyl.CollisionRadius, cyl.CollisionHeight));

        PrimitiveComponent? part = Part(thing);
        if (part is { IsValid: true })
            return (part.Bounds.Origin, part.Bounds.BoxExtent);
        return (thing.Location, new Vector3(50f, 50f, 50f));
    }

    // The part physics moves.
    private static PrimitiveComponent? Part(Actor thing) => thing switch
    {
        Pawn pawn => pawn.Mesh,
        SkeletalMeshActor skel => skel.SkeletalMeshComponent,
        _ => thing.CollisionComponent,
    };

    // Nothing of the level between the camera and it.
    private static bool InSight(RPawnPlayer pawn, Vector3 eye, Mark mark)
    {
        Actor? hit = Ground.Trace(pawn, eye, mark.Centre, Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out Vector3 at, out _);
        if (hit == null || hit.Ptr == mark.Thing.Ptr)
            return true;

        // Hit something just short of it, like its own collision or the ground it stands on.
        return Vector3.Distance(at, mark.Centre) < MathF.Max(mark.Half.X, MathF.Max(mark.Half.Y, mark.Half.Z)) + 100f;
    }
}
