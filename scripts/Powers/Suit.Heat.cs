using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Heat vision. Two laser beams from his eyes that destroy what they touch.
internal sealed partial class Suit
{
    private const float HeatReach = 8000f;
    private const float BurnKnock = 1500f;
    private const float BurnThrow = 900f;
    private const float MeltReach = 70f;
    private const float MeltForce = 300f;
    private const float BeamPush = 250f;
    private const float WreckPush = 800f;

    // Each burst has its own flash and light. Any faster and they piled up into a white out.
    private const double SparkEvery = 0.45;
    private const float BeamClear = 60f;
    // How far round from the way he faces the beams still fire, as a cosine. About 75 degrees.
    private const float BeamFrontLeast = 0.25f;
    private const int BeamTraceMost = 10;
    // The same thing isn't hit again for this long. Melting and pushing repeat much sooner.
    private const double SearAgain = 1.0;
    private const double MeltAgain = 0.1;
    private const double DropAgain = 0.4;
    private static readonly FName[] SpineBones = { new("Bip01_Spine2"), new("Bip01_Spine1"), new("Bip01_Spine") };
    // The beam still hits a thug or car this close to it, more the further off it is.
    private const float AimSlack = 15f, AimSlackPerUnit = 0.015f, AimSlackMost = 120f;

    private readonly Dictionary<nint, double> _seared = new();
    // Fires the beam leaves. Each burns out, spaced apart and capped.
    private const float FireApart = 110f;
    private const double FireSeconds = 4.5;
    private const int FiresMost = 8;
    // Room left in the effect pool for the beams themselves and the game's own effects.
    private const int PoolSpare = 8;
    // How far the beam's end can move and still count as resting, and how near him a fire can be.
    private const float FireSteady = 200f;
    private const float FireClear = 350f;
    // A thug still counts as up if his head is this far off the ground.
    private const float HeadUp = 90f;

    private readonly List<(Vector3 At, double Until)> _fires = new();
    private double _fireAt;
    private Vector3 _lastEnd;
    private bool _lastLanded;
    // The beam's end came from the trace, not from a target's box.
    private bool _endTraced;
    private readonly FxBeam _beamLeft = new(Fx.LaserBeam), _beamRight = new(Fx.LaserBeam);
    private Every _sparks;
    private Every _heatScan;

    private void HeatStep(RPawnPlayer pawn, RPlayerController pc)
    {
        if (!AimRay(out Vector3 eye, out Vector3 look))
        {
            HideBeams();
            return;
        }

        // Aimed behind him there's no beam until he's turned.
        var flat = new Vector3(look.X, look.Y, 0f);
        if (flat.LengthSquared() > 0.04f && Vector3.Dot(Vector3.Normalize(flat), Geo.Facing(pawn.Rotation.Yaw)) < BeamFrontLeast)
        {
            HideBeams();
            return;
        }

        if (!_flying && _heatScan.Elapsed(TargetScanSeconds))
            ScanTargets(pawn);

        // Flying or running, his bones are still where they were before this frame's move.
        if ((_flying || _running) && pawn.Mesh is { IsValid: true } mesh)
        {
            pawn.ForceUpdateComponents(false, false);
            mesh.ForceSkelUpdate();
        }

        Actor? hit = BeamTarget(pawn, eye, look, out Vector3 end, out Vector3 normal, out bool landed);
        EyePoints(pawn, HeadAt(pawn), end, out Vector3 left, out Vector3 right);
        _beamLeft.Show(pawn, left, end);
        _beamRight.Show(pawn, right, end);

        if (landed && _sparks.Elapsed(SparkEvery))
            Own(Fx.BeamSparks, end, Fx.Facing(normal));
        // Fire only on a real trace hit where the beam has rested, never right by him.
        bool steady = landed && _endTraced && _lastLanded && Vector3.DistanceSquared(end, _lastEnd) < FireSteady * FireSteady;
        _lastLanded = landed && _endTraced;
        _lastEnd = end;
        if (steady && hit is not (Pawn or Vehicle or RAbandonedVehicle) && Vector3.DistanceSquared(end, pawn.Location) > FireClear * FireClear)
            LeaveFire(end);

        if (hit != null)
            Sear(pawn, pc, hit, end, Vector3.Normalize(end - left));
    }

    private void LeaveFire(Vector3 at)
    {
        // Paced and capped, since a full effect pool takes back the beams first.
        double now = Clock.Now;
        if (now - _fireAt < FireSeconds / FiresMost)
            return;

        _fires.RemoveAll(f => f.Until < now);
        if (_fires.Count >= FiresMost)
            return;

        foreach (var (other, _) in _fires)
        {
            if (Vector3.DistanceSquared(other, at) < FireApart * FireApart)
                return;
        }

        if (!Own(Fx.GroundFire, at, default, FiresMost))
            return;

        _fires.Add((at, now + FireSeconds));
        _fireAt = now;
    }

