using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;
using Pov = BmSDK.GameObject.FTPOV;

namespace Trinity.Eclipse;

// The Flash's powers past the super run: speed force charges, the lightning throw, the blitz, the flurry, the
// tornado and the lightning that jumps off him while he runs. Slow time is in Suit.Slow.
internal sealed partial class Suit
{
    private const string BlitzKey = "eclipse.blitz";
    private const string BlitzPace = "blitz";

    // Charges come back while he runs fast, and slowly on their own.
    public const int ChargesMost = 3;
    private const float ChargeRunSeconds = 6f, ChargeIdleSeconds = 30f;

    // Effects and sounds, all seen or heard in game.
    private const string BoltBeam = "VFX_Vehicles.Particles.Drone_MiniPredator_Electricity";
    private const string Crackle = "PFX_Batman.fx_playerElectricution_p";
    private const string HandCharge = "VFX_Weapon.Particles.MultiThug_Electric_Fist";
    private const string BoltHit = "FFX_Combat.Particles.Destroy.Electric_Impact";
    private const string SpeedTrail = "FFX_Combat.Particles.Trails.fx_SpeedTrails3_p";
    private const string ChargeLoop = "Ww_CHR_BatmanBatarang.Play_Batarang_Electric_Charge";
    private const string Zap = "Ww_CHR_BatmanElectrocute.Play_Batman_Electrocute";
    private const string Spark = "Ww_CHR_BatmanREC.Play_REC_Spark";
    private const string ZipWhoosh = "Ww_CHR_Combat.Play_TD_Whoosh_Quick";
    private const string FieldIn = "Ww_CHR_Combat_Brute.Play_Brute_Special_Electricfield_In";
    private const string FieldOut = "Ww_CHR_Combat_Brute.Play_Brute_Special_Electricfield_Out";

    private static readonly FName FlashRHand = new("Bip01_R_Hand"), FlashLHand = new("Bip01_L_Hand");
    private static readonly FName RightFoot = new("Bip01_R_Foot"), LeftFoot = new("Bip01_L_Foot");
    private static readonly FName Spine = new("Bip01_Spine1");

    // The strike at each blitz stop.
    private const string StrikeAnim = "Attack_Close_Weak_Forward";
    // How fast the hook that throws the bolt blends in.
    private const float HookBlend = 0.1f;
    private const string HookKey = "eclipse.hook";

    private const float BoltChargeSeconds = 0.6f, BoltLeast = 0.15f, BoltReach = 4500f, BoltRadius = 300f, BoltAimSlack = 14f;
    private const double BoltCooldown = 0.4, BoltWindup = 0.4, BoltHold = 0.3, CrackleSeconds = 1.4;

    private float _charges = ChargesMost;
    private float _bolt = -1f;
    private double _boltReady, _boltFireAt = -1.0;
    private float _boltPower;
    // His own effect on his hand, so a full effect pool can't skip it.
    private readonly WornFx _handCharge = new(HandCharge, "Bip01_R_Hand");
    private Every _chargeBuzz;
    private bool _chargeFull;
    // The throw on foot is a stance change, so the game plays it, and he's settled back after.
    private static readonly FName ThrowStance = new("StandingNoTurn");
    private double _throwSettleAt = -1.0;
    private const string ChargeFullSound = "Ww_CHR_BatmanShockwaveUpgrade.Play_BatmanLandShockwave_ChargeUp";
    // A full bolt jumps on to this many more thugs this close to the first.
    private const int ChainMost = 2;
    private const float ChainReach = 600f, FacingSlack = 20f;

    // Lightning held between his hand and where it landed for a moment, and crackles on what it hit.
    private readonly List<(ParticleSystemComponent Fx, Vector3 To, double Until)> _arcs = new();
    private readonly List<(WornFx Fx, Pawn On, double Until)> _crackles = new();

    // Our own arcs, impact and crackles, reused in turn, since the game's pool is full mid fight.
    private readonly PlacedFx[] _beams = { new(BoltBeam), new(BoltBeam), new(BoltBeam) };
    // The run's arcs have their own, so they don't steal the bolt's. Each strike is three kinked pieces.
    private readonly PlacedFx[] _runBeams = Enumerable.Range(0, 6).Select(_ => new PlacedFx(BoltBeam)).ToArray();
    private int _runBeamNext;
    // The blitz's streaks stay up until time snaps back, one per zip, so its own effects are made as needed.
    private readonly List<PlacedFx> _blitzBeams = new();
    private readonly HashSet<nint> _blitzArcs = new();
    private int _blitzBeamNext;
    private readonly PlacedFx _impact = new(BoltHit);
    private readonly WornFx[] _thugArcs = Enumerable.Range(0, 8).Select(_ => new WornFx(Crackle, "Bip01_Spine1")).ToArray();
    // The ground burst under each thug as the blitz lets go.
    private const string BlitzBurst = "VFX_Vehicles.Particles.Tank.Vehicle_EMP_Car";
    private readonly PlacedFx[] _bursts = Enumerable.Range(0, 8).Select(_ => new PlacedFx(BlitzBurst)).ToArray();
    private const string BlitzIn = "Ww_SBJ_Whoosh_2D.Play_SBJ_Whoosh_Tunnel_In";
    private const string BlitzOut = "Ww_SBJ_Whoosh_2D.Play_SBJ_Whoosh_Tunnel_Out";
    private const string StrikeWhoosh = "Ww_CHR_Combat.Play_Strike_Whoosh03a_Stirong";
    private readonly List<RPawnVillain> _blitzSkip = new();

    // The blitz has two parts. Holding the button, time all but stops and he marks thugs on the crosshair. Letting
    // go, he strikes each in turn, comes back to where he stood, and time snaps back.
    private const float AimWorld = 0.2f, MarkSlack = 12f;
    private const double AimLongest = 8.0;
    private const string MarkSound = "Ww_CHR_BatmanGauntlet.Play_BM_Gaunlet_UI_Select";
    private bool _blitzAiming, _blitzReturning, _blitzReturned, _markWas, _cancelWas;
    private RPawnVillain? _blitzAim;
    private double _aimEnds;
    private Vector3 _blitzOrigin;
    private float _blitzOriginYaw;

    // The view over the fight while he strikes: high up, slowly circling the middle of it.
    private Vector3 _blitzCentre;
    private float _blitzSpread, _orbitYaw;
    private const float OrbitTurn = 20f;

    public bool BlitzAiming => _blitzing && _blitzAiming;

    // The flurry: he plants by a thug and hits him dozens of times in a blink, then launches him. On foot or running.
    // The world slows round him and his own anims run fast, like the blitz.
    private const float FlurryReach = 600f, FlurryNear = 300f, FlurryCone = 50f, FlurryWorld = 0.1f, FlurryAnimRate = 6f;
    // The thug's own anims during the flurry, as a share of real speed, so he moves in the slow motion too.
    private const float FlurryThugRate = 0.4f;
    // Far enough off him that their bodies never touch, or his blows nudge the thug away.
    private const float FlurryStandOff = 115f;
    private const double FlurrySeconds = 1.5, FlurryEvery = 0.07, FlurryCooldown = 2.0;
    private const string FlurryKey = "eclipse.flurry";
    private bool _flurry, _flurryWas;
    // A flurry out of a run keeps our camera. The game's runs on world time, so at 0.1 it crawled after him and
    // snapped when time came back.
    private bool _ownCam;
    // Kept a moment past the end, so the game's camera has caught up with him by the hand back.
    private const double OwnCamAfter = 0.5;
    private double _ownCamOff;
    private RPawnVillain? _flurryThug;
    private Vector3 _flurryHold, _flurrySpot;
    private double _flurryEnds, _flurryNext, _flurryReady;
    private float _flurryAnimWas = 1f, _flurryThugWas = 1f;
    // His ragdoll switches while he's being hit. One left on by the Flash's punches would launch him on the first punch.
    private bool _flurryNeverWas, _flurryAlwaysWas;

    public bool Flurrying => _flurry;

    // The thug the flurry is holding right now.
    public bool FlurryHolds(RPawnVillain thug) => _flurry && _flurryThug is { IsValid: true } held && held.Ptr == thug.Ptr;

    public bool FlurryReady => !_flurry && Opt.Flurry.On && Clock.Now >= _flurryReady;

    public RPawnVillain? BlitzAim => _blitzAim;
    private Blow? _blitzLastBlow;
    private int _beamNext;

    // The blitz runs itself: a short zip to each marked thug, a strike, the next one.
    private const string BlitzHold = "blitz";
    // Marked by hand, any thug in sight out to MarkReach, near enough as many as he likes. Picked for him, the
    // nearest few within BlitzReach.
    private const float BlitzReach = 2200f, MarkReach = 6000f, ZipStandOff = 85f, ZipSeconds = 0.09f;
    private const double StrikePause = 0.16, SnapPause = 0.3;
    private const int BlitzMost = 8, MarksMost = 64;

    private bool _blitzing;
    private readonly List<RPawnVillain> _blitzMarks = new();
    private readonly List<RPawnVillain> _blitzHits = new();
    private float _blitzAnimWas = 1f;
    private float _zipT = -1f;
    private Vector3 _zipFrom, _zipTo;
    private RPawnVillain? _zipTarget;
    private double _blitzNextAt;
    private double _blitzStarted;

