using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Cars that drive into him. His body stops them, this adds the damage and camera jolt.
internal sealed partial class Suit
{
    private const double CarListSeconds = 0.5;
    private const float CarListReach = 3000f;
    private const float CarNear = 150f;
    private const float CarHitFrom = 300f;
    private const float PassBackFrom = 50f;
    private const double CarHitAgain = 0.5;
    // Above this speed a car is stopped just before him, or it can skip through his bones.
    private const float CarFastStop = 800f;
    private const float CarStopGap = 60f;
    private const float CarStopLead = 0.06f;
    private const float CarBounce = 0.2f;

    private sealed class NearCar
    {
        public RVehicle Car = null!;
        public float Closing;
        public double Hit;
    }

    private readonly Dictionary<nint, NearCar> _cars = new();
    private Every _carList;

    // Near a car his step height drops to a kerb's, so he isn't stepped up onto bumpers.
    private const string StepKey = "eclipse.stepheight";
    private const float StepNearCar = 20f;
    private const float CarUnderfootSpeed = 250f;

    private static bool Heavy => Opt.HeavyOn;

    private void WallStep()
    {
        if (!Heavy || !World.InGame || World.Pawn is not RPawnPlayer { IsValid: true, bHidden: false } batman || World.Pc is not { } pc)
        {
            _cars.Clear();
            RestoreStep(World.Pawn as RPawnPlayer);
            return;
        }

        if (_carList.Elapsed(CarListSeconds))
            ListCars(batman);

        float closest = float.MaxValue;
        foreach (NearCar near in _cars.Values)
        {
            RVehicle car = near.Car;
            // One he's carrying or just threw is moving with him, not into him.
            if (!car.IsValid || car.bDeleteMe || car.Mesh is not { IsValid: true } body || IsHeld(car) || JustThrown(car))
            {
                near.Closing = 0f;
                continue;
            }

            Vector3 toCar = car.Location - batman.Location;
            toCar.Z = 0f;
            float dist = toCar.Length();
            if (dist < 1f)
                continue;

            Vector3 away = toCar / dist;
            float closing = -Vector3.Dot(car.Velocity, away);
            float was = near.Closing;
            near.Closing = closing;
            if (closing > PassBackFrom && Passing(car))
                StopPassing(car);

            float size = body.Bounds.SphereRadius;
            closest = MathF.Min(closest, dist - size);
            if (dist > size + CarNear)
                continue;

            if (closing > CarFastStop && dist < size + CarStopGap + closing * CarStopLead)
            {
                body.WakeRigidBody();
                body.SetRBLinearVelocity(car.Velocity + away * (closing * (1f + CarBounce)), false);
                near.Closing = 0f;
                Impact(car, near, batman, pc, away, closing);
                continue;
            }

            if (was < CarHitFrom || closing > was * 0.5f)
                continue;

            Impact(car, near, batman, pc, away, was);
        }

        StepGuard(batman, closest);
    }

    private void Impact(RVehicle car, NearCar near, RPawnPlayer batman, RPlayerController pc, Vector3 away, float speed)
    {
        if (Clock.Now - near.Hit < CarHitAgain)
            return;

        near.Hit = Clock.Now;
        int damage = (int)Math.Clamp(speed / Metre * 3f, 5f, 60f);
        float jolt = Math.Clamp(speed / RamWreck, 0.2f, 1f);
        _fovKick = MathF.Max(_fovKick, jolt * 6f);
        _camKick = MathF.Max(_camKick, jolt);
        Vector3 at = batman.Location + away * batman.GetCollisionRadius();
        Safe.Run("car hit damage", () => car.TakeDamageFromWeapon(damage, pc, at, -away * speed, Vector3.Zero, RDmgType_BatmobileCannon.StaticClass(),
            default, batman, false));
    }

    private void StepGuard(RPawnPlayer batman, float closest)
    {
        bool near = !_flying && closest < CarNear;
        object? saved = Shared.Get(StepKey);
        if (near)
        {
            if (saved is not float)
                Shared.Set(StepKey, batman.MaxStepHeight);

            batman.MaxStepHeight = StepNearCar;
        }
        else
        {
            RestoreStep(batman);
        }

        if (!_flying && batman.Base is { IsValid: true } under && IsCar(under) && under.CollisionComponent is { IsValid: true } part)
        {
            Vector3 dir = Geo.Facing(batman.Rotation.Yaw);
            part.WakeRigidBody();
            part.SetRBLinearVelocity(dir * CarUnderfootSpeed, false);
        }
    }

