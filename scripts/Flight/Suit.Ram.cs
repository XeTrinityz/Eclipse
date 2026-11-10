using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// He hits like the Batmobile. Thugs launch, props break and cars get shoved. Speed sets how hard.
internal sealed partial class Suit
{
    // Speeds in units a second. Hits throw at least as hard as Floor and reach full at Wreck.
    private const float RamTouch = 50f;
    private const float RamFloor = 1000f;
    private const float RamWreck = 2500f;

    // How far past this frame's move the sweep looks. Further and cars got shoved from a gap.
    private const float RamLead = 10f;
    // Below this his body does the shoving, so loose things aren't kicked from here.
    private const float RamKickFrom = 1500f;
    private const float RamUp = 0.25f;
    private const float RamCarKick = 1.3f;
    private const float RamThugKick = 1.5f;
    private const float RamSpin = 1.5f;
    private const double RamAgain = 0.3;
    private const double RamPassSeconds = 0.25;
    private const float WreckAt = 0.9f;
    private const float ShatterAt = 0.6f;
    private const float FractureReach = 120f, FractureReachFast = 180f;

    private readonly Dictionary<nint, double> _rammed = new();
    private double _ramPassUntil;

    private void ResetRam()
    {
        _rammed.Clear();
        _ramPassUntil = 0.0;
    }

    // Just after a hit, being blocked keeps his speed, since the target only moves next physics step.
    private bool RamPassing => Clock.Now < _ramPassUntil;

    // Looks along where he's going this frame, plus a little lead, for something to hit.
    private void RamStep(RPawnPlayer pawn, Vector3 delta)
    {
        if (!Opt.RamOn || World.Pc is not { } pc)
            return;

        // Use the speed he's trying to go, since being blocked keeps his real speed near zero.
        Vector3 v = _fm.Velocity;
        if (_fm.Wanted.LengthSquared() > v.LengthSquared())
            v = _fm.Wanted;
        RamAlong(pawn, pc, v, delta, true);
    }

    // On foot while heavy. Thugs are left alone in a fight, or a crowd would go flying.
    private void RamFoot(RPawnPlayer pawn, float dt)
    {
        if (!Opt.RamOn || !Opt.HeavyOn || World.Pc is not { } pc || pawn.Physics == Actor.EPhysics.PHYS_None)
            return;

        Vector3 v = FootVelocity(pawn);
        RamAlong(pawn, pc, v, v * dt, !InFight(pc));
    }

    // Use how he's pushing the stick, at least a walk, since being blocked zeroes his speed.
    private static Vector3 FootVelocity(RPawnPlayer pawn)
    {
        Vector3 v = pawn.Velocity;
        v.Z = 0f;
        Vector3 push = pawn.Acceleration;
        push.Z = 0f;
        if (push.LengthSquared() > 1f)
            v = Vector3.Normalize(push) * MathF.Max(v.Length(), FootPush);
        return v;
    }

    // The game's bump is the surest sign he's pressed against a car, so the shove starts here too.
    [Redirect(typeof(RPawnPlayer), nameof(RPawnPlayer.Bump))]
    private static void PlayerBumpRedirect(RPawnPlayer self, Actor other, PrimitiveComponent otherComp, Vector3 hitNormal)
    {
        self.Bump(other, otherComp, hitNormal);
        if (IsBatman(self))
            Safe.Run("bump shove", () => I.BumpShove(self, other, hitNormal));
    }

    // Anything he bumps gets the ram. On foot thugs are only knocked aside outside a fight.
    private void BumpShove(RPawnPlayer pawn, Actor? other, Vector3 normal)
    {
        if (!Opt.RamOn || !Opt.HeavyOn || other is not { IsValid: true, bDeleteMe: false } || World.Pc is not { } pc)
            return;
        if (other.bWorldGeometry && !Breakable(other))
            return;

        Vector3 v = _flying ? (_fm.Wanted.LengthSquared() > _fm.Velocity.LengthSquared() ? _fm.Wanted : _fm.Velocity) : FootVelocity(pawn);
        v.Z = 0f;
        float speed = v.Length();
        if (speed < RamTouch)
            return;

        // Only when he's pushing into it, not brushing past.
        Vector3 dir = v / speed;
        var face = new Vector3(normal.X, normal.Y, 0f);
        if (face.LengthSquared() > 0.01f && Vector3.Dot(dir, Vector3.Normalize(face)) > -0.3f)
            return;

        RamHit(pawn, pc, other, dir, speed, _flying || !InFight(pc), pawn.Location + dir * pawn.GetCollisionRadius());
    }