    // The tornado runs itself too: a tight circle round where he stood, for a few seconds.
    private const float TornadoRadius = 230f, TornadoSpeed = 1900f, TornadoReach = 650f;
    private const double TornadoSeconds = 3.0;
    private const string TornadoWind = "Ww_AMB_Stagg.Play_AMB_Airship_External_Wind";
    private bool _tornado;
    private Vector3 _eye;
    private double _tornadoEnds;
    private float _tornadoSide = 1f;
    private Every _tornadoPull, _tornadoPuff, _tornadoWhoosh;
    private const double TornadoCooldown = 8.0;
    private double _tornadoReady;
    // Where round the circle he is, in degrees, and the height he runs it at.
    private float _tornadoAngle, _tornadoZ;
    // Dust kicked up round the ring, on effects of his own since the game's pool is full in a fight.
    private readonly PlacedFx[] _ringDust = Enumerable.Range(0, 6).Select(_ => new PlacedFx(Fx.Dust)).ToArray();
    private int _ringDustNext;
    private int _tornadoLifting;
    // Who the bolt is on while he charges it, for the HUD's ring. Looked for a few times a second.
    private Actor? _boltAim;
    private Every _boltAimScan;
    // Points round the ring checked for walls and floor before it starts.
    private const int RingChecks = 8;
    private const float RingStepMost = 40f;

    // Trails on his hands and feet while he runs, and lightning crackling over him at top speed. Both are his own
    // components, so a full effect pool can't skip them.
    private readonly WornFx[] _trails =
    {
        new(SpeedTrail, "Bip01_R_Hand"), new(SpeedTrail, "Bip01_L_Hand"), new(SpeedTrail, "Bip01_R_Foot"), new(SpeedTrail, "Bip01_L_Foot"),
    };

    // Gold while he's on, see GoldCrackle. Three at once so it wraps him.
    private readonly WornFx[] _bodyArcs =
    {
        new(Crackle, "Bip01_Spine1"), new(Crackle, "Bip01_R_Forearm"), new(Crackle, "Bip01_L_Forearm"),
    };
    private Every _runCrackle;

    // Lightning that jumps off him at things he runs past and knocks them away from his path.
    private const float RunZapFrom = 0.5f, RunZapReach = 900f, RunZapHard = 0.7f;
    private const double RunZapEvery = 0.7, RunZapAgain = 2.0;
    private const int RunZapKinks = 3;
    private const float RunZapKinkMost = 90f;
    // How long each arc shows in world time, so it plays out in the slow motion of his speed vision. Capped so two
    // strikes fit the six pieces.
    private const double RunZapArc = 0.25, RunZapArcMost = 0.85;
    private static readonly FName[] RunZapBones = { Spine, FlashRHand, FlashLHand };
    private Every _runZap;
    private double _crackleOff;

    // The run's sounds. The ones the flight used for it aren't loaded in the city.
    private const string RunWhoosh = "Ww_CHR_Combat.Play_TD_Whoosh_Quick";
    private const string SkidWhoosh = "Ww_CHR_Combat.Play_TD_Whoosh_Slow";
    // The old rush loop. It's never started now, since it has no stop event and kept playing. It's only named to take
    // off copies an older load left on him.
    private const string RunRush = "Ww_AMB_Stagg.Play_AMB_Airship_External_Wind";

    // Whooshes as he passes things close at speed.
    private const string PassCar = "Ww_SBJ_Whoosh_Car.Play_SBJ_Whoosh_Car";
    private const string PassThug = "Ww_NPC_Ninja.Play_Ninja_Whoosh01";
    private const string PassWall = "Ww_SBJ_Whoosh_Pillar.Play_SBJ_Whoosh_Pillar";
    private const float PassSide = 450f, PassScanReach = 2500f, WallBeside = 140f;
    private readonly List<Actor> _passNear = new();
    private readonly Dictionary<nint, float> _passAhead = new();
    private readonly Dictionary<nint, double> _passedAt = new();
    private Every _passScan, _passWall;
    private double _passWallAt;
    private bool _wallBeside;

    private FrameTimer _flashClock;

    public float Charges => _charges;

    public float BoltCharge => _bolt < 0f ? 0f : Math.Clamp(_bolt / BoltChargeSeconds, 0f, 1f);

    public bool Blitzing => _blitzing;

    public float BlitzLeft => _blitzing && _blitzMarks.Count > 0 ? 1f - _blitzHits.Count / (float)_blitzMarks.Count : 0f;

    // The marked thugs in strike order, whether each has been hit and which is next.
    public IEnumerable<(RPawnVillain Thug, bool Hit, bool Next)> BlitzMarks
    {
        get
        {
            RPawnVillain? next = NextMark();
            foreach (RPawnVillain t in _blitzMarks)
            {
                if (t is { IsValid: true, bDeleteMe: false })
                    yield return (t, _blitzHits.Any(h => h.Ptr == t.Ptr), next != null && next.Ptr == t.Ptr);
            }
        }
    }

    public int BlitzStruck => _blitzHits.Count;

    public int BlitzTotal => _blitzMarks.Count;

    public bool Twisting => _tornado;

    public bool TornadoReady => !_tornado && Opt.Tornado.On && Clock.Now >= _tornadoReady;

    public float TornadoLeft => _tornado ? (float)Math.Clamp((_tornadoEnds - Clock.Now) / TornadoSeconds, 0.0, 1.0) : 0f;

    private static RPawnPlayer? FlashPawn()
    {
        if (!Heroes.Is(Hero.Flash) || !World.InGame || World.Paused || World.Pc is not { } pc || Hooks.InCinematic(pc))
            return null;
        return World.Pawn is RPawnPlayer { Health: > 0, bHidden: false, bDeleteMe: false } pawn && pawn.DrivenVehicle is not { IsValid: true } ? pawn : null;
    }

    // From the game tick.
    private void FlashStep()
    {
        float dt = _flashClock.Step(0.1f);
        ArcStep();
        RPawnPlayer? pawn = FlashPawn();
        if (pawn == null || World.Pc is not { } pc)
        {
            ResetFlash();
            return;
        }

        GoldCrackle.Apply();

        if (_ownCam && !_flurry && !_slowing && !_rewinding && Clock.Now >= _ownCamOff)
            OwnCamOff();

        ChargeStep(dt);
        SlowStep(pawn, pc, dt);
        RewindStep(pawn, pc);
        if (_rewinding)
            return;
        BoltStep(pawn, pc, dt);
        BlitzStep(pawn, pc, dt);
        FlurryStep(pawn, pc);

        if (Controls.Take(Act.Blitz) && !_blitzing && !_slowing)
            StartBlitz(pawn, pc);

        if (Controls.Take(Act.Tornado) && !_blitzing && !_slowing)
            StartTornado(pawn, pc);
    }

    private void ChargeStep(float dt)
    {
        if (Opt.InfiniteForce.On)
        {
            _charges = ChargesMost;
            return;
        }

        if (_blitzing)
            return;
        float gain = dt / ChargeIdleSeconds;
        if (_running && _runSpeed > RunTop * 0.6f)
            gain += dt / ChargeRunSeconds;
        _charges = MathF.Min(ChargesMost, _charges + gain);
    }

    // Hold to charge lightning in his hand, let go to throw it where the camera looks.
    private void BoltStep(RPawnPlayer pawn, RPlayerController pc, float dt)
    {
        if (_boltFireAt > 0.0 && Clock.Now >= _boltFireAt)
        {
            _boltFireAt = -1.0;
            Safe.Run("lightning", () => FireBolt(pawn, pc, _boltPower));
        }

        if (_throwSettleAt > 0.0 && Clock.Now >= _throwSettleAt)
        {
            _throwSettleAt = -1.0;
            HookDone(pawn);
            Safe.Run("throw pose off", () => Ground.Settle(pawn));
        }

        bool held = Controls.Down(Act.Lightning) && !_blitzing;
        if (held && _bolt < 0f && _boltFireAt < 0.0 && Clock.Now >= _boltReady)
        {
            _bolt = 0f;
            _chargeFull = false;
            _handCharge.Set(pawn, true);
            if (Opt.FlightSounds.On)
                Fx.StartLoop(pawn, ChargeLoop);
        }

        if (_bolt < 0f)
            return;

        if (held)
        {
            _bolt += dt;
            if (_boltAimScan.Elapsed(0.1))
                _boltAim = AimedAt(pawn);
            if (_chargeBuzz.Elapsed(0.28))
                Rumble.Play(Buzz.Run);
            if (!_chargeFull && _bolt >= BoltChargeSeconds)
            {
                _chargeFull = true;
                Sound(pawn, ChargeFullSound);
                Rumble.Play(Buzz.Burst);
            }

            return;
        }

        float charge = BoltCharge;
        EndCharge(pawn);
        _boltReady = Clock.Now + BoltCooldown;
        if (charge < BoltLeast / BoltChargeSeconds)
            return;

        // On his feet he throws it, and the bolt leaves his hand at the release. Running, it goes at once.
        _boltPower = charge;
        if (!_running && pawn.Physics == Actor.EPhysics.PHYS_Walking && ThrowBlow(Blows(pawn)) is { } hook)
        {
            _boltFireAt = Clock.Now + BoltWindup;
            Safe.Run("lightning face", () => FaceBoltTarget(pawn));
            Safe.Run("lightning anim", () => PlayHook(pawn, hook));
        }
        else
        {
            Safe.Run("lightning", () => FireBolt(pawn, pc, charge));
        }
    }

    private void EndCharge(RPawnPlayer? pawn)
    {
        if (_bolt < 0f && !_handCharge.On)
            return;
        _bolt = -1f;
        _handCharge.Set(pawn, false);
        if (pawn != null)
            Safe.Run("charge sound", () => Fx.StopLoop(pawn, ChargeLoop));
    }