    // On his feet, alive or not. Health alone missed thugs left standing at zero.
    private static bool OnFeet(RPawnVillain thug)
    {
        if (thug is not { IsValid: true, bDeleteMe: false, bHidden: false } || InCar(thug) || !Foe(thug) || thug.IsRagdoll())
            return false;
        if (ThugUp(thug))
            return true;
        if (thug.Mesh is not { IsValid: true } mesh || FindBone(mesh, "head", HeadBones, "Head") is not { } head)
            return false;

        float feet = thug.Location.Z - thug.GetCollisionHeight();
        return mesh.GetBoneLocation(head, 0).Z - feet > HeadUp;
    }

    private void HideBeams()
    {
        _beamLeft.Hide();
        _beamRight.Hide();
        _lastLanded = false;
    }

    // Where the beam ends. A trace finds the level or a car, then the nearest thug on the line takes it.
    private Actor? BeamTarget(RPawnPlayer pawn, Vector3 eye, Vector3 look, out Vector3 end, out Vector3 normal, out bool landed)
    {
        // The camera sits behind him. Nothing between it and his head is in the beam's way.
        float front = MathF.Max(0f, Vector3.Dot(HeadAt(pawn) - eye, look)) + BeamClear;
        Actor? struck = BeamTrace(pawn, eye, look, front, out float dist, out normal);
        landed = struck != null;

        Actor? best = null;
        float bestAt = float.MaxValue;
        Vector3 bestEnd = default;
        foreach ((Actor thing, Kind kind) in _near)
        {
            // A thug already down would only soak up the beam.
            if (kind != Kind.Thug || IsHeld(thing) || thing is not RPawnVillain thug || !OnFeet(thug))
                continue;

            (Vector3 centre, Vector3 half) = Box(thing);
            float along = Vector3.Dot(centre - eye, look);
            float slack = MathF.Min(AimSlack + MathF.Max(along, 0f) * AimSlackPerUnit, AimSlackMost);
            if (!RayEnter(eye, look, centre, half + new Vector3(slack), out float t) || t <= front || t >= dist + 50f || t >= bestAt)
                continue;

            best = thing;
            bestAt = t;
            // Where the aim line enters it, or the nearest point on a near miss.
            bestEnd = RayEnter(eye, look, centre, half, out float real)
                ? eye + look * real
                : Vector3.Clamp(eye + look * along, centre - half, centre + half);
        }

        _endTraced = best == null;
        if (best != null)
        {
            end = bestEnd;
            normal = -look;
            landed = true;
            return best;
        }

        end = eye + look * dist;
        return struck is { } s && (!s.bWorldGeometry || Breakable(s)) ? s : null;
    }

    // The first solid thing along the aim past Batman. Repeats jump further to get clear.
    private Actor? BeamTrace(RPawnPlayer pawn, Vector3 eye, Vector3 look, float front, out float dist, out Vector3 normal)
    {
        dist = HeatReach;
        normal = -look;
        var seen = new HashSet<nint>();
        float step = 5f;
        Vector3 from = eye, far = eye + look * HeatReach;
        for (int i = 0; i < BeamTraceMost; i++)
        {
            Actor? hit = Ground.Trace(pawn, from, far, Component.ECollisionFilter.COF_TraceActors, Vector3.Zero, out Vector3 at, out Vector3 n);
            if (hit == null)
                return null;

            float along = Vector3.Dot(at - eye, look);
            bool solid = hit is not Volume && (hit.bWorldGeometry || (!hit.bHidden && (hit.bBlockActors || hit is Pawn)));
            if (solid && along >= front && hit.Ptr != pawn.Ptr && !IsHeld(hit))
            {
                dist = along;
                normal = n;
                return hit;
            }

            if (!seen.Add(hit.Ptr))
                step = MathF.Min(step * 2f, 400f);
            from = eye + look * (MathF.Max(along, Vector3.Dot(from - eye, look)) + step);
            if (Vector3.Dot(far - from, look) <= 0f)
                return null;
        }

        return null;
    }