    private static bool InFight(RPlayerController pc) =>
        pc.GetStateName().ToString().Contains("Combat", StringComparison.OrdinalIgnoreCase) || World.Player is { CurrentSpecialMove: { IsValid: true } };

    private void RamAlong(RPawnPlayer pawn, RPlayerController pc, Vector3 v, Vector3 delta, bool thugs)
    {
        float speed = v.Length();
        if (speed < RamTouch)
            return;

        Vector3 dir = v / speed;
        float radius = pawn.GetCollisionRadius(), half = pawn.GetCollisionHeight();
        // A narrow box from his middle, since a wide one started inside cars and missed them.
        float wide = radius * SweepWide;
        // Starts above his feet so flying low doesn't drag it along kerbs.
        Vector3 from = pawn.Location + Vector3.UnitZ * (half * SweepRaise);
        Vector3 to = from + dir * (delta.Length() + RamLead + radius - wide);
        Actor? hit = Ahead(pawn, pc, from, to, dir, new Vector3(wide, wide, half * SweepTall), out Vector3 at);
        if (hit != null)
            RamHit(pawn, pc, hit, dir, speed, thugs, at == Vector3.Zero ? hit.Location : at);
    }

    private void RamHit(RPawnPlayer pawn, RPlayerController pc, Actor hit, Vector3 dir, float speed, bool thugs, Vector3 at)
    {
        if (hit is Pawn { DrivenVehicle: { IsValid: true } driven })
            hit = driven;
        if (IsHeld(hit) || IsFoe(hit))
            return;
        if (IsRush(hit))
        {
            Arrive(pawn, pc, hit, dir);
            return;
        }

        // In a fight, a slow brush leaves the thug standing for the next punch.
        if (hit is RPawnVillain && _flying && speed < RamKickFrom && Brawling)
            return;

        if (_rammed.TryGetValue(hit.Ptr, out double last) && Clock.Now - last < RamAgain)
            return;

        float force = MathF.Max(speed, RamFloor) * Opt.RamForce.Number;
        if (!Ram(hit, pawn, pc, dir, speed, force, thugs, at))
            return;

        _rammed[hit.Ptr] = Clock.Now;
        _ramPassUntil = Clock.Now + RamPassSeconds;
        if (_flying || _running)
        {
            float jolt = Math.Clamp(speed / RamWreck, 0.15f, 1f);
            _fovKick = MathF.Max(_fovKick, jolt * 8f);
            _camKick = MathF.Max(_camKick, jolt);
        }
    }

    private const int SeeThroughMost = 12;
    // Lifts the sweep about 20 cm so it clears the ground but still sees a bench.
    private const float SweepRaise = 0.1f, SweepTall = 0.9f, SweepWide = 0.7f;

    // The first thing the sweep finds that's worth ramming.
    private static Actor? Ahead(RPawnPlayer pawn, RPlayerController pc, Vector3 from, Vector3 to, Vector3 dir, Vector3 extent, out Vector3 at)
    {
        at = Vector3.Zero;
        var seen = new HashSet<nint>();
        float step = 5f;
        for (int i = 0; i < SeeThroughMost; i++)
        {
            // Filtered as the player collides, so it only sees what would stop him.
            Actor? hit = Ground.Trace(pc, from, to, Component.ECollisionFilter.COF_PawnPlayer, extent, out at, out _);
            if (hit is not { bDeleteMe: false })
                return null;

            // Fracture meshes like benches count as part of the level, but they break.
            bool his = hit.Ptr == pawn.Ptr;
            // Something he's shoving has its blocking off for a moment, and still counts.
            if (!his && hit is not Volume && (hit.bBlockActors || Passing(hit)) && (!hit.bWorldGeometry || Breakable(hit)))
                return hit;

            // Skip himself, volumes, non-blocking things and the level. Repeats jump further to get clear.
            if (!seen.Add(hit.Ptr))
                step = MathF.Min(step * 2f, 400f);
            Vector3 next = (at == Vector3.Zero ? from : at) + dir * step;
            if (Vector3.Dot(to - next, dir) <= 0f)
                return null;
            from = next;
        }

        return null;
    }