    // Starts on a fresh press of the flurry button, at the thug in front of him.
    private void FlurryStep(RPawnPlayer pawn, RPlayerController pc)
    {
        bool down = Controls.Down(Act.Flurry);
        bool pressed = down && !_flurryWas;
        _flurryWas = down;
        if (_flurry)
        {
            FlurryHits(pawn, pc);
            return;
        }

        if (!pressed || !FlurryReady || _blitzing || _tornado || _flying || _slowing || _rewinding)
            return;
        Vector3 ahead = _running ? _runHeading : FacingFlat(pawn);
        if (FlurryTarget(pawn, ahead) is not { } thug)
            return;
        var toThug = new Vector3(thug.Location.X - pawn.Location.X, thug.Location.Y - pawn.Location.Y, 0f);
        Vector3 approach = toThug.LengthSquared() > 1f ? Vector3.Normalize(toThug) : ahead;
        if (StandBy(pawn, thug, approach, FlurryStandOff) is not { } spot)
            return;

        Vector3 from = pawn.Location;
        if (_running)
        {
            // Out of the run's pose and state first, so it plays the same as from a walk.
            _camHandOff = true;
            try
            {
                EndRun(RunEnd.Stop);
            }
            finally
            {
                _camHandOff = false;
            }

            _ownCam = true;
            pawn.Velocity = Vector3.Zero;
            Safe.Run("flurry settle", () => Ground.Settle(pawn));
        }

        pawn.SetLocation(spot);
        _flurrySpot = spot;
        var face = new Vector3(thug.Location.X - spot.X, thug.Location.Y - spot.Y, 0f);
        if (face.LengthSquared() > 1f)
            pawn.SetRotation(new Rotator(0f, MathF.Atan2(face.Y, face.X) * Geo.Deg, 0f));
        Arc(from, spot + Vector3.UnitZ * 20f, 0.3);

        _flurry = true;
        _flurryThug = thug;
        _flurryHold = thug.Location;
        _flurryEnds = Clock.Now + FlurrySeconds;
        _flurryNext = Clock.Now;
        Pace.Hold(FlurryKey, FlurryWorld);
        if (pawn.Mesh is { IsValid: true } mesh)
        {
            _flurryAnimWas = SaneRate(mesh.GlobalAnimRateScale);
            Shared.Set(FlurryKey, _flurryAnimWas);
            mesh.GlobalAnimRateScale = _flurryAnimWas * FlurryAnimRate / FlurryWorld;
        }

        if (thug.Mesh is { IsValid: true } thugMesh)
        {
            _flurryThugWas = SaneRate(thugMesh.GlobalAnimRateScale);
            thugMesh.GlobalAnimRateScale = _flurryThugWas * FlurryThugRate / FlurryWorld;
        }

        _flurryNeverWas = thug.bNeverRagdollWhenHit;
        _flurryAlwaysWas = thug.bAlwaysRagdollWhenHit;
        thug.bNeverRagdollWhenHit = true;
        thug.bAlwaysRagdollWhenHit = false;
        NoMove(pc, true);
        GameKeys.Hold(FlurryKey, new[] { Keys.LButton }, new[] { "XboxTypeS_X" });
        CrackleOn(thug, FlurrySeconds + 0.3);
        foreach (WornFx trail in _trails)
            trail.Set(pawn, true);
        Sound(pawn, ZipWhoosh);
        Sound(pawn, FieldIn);
        Rumble.Play(Buzz.Burst);
    }

    // One blow every few hundredths of a second, the thug held where he stood, then the launch.
    private void FlurryHits(RPawnPlayer pawn, RPlayerController pc)
    {
        if (_flurryThug is not { IsValid: true, bDeleteMe: false } thug)
        {
            EndFlurry(pawn, pc, launch: false);
            return;
        }

        // Both held where they met. The blows carry his own root motion, which drifts him off the thug. He goes
        // back first, so he's never in the way of the thug going back.
        pawn.SetLocation(_flurrySpot);
        pawn.Velocity = Vector3.Zero;
        thug.SetLocation(_flurryHold);
        thug.Velocity = Vector3.Zero;
        var face = new Vector3(_flurryHold.X - _flurrySpot.X, _flurryHold.Y - _flurrySpot.Y, 0f);
        if (face.LengthSquared() > 1f)
            pawn.SetRotation(new Rotator(0f, MathF.Atan2(face.Y, face.X) * Geo.Deg, 0f));
        if (Clock.Now >= _flurryEnds)
        {
            EndFlurry(pawn, pc, launch: true);
            return;
        }

        if (Clock.Now < _flurryNext)
            return;
        _flurryNext = Clock.Now + FlurryEvery;
        Safe.Run("flurry blow", () => BlitzStrike(pawn));
        _impact.At(pawn, Box(thug).Centre, Fx.Facing(Geo.Facing(pawn.Rotation.Yaw + 180f)));
        HitSound(pawn, thug, finishing: false, heavy: false);
        Rumble.Play(Buzz.Hit);
    }

    private void EndFlurry(RPawnPlayer? pawn, PlayerController? pc, bool launch)
    {
        if (!_flurry)
            return;

        _flurry = false;
        _flurryReady = Clock.Now + FlurryCooldown;
        // Time snaps back for the last hit.
        Pace.Drop(FlurryKey);
        GameKeys.Release(FlurryKey);
        NoMove(pc, false);
        if (pawn?.Mesh is { IsValid: true } mesh)
            mesh.GlobalAnimRateScale = _flurryAnimWas;
        Shared.Set(FlurryKey, null);
        _ownCamOff = Clock.Now + OwnCamAfter;
        RPawnVillain? thug = _flurryThug;
        _flurryThug = null;
        if (thug?.Mesh is { IsValid: true } thugMesh)
            thugMesh.GlobalAnimRateScale = _flurryThugWas;
        if (thug is { IsValid: true })
        {
            thug.bNeverRagdollWhenHit = _flurryNeverWas;
            thug.bAlwaysRagdollWhenHit = _flurryAlwaysWas;
        }
        if (pawn == null)
            return;
        Safe.Run("flurry pose off", () => Ground.Settle(pawn));
        if (!_running)
        {
            foreach (WornFx trail in _trails)
                trail.Set(pawn, false);
        }

        if (!launch || thug is not { IsValid: true, bDeleteMe: false } || pc is not RPlayerController rpc)
            return;

        // The last hit sends him flying.
        Vector3 away = thug.Location - pawn.Location;
        away.Z = 0f;
        Vector3 dir = away.LengthSquared() > 1f ? Vector3.Normalize(away) : Geo.Facing(pawn.Rotation.Yaw);
        float force = Opt.FlashPunchForce.Number;
        Safe.Run("flurry launch", () =>
        {
            RamThug(thug, pawn, rpc, dir, WaveForce * 1.5f * force, 1f, thug.Location);
            QueueThrow(thug, (dir + Vector3.UnitZ * PunchUp) * (StrikeThrow * 1.6f * force));
        });
        _bursts[0].At(pawn, thug.Location - Vector3.UnitZ * thug.GetCollisionHeight(), default);
        CrackleOn(thug, CrackleSeconds);
        Sound(pawn, StrikeWhoosh);
        Sound(pawn, Fx.WaveSound);
        Sound(pawn, FieldOut);
        Rumble.Play(Buzz.Heavy);
        _camKick = MathF.Max(_camKick, 1.2f);
    }

    // Where the camera looks, flat, or where he faces without one.
    private static Vector3 FacingFlat(RPawnPlayer pawn)
    {
        float yaw = Hooks.PlayerCam() is { } cam ? cam.CameraCache.POV.Rotation.Yaw : pawn.Rotation.Yaw;
        return Geo.Facing(yaw);
    }

    // The standing thug in front of him with a clear line, or failing that the nearest one right next to him.
    private RPawnVillain? FlurryTarget(RPawnPlayer pawn, Vector3 ahead)
    {
        Vector3 chest = pawn.Location + Vector3.UnitZ * 20f;
        RPawnVillain? best = null;
        float bestDist = FlurryReach;
        foreach (RPawnVillain thug in Game.FindObjects<RPawnVillain>())
        {
            if (thug is not { IsValid: true, IsClassDefaultObject: false } || !ThugUp(thug))
                continue;
            var flat = new Vector3(thug.Location.X - pawn.Location.X, thug.Location.Y - pawn.Location.Y, 0f);
            float dist = flat.Length();
            bool inFront = dist > 1f && Vector3.Dot(flat / dist, ahead) >= MathF.Cos(FlurryCone / Geo.Deg);
            if (dist < 1f || dist >= bestDist || (!inFront && dist > FlurryNear))
                continue;
            if (Ground.Ray(pawn, chest, Box(thug).Centre, out _, out _))
                continue;
            best = thug;
            bestDist = dist;
        }

        return best;
    }

    // The game's own pose changes allowed again.
    private static void HookDone(RPawnPlayer? pawn)
    {
        if (Shared.Get(HookKey) is not true)
            return;
        Shared.Set(HookKey, null);
        if (pawn?.Anim is { IsValid: true } anim)
            anim.ForceAnimatedAutomaticTransitionsOff = false;
    }

    // Turns him to what the bolt will hit, so the strike points where the lightning goes.
    private void FaceBoltTarget(RPawnPlayer pawn)
    {
        Vector3 eye = pawn.Location + Vector3.UnitZ * 60f;
        Vector3 fwd = Geo.Facing(pawn.Rotation.Yaw);
        if (Hooks.PlayerCam() is { } cam)
        {
            eye = cam.CameraCache.POV.Location;
            Geo.Axes(cam.CameraCache.POV.Rotation, out fwd, out _, out _);
        }

        if ((BoltTarget(pawn, eye, fwd, out Vector3 at) ?? FacingTarget(pawn, out at)) == null)
            return;
        var flat = new Vector3(at.X - pawn.Location.X, at.Y - pawn.Location.Y, 0f);
        if (flat.LengthSquared() > 1f)
            pawn.SetRotation(new Rotator(0f, MathF.Atan2(flat.Y, flat.X) * Geo.Deg, 0f));
    }

