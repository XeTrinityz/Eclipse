using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Reaching things in flight. Punch flies him to the target, grab picks it up and throws it.
internal sealed partial class Suit
{
    private const float RushSpeed = 4500f;
    private const float RushReach = 60f;
    private const double RushMost = 4.0;
    // How far into the throw anim it lets go.
    private const float ThrowReleaseMost = 0.35f;

    private const float HoldLost = 1500f;
    private const float ThrowSpeed = 4000f;
    private const float ThrowLiftMost = 0.25f;
    private const float ThrowGravity = 980f;
    private const float ThrowAimFar = 20000f;

    private const float LandFrom = 1500f;
    private const float LandRadius = 500f;
    private const double FlungWatch = 6.0;

    private Actor? _rush;
    private Kind _rushKind;
    private bool _rushGrab, _rushHeavy, _rushDive;
    private float _rushHard = 1f;
    private double _rushUntil;

    private Actor? _held;
    private Kind _heldKind;

    private bool _punchWas, _grabWas;
    private bool _throwing;
    private double _throwAt;

    // With no set point, a throw lets go once his hands slow, between the earliest and latest points.
    private const double ThrowHandsEarliest = 0.25;
    private const float ThrowHandsLatest = 0.5f;
    private const float ThrowHandsMost = 1.5f;
    private const float ThrowHandsFrom = 350f;
    private bool _throwByHands;
    private double _throwEarliest, _handsWasAt;
    private float _handsFwd, _handsPeak;
    private Vector3? _handsWas;

    private bool ThrowDue()
    {
        double now = Clock.Now;
        if (now >= _throwAt)
            return true;

        if (!_throwByHands || now < _throwEarliest || _handsPeak < ThrowHandsFrom || _handsFwd > _handsPeak * 0.6f)
            return false;

        return true;
    }

    // How fast his hands are moving forward in his own frame, while a throw is on.
    private void TrackThrowHands(Vector3 handsLocal)
    {
        double now = Clock.Now;
        if (_handsWas is { } was && now > _handsWasAt)
        {
            _handsFwd = (handsLocal.X - was.X) / (float)(now - _handsWasAt);
            if (now >= _throwEarliest)
                _handsPeak = MathF.Max(_handsPeak, _handsFwd);
        }

        _handsWas = handsLocal;
        _handsWasAt = now;
    }

    private sealed class Flung
    {
        public Actor Thing = null!;
        public Kind Kind;
        public double Since;
        public float Fastest;
        public Vector3? Was;
        public readonly HashSet<nint> Hit = new();
        public List<Actor>? Near;
        // A thrown car or object keeps the throw's speed until it's clear of him.
        public Vector3 Push;
        public double PushUntil;
    }

    private const double ThrowPushFor = 0.2;

    // Hit range and minimum speed for thrown things. Ragdolls don't collide with pawns or props.
    private const float PathReach = 40f;
    private const float PathFrom = 800f;
    private const float PathLook = 1500f;

    private static readonly List<Flung> _flung = new();

    private bool Rushing => _rush != null;

    public bool Holding => _held is { IsValid: true, bDeleteMe: false };

    public bool HoldingThug => Holding && _heldKind == Kind.Thug;

    // His bones ignore a thrown car briefly, or it catches and drops on his head.
    private const double ThrowClearOfHim = 0.8;
    private double _threwAt = -10.0;

    public bool ThrowLeaving => Clock.Now - _threwAt < ThrowClearOfHim;

    private bool IsHeld(Actor thing) => _held is { } held && held.Ptr == thing.Ptr;

    private bool IsRush(Actor thing) => _rush is { } rush && rush.Ptr == thing.Ptr;

    private void ResetCombat()
    {
        _rush = null;
        _held = null;
        _throwing = false;
        ResetFight();
        _punchWas = Keyboard.IsDown(Opt.PunchKey.Key) || Pad.Button("x");
        _grabWas = Keyboard.IsDown(Opt.GrabKey.Key) || Pad.Button("y");
    }