    internal static void RestoreStep(RPawnPlayer? batman)
    {
        if (Shared.Get(StepKey) is not float was)
            return;

        Shared.Set(StepKey, null);
        if (batman is { IsValid: true })
            batman.MaxStepHeight = was;
    }

    private void ListCars(RPawnPlayer batman)
    {
        var keep = new HashSet<nint>();
        foreach (RVehicle car in Game.FindObjects<RVehicle>())
        {
            if (car is not { IsValid: true, IsClassDefaultObject: false, bDeleteMe: false } || car is RVehicleBatmobileBase
                || Vector3.DistanceSquared(car.Location, batman.Location) > CarListReach * CarListReach)
                continue;

            keep.Add(car.Ptr);
            if (!_cars.ContainsKey(car.Ptr))
                _cars[car.Ptr] = new NearCar { Car = car };
        }

        foreach (nint gone in _cars.Keys.Where(k => !keep.Contains(k)).ToList())
            _cars.Remove(gone);
    }

    // Moves aimed at a car are dropped while heavy, so he walks into it instead.
    [Redirect(typeof(RSpecialMoveConfig), nameof(RSpecialMoveConfig.TriggerSpecialMove))]
    private static void TriggerSpecialMoveRedirect(RSpecialMoveConfig self, RPlayerController controller,
        REnvironmentCheckTicker.FEnvironmentSpecialMoveLocator moveLocation, bool forceReplicateToClient)
    {
        if (controller is { IsValid: true } && IsYours(controller))
        {
            Actor? on = moveLocation.ActorInstance is { IsValid: true } a ? a : moveLocation.SecondaryTarget is { IsValid: true } b ? b : null;
            if (Heavy && self is not RSpecialMoveConfig_DriveCar && (self is RSpecialMoveConfig_PullDriverFromCar || IsCar(on)))
                return;
        }

        self.TriggerSpecialMove(controller, moveLocation, forceReplicateToClient);
    }

    // Never the Batmobile, he gets in that one.
    private static bool IsCar(Actor? actor) => actor is not RVehicleBatmobileBase && actor is Vehicle or RAbandonedVehicle or RDeadVehicle;

    [Redirect(typeof(RPawnPlayer), nameof(RPawnPlayer.RanOver))]
    private static void RanOverRedirect(RPawnPlayer self, RVehicle attacker)
    {
        if (Heavy && IsBatman(self))
            return;

        self.RanOver(attacker);
    }

    // A car or loose prop moving into him. Physics already stopped it, so the game's reaction is skipped.
    [Redirect(typeof(RPawnPlayer), nameof(RPawnPlayer.EncroachedBy))]
    private static void EncroachedByRedirect(RPawnPlayer self, Actor other)
    {
        if (Heavy && IsBatman(self) && other is Vehicle or RAbandonedVehicle or RDeadVehicle or KActor)
            return;

        self.EncroachedBy(other);
    }

    // A vehicle hurting him does nothing, and while he's invincible nothing does.
    [Redirect(typeof(RPawnPlayer), nameof(RPawnPlayer.TakeDamage))]
    private static void PlayerTakeDamageRedirect(RPawnPlayer self, int damage, Controller instigatedBy, Vector3 hitLocation, Vector3 momentum,
        Class damageType, Actor.FTraceHitInfo hitInfo, Actor damageCauser)
    {
        if (Invincible(self, damageType) || (Heavy && IsBatman(self) && FromVehicle(instigatedBy, damageCauser)))
            return;

        self.TakeDamage(damage, instigatedBy, hitLocation, momentum, damageType, hitInfo, damageCauser);
    }

    private static bool FromVehicle(Controller? by, Actor? causer) =>
        causer is Vehicle { IsValid: true } || (by is { IsValid: true } && by.Pawn is Vehicle { IsValid: true });
}
