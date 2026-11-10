using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Flight punching. He flies to the target, taps chain into jabs and holding winds up a heavy punch.
internal sealed partial class Suit
{
    // Past the edge of the target, about an arm's length.
    private const float PunchReach = 90f;
    // A hit still lands if the target has drifted this much of an arm past reach, no further.
    private const float MissPast = 0.6f;
    // From this far off he flies in and throws the strike as he arrives.
    private const float FarPunch = 1200f;
    private const float DiveEarly = 250f;
    private const float DiveContact = 0.18f;
    private const float DiveSeconds = 0.55f;
    private const float QuickForce = 1500f;
    private const float StrikeForce = 3500f;
    private const float StrikeThrow = 2500f;
    private const float StrikeRecoil = 300f;
    private const float LungeMost = 2500f;
    private const float LungeHeavyMost = 900f;
    // A heavy punch reaches a little further, since the wind up drew him back.
    private const float HeavyReachExtra = 120f;
    private const float HeavySeconds = 0.9f;
    // Let go inside this and it's a tap. Held past it the wind up starts and takes this long.
    private const double TapWindow = 0.18;
    private const double ChargeFull = 0.9;
    private const float ChargeLeast = 0.2f;
    private const float DrawBack = 160f;
    private const float DrawPitch = 12f;
    private const float ZoomMost = 16f;
    private const float HeavyHard = 1.6f;
    // Share of a thug's health a jab takes.
    private const float JabShare = 0.4f;
    private const float WaveRadius = 500f;
    private const float WaveForce = 2200f;
    private const float SlowTap = 0.12f, SlowHeavy = 0.06f;
    private const double StopTap = 0.1, StopHeavy = 0.35;
    private const double BufferMost = 0.5;
    // Where in the punch time the fist lands. A heavy punch is already wound up, so it lands quickly.
    private const float ContactShare = 0.5f;
    private const float HeavyContact = 0.25f;
    private const float SettleBlend = 0.3f;

    private sealed class Swing
    {
        public Actor? Target;
        public Kind Kind;
        public Vector3 Dir;
        public bool Heavy;
        // Throws the thug in his hand instead of hitting him.
        public bool Throw;
        public float Hard = 1f;
        public float Seconds;
        public float Lunge;
        public Blow? Blow;
        public bool Mirrored;
        public double Since;
        public bool Landed;
        // Seconds into the swing the fist lands.
        public float ContactAt;
    }

    private Swing? _swing;
    private double _pressAt = -1.0;
    private bool _pending, _charging;
    private float _charge;
    private double _chargeTick;
    private bool _buffered;
    private double _bufferedAt;
    private float _fightPitch, _zoomWant, _windCharge;
    private double _lastPunchAt = -1.0;

    // A punch is going, winding up, or just landed.
    private bool Brawling => _swing != null || _charging || Clock.Now - _lastPunchAt < 1.5;
    private static bool _jabbing;

    public float Charge => _flying && _charging ? _charge : 0f;

    // The ram leaves the thing he's punching alone.
    private bool IsFoe(Actor thing) => _swing is { Target: { } target } && target.Ptr == thing.Ptr;

    private void ResetFight()
    {
        _swing = null;
        _pressAt = -1.0;
        _pending = false;
        _charging = false;
        _charge = 0f;
        _buffered = false;
        _fightPitch = 0f;
        _zoomWant = 0f;
    }

    private void EndFight()
    {
        ResetFight();
        ThawWind();
        EndSlow();
    }