    // Called with the flight's speed already worked out, so a rush or punch can take over.
    private void CombatStep(RPawnPlayer pawn, float dt)
    {
        if (World.Pc is not { } pc)
            return;

        // Held state updates even with a menu open, so a menu click isn't a punch.
        bool punchDown = Keyboard.IsDown(Opt.PunchKey.Key) || Pad.Button("x");
        bool grabDown = Keyboard.IsDown(Opt.GrabKey.Key) || Pad.Button("y");
        bool punch = Listening && punchDown, grab = Listening && grabDown;
        bool punched = punch && !_punchWas, grabbed = grab && !_grabWas;
        _punchWas = punchDown;
        _grabWas = grabDown;
        // Holding a thug, the punch key only finishes him. The grab key still throws.
        bool gripping = Holding && _heldKind == Kind.Thug;
        if (_finishing)
        {
            FinishStep(pawn, pc);
            return;
        }

        if (grabbed)
            GrabPress(pawn, Look());
        if (gripping)
        {
            if (punched && !grabbed && !_throwing)
                StartFinish(pawn);
        }
        else
        {
            FightStep(pawn, pc, punch, punched && !grabbed, dt);
        }

        if (_throwing && ThrowDue())
        {
            _throwing = false;
            ThrowHeld(pawn, Look());
            CutThrow();
        }

        if (_rush != null)
            RushStep(pawn, pc);
    }

    private Vector3 Look()
    {
        if (Hooks.PlayerCam() is not { } cam)
            return Vector3.UnitX;
        BmSDK.GameObject.FTPOV view = ViewPov(cam);
        return Geo.Dir(view.Rotation.Yaw, Geo.WrapDegrees(view.Rotation.Pitch));
    }

    private void GrabPress(RPawnPlayer pawn, Vector3 look)
    {
        if (Holding)
        {
            if (_throwing || _finishing)
                return;

            // A thug is thrown with a right hand punch. Cars and objects use the throw anim.
            if (_heldKind == Kind.Thug)
            {
                StartFinish(pawn, throwing: true);
                return;
            }

            // Lets go as his hands come through. The anim's cues only set the earliest point.
            double start = Clock.Now;
            _throwing = true;
            _throwAt = start + ThrowReleaseMost;
            _throwByHands = false;
            _handsPeak = 0f;
            _handsFwd = 0f;
            _handsWas = null;
            if (PlayMove(pawn, Move.Throw) && _overlaySeq is { } seq)
            {
                float len = Length(seq);
                float cue = LetGoPoint(seq);
                _throwEarliest = start + (cue >= 0f ? len * cue : ThrowHandsEarliest);
                _throwAt = Math.Max(_throwEarliest + 0.1, start + MathF.Min(len * ThrowHandsLatest, ThrowHandsMost));
                _throwByHands = true;
            }

            return;
        }

        if (Rushing || Aim is not { } aim || !Grabbable(aim))
            return;

        StartRush(aim, grab: true);
    }

    private void StartRush(Mark aim, bool grab, bool heavy = false, float hard = 1f, bool dive = false)
    {
        _rush = aim.Thing;
        _rushKind = aim.Kind;
        _rushGrab = grab;
        _rushHeavy = heavy;
        _rushHard = hard;
        _rushDive = dive;
        _rushUntil = Clock.Now + RushMost;
        _fovKick = MathF.Max(_fovKick, FovKick);
    }

    // Straight at it, until he's close enough to hit it or take hold.
    private void RushStep(RPawnPlayer pawn, RPlayerController pc)
    {
        if (_rush is not { IsValid: true, bDeleteMe: false } thing || Clock.Now > _rushUntil || !Usable(thing, _rushKind))
        {
            _rush = null;
            return;
        }

        (Vector3 centre, Vector3 half) = _rushGrab ? HoldBox(thing) : Box(thing);
        Vector3 to = centre - pawn.Location;
        float dist = to.Length();
        Vector3 dir = dist > 1f ? to / dist : Look();
        float reach = pawn.GetCollisionRadius() + MathF.Max(half.X, half.Y) + RushReach;
        // A long fly in starts its strike a little early, so the fist is out as he arrives.
        bool there = _rushGrab ? dist <= reach : InReach(pawn, thing, _rushDive ? DiveEarly : 0f);
        if (!there)
        {
            // Aims where a moving target will be by the time he gets there.
            Vector3 lead = centre + Motion(thing) * (dist / RushSpeed) - pawn.Location;
            if (lead.LengthSquared() > 1f)
                dir = Vector3.Normalize(lead);
            _fm.Velocity = dir * RushSpeed;
            return;
        }

        Arrive(pawn, pc, thing, dir);
    }