    private static bool Breakable(Actor hit) => hit is FracturedStaticMeshActor or RDestructibleProp or RDestructibleActor;

    // False when it isn't something he rams.
    private bool Ram(Actor hit, RPawnPlayer pawn, PlayerController pc, Vector3 dir, float speed, float force, bool thugs, Vector3 at)
    {
        float hard = Math.Clamp((force - RamFloor) / (RamWreck - RamFloor), 0f, 1f);
        bool fast = speed >= RamKickFrom;

        // A driver can poke out of his car, so the hit goes to the car instead.
        if (hit is Pawn { DrivenVehicle: { IsValid: true } driven })
            hit = driven;
        else if (hit is Pawn { Base: Vehicle { IsValid: true } carried })
            hit = carried;

        // Slow hits leave loose things for his body to shove. Parked cars are woken so the shove works.
        if (!fast && Loose(hit))
        {
            Shove(hit, dir, speed);
            return false;
        }

        switch (hit)
        {
            case RVehicleBatmobileBase:
                return false;

            case RPawnVillain thug:
                if (!thugs || !ThugUp(thug))
                    return false;
                RamThug(thug, pawn, pc, dir, force, hard, at);
                return true;

            case RVehicle car:
                RamCar(car, pawn, pc, dir, force, hard, at);
                return true;

            case RDestructibleProp prop:
                BreakProp(prop, pawn, pc, dir, force, hard, at);
                return true;

            case RDestructibleActor:
                hit.TakeDamage((int)(1000f + 1000f * hard), pc, at, dir * force * 2f, RDmgType_BatmobileCannon.StaticClass(), default, pawn);
                Kick(hit.CollisionComponent, dir * force * RamCarKick, hard);
                return true;

            // Parked and wrecked cars are physics meshes, not vehicles.
            case RAbandonedVehicle parked:
                Wake(parked);
                RamParked(parked, pawn, pc, dir, force, hard, at);
                return true;

            case RDeadVehicle wreck:
                Wake(wreck);
                Kick(wreck.SkeletalMeshComponent, Lifted(dir, hard) * (force * RamCarKick), 0.3f + 0.7f * hard);
                wreck.TakeDamage((int)(100f + 400f * hard), pc, at, dir * force * 2f, RDmgType_BatmobileCannon.StaticClass(), default, pawn);
                return true;

            // A slow hit knocks out the pieces around where he hit it. A hard one shatters the lot.
            case FracturedStaticMeshActor fractured:
                if (hard >= ShatterAt)
                {
                    fractured.Explode(pawn);
                    return true;
                }

                float reach = FractureReach + FractureReachFast * hard;
                fractured.BreakOffPartsInRadius(at, reach, force * RamCarKick, true, pawn, true, -dir * 50f);
                fractured.TakeDamage((int)(1000f + 1000f * hard), pc, at, dir * force * 2f, RDmgType_BatmobileCannon.StaticClass(), default, pawn);
                return true;

            case KActor:
                Kick(hit.CollisionComponent, dir * force * RamCarKick, hard);
                return true;

            case SkeletalMeshActor body when body.Physics == Actor.EPhysics.PHYS_RigidBody:
                Kick(body.SkeletalMeshComponent, Lifted(dir, hard) * (force * RamCarKick), hard);
                return true;

            case { Physics: Actor.EPhysics.PHYS_RigidBody }:
                Kick(hit.CollisionComponent, Lifted(dir, hard) * (force * RamCarKick), hard);
                return true;

            default:
                return false;
        }
    }

    private static RDestructibleProp? _breaking;

    // Props are told they can take his damage, like the Batmobile's collision.
    [Redirect(typeof(RDestructibleProp), nameof(RDestructibleProp.CanTakeDamage))]
    private static bool CanTakeDamageRedirect(RDestructibleProp self, Class damageType, float damageAmount, out RDestructibleProp.EDamageSuppressed suppressed)
    {
        if (_breaking is { } prop && prop.Ptr == self.Ptr)
        {
            suppressed = RDestructibleProp.EDamageSuppressed.DamageSuppressed_False;
            return true;
        }

        return self.CanTakeDamage(damageType, damageAmount, out suppressed);
    }