    private void FightStep(RPawnPlayer pawn, RPlayerController pc, bool held, bool pressed, float dt)
    {
        double now = Clock.Now;
        if (_swing is { } swing)
            SwingStep(pawn, pc, swing);

        bool free = _swing == null || _swing.Landed;
        if (pressed)
        {
            _pending = true;
            _pressAt = now;
        }

        // A quick release is a tap and holding winds up. Held through a swing, it starts once he's free.
        if (_pending)
        {
            if (!held)
            {
                _pending = false;
                Tap(pawn, free, now);
            }
            else if (now - _pressAt > TapWindow && free && !Rushing)
            {
                _pending = false;
                StartCharge(pawn, now);
            }
        }
        else if (_buffered && free)
        {
            _buffered = false;
            if (now - _bufferedAt < BufferMost)
                PunchPress(pawn, heavy: false, 0f);
        }

        if (_buffered && now - _bufferedAt > BufferMost)
            _buffered = false;

        if (_charging)
        {
            if (held)
                ChargeStep(pawn, now);
            else
                Release(pawn);
            return;
        }

        _fightPitch += (0f - _fightPitch) * Ease.Follow(10f, dt);
        _zoomWant = 0f;
    }

    // A tap mid swing waits for the fist to land, then goes straight into the next punch.
    private void Tap(RPawnPlayer pawn, bool free, double now)
    {
        if (free)
        {
            PunchPress(pawn, heavy: false, 0f);
            return;
        }

        _buffered = true;
        _bufferedAt = now;
    }

    private void StartCharge(RPawnPlayer pawn, double now)
    {
        _charging = true;
        _charge = 0f;
        _chargeTick = now;
        _lastHand = _lastHand == "Left" ? "Right" : "Left";
        PlayWind(pawn, _lastHand == "Left" ? "Right" : "Left");
    }

    // He draws back and leans as the punch winds up, and the camera closes in.
    private void ChargeStep(RPawnPlayer pawn, double now)
    {
        float dt = (float)Math.Clamp(now - _chargeTick, 0.0, 0.1);
        _chargeTick = now;
        _charge = MathF.Min(1f, _charge + dt / (float)ChargeFull);
        // The strike runs up to its cocked frame as the charge fills, then holds there.
        if (_charge >= 1f)
            HoldWind();
        Vector3 dir = AimDir(pawn);
        _fm.Velocity = -dir * (DrawBack * (1f - _charge));
        _fightPitch += (DrawPitch * _charge - _fightPitch) * Ease.Follow(8f, dt);
        _zoomWant = ZoomMost * Ease.Smooth(_charge);
    }

    private void Release(RPawnPlayer pawn)
    {
        _charging = false;
        ThawWind();
        float charge = _charge;
        _charge = 0f;
        _zoomWant = 0f;

        float hard = HeavyHard + charge;
        if (charge >= ChargeLeast && Aim is { } aim && Punchable(aim) && InReach(pawn, aim.Thing, HeavyReachExtra))
        {
            // In reach, so the held strike carries on straight through the hit.
            _windCharge = charge;
            StartSwing(pawn, aim.Thing, aim.Kind, heavy: true, hard);
            return;
        }

        // The hover goes back on first, since a fly in wouldn't replace the wind up idle.
        ThawWind();
        _overlaySeq = null;
        ApplySpec(pawn, HoverKey(), force: false, SettleBlend);
        if (charge < ChargeLeast)
            return;

        PunchPress(pawn, heavy: true, charge);
    }

    private void PunchPress(RPawnPlayer pawn, bool heavy, float charge)
    {
        if (Rushing)
            return;

        float hard = heavy ? HeavyHard + charge : 1f;
        // Nothing bracketed, nothing thrown. A swing at the air lunged him into things.
        if (Aim is not { } aim || !Punchable(aim))
            return;

        if (InPunchReach(pawn, aim.Thing))
            StartSwing(pawn, aim.Thing, aim.Kind, heavy, hard);
        else
            StartRush(aim, grab: false, heavy, hard, dive: aim.Distance > FarPunch);
    }

    private Vector3 AimDir(RPawnPlayer pawn)
    {
        if (Aim is { } aim)
        {
            Vector3 to = aim.Centre - pawn.Location;
            if (to.LengthSquared() > 1f)
                return Vector3.Normalize(to);
        }

        return Look();
    }

    // Fist distance to the nearest face of its box, and the way to its middle. Zero or less is in reach.
    private static float Gap(RPawnPlayer pawn, Actor thing, out Vector3 to)
    {
        (Vector3 centre, Vector3 half) = Box(thing);
        to = centre - pawn.Location;
        Vector3 outside = Vector3.Max(Vector3.Abs(to) - half, Vector3.Zero);
        return outside.Length() - pawn.GetCollisionRadius() - PunchReach;
    }