    // The ram can meet the target first at this speed, so it hands it over here.
    private void Arrive(RPawnPlayer pawn, RPlayerController pc, Actor thing, Vector3 dir)
    {
        bool grab = _rushGrab, heavy = _rushHeavy, dive = _rushDive;
        Kind kind = _rushKind;
        float hard = _rushHard;
        _rush = null;
        if (grab)
        {
            TakeHold(thing, kind);
            _fm.Velocity *= 0.2f;
            return;
        }

        StartSwing(pawn, thing, kind, heavy, hard, dive);
    }

    private void TakeHold(Actor thing, Kind kind)
    {
        switch (thing)
        {
            case RPawnVillain thug:
                Grip(thug);
                break;
            case RVehicle car:
                FreeCar(car);
                break;
            case RAbandonedVehicle or RDeadVehicle:
                Wake(thing);
                break;
        }

        if (thing is not RPawnVillain)
            StartCarry(thing, kind);
        _held = thing;
        _heldKind = kind;
        if (World.Player is not { } pawn)
            return;

        // A thug's grab runs into the hold. A car or object goes up over his head.
        if (thing is not RPawnVillain || (!PlayGrabInto(pawn, Move.Grab, Move.Neck) && !PlayMove(pawn, Move.Grab)))
            PlayMove(pawn, HoldMove);
    }

    // Each frame after he moves. Thugs go in his hand, anything else rests on his hands.
    private void HoldStep(RPawnPlayer pawn)
    {
        if (_held == null)
            return;
        if (_held is not { IsValid: true, bDeleteMe: false } thing || Part(thing) is not { IsValid: true } part)
        {
            Ungrip();
            _held = null;
            return;
        }

        if (thing is RPawnVillain thug)
        {
            // Knocked down while in hand, he's let go rather than carried as a ragdoll.
            if (thug.IsRagdoll() || !ThugUp(thug))
            {
                Ungrip();
                _held = null;
                return;
            }

            GripStep(pawn, thug);
            return;
        }

        KeepPassing(thing);
        CarryStep(pawn, thing, part);
    }

    // At another bracketed target if there is one, or where he's looking.
    private void ThrowHeld(RPawnPlayer pawn, Vector3 look)
    {
        if (_held is not { IsValid: true, bDeleteMe: false } thing || Part(thing) is not { IsValid: true } part)
        {
            _held = null;
            return;
        }

        // Aimed from where it is to the target or aim dot, not along the camera line.
        (Vector3 from, Vector3 size) = HoldBox(thing);
        float past = size.Length();
        bool marked = Aim is { } aim && aim.Thing.Ptr != thing.Ptr && Vector3.Dot(aim.Centre - from, look) > past;
        Vector3 target = marked ? Aim!.Value.Centre : AimPoint(pawn, look, from, past);
        Vector3 to = target - from;
        float dist = to.Length();
        Vector3 dir = dist > 1f ? to / dist : look;

        // Raised for drop, and only his speed along the throw, so strafing doesn't pull it off line.
        float speed = ThrowSpeed * Opt.PunchForce.Number;
        float flight = dist / speed;
        float lift = Math.Clamp(0.5f * ThrowGravity * flight * flight / MathF.Max(dist, 1f), 0f, ThrowLiftMost);
        Vector3 v = Vector3.Normalize(dir + Vector3.UnitZ * lift) * speed + dir * (MathF.Max(0f, Vector3.Dot(_fm.Velocity, dir)) * 0.5f);
        Kind kind = _heldKind;
        _held = null;

        if (thing is RPawnVillain thug)
        {
            // Sent off through the game's hit so his ragdoll knocks people over, then given throw speed.
            Ungrip();
            var info = new RPawnCombat.FDamageInfo
            {
                Attacker = pawn,
                DamageAmount = 1f,
                Impulse = v,
                ForceHitReactionHeading = v,
                RagdollForce = v,
                RagdollForceDuration = 0.25f,
                ARF = RPawnCombat.AdditionalRagdollForce.ARF_HeadBackFeetUp,
                DamageType = RDmgType_BaseballBat.StaticClass(),
                bHeavyHit = true,
                bWillKnockOverOtherPawns = true,
            };
            Safe.Run("throw hit", () => thug.DamagedBy(info));
            if (!thug.IsRagdoll())
                thug.GenericGoRagdoll();
            QueueThrow(thug, v);
        }
        else
        {
            _threwAt = Clock.Now;
            part.WakeRigidBody();
            part.SetRBLinearVelocity(v, false);
            var spin = new Vector3(Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f);
            part.SetRBAngularVelocity(spin * RamSpin, false);
        }

        Fling(thing, kind);
        if (thing is not RPawnVillain && _flung.Count > 0)
        {
            Flung f = _flung[^1];
            f.Push = v;
            f.PushUntil = Clock.Now + ThrowPushFor;
        }
        _camKick = MathF.Max(_camKick, 0.5f);
    }

