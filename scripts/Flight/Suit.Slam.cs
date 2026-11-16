using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// The ground slam from a high hover, and the sonic burst while boosting.
internal sealed partial class Suit
{
    // Lower than this it's the plain drop.
    private const float SlamFrom = 400f;
    private const float SlamStart = 2000f, SlamMost = 9000f, SlamAccel = 9000f;
    private const float SlamLandAt = 50f;
    private const double SlamMostSeconds = 6.0;
    // He turns feet first this many seconds before the ground.
    private const float SlamFlare = 0.3f;
    // A fall this far hits as hard as it gets.
    private const float SlamFullFall = 4000f;
    private const float SlamReach = 750f;
    // Ground this close is stepped down onto, or the game plays its own landing over ours.
    private const float SlamSnap = 150f;
    // Tried in order when the one in Rig isn't loaded.
    private static readonly string[] SlamLandAnims = { "Glide_Land_Hard", "Land" };
    private static readonly FName StandMove = new("Standing");
    private static readonly FName StandWeapon = new("Relaxed");
    private static readonly FName WalkState = new("WalkingMode");

    private const float SonicReach = 450f;
    private const double SonicAgain = 2.5;

    private bool _slamming, _slamFlaring;
    private float _slamSpeed, _slamTop, _slamZ, _slamStep;
    private double _slamSince, _sonicReady;

    private void ResetSlam()
    {
        _slamming = false;
        _slamFlaring = false;
        _sonicReady = 0.0;
    }

    private bool CanSlam =>
        _ground > SlamFrom && _ground < GroundReach && !Holding && _swing == null && !_charging && !Rushing && !_finishing;

    private void StartSlam(RPawnPlayer pawn)
    {
        _slamming = true;
        _slamFlaring = false;
        _slamSpeed = MathF.Max(SlamStart, -_fm.Velocity.Z);
        _slamTop = pawn.Location.Z;
        _slamSince = Clock.Now;
        _fovKick = MathF.Max(_fovKick, FovKick);
        Sound(pawn, Fx.DiveSound);
    }

    // After the flight model, before he's moved. The hover still steers him a little.
    private void SlamStep(RPawnPlayer pawn, in FlightInput input, float dt)
    {
        Vector3 v = _fm.Velocity;
        if (input.Lift > 0.5f || Clock.Now - _slamSince > SlamMostSeconds)
        {
            // Pulled out of it. Most of the fall is taken off at once, the hover brakes the rest.
            _slamming = false;
            _slamFlaring = false;
            _fm.Velocity = new Vector3(v.X, v.Y, MathF.Max(v.Z, -_fm.ClimbSpeed * 2f));
            return;
        }

        _slamSpeed = MathF.Min(SlamMost, _slamSpeed + SlamAccel * dt);
        _fm.Velocity = new Vector3(v.X, v.Y, -_slamSpeed);
        _slamZ = pawn.Location.Z;
        _slamStep = _slamSpeed * dt;
    }

    // After he's moved. He's landed when the ground is at his feet or something stopped him.
    private void SlamCheck(RPawnPlayer pawn, RPlayerController pc)
    {
        _ground = Ground.Below(pawn, GroundReach);
        _slamFlaring = _ground < _slamSpeed * SlamFlare;
        bool stopped = _slamStep > 5f && _slamZ - pawn.Location.Z < _slamStep * 0.3f;
        if (stopped || _ground < SlamLandAt)
            SlamLand(pawn, pc);
    }

    private void SlamLand(RPawnPlayer pawn, RPlayerController pc)
    {
        _slamming = false;
        _slamFlaring = false;
        float hard = Math.Clamp((_slamTop - pawn.Location.Z) / SlamFullFall, 0.3f, 1f);
        _fm.Velocity = Vector3.Zero;

        float below = Ground.Below(pawn, SlamSnap);
        bool onGround = below < SlamSnap;
        if (onGround && below > 1f)
            pawn.Move(-Vector3.UnitZ * below);

        Vector3 feet = Feet(pawn);
        Shockwave(pawn, pc, null, feet + Vector3.UnitZ * 60f, 1f + hard, SlamReach);
        SlamFeel(pawn, feet, hard);
        HitStop(heavy: true);

        // Stopped on something the trace can't see, like a car roof, he just drops out of flight.
        End("GROUND SLAM", fall: !onGround);
        if (onGround)
            Safe.Run("slam landing", () => LandOnFeet(pawn, pc, Rig.SlamLandAnim, SlamLandAnims));
    }

    // Standing and walking again, with the landing played into his standing pose.
    private void LandOnFeet(RPawnPlayer pawn, RPlayerController pc, string first, params string[] backups)
    {
        pawn.Velocity = Vector3.Zero;
        pawn.SetPhysics(Actor.EPhysics.PHYS_Walking, false);
        pc.GotoState(WalkState);
        pc.OnPlayerHasBeenMoved();

        AnimSequence? seq = FindAnim(first);
        foreach (string name in backups)
            seq ??= FindAnim(name);
        if (seq is { IsValid: true })
            PlayIntoStand(pawn, seq);
        else
            Ground.Settle(pawn);
    }

    // On foot, an anim played once on all of him that ends in his standing pose.
    private static void PlayIntoStand(RPawnPlayer pawn, AnimSequence seq)
    {
        if (pawn.Anim is { IsValid: true } anim)
            anim.ForceAnimatedAutomaticTransitionsOff = false;
        var transition = new RPawnCharacter.FCustomTransitionConfig
        {
            Config = new RAnimConfig.FCustomAnimConfig { FullBodyAnim = seq.SequenceName, CustomFullBodyAnim = seq },
        };
        pawn.ForceChangePose(StandMove, StandWeapon, default, false, transition, default, default, Length(seq));
    }

    public bool Slamming => _flying && _slamming;

    // How much of a sonic burst is left, 1 down to 0.
    public float SurgeLeft => _flying ? _fm.Surge : 0f;

    // Seconds before the sonic burst can be used again.
    public double SonicWait => Math.Max(0.0, _sonicReady - Clock.Now);

    private bool CanSonic => _fm.Boosting && !_slamming && !Rushing && !Holding && Clock.Now >= _sonicReady;

    private void SonicBurst(RPawnPlayer pawn)
    {
        _sonicReady = Clock.Now + SonicAgain;
        _fm.StartSurge();
        SonicFeel(pawn);
        if (World.Pc is { } pc)
            Shockwave(pawn, pc, null, pawn.Location, 0.6f, SonicReach);
    }
}