    private static bool InPunchReach(RPawnPlayer pawn, Actor thing) => InReach(pawn, thing, 0f);

    private static bool InReach(RPawnPlayer pawn, Actor thing, float extra) => Gap(pawn, thing, out _) <= extra;

    // A dive swing lands at once out of the fast pose, and the hover returns as he slows.
    private void StartSwing(RPawnPlayer pawn, Actor? target, Kind kind, bool heavy, float hard, bool dive = false)
    {
        var swing = new Swing
        {
            Target = target,
            Kind = kind,
            Heavy = heavy,
            Hard = hard,
            Seconds = dive ? DiveSeconds : heavy ? HeavySeconds : Opt.PunchTime.Number,
            Dir = Look(),
            Since = Clock.Now,
            Throw = _throwPunch,
        };
        swing.ContactAt = dive ? DiveContact : heavy ? HeavyContact : swing.Seconds * ContactShare;

        if (!(heavy && ReleaseWind(swing, _windCharge)))
            PlayBlow(pawn, swing);
        _windCharge = 0f;
        Whoosh(pawn, heavy);

        if (target != null)
        {
            // Closes the gap so the fist arrives on contact, keeping up with a moving target.
            float gap = Gap(pawn, target, out Vector3 to) + PunchReach * 0.4f;
            float dist = to.Length();
            if (dist > 1f)
                swing.Dir = to / dist;
            swing.Lunge = gap > 0f ? MathF.Min(gap / MathF.Max(swing.ContactAt, 0.05f), heavy && !dive ? LungeHeavyMost : LungeMost) : 0f;
            _fm.Velocity = Motion(target) + swing.Dir * swing.Lunge;
        }

        _swing = swing;
        _rush = null;
    }

    // Timed off the swing. Once the punch time is up the hover blends over the recovery.
    private void SwingStep(RPawnPlayer pawn, RPlayerController pc, Swing s)
    {
        double age = Clock.Now - s.Since;
        if (!s.Landed)
        {
            if (s.Target is { IsValid: true, bDeleteMe: false } target && !IsHeld(target))
            {
                float gap = Gap(pawn, target, out Vector3 to);
                float dist = to.Length();
                if (dist > 1f)
                    s.Dir = to / dist;
                bool close = gap <= -PunchReach * 0.4f;
                _fm.Velocity = Motion(target) + (close || s.Lunge <= 0f ? Vector3.Zero : s.Dir * s.Lunge);
            }

            if (age >= s.ContactAt)
                Land(pawn, pc, s);
        }

        if (age < s.Seconds)
            return;

        _swing = null;
        if (s.Blow == null)
            return;

        ThawWind();
        _overlaySeq = null;
        ApplySpec(pawn, HoverKey(), force: false, SettleBlend);
    }

    private void Land(RPawnPlayer pawn, RPlayerController pc, Swing s)
    {
        s.Landed = true;
        _fovKick = MathF.Max(_fovKick, s.Heavy ? 14f : 6f);
        if (s.Target is not { IsValid: true, bDeleteMe: false } thing || !Usable(thing, s.Kind))
            return;

        float gap = Gap(pawn, thing, out Vector3 to);
        float dist = to.Length();
        if (gap > PunchReach * MissPast)
            return;

        Vector3 dir = dist > 1f ? to / dist : s.Dir;
        _lastPunchAt = Clock.Now;
        // The throw punch lets go of him and sends him off like a throw, with a punch's hit.
        if (s.Throw && IsHeld(thing))
        {
            ThrowHeld(pawn, Look());
            if (thing is RPawnVillain thrown)
                HitSound(pawn, thrown, finishing: false, heavy: true);
            _camKick = MathF.Max(_camKick, 1f);
            HitStop(heavy: false);
            _lastPunchAt = Clock.Now;
            return;
        }

        // The held thug goes where he's looking. The finisher has no shockwave.
        bool held = IsHeld(thing);
        if (held)
            dir = Look();
        LetGoFor(thing);
        Vector3 at = Box(thing).Centre;
        Strike(pawn, pc, thing, s.Kind, dir, s.Heavy, s.Hard);
        if (s.Heavy && !held)
        {
            Shockwave(pawn, pc, thing, at, s.Hard, WaveRadius);
            WaveFeel(pawn, at, dir);
        }

        _fm.Velocity = -dir * (StrikeRecoil * (s.Heavy ? 1.5f : 1f));
        _camKick = MathF.Max(_camKick, s.Heavy ? 2.5f : 0.8f);
        HitStop(s.Heavy);
    }