    // What's under the aim dot, traced from past what he holds so it doesn't hit itself.
    private Vector3 AimPoint(RPawnPlayer pawn, Vector3 look, Vector3 held, float past)
    {
        if (Hooks.PlayerCam() is not { } cam)
            return held + look * ThrowAimFar;

        Vector3 eye = ViewPov(cam).Location;
        Vector3 start = eye + look * (MathF.Max(0f, Vector3.Dot(held - eye, look)) + past);
        Vector3 end = eye + look * ThrowAimFar;
        return Ground.Trace(pawn, start, end, Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out Vector3 at, out _) != null ? at : end;
    }

    private void DropHeld()
    {
        Ungrip();
        _held = null;
        _rush = null;
        _throwing = false;
    }

    private static void QueueThrow(RPawnVillain thug, Vector3 speed)
    {
        if (!thug.IsRagdoll())
            thug.GenericGoRagdoll();
        _throws.RemoveAll(t => t.Thug.Ptr == thug.Ptr);
        _throws.Add(new Throw { Thug = thug, Speed = speed, Until = Clock.Now + ThrowWaitMost, ForceAt = Clock.Now + ForceRagdollAfter });
    }

    private const double ThrownClear = 0.6;

    private static bool JustThrown(Actor thing) =>
        _flung.Any(f => f.Thing.Ptr == thing.Ptr && Clock.Now - f.Since < ThrownClear);

    private static void Fling(Actor thing, Kind kind)
    {
        _flung.RemoveAll(f => f.Thing.Ptr == thing.Ptr);
        _flung.Add(new Flung { Thing = thing, Kind = kind, Since = Clock.Now });
    }

    // Watches thrown and punched things and hurts what's round them when they land.
    private void FlungStep()
    {
        for (int i = _flung.Count - 1; i >= 0; i--)
        {
            Flung f = _flung[i];
            double t = Clock.Now - f.Since;
            if (!f.Thing.IsValid || f.Thing.bDeleteMe || t > FlungWatch)
            {
                _flung.RemoveAt(i);
                continue;
            }

            // It leaves from inside him and lost its speed there, so the throw speed is kept up briefly.
            if (Clock.Now < f.PushUntil && Part(f.Thing) is { IsValid: true } pushed)
            {
                pushed.WakeRigidBody();
                pushed.SetRBLinearVelocity(f.Push, false);
            }

            float speed = Speed(f.Thing);
            f.Fastest = MathF.Max(f.Fastest, speed);
            Vector3 now = Box(f.Thing).Centre;
            if (f.Was is { } was && speed > PathFrom && f.Kind == Kind.Thug)
                Safe.Run("thrown path", () => PathHits(f, was, now, speed));
            if (f.Was is { } before && speed > PathFrom)
                Safe.Run("thrown glass", () => GlassAhead(f, before, now, speed));
            f.Was = now;
            if (t < 0.15 || f.Fastest < LandFrom || speed > f.Fastest * 0.45f)
                continue;

            _flung.RemoveAt(i);
            Safe.Run("landing", () => Landed(f));
        }
    }