    private void BreakProp(RDestructibleProp prop, RPawnPlayer pawn, PlayerController pc, Vector3 dir, float force, float hard, Vector3 at)
    {
        // Metal fences and rails want far more than a lamp post does.
        int damage = (int)(5000f + 5000f * hard);
        int stage = prop.CurrentDamageStage;
        float health = prop.Health;
        bool started = prop.bHasTakenInitialDamage;

        // Some barriers only break for an armoured vehicle. He counts as one.
        bool armoured = prop.InitialDamageOnlyByArmouredVehicles;
        if (armoured)
            prop.InitialDamageOnlyByArmouredVehicles = false;
        if (!prop.bCanBreakOnAnything)
            prop.AllowBreakOnAnything();

        bool Took() => prop.bDeleteMe || prop.CurrentDamageStage != stage || prop.Health != health || prop.bHasTakenInitialDamage != started;

        // The Batmobile's damage first, then the other kinds these barriers take.
        bool broke = false;
        Class[] kinds = { RDmgType_BatmobileCannon.StaticClass(), RDmgType_Barge.StaticClass(), RDmgType_BatmobileChainGun.StaticClass() };
        _breaking = prop;
        try
        {
            foreach (Class kind in kinds)
            {
                prop.TakeDamage(damage, pc, at, dir * force * 2f, kind, default, pawn);
                if (Took())
                {
                    broke = true;
                    break;
                }
            }

            if (!broke)
            {
                prop.TakeRadiusDamage(pc, damage, 300f, RDmgType_Explosion.StaticClass(), force * 2f, at, true, pawn);
                broke = Took();
            }
        }
        finally
        {
            _breaking = null;
        }

        Kick(prop.CollisionComponent, dir * force * RamCarKick, hard);

        // Already broken but still blocking. Only the blocking goes, the pieces keep their physics.
        if (!broke && !armoured && health <= 0f && prop.bBlockActors)
            prop.SetCollision(prop.bCollideActors, false, prop.bIgnoreEncroachers);
    }

    // Wakes parked cars and wrecks so a kick can move them.
    private static void Wake(Actor body)
    {
        if (body.Physics != Actor.EPhysics.PHYS_RigidBody)
            body.SetPhysics(Actor.EPhysics.PHYS_RigidBody, true);
    }

    private const float ShoveGain = 1.3f;
    private const float ShoveLeast = 150f;
    private const float FootPush = 250f;

    // Keeps loose things he pushes moving a bit faster than him, since wheels made them jerky.
    private void Shove(Actor hit, Vector3 dir, float speed)
    {
        PrimitiveComponent? part = hit switch
        {
            RVehicle car => car.Mesh,
            SkeletalMeshActor skel => skel.SkeletalMeshComponent,
            _ => hit.CollisionComponent,
        };
        if (part is not { IsValid: true })
            return;

        float want = MathF.Max(speed * ShoveGain, ShoveLeast);
        Vector3 v = hit.Velocity;
        float along = Vector3.Dot(v, dir);
        part.WakeRigidBody();
        if (along < want)
            part.SetRBLinearVelocity(v + dir * (want - along), false);
        Pass(hit, along, speed);
    }

    // Unpins and wakes a car the thugs left, so it can be shoved.
    private static void FreeCar(RVehicle car)
    {
        if (car.Physics != Actor.EPhysics.PHYS_RigidBody)
            car.SetPhysics(Actor.EPhysics.PHYS_RigidBody, true);
        car.SetRigidBodyFixedNPC(false);
        car.SetRigidBodyFixed(false);
        // A parked car sits on its handbrake, which holds it against a shove.
        car.bOutputHandbrake = false;
        car.bHoldingDownHandbrake = false;
        car.IsVehicleAsleep = false;

        // The game's unpin didn't always take, so every body in it is unpinned too.
        if (car.Mesh is not { IsValid: true } mesh)
            return;

        if (mesh.PhysicsAssetInstance is { IsValid: true } physics)
        {
            foreach (RB_BodyInstance body in physics.Bodies)
            {
                if (body is { IsValid: true } && body.IsValidBodyInstance() && body.IsFixed())
                    body.SetFixed(false);
            }
        }

        mesh.WakeRigidBody();
    }