    private void Strike(RPawnPlayer pawn, RPlayerController pc, Actor thing, Kind kind, Vector3 dir, bool heavy, float hard)
    {
        float force = (heavy ? StrikeForce * hard : QuickForce) * Opt.RamForce.Number;
        Vector3 at = Box(thing).Centre;
        bool flung = heavy;
        switch (thing)
        {
            case RPawnVillain thug:
                PunchThug(thug, pawn, pc, dir, force, heavy, hard, out flung);
                break;
            // A driven car only takes the knock and damage. Unpinning it threw the passengers about.
            case RVehicle driven when driven.Driver is { IsValid: true } || driven.bDriving:
                PunchDriven(driven, pawn, pc, dir, force, heavy, at);
                break;
            // A quick punch dents and shoves a car. Only a heavy one wrecks it.
            case RVehicle car:
                RamCar(car, pawn, pc, dir, force, heavy ? 1f : 0.4f, at);
                break;
            case RAbandonedVehicle parked:
                Wake(parked);
                RamParked(parked, pawn, pc, dir, force, heavy ? 1f : 0f, at);
                break;
            case RDestructibleProp prop:
                BreakProp(prop, pawn, pc, dir, force, 1f, at);
                break;
            default:
                Ram(thing, pawn, pc, dir, RamWreck, force, true, at);
                break;
        }

        _rammed[thing.Ptr] = Clock.Now;
        // Only a heavy punch hurts things where what it hit lands.
        if (kind != Kind.Prop && flung)
            Fling(thing, kind);
        if (thing is not RPawnVillain)
            Thud(pawn, heavy);
    }

    // A driven car keeps its physics. The game's weapon hit does the damage and throws passengers out.
    private static void PunchDriven(RVehicle car, RPawnPlayer pawn, PlayerController pc, Vector3 dir, float force, bool heavy, Vector3 at)
    {
        float hard = heavy ? 1f : 0.3f;
        Kick(car.Mesh, Lifted(dir, hard) * (force * RamCarKick), hard);
        int health = car.Health;
        int damage = heavy ? health + 1 : Math.Max(1, (int)(health * 0.25f));
        car.TakeDamageFromWeapon(damage, pc, at, dir * force * 2f, Vector3.Zero, RDmgType_BatmobileCannon.StaticClass(), default, pawn, false);
    }

    // A jab hurts and rocks him. The jab that drops him and every heavy punch launch him.
    private static void PunchThug(RPawnVillain thug, RPawnPlayer pawn, PlayerController pc, Vector3 dir, float force, bool heavy, float hard, out bool flung)
    {
        int health = thug.Health;
        int most = thug.HealthMax > 0 ? thug.HealthMax : Math.Max(health, 100);
        int jab = Math.Max(1, (int)(most * JabShare));
        if (!heavy && jab < health)
        {
            flung = false;
            Vector3 push = dir * (force * 0.3f);
            var info = new RPawnCombat.FDamageInfo
            {
                Attacker = pawn,
                Impulse = push,
                ForceHitReactionHeading = push,
                DamageAmount = jab,
                DamageType = RDmgType_Strike.StaticClass(),
                bHeavyHit = true,
            };

            _jabbing = true;
            try
            {
                thug.DamagedBy(info);
            }
            finally
            {
                _jabbing = false;
            }

            HitSound(pawn, thug, finishing: false, heavy: false);
            return;
        }

        flung = true;
        RamThug(thug, pawn, pc, dir, force, 1f, thug.Location);
        QueueThrow(thug, (dir + Vector3.UnitZ * PunchUp) * (StrikeThrow * hard * Opt.PunchForce.Number));
        HitSound(pawn, thug, finishing: true, heavy);
    }