    // Hits what a thrown thug passed through, each once. Nearby things are gathered once at the throw.
    private void PathHits(Flung f, Vector3 from, Vector3 to, float speed)
    {
        if (World.Pc is not { } pc || World.Player is not { IsValid: true } batman)
            return;

        Vector3 path = to - from;
        float len = path.Length();
        if (len < 1f)
            return;

        f.Near ??= NearThrown(f.Thing);
        Vector3 dir = path / len;
        float hard = Math.Clamp(speed / (RamWreck * 1.5f), 0.3f, 1f);
        float force = MathF.Max(speed, RamFloor) * Opt.RamForce.Number;
        foreach (Actor thing in f.Near)
        {
            if (!thing.IsValid || thing.bDeleteMe || f.Hit.Contains(thing.Ptr))
                continue;

            (Vector3 centre, Vector3 half) = Box(thing);
            if (!NearPath(centre, from, dir, len, MathF.Max(half.X, half.Y) + PathReach, half.Z))
                continue;

            switch (thing)
            {
                case RPawnVillain thug when ThugUp(thug):
                    f.Hit.Add(thug.Ptr);
                    RamThug(thug, batman, pc, dir, force, hard, thug.Location);
                    QueueThrow(thug, (dir + Vector3.UnitZ * PunchUp) * (speed * 0.5f));
                    Fling(thug, Kind.Thug);
                    break;
                case RDestructibleProp prop when prop.Health > 0:
                    f.Hit.Add(prop.Ptr);
                    BreakProp(prop, batman, pc, dir, force, hard, centre);
                    break;
                case RVehicle car when car.Health > 0:
                    f.Hit.Add(car.Ptr);
                    RamCar(car, batman, pc, dir, force * 0.5f, hard * 0.5f, centre);
                    break;
            }
        }
    }

    private const float NearThrownReach = 4000f;

    // Thugs, props and cars within reach of where something was thrown from.
    private static List<Actor> NearThrown(Actor thrown)
    {
        var near = new List<Actor>();
        Vector3 at = thrown.Location;
        float reach = NearThrownReach * NearThrownReach;
        foreach (RPawnVillain thug in Game.FindObjects<RPawnVillain>())
        {
            if (thug.Ptr != thrown.Ptr && thug is { IsValid: true, IsClassDefaultObject: false } && Vector3.DistanceSquared(thug.Location, at) < reach)
                near.Add(thug);
        }

        foreach (RDestructibleProp prop in Game.FindObjects<RDestructibleProp>())
        {
            if (prop is { IsValid: true, IsClassDefaultObject: false } && Vector3.DistanceSquared(prop.Location, at) < reach)
                near.Add(prop);
        }

        foreach (RVehicle car in Game.FindObjects<RVehicle>())
        {
            if (car is { IsValid: true, IsClassDefaultObject: false } and not RVehicleBatmobileBase && Vector3.DistanceSquared(car.Location, at) < reach)
                near.Add(car);
        }

        return near;
    }

    // Smashes glass and breakables just ahead of a thrown thing, like the ram does.
    private const float GlassLook = 0.08f;

    private void GlassAhead(Flung f, Vector3 from, Vector3 to, float speed)
    {
        if (World.Pc is not { } pc || World.Player is not { IsValid: true } batman)
            return;

        Vector3 path = to - from;
        if (path.LengthSquared() < 1f)
            return;

        Vector3 dir = Vector3.Normalize(path);
        Vector3 end = to + dir * (speed * GlassLook);
        Actor? hit = Ground.Trace(pc, from, end, Component.ECollisionFilter.COF_PawnPlayer, new Vector3(25f, 25f, 25f), out Vector3 at, out _);
        if (hit is not { bDeleteMe: false } || hit.Ptr == f.Thing.Ptr || !Breakable(hit) || f.Hit.Contains(hit.Ptr))
            return;

        f.Hit.Add(hit.Ptr);
        float force = MathF.Max(speed, RamFloor) * Opt.RamForce.Number;
        Ram(hit, batman, pc, dir, speed, force, true, at == Vector3.Zero ? to : at);
    }