    // What the beam does to what it's on. Thugs, cars and props go on the first touch.
    private void Sear(RPawnPlayer pawn, RPlayerController pc, Actor hit, Vector3 at, Vector3 dir)
    {
        // A driver pokes out of his car. The car takes the beam.
        if (hit is Pawn { DrivenVehicle: { IsValid: true } driven })
            hit = driven;
        else if (hit is Pawn { Base: Vehicle { IsValid: true } carried })
            hit = carried;

        double now = Clock.Now;
        if (_seared.TryGetValue(hit.Ptr, out double next) && now < next)
            return;

        float force = Opt.PowerForce.Number;
        double again = SearAgain;
        bool loud = true;
        switch (hit)
        {
            case RVehicleBatmobileBase:
                return;

            // Knocked down but still alive, he'd get up again. The beam finishes him where he lies.
            case RPawnVillain downed when !OnFeet(downed):
                if (downed.Health <= 0 || !Foe(downed) || InCar(downed))
                    return;
                Safe.Run("heat finish", downed.KillMe);
                if (downed.Health > 0 && Log.Due("heat finish " + downed.Class.Name))
                    Log.Info("Heat vision couldn't finish a downed " + downed.Class.Name + ".");
                if (downed.Mesh is { IsValid: true } lying && FindBone(lying, "chest", SpineBones, "Spine", "Pelvis") is { } chest)
                    Fx.SpawnOn(Fx.BodyFire, lying, chest);
                Fx.Play(downed, Fx.BurnSound);
                loud = false;
                break;

            case RPawnVillain thug:
                bool first = thug.Health > 0;
                if (first)
                {
                    RamThug(thug, pawn, pc, dir, BurnKnock * force, 1f, at);
                    // Armoured and shielded ones shrug that hit off. The game's own kill takes any of them.
                    if (thug.Health > 0)
                        Safe.Run("heat kill", thug.KillMe);
                    if (thug.Health > 0 && Log.Due("heat " + thug.Class.Name))
                        Log.Info("Heat vision couldn't put down a " + thug.Class.Name + ".");
                }

                // Out of health but standing. The ragdoll kill drops him and the throw keeps trying.
                if (!thug.IsRagdoll())
                    Safe.Run("heat drop", thug.RagdollKill);
                QueueThrow(thug, (dir + Vector3.UnitZ * 0.3f) * (BurnThrow * force));
                again = DropAgain;
                loud = false;
                if (!first)
                    break;

                HitSound(pawn, thug, finishing: true, heavy: false);
                Own(Fx.Ignite, Box(thug).Centre, Fx.Facing(-dir));
                if (thug.Mesh is { IsValid: true } body && FindBone(body, "chest", SpineBones, "Spine", "Pelvis") is { } spine)
                    Fx.SpawnOn(Fx.BodyFire, body, spine);
                Fx.Play(thug, Fx.BurnSound);
                break;

            case RVehicle { Health: <= 0 } burnt:
                FreeCar(burnt);
                Kick(burnt.Mesh, Lifted(dir, 0.5f) * (WreckPush * force), 0.5f);
                loud = false;
                break;

            case RVehicle car:
                car.TakeDamageFromWeapon(car.Health + 1, pc, at, dir * 2000f, Vector3.Zero, RDmgType_BatmobileCannon.StaticClass(), default, pawn, false);
                Own(Fx.Fireball, Box(car).Centre, default);
                break;

            case RAbandonedVehicle parked when parked.Health > 0:
                parked.TakeDamage(parked.Health + 1, pc, at, dir * 2000f, RDmgType_BatmobileCannon.StaticClass(), default, pawn);
                Own(Fx.Fireball, Box(parked).Centre, default);
                break;

            // Already wrecked. The beam shoves it along.
            case RDeadVehicle wreck:
                Wake(wreck);
                Kick(wreck.SkeletalMeshComponent, Lifted(dir, 0.5f) * (WreckPush * force), 0.5f);
                break;

            case RDestructibleProp prop:
                BreakProp(prop, pawn, pc, dir, BurnKnock * force, 1f, at);
                break;

            case RDestructibleActor:
                hit.TakeDamage(2000, pc, at, dir * 2000f, RDmgType_BatmobileCannon.StaticClass(), default, pawn);
                break;

            case FracturedStaticMeshActor fractured:
                fractured.BreakOffPartsInRadius(at, MeltReach, MeltForce * force, true, pawn, true, -dir * 30f);
                again = MeltAgain;
                loud = false;
                break;

            case KActor:
                Kick(hit.CollisionComponent, dir * (BeamPush * force), 0f);
                again = MeltAgain;
                loud = false;
                break;

            default:
                return;
        }

        _seared[hit.Ptr] = now + again;
        if (!loud)
            return;

        Own(Fx.BeamSparks, at, Fx.Facing(-dir));
        Thud(pawn, heavy: false);
    }

    // Forgets what was hit once it can be hit again.
    private void SearedStep()
    {
        if (_seared.Count < 32)
            return;

        double now = Clock.Now;
        foreach (nint old in _seared.Where(s => s.Value < now).Select(s => s.Key).ToList())
            _seared.Remove(old);
    }
}
