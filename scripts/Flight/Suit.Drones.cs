using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.BmScript;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Drones, sentry guns and turrets. Batman needs gadgets for them, so one hit from him wrecks any of them.
internal sealed partial class Suit
{
    // Some wrecks keep their health, so they're remembered to stop them being picked again.
    private static readonly HashSet<nint> _wrecked = new();

    private static bool IsDrone(Actor thing) =>
        thing is RVehicleFlying or RPredatorDrone or RPredatorDroneMini or RSentryGunBase or RTurretBase;

    private static bool DroneUp(Actor thing)
    {
        if (!thing.IsValid || thing.bDeleteMe || _wrecked.Contains(thing.Ptr))
            return false;

        return thing switch
        {
            RVehicleFlying flyer => flyer.Health > 0,
            RHelicopterBase heli => heli.Health > 0,
            RSentryGunBase sentry => !sentry.bDisabled,
            RTurretBase turret => !turret.bDisabled,
            _ => false,
        };
    }

    private void Wreck(Actor thing, RPawnPlayer pawn, PlayerController pc, Vector3 at, Vector3 dir)
    {
        if (!DroneUp(thing))
            return;

        Vector3 push = dir * 2000f;
        switch (thing)
        {
            // The flying ones shrug off most damage. The gel is what Batman uses on them.
            case RVehicleFlying flyer:
                Class[] kinds =
                {
                    RDmgType_BatmobileCannon.StaticClass(), RDmgType_BatmobileMissileBarrage.StaticClass(),
                    RDmgType_ExplosiveGel.StaticClass(), RDmgType_Explosion.StaticClass(),
                };
                foreach (Class kind in kinds)
                {
                    if (!flyer.IsVulnerableToDamageType(kind))
                        continue;
                    flyer.TakeDamageFromWeapon(flyer.Health + 1, pc, at, push, Vector3.Zero, kind, default, pawn, false);
                    break;
                }

                if (flyer.Health > 0)
                    Safe.Run("drone explode", flyer.ExplodeMe);
                break;

            case RPredatorDrone drone:
                drone.TakeDamageFromWeapon(drone.Health + 1, pc, at, push, Vector3.Zero, RDmgType_ExplosiveGel.StaticClass(), default, pawn, false);
                if (drone.Health > 0)
                    Safe.Run("drone explode", drone.Explode);
                break;

            case RPredatorDroneMini mini:
                mini.TakeDamageFromWeapon(mini.Health + 1, pc, at, push, Vector3.Zero, RDmgType_ExplosiveGel.StaticClass(), default, pawn, false);
                if (mini.Health > 0)
                    Safe.Run("mini drone explode", () => mini._ObjectStateExplode(false));
                break;

            case RSentryGunBase sentry:
                sentry.TakeDamageFromWeapon(1000, pc, at, push, Vector3.Zero, RDmgType_ExplosiveGel.StaticClass(), default, pawn, false);
                if (!sentry.bDisabled)
                    Safe.Run("sentry explode", () => sentry.Explode(false));
                break;

            case RTurretBase turret:
                TurretDown(turret, pawn, pc, at, push);
                break;
        }

        if (_wrecked.Count > 64)
            _wrecked.Clear();
        _wrecked.Add(thing.Ptr);
        Own(Fx.Fireball, Box(thing).Centre, default);
        Thud(pawn, heavy: true);

        // The game's own explode doesn't always flag them, so a log line shows what got past.
        bool still = thing switch
        {
            Pawn p => p.IsValid && !p.bDeleteMe && p.Health > 0,
            RSentryGunBase s => !s.bDisabled,
            RTurretBase t => !t.bDisabled,
            _ => false,
        };
        if (still && Log.Due("wreck " + thing.Class.Name))
            Log.Info("Couldn't wreck a " + thing.Class.Name + ".");
    }

    // Some watchtower turrets ignored the cannon and SetDamaged both. Each way is tried until one takes,
    // and a turret that takes none is switched off by hand.
    private static void TurretDown(RTurretBase turret, RPawnPlayer pawn, PlayerController pc, Vector3 at, Vector3 push)
    {
        string name = turret.Class.Name;
        Class[] kinds =
        {
            RDmgType_BatmobileCannon.StaticClass(), RDmgType_BatmobileMissileBarrage.StaticClass(), RDmgType_ExplosiveGel.StaticClass(),
            RDmgType_Explosion.StaticClass(), RDmgType_Batarang.StaticClass(),
        };
        foreach (Class kind in kinds)
        {
            turret.TakeDamage(1000, pc, at, push, kind, default, pawn);
            if (turret.bDisabled)
            {
                if (Log.Due("turret took " + name + kind.Name))
                    Log.Info(name + " went down to " + kind.Name + ".");
                return;
            }
        }

        Safe.Run("turret disrupt", () => turret.HitByDisruptorGadgetCharge());
        if (!turret.bDisabled)
            Safe.Run("turret damaged", turret.SetDamaged);
        if (turret.bDisabled)
        {
            if (Log.Due("turret set " + name))
                Log.Info(name + " went down to the disruptor or SetDamaged.");
            return;
        }

        if (Log.Due("turret forced " + name))
            Log.Info(name + " took nothing, switched off by hand. State " + turret.GetStateName() + ", active " + turret.IsActive() + ".");
        Safe.Run("turret off", turret.SwitchOff);
        Safe.Run("turret lights", () => turret.EnableTurretLights(false));
        turret.bDisabled = true;
    }
}