    // A point within reach of the path segment, and within its height of it.
    private static bool NearPath(Vector3 point, Vector3 from, Vector3 dir, float len, float reach, float tall)
    {
        if (Vector3.DistanceSquared(point, from) > (len + PathLook) * (len + PathLook))
            return false;
        float along = Math.Clamp(Vector3.Dot(point - from, dir), 0f, len);
        Vector3 off = point - (from + dir * along);
        return off.X * off.X + off.Y * off.Y <= reach * reach && MathF.Abs(off.Z) <= tall + PathReach;
    }

    private static float Speed(Actor thing) => Motion(thing).Length();

    // How it's moving. Physics bodies carry the speed, not the actor or pawn.
    private static Vector3 Motion(Actor thing)
    {
        if (thing is Vehicle)
            return thing.Velocity;
        if (thing is RPawnVillain thug && thug.IsRagdoll() && thug.Mesh is { IsValid: true } body)
            return body.GetRBLinearVelocity();
        if (thing.Physics == Actor.EPhysics.PHYS_RigidBody && Part(thing) is { IsValid: true } part)
            return part.GetRBLinearVelocity();
        return thing.Velocity;
    }

    private void Landed(Flung f)
    {
        if (World.Pc is not { } pc || World.Player is not { IsValid: true } batman)
            return;

        Vector3 at = Box(f.Thing).Centre;
        float hard = Math.Clamp(f.Fastest / (RamWreck * 1.5f), 0.2f, 1f);
        float radius = LandRadius * (0.6f + hard);
        int hit = 0;

        foreach (RPawnVillain thug in Game.FindObjects<RPawnVillain>())
        {
            if (thug.Ptr == f.Thing.Ptr || !thug.IsValid || !ThugUp(thug) || Vector3.Distance(thug.Location, at) > radius)
                continue;

            Vector3 away = thug.Location - at;
            away.Z = 0f;
            Vector3 dir = away.LengthSquared() > 1f ? Vector3.Normalize(away) : Vector3.UnitX;
            RamThug(thug, batman, pc, dir, RamFloor + (RamWreck - RamFloor) * hard, hard, thug.Location);
            hit++;
        }

        foreach (RVehicle car in Game.FindObjects<RVehicle>())
        {
            if (car.Ptr == f.Thing.Ptr || car is RVehicleBatmobileBase || car is not { IsValid: true, bDeleteMe: false, Health: > 0 }
                || Vector3.Distance(car.Location, at) > radius + 200f)
                continue;

            int damage = (int)(car.Health * 0.6f * hard) + 1;
            car.TakeDamageFromWeapon(damage, pc, car.Location, Vector3.Zero, Vector3.Zero, RDmgType_BatmobileCannon.StaticClass(), default, batman, false);
            hit++;
        }

        // What he threw takes it too. A thug is put down, a car hard enough is wrecked.
        switch (f.Thing)
        {
            case RPawnVillain thrown when thrown.Health > 0:
                thrown.DamagedBy(new RPawnCombat.FDamageInfo { Attacker = batman, DamageAmount = thrown.Health + 1, DamageType = RDmgType_GlideKick.StaticClass() });
                break;
            case RVehicle car when car.Health > 0:
                int damage = hard >= WreckAt ? car.Health + 1 : (int)(car.Health * 0.6f * hard);
                if (damage > 0)
                    car.TakeDamageFromWeapon(damage, pc, at, Vector3.Zero, Vector3.Zero, RDmgType_BatmobileCannon.StaticClass(), default, batman, false);
                break;
            case RAbandonedVehicle parked when parked.Health > 0:
                parked.TakeDamage(hard >= WreckAt ? parked.Health + 1 : (int)(parked.Health * 0.6f * hard), pc, at, Vector3.Zero,
                    RDmgType_BatmobileCannon.StaticClass(), default, batman);
                break;
        }
    }
}