    // True for things physics can shove, once they're awake.
    private static bool Loose(Actor hit)
    {
        switch (hit)
        {
            case RVehicleBatmobileBase:
                return false;
            case RVehicle car:
                FreeCar(car);
                return true;
            case Pawn:
                return false;
            case RAbandonedVehicle or RDeadVehicle:
                Wake(hit);
                return true;
            case KActor:
                return true;
            default:
                return hit.Physics == Actor.EPhysics.PHYS_RigidBody;
        }
    }

    // Always a little lift off the ground, more the harder the hit.
    private static Vector3 Lifted(Vector3 dir, float hard) => dir + Vector3.UnitZ * (RamUp * (0.3f + 0.7f * hard));

    private static void RamParked(RAbandonedVehicle parked, RPawnPlayer pawn, PlayerController pc, Vector3 dir, float force, float hard, Vector3 at)
    {
        Kick(parked.SkeletalMeshComponent, Lifted(dir, hard) * (force * RamCarKick), 0.3f + 0.7f * hard);
        parked.SmashWindscreen(at, -dir);

        // Blowing up swaps in a wreck before the push, so only a near full speed hit blows one up.
        int health = parked.Health;
        int damage = hard >= WreckAt ? health + 1 : (int)(health * 0.6f * hard);
        if (damage > 0)
            parked.TakeDamage(damage, pc, at, dir * force * 2f, RDmgType_BatmobileCannon.StaticClass(), default, pawn);
    }

    private static bool ThugUp(RPawnVillain thug) =>
        thug is { bDeleteMe: false, bGoodAsDead: false, bHidden: false } && thug.Health > 0 && !InCar(thug)
        && thug.CombatPawnType is not (RPawnCombat.ECombatPawnType.ECPT_Player or RPawnCombat.ECombatPawnType.ECPT_Friendly);

    // Seated thugs are left to the car, since hitting them broke their state.
    private static bool InCar(Pawn thug) =>
        thug.DrivenVehicle is { IsValid: true } || thug.Base is Vehicle or RAbandonedVehicle || thug.bHardAttach;

    // A hit only makes them flinch, so a tackle ragdoll and launch follow if needed.
    private static void RamThug(RPawnVillain thug, RPawnPlayer pawn, PlayerController pc, Vector3 dir, float force, float hard, Vector3 at)
    {
        Vector3 push = (dir + Vector3.UnitZ * RamUp) * (force * RamThugKick);
        float amount = hard >= 1f ? thug.Health + 1 : MathF.Max(1f, thug.Health * (0.35f + 0.65f * hard));
        var info = new RPawnCombat.FDamageInfo
        {
            Attacker = pawn,
            Impulse = push,
            ForceHitReactionHeading = push,
            DamageAmount = amount,
            DamageType = RDmgType_GlideKick.StaticClass(),
            bHeavyHit = true,
            bWillKnockOverOtherPawns = true,
            RagdollForce = push,
            RagdollForceDuration = 0.25f,
            ARF = RPawnCombat.AdditionalRagdollForce.ARF_Random,
        };

        thug.DamagedBy(info);
        if (!thug.IsRagdoll())
            thug.PlayRagdoll(info, RGameInfo.RagdollStunType.RAGSTUN_GlideBoostTackle, false);
    }

    private static void RamCar(RVehicle car, RPawnPlayer pawn, PlayerController pc, Vector3 dir, float force, float hard, Vector3 at)
    {
        FreeCar(car);
        // It always gets lifted off its wheels a little. A hard hit lifts it more and spins it.
        Vector3 kick = Lifted(dir, hard) * (force * RamCarKick);
        Kick(car.Mesh, kick, 0.3f + 0.7f * hard);

        // Wrecking swaps in a dead car before the push, so only a near full speed hit wrecks one.
        int health = car.Health;
        int damage = hard >= WreckAt ? health + 1 : (int)(health * 0.6f * hard);
        if (damage > 0)
            car.TakeDamageFromWeapon(damage, pc, at, dir * force * 2f, Vector3.Zero, RDmgType_BatmobileCannon.StaticClass(), default, pawn, false);
    }

    // Changes its speed outright, so a heavy car moves as much as a light one.
    private static void Kick(PrimitiveComponent? body, Vector3 kick, float hard)
    {
        if (body is not { IsValid: true })
            return;

        body.WakeRigidBody();
        body.SetRBLinearVelocity(kick, true);
        if (hard > 0f)
        {
            var spin = new Vector3(Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f);
            body.SetRBAngularVelocity(spin * (RamSpin * hard), true);
        }
    }
}