    // The same right hook Superman throws a held thug with, played as a transition into a stance he isn't in, at its
    // own pace. The bolt leaves at the moment of contact.
    private void PlayHook(RPawnPlayer pawn, Blow hook)
    {
        if (pawn.Anim is { IsValid: true } anim)
            anim.ForceAnimatedAutomaticTransitionsOff = false;
        bool mirrored = hook.Hand.Length == 0 || (hook.Hand != "Right" && hook.Mirrorable);
        float contact = hook.Contact * Length(hook.Seq);
        float duration = Length(hook.Seq);
        var transition = new RPawnCharacter.FCustomTransitionConfig
        {
            Config = new RAnimConfig.FCustomAnimConfig { FullBodyAnim = hook.Seq.SequenceName, CustomFullBodyAnim = hook.Seq },
        };
        // The last number is how long it blends in. Given the whole length he never got far into the swing.
        pawn.ForceChangePose(ThrowStance, StandWeapon, default, mirrored, transition, default,
            mirrored ? RAnimUtil.EMirrorChoice.MC_Yes : default, HookBlend);
        // On foot the game's own pose changes cut in part way through the punch. They're held off until it's done.
        if (pawn.Anim is { IsValid: true } held)
        {
            held.ForceAnimatedAutomaticTransitionsOff = true;
            Shared.Set(HookKey, true);
        }

        _boltFireAt = Clock.Now + contact;
        // Only after the whole swing, or the follow through gets cut off.
        _throwSettleAt = Clock.Now + duration + 0.05;
    }

    private void FireBolt(RPawnPlayer pawn, RPlayerController pc, float charge)
    {
        Vector3 eye;
        Vector3 fwd;
        if (Hooks.PlayerCam() is { } cam)
        {
            eye = cam.CameraCache.POV.Location;
            Geo.Axes(cam.CameraCache.POV.Rotation, out fwd, out _, out _);
        }
        else
        {
            eye = pawn.Location + Vector3.UnitZ * 60f;
            fwd = Geo.Facing(pawn.Rotation.Yaw);
        }

        Vector3 hand = HandAt(pawn);
        Actor? target = BoltTarget(pawn, eye, fwd, out Vector3 at) ?? FacingTarget(pawn, out at);
        if (target == null)
            at = Ground.Ray(pawn, eye, eye + fwd * BoltReach, out Vector3 hit, out _) ? hit : eye + fwd * BoltReach;

        Arc(hand, at, BoltHold);
        _impact.At(pawn, at, Fx.Facing(-fwd));
        if (target is RPawnVillain struck)
            CrackleOn(struck, CrackleSeconds);
        Sound(target ?? pawn, Zap);
        Sound(pawn, Spark);
        Rumble.Play(Buzz.Burst);

        ScanTargets(pawn);
        float hard = 0.5f + 0.5f * charge;
        float force = Opt.LightningForce.Number;
        if (target != null && _near.Any(n => n.Thing.Ptr == target.Ptr))
        {
            (Actor thing, Kind kind) = _near.First(n => n.Thing.Ptr == target.Ptr);
            Vector3 dir = new Vector3(fwd.X, fwd.Y, 0f);
            dir = dir.LengthSquared() > 0.01f ? Vector3.Normalize(dir) : Geo.Facing(pawn.Rotation.Yaw);
            Blast(pawn, pc, thing, kind, dir, WaveForce * hard * force, hard, StrikeThrow * hard * force, PunchUp);
        }

        Shockwave(pawn, pc, target, at, 0.5f * hard * force, BoltRadius * (0.6f + charge));
        if (charge >= 0.99f && target is RPawnVillain first)
            Chain(pawn, pc, first, force);
    }

    // A full bolt jumps from the first thug to the nearest others standing close by.
    private void Chain(RPawnPlayer pawn, RPlayerController pc, RPawnVillain first, float force)
    {
        Vector3 from = Box(first).Centre;
        var next = Game.FindObjects<RPawnVillain>()
            .Where(t => t is { IsValid: true, IsClassDefaultObject: false } && t.Ptr != first.Ptr && ThugUp(t)
                && Vector3.DistanceSquared(t.Location, first.Location) < ChainReach * ChainReach)
            .OrderBy(t => Vector3.DistanceSquared(t.Location, first.Location)).Take(ChainMost).ToList();
        foreach (RPawnVillain thug in next)
        {
            Vector3 to = Box(thug).Centre;
            Arc(from, to, BoltHold);
            CrackleOn(thug, CrackleSeconds);
            Sound(thug, Zap);
            var dir = new Vector3(to.X - from.X, to.Y - from.Y, 0f);
            dir = dir.LengthSquared() > 1f ? Vector3.Normalize(dir) : Geo.Facing(pawn.Rotation.Yaw);
            Safe.Run("chain hit", () =>
            {
                RamThug(thug, pawn, pc, dir, WaveForce * 0.6f * force, 0.6f, thug.Location);
                QueueThrow(thug, (dir + Vector3.UnitZ * PunchUp) * (StrikeThrow * 0.5f * force));
            });
            from = to;
        }
    }

    // What a bolt let go now would hit, the same way FireBolt picks it.
    private Actor? AimedAt(RPawnPlayer pawn)
    {
        Vector3 eye = pawn.Location + Vector3.UnitZ * 60f;
        Vector3 fwd = Geo.Facing(pawn.Rotation.Yaw);
        if (Hooks.PlayerCam() is { } cam)
        {
            eye = cam.CameraCache.POV.Location;
            Geo.Axes(cam.CameraCache.POV.Rotation, out fwd, out _, out _);
        }

        return BoltTarget(pawn, eye, fwd, out _) ?? FacingTarget(pawn, out _);
    }

    // No thug near the crosshair: the nearest standing one roughly where he faces, with a clear line.
    private Actor? FacingTarget(RPawnPlayer pawn, out Vector3 at)
    {
        at = Vector3.Zero;
        Vector3 eye = pawn.Location + Vector3.UnitZ * 60f;
        Vector3 facing = Geo.Facing(pawn.Rotation.Yaw);
        Actor? best = null;
        float bestDist = BoltReach;
        foreach (RPawnVillain thug in Game.FindObjects<RPawnVillain>())
        {
            if (thug is not { IsValid: true, IsClassDefaultObject: false } || !ThugUp(thug))
                continue;
            Vector3 centre = Box(thug).Centre;
            var flat = new Vector3(centre.X - eye.X, centre.Y - eye.Y, 0f);
            float dist = flat.Length();
            if (dist < 100f || dist >= bestDist || Vector3.Dot(flat / dist, facing) < MathF.Cos(FacingSlack / Geo.Deg))
                continue;
            if (Ground.Trace(pawn, eye, centre, Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out _, out _) is not null)
                continue;
            best = thug;
            bestDist = dist;
            at = centre;
        }

        return best;
    }

    private static Vector3 HandAt(RPawnPlayer pawn) =>
        pawn.Mesh is { IsValid: true } mesh ? mesh.GetBoneLocation(FlashRHand, 0) : pawn.Location + Vector3.UnitZ * 40f;

    // A thug or car near the crosshair with nothing in the way, or null.
    private Actor? BoltTarget(RPawnPlayer pawn, Vector3 eye, Vector3 fwd, out Vector3 at)
    {
        at = Vector3.Zero;
        Actor? best = null;
        float bestAngle = BoltAimSlack;
        var candidates = Game.FindObjects<RPawnVillain>().Where(t => t is { IsValid: true, IsClassDefaultObject: false } && ThugUp(t)).Cast<Actor>()
            .Concat(Game.FindObjects<RVehicle>().Where(c => c is { IsValid: true, IsClassDefaultObject: false } and not RVehicleBatmobileBase));
        foreach (Actor thing in candidates)
        {
            Vector3 centre = Box(thing).Centre;
            Vector3 to = centre - eye;
            float dist = to.Length();
            if (dist < 100f || dist > BoltReach)
                continue;

            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(to / dist, fwd), -1f, 1f)) * Geo.Deg;
            if (angle >= bestAngle)
                continue;
            if (Ground.Trace(pawn, eye, centre, Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out _, out _) is not null)
                continue;