    // Knocks everything round a point away. Used by heavy punches, the ground slam and the sonic burst.
    private void Shockwave(RPawnPlayer pawn, RPlayerController pc, Actor? hit, Vector3 at, float hard, float reach)
    {
        float radius = reach * (0.6f + 0.35f * hard);
        foreach ((Actor thing, Kind kind) in _near)
        {
            if ((hit != null && thing.Ptr == hit.Ptr) || IsHeld(thing) || !Usable(thing, kind))
                continue;

            Vector3 centre = Box(thing).Centre;
            Vector3 away = centre - at;
            float dist = away.Length();
            if (dist > radius)
                continue;

            away.Z = 0f;
            Vector3 dir = away.LengthSquared() > 1f ? Vector3.Normalize(away) : Vector3.UnitX;
            float fall = 1f - dist / radius * 0.5f;
            float force = WaveForce * hard * fall * Opt.RamForce.Number;
            Blast(pawn, pc, thing, kind, dir, force, fall, StrikeThrow * 0.6f * fall * Opt.PunchForce.Number, PunchUp);
        }
    }

    // Knocks one thing away from a blast. hard is the share of a full hit, 0 to 1.
    private void Blast(RPawnPlayer pawn, PlayerController pc, Actor thing, Kind kind, Vector3 dir, float force, float hard, float thugThrow, float thugUp)
    {
        Vector3 centre = Box(thing).Centre;
        switch (thing)
        {
            case RPawnVillain thug when ThugUp(thug):
                RamThug(thug, pawn, pc, dir, force, 1f, thug.Location);
                QueueThrow(thug, (dir + Vector3.UnitZ * thugUp) * thugThrow);
                break;
            // A driven car only gets the knock and damage, unpinning it threw the passengers about.
            case RVehicle driven when driven.Driver is { IsValid: true } || driven.bDriving:
                PunchDriven(driven, pawn, pc, dir, force, hard >= 1f, centre);
                break;
            case RVehicle car:
                RamCar(car, pawn, pc, dir, force, 0.5f * hard, centre);
                break;
            case RAbandonedVehicle parked:
                Wake(parked);
                RamParked(parked, pawn, pc, dir, force, 0.5f * hard, centre);
                break;
            case RDestructibleProp prop:
                BreakProp(prop, pawn, pc, dir, force, hard, centre);
                break;
            default:
                if (thing is RDeadVehicle)
                    Wake(thing);
                Kick(Part(thing), Lifted(dir, hard) * force, hard);
                break;
        }

        _rammed[thing.Ptr] = Clock.Now;
        if (kind != Kind.Prop)
            Fling(thing, kind);
    }

    // A short slow motion on each hit, in real seconds. The game's combat slow mo froze thug AI.
    private static void HitStop(bool heavy)
    {
        if (Opt.HitStop.On)
            Pace.Hold(HitStopKey, heavy ? SlowHeavy : SlowTap, heavy ? StopHeavy : StopTap);
    }

    private const string HitStopKey = "hit stop";

    private static void EndSlow() => Pace.Drop(HitStopKey);

    // While a punch is going or winding up he faces the target.
    private bool FaceTarget(RPawnPlayer pawn, out float yaw)
    {
        yaw = 0f;
        Actor? target = _swing?.Target ?? (_charging ? Aim?.Thing : null);
        if (target is not { IsValid: true, bDeleteMe: false })
            return false;

        // The held thug is carried from his facing, so turning to him spun them both.
        if (IsHeld(target))
            return false;

        Vector3 to = Box(target).Centre - pawn.Location;
        if (to.X * to.X + to.Y * to.Y < 1f)
            return false;

        yaw = MathF.Atan2(to.Y, to.X) * Geo.Deg;
        return true;
    }
}