            best = thing;
            bestAngle = angle;
            at = centre;
        }

        return best;
    }

    // A jagged run arc: the straight line broken into pieces, each bend pushed off to the side at random.
    private void Bolt(Vector3 from, Vector3 to, double seconds)
    {
        Vector3 line = to - from;
        float length = line.Length();
        if (length < 1f)
            return;
        Vector3 across = Vector3.Cross(line / length, Vector3.UnitZ);
        if (across.LengthSquared() < 0.01f)
            across = Vector3.UnitX;
        across = Vector3.Normalize(across);
        float kink = MathF.Min(length * 0.18f, RunZapKinkMost);
        Vector3 last = from;
        for (int i = 1; i <= RunZapKinks; i++)
        {
            Vector3 next = i == RunZapKinks ? to : Vector3.Lerp(from, to, i / (float)RunZapKinks)
                + across * ((Random.Shared.NextSingle() * 2f - 1f) * kink)
                + Vector3.UnitZ * ((Random.Shared.NextSingle() * 2f - 1f) * kink * 0.6f);
            Arc(last, next, seconds, run: true);
            last = next;
        }
    }

    // A lightning arc from one point to another, held for a moment.
    private void Arc(Vector3 from, Vector3 to, double seconds, bool run = false)
    {
        PlacedFx beam = run ? _runBeams[_runBeamNext++ % _runBeams.Length] : _beams[_beamNext++ % _beams.Length];
        Shoot(beam, from, to, seconds);
    }

    // A blitz streak, held until the blitz ends.
    private void BlitzArc(Vector3 from, Vector3 to)
    {
        if (_blitzBeamNext >= MarksMost + 1)
            return;
        if (_blitzBeamNext >= _blitzBeams.Count)
            _blitzBeams.Add(new PlacedFx(BoltBeam));
        if (Shoot(_blitzBeams[_blitzBeamNext++], from, to, double.PositiveInfinity) is { } fx)
            _blitzArcs.Add(fx.Ptr);
    }

    // Puts the blitz's streaks out.
    private void BlitzArcsOff()
    {
        double now = Clock.Now;
        for (int i = 0; i < _arcs.Count; i++)
        {
            if (_blitzArcs.Contains(_arcs[i].Fx.Ptr))
                _arcs[i] = (_arcs[i].Fx, _arcs[i].To, now);
        }

        _blitzArcs.Clear();
        _blitzBeamNext = 0;
    }

    private ParticleSystemComponent? Shoot(PlacedFx beam, Vector3 from, Vector3 to, double seconds)
    {
        if (World.Player is not { IsValid: true } owner)
            return null;
        if (beam.At(owner, from, default) is not { } fx)
            return null;

        Safe.Run("arc", () =>
        {
            fx.SetBeamTargetPoint(0, to, 0);
            fx.SetBeamTargetPoint(1, to, 0);
        });
        _arcs.RemoveAll(a => a.Fx.Ptr == fx.Ptr);
        _arcs.Add((fx, to, Clock.Now + seconds));
        return fx;
    }

    // Lightning crackling over someone for a while, on one of our own effects, the free one or else the oldest.
    private void CrackleOn(Pawn pawn, double seconds)
    {
        WornFx slot = _thugArcs.FirstOrDefault(a => !_crackles.Any(c => c.Fx == a))
            ?? _crackles.OrderBy(c => c.Until).First().Fx;
        _crackles.RemoveAll(c => c.Fx == slot);
        slot.Pulse(pawn);
        if (slot.On)
            _crackles.Add((slot, pawn, Clock.Now + seconds));
    }

    // Keeps the arcs pointed where they were thrown and puts out what's done.
    private void ArcStep()
    {
        double now = Clock.Now;
        for (int i = _arcs.Count - 1; i >= 0; i--)
        {
            (ParticleSystemComponent fx, Vector3 to, double until) = _arcs[i];
            if (now >= until || !fx.IsValid)
            {
                Fx.Kill(fx);
                _arcs.RemoveAt(i);
                continue;
            }

            Safe.Run("arc", () =>
            {
                fx.SetBeamTargetPoint(0, to, 0);
                fx.SetBeamTargetPoint(1, to, 0);
            });
        }

        for (int i = _crackles.Count - 1; i >= 0; i--)
        {
            (WornFx worn, Pawn on, double until) = _crackles[i];
            if (now >= until || !on.IsValid)
            {
                worn.Set(on.IsValid ? on : null, false);
                _crackles.RemoveAt(i);
            }
        }
    }

    // Time all but stops and he takes every thug round him in turn by himself: a zip, a strike, the next one.
    // When the last is down time snaps back and they all go flying. The game can't speed one actor up, so he's
    // placed by us and his anims are sped up to match, like the super run.
    private void StartBlitz(RPawnPlayer pawn, RPlayerController pc)
    {
        if (!Opt.Blitz.On || _flying || _tornado)
            return;
        if (_charges < 1f)
        {
            Readout.Toast("FLASH", "NO SPEED FORCE CHARGE", false);
            return;
        }

        // Our camera carries on from the run into the aiming. Handing it to the game's and back misplaced it.
        bool fromRun = _running;
        if (_running)
        {
            _camHandOff = true;
            try
            {
                EndRun(RunEnd.Stop);
            }
            finally
            {
                _camHandOff = false;
            }
        }
        else
        {
            StartView();
        }

        EndCharge(pawn);

        _charges -= 1f;
        _blitzing = true;
        _blitzAiming = true;
        _blitzReturning = false;
        _blitzReturned = false;
        _blitzMarks.Clear();
        _blitzHits.Clear();
        _blitzSkip.Clear();
        _blitzAim = null;
        _markWas = true;
        _cancelWas = true;
        _zipT = -1f;
        _aimEnds = Clock.Now + AimLongest;
        Pace.Hold(BlitzPace, AimWorld);
        if (pawn.Mesh is { IsValid: true } mesh)
        {
            _blitzAnimWas = SaneRate(mesh.GlobalAnimRateScale);
            Shared.Set(BlitzKey, _blitzAnimWas);
        }

        NoMove(pc, true);
        GameKeys.Hold(BlitzHold, new[] { Keys.LButton, Keys.RButton }, new[] { "XboxTypeS_X", "XboxTypeS_B" });
        // Marking at a run is too fiddly. Out of a run he goes straight in on the nearest thugs.
        if (fromRun)
            GoBlitz(pawn, pc);
        CrackleOn(pawn, 0.6);
        Sound(pawn, BlitzIn);
        Sound(pawn, FieldIn);
        Rumble.Play(Buzz.Burst);
    }

    private void BlitzStep(RPawnPlayer pawn, RPlayerController pc, float dt)
    {
        if (!_blitzing)
            return;

        if (_blitzAiming)
        {
            AimStep(pawn, pc);
            return;
        }

        if (_zipT >= 0f)
        {
            ZipStep(pawn, dt);
            return;
        }

        if (Clock.Now < _blitzNextAt)
            return;

        // A safety net, so a stuck zip can't leave the world slowed.
        if (Clock.Now - _blitzStarted > Math.Max(BlitzMost, _blitzMarks.Count) * 0.8)
        {
            EndBlitz(pawn, pc, land: true);
            return;
        }

        if (NextMark() is { } thug)
        {
            StartZip(pawn, thug);
            return;
        }

        // Every mark struck. Back to where he stood, then time snaps back.
        if (!_blitzReturned)
            StartReturn(pawn);
        else
            EndBlitz(pawn, pc, land: true);
    }

    // Time all but stopped. The thug on the crosshair is marked or unmarked with the strike button, letting go of
    // the blitz button starts the strikes, and the cape button calls it off.
    private void AimStep(RPawnPlayer pawn, RPlayerController pc)
    {
        _blitzAim = CrosshairThug(pawn);
        bool mark = Controls.Listening && (Pad.Pressed(PadButton.X) || (Keyboard.IsDown(Keys.LButton) && !_markWas));
        bool cancel = Controls.Listening && (Pad.Pressed(PadButton.B) || (Keyboard.IsDown(Keys.RButton) && !_cancelWas));
        _markWas = Keyboard.IsDown(Keys.LButton);
        _cancelWas = Keyboard.IsDown(Keys.RButton);

        if (cancel)
        {
            _charges = MathF.Min(ChargesMost, _charges + 1f);
            EndBlitz(pawn, pc, land: false);
            return;
        }

        if (mark && _blitzAim is { } aimed)
        {
            int at = _blitzMarks.FindIndex(t => t.Ptr == aimed.Ptr);
            if (at >= 0)
                _blitzMarks.RemoveAt(at);
            else if (_blitzMarks.Count < MarksMost)
                _blitzMarks.Add(aimed);
            Sound(pawn, MarkSound);
            Rumble.Play(Buzz.Hit);
        }

        if (Controls.Down(Act.Blitz) && Clock.Now < _aimEnds)
            return;

        GoBlitz(pawn, pc);
    }

    // Let go. With nothing marked he takes the nearest thugs in a chain round the crowd, like a quick tap.
    private void GoBlitz(RPawnPlayer pawn, RPlayerController pc)
    {
        _blitzAim = null;
        if (_blitzMarks.Count == 0)
        {
            Vector3 from = pawn.Location;
            var left = Game.FindObjects<RPawnVillain>()
                .Where(t => t is { IsValid: true, IsClassDefaultObject: false } && ThugUp(t) && Vector3.DistanceSquared(t.Location, from) < BlitzReach * BlitzReach)
                .ToList();
            while (left.Count > 0 && _blitzMarks.Count < BlitzMost)
            {
                RPawnVillain next = left.MinBy(t => Vector3.DistanceSquared(t.Location, from))!;
                left.Remove(next);
                _blitzMarks.Add(next);
                from = next.Location;
            }
        }

        if (_blitzMarks.Count == 0)
        {
            Readout.Toast("FLASH", "NO THUGS IN REACH", false);
            _charges = MathF.Min(ChargesMost, _charges + 1f);
            EndBlitz(pawn, pc, land: false);
            return;
        }

        _blitzAiming = false;
        _blitzOrigin = pawn.Location;
        _blitzOriginYaw = pawn.Rotation.Yaw;
        _blitzStarted = Clock.Now;
        _blitzNextAt = Clock.Now + 0.1;
        float world = Opt.BlitzSpeed.Number;
        Pace.Hold(BlitzPace, world);
        if (pawn.Mesh is { IsValid: true } mesh)
            mesh.GlobalAnimRateScale = _blitzAnimWas / world;

        _blitzCentre = _blitzMarks.Aggregate(_blitzOrigin, (sum, t) => sum + t.Location) / (_blitzMarks.Count + 1);
        _blitzSpread = _blitzMarks.Select(t => Vector3.Distance(t.Location, _blitzCentre)).Append(Vector3.Distance(_blitzOrigin, _blitzCentre)).Max();
        _orbitYaw = Hooks.PlayerCam() is { } cam ? cam.CameraCache.POV.Rotation.Yaw : pawn.Rotation.Yaw;
        // The game's camera runs on world time, so ours shows the strikes from above, blended from where it was.
        _camFrom = _camLast;
        _camBlendIn = CamBlendIn;
        Sound(pawn, ZipWhoosh);
        Rumble.Play(Buzz.Burst);
    }

    // The standing thug nearest the crosshair, in reach with a clear line, or null.
    private RPawnVillain? CrosshairThug(RPawnPlayer pawn)
    {
        if (Hooks.PlayerCam() is not { } cam)
            return null;
        Vector3 eye = cam.CameraCache.POV.Location;
        Geo.Axes(cam.CameraCache.POV.Rotation, out Vector3 fwd, out _, out _);
        RPawnVillain? best = null;
        float bestAngle = MarkSlack;
        foreach (RPawnVillain thug in Game.FindObjects<RPawnVillain>())
        {
            if (thug is not { IsValid: true, IsClassDefaultObject: false } || !ThugUp(thug)
                || Vector3.DistanceSquared(thug.Location, pawn.Location) > MarkReach * MarkReach)
                continue;
            Vector3 centre = Box(thug).Centre;
            Vector3 to = centre - eye;
            float dist = to.Length();
            if (dist < 1f)
                continue;
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(to / dist, fwd), -1f, 1f)) * Geo.Deg;
            if (angle >= bestAngle || Ground.Trace(pawn, eye, centre, Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out _, out _) is not null)
                continue;
            best = thug;
            bestAngle = angle;
        }

        return best;
    }

    // Back to where he stood, with a streak behind him.
    private void StartReturn(RPawnPlayer pawn)
    {
        _zipFrom = pawn.Location;
        _zipTo = _blitzOrigin;
        _zipTarget = null;
        _zipT = 0f;
        _blitzReturning = true;
        BlitzArc(_zipFrom, _zipTo + Vector3.UnitZ * 20f);
        Sound(pawn, ZipWhoosh);
    }

    // The view while he strikes: high over the middle of the fight, slowly circling it, looking down at it.
    private bool BlitzView(in Pov game, ref Pov pov, float step)
    {
        _orbitYaw += OrbitTurn * step;
        float back = 500f + _blitzSpread * 1.1f;
        float up = 450f + _blitzSpread * 0.7f;
        Vector3 centre = _blitzCentre + Vector3.UnitZ * 60f;
        Vector3 eye = centre - Geo.Facing(_orbitYaw) * back + Vector3.UnitZ * up;
        if (World.Player is { IsValid: true } player && Ground.Ray(player, centre, eye, out Vector3 wall, out Vector3 normal))
            eye = wall + normal * CamWallGap;

        Pov mine = new() { Location = eye, Rotation = Fx.Facing(centre - eye), FOV = game.FOV > 1f ? game.FOV : 70f };
        if (_camBlendIn > 0f)
        {
            _camBlendIn = MathF.Max(0f, _camBlendIn - step);
            mine = Blend(_camFrom.FOV > 1f ? _camFrom : game, mine, Ease.Smooth(1f - _camBlendIn / CamBlendIn));
        }

        pov = mine;
        _camLast = mine;
        return true;
    }

    // The next marked thug still worth a hit.
    private RPawnVillain? NextMark() =>
        _blitzMarks.FirstOrDefault(t => t is { IsValid: true, bDeleteMe: false } && !_blitzHits.Any(h => h.Ptr == t.Ptr)
            && !_blitzSkip.Any(h => h.Ptr == t.Ptr));

    private void StartZip(RPawnPlayer pawn, RPawnVillain thug)
    {
        Vector3 to = thug.Location - pawn.Location;
        to.Z = 0f;
        float dist = to.Length();
        Vector3 dir = dist > 1f ? to / dist : Geo.Facing(pawn.Rotation.Yaw);
        if (StandBy(pawn, thug, dir) is not { } spot)
        {
            // Nowhere clear to stand by him. He's left out, and the next one goes on.
            _blitzSkip.Add(thug);
            return;
        }

        _zipFrom = pawn.Location;
        _zipTo = spot;
        _zipTarget = thug;
        _zipT = 0f;
        var face = new Vector3(thug.Location.X - spot.X, thug.Location.Y - spot.Y, 0f);
        float yaw = face.LengthSquared() > 1f ? MathF.Atan2(face.Y, face.X) * Geo.Deg : MathF.Atan2(dir.Y, dir.X) * Geo.Deg;
        pawn.SetRotation(new Rotator(0f, yaw, 0f));
        // A streak of lightning left where he was, along the way he went. It stays until time snaps back.
        BlitzArc(_zipFrom, _zipTo + Vector3.UnitZ * 20f);
        Sound(pawn, ZipWhoosh);
    }

    // A spot by the thug to strike from, with floor under it and a clear line to him. Straight on first, then round
    // him. Null if there's nowhere.
    private Vector3? StandBy(RPawnPlayer pawn, RPawnVillain thug, Vector3 dir, float off = ZipStandOff)
    {
        float half = pawn.GetCollisionHeight();
        Vector3 chest = Box(thug).Centre;
        float approach = MathF.Atan2(dir.Y, dir.X) * Geo.Deg;
        foreach (float turn in new[] { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f })
        {
            Vector3 back = Geo.Facing(approach + turn);
            Vector3 flat = new Vector3(thug.Location.X, thug.Location.Y, 0f) - back * off;
            Vector3 top = new Vector3(flat.X, flat.Y, thug.Location.Z + 150f);
            if (!Ground.Ray(pawn, top, top - Vector3.UnitZ * 450f, out Vector3 floor, out Vector3 n) || n.Z < 0.7f)
                continue;
            var spot = new Vector3(flat.X, flat.Y, floor.Z + half + 2f);
            if (Ground.Ray(pawn, chest, spot, out _, out _))
                continue;
            return spot;
        }

        return null;
    }

    private void ZipStep(RPawnPlayer pawn, float dt)
    {
        // Placed along the way, not swept, so nothing on the way can stop him.
        _zipT = MathF.Min(1f, _zipT + dt / ZipSeconds);
        pawn.SetLocation(Vector3.Lerp(_zipFrom, _zipTo, Ease.Smooth(_zipT)));
        if (_zipT < 1f)
            return;

        _zipT = -1f;
        _blitzNextAt = Clock.Now + StrikePause;
        if (_blitzReturning)
        {
            _blitzReturning = false;
            _blitzReturned = true;
            pawn.SetRotation(new Rotator(0f, _blitzOriginYaw, 0f));
            _blitzNextAt = Clock.Now + SnapPause;
            return;
        }

        if (_zipTarget is not { IsValid: true } thug)
            return;

        _blitzHits.Add(thug);
        _impact.At(pawn, Box(thug).Centre, Fx.Facing(Geo.Facing(pawn.Rotation.Yaw + 180f)));
        HitSound(pawn, thug, finishing: false, heavy: false);
        Sound(pawn, StrikeWhoosh);
        Rumble.Play(Buzz.Hit);
        Safe.Run("blitz strike", () => BlitzStrike(pawn));

    }

    // One of Batman's own blows, a different one each stop, blended in fast like the lightning hook.
    private void BlitzStrike(RPawnPlayer pawn)
    {
        List<Blow> blows = Blows(pawn);
        if (blows.Count == 0)
            return;
        List<Blow> fresh = blows.Where(b => b != _blitzLastBlow).ToList();
        Blow blow = (fresh.Count > 0 ? fresh : blows)[Random.Shared.Next(fresh.Count > 0 ? fresh.Count : blows.Count)];
        _blitzLastBlow = blow;
        bool mirrored = blow.Hand.Length == 0 ? Random.Shared.Next(2) == 0 : false;
        if (pawn.Anim is { IsValid: true } anim)
            anim.ForceAnimatedAutomaticTransitionsOff = false;
        var transition = new RPawnCharacter.FCustomTransitionConfig
        {
            Config = new RAnimConfig.FCustomAnimConfig { FullBodyAnim = blow.Seq.SequenceName, CustomFullBodyAnim = blow.Seq },
        };
        pawn.ForceChangePose(ThrowStance, StandWeapon, default, mirrored, transition, default,
            mirrored ? RAnimUtil.EMirrorChoice.MC_Yes : default, HookBlend);
    }

    private void EndBlitz(RPawnPlayer? pawn, PlayerController? pc, bool land)
    {
        if (!_blitzing)
            return;

        _blitzing = false;
        BlitzArcsOff();
        _blitzAiming = false;
        _blitzReturning = false;
        _blitzReturned = false;
        _blitzAim = null;
        _zipT = -1f;
        Pace.Drop(BlitzPace);
        GameKeys.Release(BlitzHold);
        NoMove(pc, false);
        if (!_flying && !_running)
            Safe.Run("camera end", EndView);
        if (pawn?.Mesh is { IsValid: true } mesh)
            mesh.GlobalAnimRateScale = _blitzAnimWas;
        Shared.Set(BlitzKey, null);
        if (pawn != null)
            Safe.Run("blitz pose off", () => Ground.Settle(pawn));
        _blitzSkip.Clear();
        if (!land || pawn == null || pc == null)
        {
            _blitzHits.Clear();
            _blitzMarks.Clear();
            return;
        }

        // Every hit lands at once as time snaps back.
        foreach (RPawnVillain thug in _blitzHits)
        {
            if (!thug.IsValid || thug.bDeleteMe)
                continue;

            Vector3 away = thug.Location - pawn.Location;
            away.Z = 0f;
            Vector3 dir = away.LengthSquared() > 1f ? Vector3.Normalize(away) : Geo.Facing(pawn.Rotation.Yaw);
            Safe.Run("blitz hit", () =>
            {
                RamThug(thug, pawn, pc, dir, WaveForce * Opt.FlashPunchForce.Number, 1f, thug.Location);
                QueueThrow(thug, (dir + Vector3.UnitZ * PunchUp) * (StrikeThrow * Opt.FlashPunchForce.Number));
            });
            CrackleOn(thug, CrackleSeconds);
            int i = _blitzHits.IndexOf(thug) % _bursts.Length;
            _bursts[i].At(pawn, thug.Location - Vector3.UnitZ * thug.GetCollisionHeight(), default);
        }

        if (_blitzHits.Count > 0)
        {
            Sound(pawn, BlitzOut);
            Sound(pawn, FieldOut);
            Sound(pawn, Fx.WaveSound);
            Rumble.Play(Buzz.Heavy);
        }

        _blitzHits.Clear();
        _blitzMarks.Clear();
    }

    // Scripts reloaded mid blitz. His anim speed goes back.
    private static void RecoverBlitz()
    {
        if (Shared.Get(BlitzKey) is not float was)
            return;
        Shared.Set(BlitzKey, null);
        if (World.Player?.Mesh is { IsValid: true } mesh)
            mesh.GlobalAnimRateScale = was;
    }

    // One press: he runs a tight circle round where he stood for a few seconds, lifting what's near into the
    // funnel, then flings it all out and skids to a stop.
    private void StartTornado(RPawnPlayer pawn, RPlayerController pc)
    {
        if (!TornadoReady || _flying)
            return;

        // The circle goes off to his left if there's room, else his right.
        float yaw = _running ? _runYaw : pawn.Rotation.Yaw;
        float side = RingClear(pawn, yaw, 1f) ? 1f : RingClear(pawn, yaw, -1f) ? -1f : 0f;
        if (side == 0f)
        {
            Toast("NO ROOM FOR A TORNADO");
            return;
        }

        if (!_running)
        {
            if (pawn.Physics != Actor.EPhysics.PHYS_Walking)
                return;
            StartRun(pawn, pc, Geo.Facing(pawn.Rotation.Yaw) * RunStartSpeed);
            if (!_running)
                return;
        }

        _tornado = true;
        _tornadoEnds = Clock.Now + TornadoSeconds;
        _tornadoSide = side;
        _eye = pawn.Location + Geo.Facing(_runYaw + 90f * _tornadoSide) * TornadoRadius;
        _tornadoAngle = _runYaw - 90f * _tornadoSide;
        _tornadoZ = pawn.Location.Z;
        _padRun = false;
        Sound(pawn, FieldIn);
        Rumble.Play(Buzz.Burst);
    }

    // The run's heading and speed while the tornado is on. False when it isn't.
    private bool TornadoStep(RPawnPlayer pawn)
    {
        if (!_tornado)
            return false;
        if (Clock.Now >= _tornadoEnds)
        {
            EndTornado(pawn);
            return false;
        }

        _runHeading = Geo.Facing(_tornadoAngle + 90f * _tornadoSide);
        _runSpeed = TornadoSpeed;
        if (_skidding)
            EndSkid(pawn);
        Swirl(pawn);
        return true;
    }

    private void Swirl(RPawnPlayer pawn)
    {
        // The airship wind it used had no stop, so it's whooshes now.
        if (_tornadoWhoosh.Elapsed(0.6))
            Sound(pawn, SkidWhoosh);
        if (_tornadoPuff.Elapsed(0.1) && Opt.FlightFx.On)
        {
            float a = Random.Shared.NextSingle() * 360f;
            Vector3 at = _eye + Geo.Facing(a) * TornadoRadius * 0.9f
                + Vector3.UnitZ * (Random.Shared.Next(0, 160) - pawn.GetCollisionHeight());
            _ringDust[_ringDustNext++ % _ringDust.Length].At(pawn, at, Fx.Facing(Geo.Facing(a + 90f * _tornadoSide)));
        }

        if (!_tornadoPull.Elapsed(0.2) || World.Pc is not { } pc)
            return;

        ScanTargets(pawn);
        float rise = (float)(1.0 - (_tornadoEnds - Clock.Now) / TornadoSeconds);
        int lifting = 0;
        foreach ((Actor thing, Kind kind) in _near)
        {
            Vector3 off = Box(thing).Centre - _eye;
            off.Z = 0f;
            float dist = off.Length();
            if (dist > TornadoReach || dist < 1f || !Usable(thing, kind) || kind == Kind.Prop)
                continue;

            Vector3 around = new Vector3(-off.Y, off.X, 0f) / dist * _tornadoSide;
            Vector3 lift = around * 650f - off / dist * 420f + Vector3.UnitZ * (300f + 400f * rise);
            switch (thing)
            {
                case RPawnVillain thug when ThugUp(thug) || thug.IsRagdoll():
                    if (ThugUp(thug))
                        RamThug(thug, pawn, pc, around, 250f, 0.2f, thug.Location);
                    QueueThrow(thug, lift);
                    lifting++;
                    break;
                case RVehicle or RAbandonedVehicle or RDeadVehicle or KActor:
                    Wake(thing);
                    Kick(Part(thing), lift * 0.6f, 0.3f);
                    lifting++;
                    break;
            }
        }

        _tornadoLifting = lifting;
    }

    private void EndTornado(RPawnPlayer? pawn)
    {
        if (!_tornado)
            return;

        _tornado = false;
        _tornadoReady = Clock.Now + TornadoCooldown;
        if (pawn == null || World.Pc is not { } pc)
            return;

        Fx.StopAllLoops(pawn, TornadoWind);
        Sound(pawn, FieldOut);
        _bursts[0].At(pawn, new Vector3(_eye.X, _eye.Y, _tornadoZ - pawn.GetCollisionHeight()), default);
        Shockwave(pawn, pc, null, _eye + Vector3.UnitZ * 60f, 0.5f * Opt.LightningForce.Number, TornadoReach);
        Rumble.Play(Buzz.Heavy);
        // He comes out of it skidding.
        var away = new Vector3(pawn.Location.X - _eye.X, pawn.Location.Y - _eye.Y, 0f);
        if (away.LengthSquared() > 1f)
            _runHeading = Vector3.Normalize(away);
        _runSpeed = RunTop * 0.6f;
    }

    // Puts him on the circle for the frame. Set rather than swept, so a thug or a kerb can't stall him.
    private void TornadoPlace(RPawnPlayer pawn, float dt)
    {
        _tornadoAngle += TornadoSpeed / TornadoRadius * Geo.Deg * dt * _tornadoSide;
        Vector3 flat = _eye + Geo.Facing(_tornadoAngle) * TornadoRadius;
        pawn.SetLocation(new Vector3(flat.X, flat.Y, _tornadoZ));
        _runAt = pawn.Location;
        _runHeading = Geo.Facing(_tornadoAngle + 90f * _tornadoSide);
        _stuckFor = 0f;
    }

    // Nothing of the level across the circle and floor all the way round at about his height.
    private bool RingClear(RPawnPlayer pawn, float yaw, float side)
    {
        Vector3 at = pawn.Location;
        float half = pawn.GetCollisionHeight();
        Vector3 eye = at + Geo.Facing(yaw + 90f * side) * TornadoRadius;
        float start = yaw - 90f * side;
        Vector3 last = at;
        for (int i = 1; i <= RingChecks; i++)
        {
            Vector3 flat = eye + Geo.Facing(start + 360f / RingChecks * i * side) * TornadoRadius;
            var next = new Vector3(flat.X, flat.Y, at.Z);
            if (Ground.Ray(pawn, last, next, out _, out _))
                return false;
            if (!Ground.Ray(pawn, next, next - Vector3.UnitZ * (half + RingStepMost), out Vector3 floor, out _)
                || MathF.Abs(floor.Z - (at.Z - half)) > RingStepMost)
                return false;
            last = next;
        }

        return true;
    }

    // Trails on his hands and feet while he's moving fast, and lightning crackling over him in pulses near top speed.
    private void FlashRunFx(RPawnPlayer pawn)
    {
        if (!Heroes.Is(Hero.Flash))
            return;

        bool trails = !_skidding && (_runSpeed > RunTop * 0.3f || _tornado);
        foreach (WornFx trail in _trails)
            trail.Set(pawn, trails);

        if (!_skidding && _runSpeed > RunTop * 0.75f && _runCrackle.Elapsed(0.8))
        {
            foreach (WornFx arc in _bodyArcs)
                arc.Pulse(pawn);
            _crackleOff = Clock.Now + 0.35;
        }
        else if (_bodyArcs[0].On && Clock.Now >= _crackleOff)
        {
            foreach (WornFx arc in _bodyArcs)
                arc.Set(pawn, false);
        }

        RunZap(pawn);
        if (Opt.FlightSounds.On)
            PassBys(pawn);
    }

    private void RunZap(RPawnPlayer pawn)
    {
        if (!Opt.RunLightning.On || _skidding || _tornado || _runSpeed < RunTop * RunZapFrom || World.Pc is not { } pc
            || !_runZap.Elapsed(RunZapEvery))
            return;

        ScanTargets(pawn);
        Vector3 chest = pawn.Location + Vector3.UnitZ * 30f;
        (Actor Thing, Kind Kind, Vector3 Centre)? best = null;
        float bestDist = RunZapReach;
        foreach ((Actor thing, Kind kind) in _near)
        {
            if (!Usable(thing, kind) || (_rammed.TryGetValue(thing.Ptr, out double hitAt) && Clock.Now - hitAt < RunZapAgain))
                continue;
            (Vector3 centre, Vector3 half) = Box(thing);
            float dist = Vector3.Distance(centre, chest);
            if (dist >= bestDist || !InSight(pawn, chest, new Mark(thing, kind, centre, half, dist)))
                continue;
            best = (thing, kind, centre);
            bestDist = dist;
        }

        if (best is not { } hit)
            return;

        // Knocked off to the side of his path and a little ahead, so it clears the way rather than landing in it.
        var to = new Vector3(hit.Centre.X - pawn.Location.X, hit.Centre.Y - pawn.Location.Y, 0f);
        Vector3 side = to - _runHeading * Vector3.Dot(to, _runHeading);
        Vector3 dir = side.LengthSquared() > 100f ? Vector3.Normalize(Vector3.Normalize(side) + _runHeading * 0.6f) : _runHeading;
        Vector3 from = pawn.Mesh is { IsValid: true } mesh
            ? mesh.GetBoneLocation(RunZapBones[Random.Shared.Next(RunZapBones.Length)], 0)
            : chest;

        Bolt(from, hit.Centre, Math.Min(RunZapArc / MathF.Max(Pace.Speed, 0.1f), RunZapArcMost));
        _impact.At(pawn, hit.Centre, Fx.Facing(-dir));
        if (hit.Thing is RPawnVillain thug)
            CrackleOn(thug, CrackleSeconds);
        Sound(hit.Thing, Zap);
        Rumble.Play(Buzz.Hit);
        float force = Opt.LightningForce.Number;
        Safe.Run("run zap", () => Blast(pawn, pc, hit.Thing, hit.Kind, dir, WaveForce * RunZapHard * force, RunZapHard,
            StrikeThrow * RunZapHard * force, PunchUp));
    }

    // A whoosh for each car or thug he passes close, and for a wall that comes up beside him.
    private void PassBys(RPawnPlayer pawn)
    {
        if (_skidding || _runSpeed < RunTop * 0.5f)
        {
            _passAhead.Clear();
            return;
        }

        Vector3 at = pawn.Location;
        if (_passScan.Elapsed(0.25))
        {
            _passNear.Clear();
            float reach = PassScanReach * PassScanReach;
            foreach (RPawnVillain thug in Game.FindObjects<RPawnVillain>())
            {
                if (thug is { IsValid: true, IsClassDefaultObject: false, bHidden: false } && Vector3.DistanceSquared(thug.Location, at) < reach)
                    _passNear.Add(thug);
            }

            foreach (SkeletalMeshActor car in Game.FindObjects<SkeletalMeshActor>())
            {
                if (car is RAbandonedVehicle or RDeadVehicle && car is { IsValid: true, IsClassDefaultObject: false }
                    && Vector3.DistanceSquared(car.Location, at) < reach)
                    _passNear.Add(car);
            }

            foreach (RVehicle car in Game.FindObjects<RVehicle>())
            {
                if (car is { IsValid: true, IsClassDefaultObject: false } and not RVehicleBatmobileBase && Vector3.DistanceSquared(car.Location, at) < reach)
                    _passNear.Add(car);
            }
        }

        foreach (Actor thing in _passNear)
        {
            if (!thing.IsValid || thing.bDeleteMe)
                continue;
            Vector3 rel = thing.Location - at;
            rel.Z = 0f;
            float ahead = Vector3.Dot(rel, _runHeading);
            float side = (rel - _runHeading * ahead).Length();
            if (_passAhead.TryGetValue(thing.Ptr, out float was) && was > 0f && ahead <= 0f && side < PassSide
                && (!_passedAt.TryGetValue(thing.Ptr, out double last) || Clock.Now - last > 1.0))
            {
                _passedAt[thing.Ptr] = Clock.Now;
                Sound(thing, thing is RPawnVillain ? PassThug : PassCar);
            }

            _passAhead[thing.Ptr] = ahead;
        }

        if (!_passWall.Elapsed(0.1))
            return;
        Vector3 chest = at + Vector3.UnitZ * 20f;
        Vector3 across = new(-_runHeading.Y, _runHeading.X, 0f);
        bool beside = WallAt(pawn, chest, across) || WallAt(pawn, chest, -across);
        if (beside && !_wallBeside && Clock.Now - _passWallAt > 0.6)
        {
            _passWallAt = Clock.Now;
            Sound(pawn, PassWall);
        }

        _wallBeside = beside;
    }

    private static bool WallAt(RPawnPlayer pawn, Vector3 from, Vector3 dir) =>
        Ground.Ray(pawn, from, from + dir * (pawn.GetCollisionRadius() + WallBeside), out _, out Vector3 n) && MathF.Abs(n.Z) < 0.4f;

    // The game's own move for a ledge or a drop while running. Its name is the only way to tell it apart.
    private const string TraversalMove = "RSpecialMoveInstance_RunningRelativeAnimMove";
    private const float TraverseRate = 2f;
    private const string TraverseWhoosh = "Ww_CHR_Combat.Play_TD_Whoosh02";
    private bool _traversing;
    // His speed just before he met the ledge, which the blocked frame has already eaten into.
    private float _speedHeld;

    private static bool InTraversal(RPawnPlayer pawn) =>
        pawn.CurrentSpecialMove is { IsValid: true } move && move.Class?.Name.ToString() == TraversalMove;

    // One frame of the game's climb or drop. Sped up, with his trails on, and the run kept waiting.
    private void Traverse(RPawnPlayer pawn, float dt)
    {
        if (!_traversing)
        {
            _traversing = true;
            Sound(pawn, TraverseWhoosh);
            Rumble.Play(Buzz.TakeOff);
        }

        _runAt = pawn.Location;
        if (pawn.Mesh is { IsValid: true } mesh)
            mesh.GlobalAnimRateScale = _animRateWas * TraverseRate / MathF.Max(Pace.Speed, 0.05f);
        FlashRunFx(pawn);
    }

    private void EndTraverse(RPawnPlayer pawn)
    {
        _traversing = false;
        _runAt = pawn.Location;
        _runSpeed = MathF.Max(_runSpeed, _speedHeld * 0.9f);
        if (pawn.Physics != Actor.EPhysics.PHYS_Walking && Ground.Below(pawn, 60f) < 50f)
            pawn.SetPhysics(Actor.EPhysics.PHYS_Walking, false);
        FlashPuff(Fx.Dust, Feet(pawn));
        Rumble.Play(Buzz.Land);
    }

    private void FlashRunFxOff()
    {
        RPawnPlayer? pawn = World.Player as RPawnPlayer;
        foreach (WornFx worn in _trails.Concat(_bodyArcs))
            worn.Set(pawn, false);
    }

    // Takes his effects off him for good, when the world changes or the scripts unload.
    private void DropWorn()
    {
        foreach (WornFx worn in _trails.Concat(_bodyArcs).Concat(_thugArcs).Append(_handCharge))
            worn.Drop();
        foreach (PlacedFx placed in _beams.Concat(_runBeams).Concat(_blitzBeams).Append(_impact).Concat(_bursts).Concat(_ringDust))
            placed.Drop();
        _arcs.Clear();
        _crackles.Clear();
        GoldCrackle.Restore();
    }

    // Scripts reloaded. The last load's effects are still on his bones, and the crackle may still be gold.
    private void RecoverWorn()
    {
        GoldCrackle.Recover();
        HookDone(World.Player as RPawnPlayer);
        RecoverSlow();
        // Scripts reloaded mid flurry. His anim speed goes back.
        if (Shared.Get(FlurryKey) is float flurryRate)
        {
            Shared.Set(FlurryKey, null);
            if (World.Player?.Mesh is { IsValid: true } flurried)
                flurried.GlobalAnimRateScale = flurryRate;
        }

        if (World.Player is { IsValid: true } player)
        {
            PlacedFx.Sweep(player, _impact.Template);
            PlacedFx.Sweep(player, _beams[0].Template);
            PlacedFx.Sweep(player, _bursts[0].Template);
        }

        if (World.Player?.Mesh is not SkeletalMeshComponent { IsValid: true } mesh)
            return;
        foreach (WornFx worn in _trails.Concat(_bodyArcs).Append(_handCharge))
            WornFx.Sweep(mesh, worn.Template, null);
    }

    // A one-off effect, spawned only while the game's pool has a free slot, so it never pushes out another.
    private static void FlashPuff(string path, Vector3 at, Vector3 facing = default)
    {
        if (Opt.FlightFx.On && Fx.Room(1))
            Safe.Run("flash effect", () => Fx.Spawn(path, at, facing));
    }

    // Past the speed vision mark: a ring at his feet, a boom and a kick of the view.
    private void BoomFeel(RPawnPlayer pawn)
    {
        FlashPuff(Fx.Dust, Feet(pawn));
        Sound(pawn, Fx.WaveSound);
        Rumble.Play(Buzz.Burst);
        _fovKick = MathF.Max(_fovKick, 14f);
        _camKick = MathF.Max(_camKick, 0.8f);
    }

    private void OwnCamOff()
    {
        if (!_ownCam)
            return;
        _ownCam = false;
        Safe.Run("camera end", EndView);
    }

    private void ResetFlash()
    {
        EndCharge(World.Player);
        _boltFireAt = -1.0;
        _throwSettleAt = -1.0;
        HookDone(World.Player);
        EndFlurry(World.Player, World.Pc, launch: false);
        EndSlow(World.Player, World.Pc, release: false);
        ForgetPath();
        OwnCamOff();
        if (_blitzing)
            EndBlitz(World.Player, World.Pc, land: false);
        EndTornado(null);
        FlashRunFxOff();
        GoldCrackle.Restore();
    }
}
