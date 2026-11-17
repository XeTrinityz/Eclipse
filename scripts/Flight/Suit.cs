using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

internal sealed partial class Suit
{
    public static readonly Suit I = new();

    private const string Title = "ECLIPSE";
    private const string FlyingKey = "eclipse.flying";
    private const string NoMoveKey = "eclipse.nomove";

    private const float Metre = 100f;
    private const float MovedAway = 2500f;
    private const float GroundReach = 30000f;

    // Leaving flight this high and this fast goes into a glide. Slower or lower he just falls.
    private const float GlideFrom = 600f;
    private const float GlideSpeed = 600f;
    private const double ForceGlideSeconds = 1.5;

    // How far and how fast the body leans along the flight path in the fast pose.
    private const float TiltMost = 70f;
    private const float TiltSlam = 85f;
    private const float TiltRate = 5f;

    private readonly FlightModel _fm = new();
    private RPawnPlayer? _pawn;
    private bool _flying;
    private double _stepAt;
    private Vector3 _lastAt;
    private float _faceYaw, _tilt;
    private float _ground = GroundReach;
    private Every _groundEvery;
    private Every _reassert;

    private bool _forceGlideWas;
    // RB takes off and boosts. The press that took off doesn't count as boost until it's let go.
    private bool _boostWait;
    private const int BadFramesMost = 20;
    private int _badFrames;

    public bool Flying => _flying;

    // For the readout. Speed, the bar mark in m/s, and height off the ground or -1.
    public float GaugeSpeed => _flying ? _fm.Velocity.Length() : _running ? _runSpeed : 0f;

    public float GaugeMark => _running ? Opt.RunSpeed.Number : Opt.BoostSpeed.Number;

    public float AirHeight => _flying && _ground < GroundReach ? _ground : -1f;

    // What he's doing in the air, for the HUD.
    public string FlightWord => _slamming ? "SLAM" : _fm.Boosting ? "BOOST" : "HOVER";

    // How far into speed vision the run is, 0 to 1.
    public float VisionNow => _running ? _runVision : 0f;

    // For the control legend: what the flight buttons do right now.
    public bool FastPose => _flying && _autoFast == true;

    public bool SlamReady => _flying && CanSlam;

    public bool SonicReady => _flying && CanSonic;

    public bool Firing => _firing;

    private static bool Listening => !Shared.AnyMenuOpen && Native.GameInFront() && !World.Paused;

    private RPawnPlayer? Held() =>
        _pawn is { IsValid: true, bDeleteMe: false } p && World.Player is { } now && now.Ptr == p.Ptr ? p : null;

    public void Toggle()
    {
        if (_flying)
        {
            Safe.Run("flight end", () => End("FLIGHT OFF", fall: true));
            return;
        }

        if (!Heroes.Is(Hero.Superman))
        {
            Readout.Toast(Title, Opt.Powers.On ? "ONLY SUPERMAN FLIES" : "POWERS ARE OFF", false);
            return;
        }

        try
        {
            Begin();
        }
        catch (Exception ex)
        {
            Log.Info("Flight refused: " + ex.Message);
            Readout.Notice(Title, ex.Message.ToUpperInvariant(), false);
        }
    }

    public void PowersOff()
    {
        if (_flying)
            Safe.Run("flight end", () => End("POWERS OFF", fall: true));
        Safe.Run("run end", () => EndRun(RunEnd.Stop));
        Safe.Run("flash off", ResetFlash);
    }

    private static RPawnPlayer Require()
    {
        if (!World.Ready)
            throw new InvalidOperationException("the game is still loading");
        if (World.Paused)
            throw new InvalidOperationException("not while paused");

        PlayerController pc = World.Pc ?? throw new InvalidOperationException("the game is still loading");
        if (World.Pawn is RVehicle)
            throw new InvalidOperationException("get out of the Batmobile first");
        if (World.Pawn is not RPawnPlayer pawn)
            throw new InvalidOperationException("the game is still loading");
        if (Hooks.InCinematic(pc))
            throw new InvalidOperationException("not during a cutscene");
        if (pawn.Health <= 0)
            throw new InvalidOperationException("not while you're down");
        if (pawn.bHidden)
            throw new InvalidOperationException("Batman is hidden, maybe in the Batwing");

        return pawn;
    }

    private void Begin()
    {
        RPawnPlayer pawn = Require();
        RPlayerController pc = World.Pc!;
        // Out of a super run, he takes off at the speed he was running.
        EndRun(RunEnd.Carry);
        Safe.Run("radio off", EndRadio);
        Safe.Run("facing off", EndFootFace);

        StopForcedGlide(pc);
        _pawn = pawn;
        _badFrames = 0;
        _forceGlideWas = pc.bForceGlide;
        Shared.Set(FlyingKey, true);

        Safe.Run("idle anim scan", () => ScanIdleAnims(pawn));

        ApplySpeeds();
        Vector3 v = pawn.Velocity;
        if (v.Length() > _fm.BoostSpeed)
            v = Vector3.Normalize(v) * _fm.BoostSpeed;
        _faceYaw = pawn.Rotation.Yaw;
        _fm.Reset(v, _faceYaw);
        _tilt = 0f;
        ResetDrift();
        _poseComp = 0f;
        _swapT = -1f;
        _animBefore = _animAfter = 0f;
        _lastAt = pawn.Location;
        _ground = Ground.Below(pawn, GroundReach);
        _flying = true;
        _boostWait = true;
        ResetPoses();

        HoldFlightKeys();
        Controls.HoldsChanged();

        // Keys held as flight starts never send their release, so clear them. Not otherwise, a run needs them.
        if (!_camHandOff && GameKeys.ButtonKeys.Any(Keyboard.IsDown))
        {
            Safe.Run("input flush", () =>
            {
                pc.FlushPressedKeys();
                pc.ResetPlayerMovementInput();
                if (pc.PlayerInput is { IsValid: true } input)
                    input.ResetInput();
            });
        }

        NoMove(pc, true);

        StartCarry(pawn, pc);
        StartIdleHold(pawn);
        // Straight out of a super run the camera carries on as it was.
        if (!_camHandOff)
            StartView();
        ResetMoves();
        ResetRam();
        ResetTargets();
        ResetCombat();
        ResetCape();
        ResetSlam();
        TakeOff(pawn);

        Toast("FLIGHT ON");
    }

    // Space, Ctrl, Shift and the punch and grab keys are kept from the game while he flies.
    private static void HoldFlightKeys() =>
        GameKeys.Hold("flight", GameKeys.ButtonKeys.Concat(new[] { Controls.Key(Act.Punch), Controls.Key(Act.Grab), Controls.Key(Act.Dash), Controls.Key(Act.Roll) }).Where(k => k != Keys.None).ToArray());

    // The game's movement off while he flies. It counts the asks, so each is given back once.
    private bool _noMove;

    private void NoMove(PlayerController? pc, bool on)
    {
        if (on == _noMove || pc is not { IsValid: true })
            return;

        _noMove = on;
        Shared.Set(NoMoveKey, on);
        pc.IgnoreMoveInput(on);
    }

    // A key was rebound in the menu, maybe mid flight.
    public void KeysChanged()
    {
        if (_flying)
            HoldFlightKeys();
    }

    private void ApplySpeeds()
    {
        _fm.MoveSpeed = Opt.MoveSpeed.Number * Metre;
        _fm.BoostSpeed = Opt.BoostSpeed.Number * Metre;
        _fm.ClimbSpeed = Opt.ClimbSpeed.Number * Metre;
        _fm.CoastSeconds = Opt.Coast.Number;
    }

    public void CameraStep(float dt)
    {
        _stepAt = Clock.Now;
        Frame(Math.Clamp(dt, 0f, 0.1f));
    }

    // Runs after the game moves him. Flight or super run first, then the beams from his eyes.
    private void Frame(float dt)
    {
        if (_flying)
        {
            // One bad frame is logged and flown through. Only a run of them ends the flight.
            try
            {
                Step(dt);
                _badFrames = 0;
            }
            catch (Exception ex)
            {
                if (++_badFrames >= BadFramesMost)
                    Fail(ex);
                else
                    Log.Error("flight step", "A flight frame", ex);
            }
        }
        else if (_running)
        {
            try
            {
                RunStep();
            }
            catch (Exception ex)
            {
                Log.Error("run step", "Super run", ex);
                Safe.Run("run end", () => EndRun(RunEnd.Stop));
            }
        }
        else
        {
            if (_firing || _footFace || _bolt >= 0f)
                Safe.Run("foot facing", () => FootFaceStep(dt));
            Safe.Run("run watch", RunWatch);
        }

        if (_firing)
            Safe.Run("heat vision", BeamStep);
    }

    public void Tick(float dt)
    {
        Safe.Run("watch", Watch);
        Safe.Run("invincible", GodStep);
        Safe.Run("game speed", Pace.Step);
        Safe.Run("powers", PowersStep);
        Safe.Run("flash", FlashStep);
        Safe.Run("throws", ThrowStep);
        Safe.Run("landings", FlungStep);
        Safe.Run("car hits", WallStep);
        Safe.Run("passing", PassStep);
        if (!_flying && !_running && !World.Paused && World.InGame && World.Pawn is RPawnPlayer { IsValid: true } walker)
            Safe.Run("foot ram", () => RamFoot(walker, Math.Clamp(dt, 0f, 0.1f)));

        // Normally driven from the camera update, or the game tick if that goes quiet.
        if (!World.Paused && Clock.Now - _stepAt >= 0.25)
            Frame(Math.Clamp(dt, 0f, 0.1f));
    }

    private void Step(float dt)
    {
        RPawnPlayer? pawn = Held();
        if (pawn == null)
        {
            End("BATMAN CHANGED", fall: false);
            return;
        }

        if (World.Pc is not { } pc)
            return;

        if (Hooks.InCinematic(pc))
        {
            End("CUTSCENE", fall: true);
            return;
        }

        if (pawn.Health <= 0)
        {
            End("BATMAN IS DOWN", fall: false);
            return;
        }

        if (pawn.bHidden)
        {
            End("BATMAN WAS HIDDEN", fall: false);
            return;
        }

        if (Vector3.DistanceSquared(pawn.Location, _lastAt) > MovedAway * MovedAway)
        {
            Log.Info("Game moved Batman from " + Geo.Position(_lastAt) + " to " + Geo.Position(pawn.Location) + ", letting go.");
            End("MOVED BY THE GAME", fall: false);
            return;
        }

        if (_reassert.Elapsed(0.5))
            GameKeys.Reassert();
        if (dt <= 0f)
            return;

        ApplySpeeds();
        FlightInput input = Gather(pc);
        Moves(pawn, input);
        _steer = input.Move.X;
        _fm.Step(dt, input);
        TargetStep(pawn);
        // A slam is all he does until he lands. Anything else can take over the flight's speed.
        if (_slamming)
            SlamStep(pawn, input, dt);
        else
            CombatStep(pawn, dt);
        Drive(pawn, pc, dt);
        if (_slamming)
            SlamCheck(pawn, pc);
        // The slam may have landed and ended the flight just now. Nothing after this may tilt him again.
        if (!_flying)
            return;
        OverlayStep(pawn);
        if (LandCheck(pawn, input))
            return;
        AutoPose(pawn);
        BoostStep(pawn);
        Face(pawn, input, dt);
        FeelStep(pawn);

        _lastAt = pawn.Location;

        if (_groundEvery.Elapsed(0.2))
            _ground = Ground.Below(pawn, GroundReach);

        HoldPose(pawn);
        HoldIdles(pawn);
        // Last, so what he carries sits where his hand ended up.
        HoldStep(pawn);
    }

    private FlightInput Gather(PlayerController pc)
    {
        Rotator view = Hooks.PlayerCam() is { } cam ? cam.CameraCache.POV.Rotation : pc.Rotation;
        var input = new FlightInput { Yaw = view.Yaw, Pitch = Geo.WrapDegrees(view.Pitch), Ground = _ground };
        if (!Listening)
            return input;

        Vector2 ls = Controls.LeftStick;
        input.Move = new Vector2(
            Math.Clamp(Axis(Keys.D, Keys.A) + ls.X, -1f, 1f),
            Math.Clamp(Axis(Keys.W, Keys.S) + ls.Y, -1f, 1f));
        input.Lift = Math.Clamp(Axis(Keys.Space, Keys.ControlKey) + Controls.RightTrigger - Controls.LeftTrigger, -1f, 1f);
        // A slam falls in the hover's way of flying, whatever is held.
        bool padBoost = Controls.Down(Act.Boost);
        if (!padBoost)
            _boostWait = false;
        input.Boost = !_slamming && (Keyboard.IsDown(Keys.ShiftKey) || (padBoost && !_boostWait));
        return input;
    }

    private static float Axis(Keys plus, Keys minus) => (Keyboard.IsDown(plus) ? 1f : 0f) - (Keyboard.IsDown(minus) ? 1f : 0f);

    // Faces the camera when hovering and his path in the fast pose, leaning along it.
    private void Face(RPawnPlayer pawn, in FlightInput input, float dt)
    {
        Vector3 v = _fm.Velocity;
        float flat = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
        bool fast = _autoFast == true;
        float want = input.Yaw;
        // A punch turns him square onto its target.
        bool punching = FaceTarget(pawn, out float targetYaw);
        if (punching)
            want = targetYaw;
        else if (flat > 300f)
        {
            // Turn onto his path before the dive pose swaps in, so the swap doesn't spin him.
            float onPath = fast ? 1f : _fm.Boosting ? Ease.Smooth((v.Length() - _fm.MoveSpeed * 0.5f) / (_fm.MoveSpeed * 0.75f)) : 0f;
            float pathYaw = MathF.Atan2(v.Y, v.X) * Geo.Deg;
            want = input.Yaw + Geo.WrapDegrees(pathYaw - input.Yaw) * onPath;
        }

        _faceYaw = Geo.WrapDegrees(_faceYaw + Geo.WrapDegrees(want - _faceYaw) * Ease.Follow(punching ? 20f : 8f, dt));

        float tiltWant = 0f;
        if (fast && v.LengthSquared() > 300f * 300f)
        {
            float most = _slamming ? TiltSlam : TiltMost;
            if (Opt.Tilt.On)
                tiltWant = Math.Clamp(MathF.Atan2(v.Z, flat) * Geo.Deg, -most, most);
        }

        _tilt += (tiltWant - _tilt) * Ease.Follow(TiltRate, dt);
        Vector2 drift = DriftStep(pawn, dt);
        CompStep(dt);
        bool gripping = Holding && _heldKind == Kind.Thug;
        float holdPitch = gripping ? Rig.GripPitch : 0f, holdRoll = gripping ? Rig.GripRoll : 0f;
        pawn.SetRotation(new Rotator(_tilt + drift.X + _poseComp + _fightPitch + holdPitch, _faceYaw, drift.Y + RollStep(dt) + holdRoll));
    }

    private void End(string why, bool fall)
    {
        if (!_flying)
            return;

        RPawnPlayer? pawn = Held();
        _flying = false;
        Shared.Set(FlyingKey, false);
        DropHeld();
        StopOverlay();
        ResetSlam();
        Safe.Run("fight end", EndFight);
        Safe.Run("flight sounds off", EndFeel);
        Safe.Run("boost off", EndBoost);
        if (!_camHandOff)
            Safe.Run("camera end", EndView);
        Safe.Run("flight keys", () => GameKeys.Release("flight"));
        Safe.Run("movement back", () => NoMove(World.Pc, false));
        Controls.HoldsChanged();

        if (pawn != null)
            Safe.Run("flight release", () => Release(pawn, fall));

        _pawn = null;
        _afterUntil = Clock.Now + AfterSeconds;
        Toast(why);
    }

    private void Release(RPawnPlayer pawn, bool fall)
    {
        RPlayerController? pc = World.Pc;
        ClearMeshLift(pawn);
        EndIdleHold(pawn);
        pawn.SetRotation(new Rotator(0f, _faceYaw, 0f));
        pawn.Acceleration = Vector3.Zero;

        Vector3 v = _fm.Velocity;
        if (pawn.Physics is Actor.EPhysics.PHYS_None or Actor.EPhysics.PHYS_Flying)
            pawn.SetPhysics(Actor.EPhysics.PHYS_Falling, false);
        if (pc != null && InFlyingState(pc))
            pc.GotoState(FallingState);

        if (!fall)
            return;

        pawn.Velocity = v;
        ClearPose(pawn);
        float ground = Ground.Below(pawn, GroundReach);
        if (ground < 60f)
        {
            pawn.Velocity = new Vector3(0f, 0f, MathF.Min(v.Z, 0f));
            Ground.Settle(pawn);
            return;
        }

        if (ground < GlideFrom || v.Length() < GlideSpeed || pc == null)
            return;

        // Force glide got the glide going every time in the first test, a moment after it went on.
        pc.bForceGlide = true;
        _forceGlideUntil = Clock.Now + ForceGlideSeconds;
    }

    private void Fail(Exception ex)
    {
        Log.Info("Flight stopped: " + Log.Why(ex));
        Safe.Run("flight stop", () => End("STOPPED: " + ex.Message.ToUpperInvariant(), fall: true));
    }

    public void WorldChanged()
    {
        ForgetGrip();
        End("WORLD CHANGED", fall: false);
        Safe.Run("run end", () => EndRun(RunEnd.Stop));
        Safe.Run("passing off", EndPassing);
        Safe.Run("powers off", ResetPowers);
        Safe.Run("flash off", ResetFlash);
        Safe.Run("worn effects off", DropWorn);
        Safe.Run("owned effects off", DropOwned);
        ResetTargets();
        Pace.Forget();
        _forceGlideUntil = -1.0;
    }

    public void Shutdown()
    {
        Safe.Run("radio off", EndRadio);
        Safe.Run("facing off", EndFootFace);
        Safe.Run("powers off", ResetPowers);
        Safe.Run("flash off", ResetFlash);
        Safe.Run("game speed back", Pace.Reset);
        Safe.Run("invincible off", GodOff);
        Safe.Run("passing off", EndPassing);
        RestoreStep(World.Pawn as RPawnPlayer);
        Safe.Run("run end", () => EndRun(RunEnd.Stop));
        Safe.Run("worn effects off", DropWorn);
        Safe.Run("owned effects off", DropOwned);
        End("SCRIPTS UNLOADED", fall: true);
        if (World.Pc is { } pc)
            StopForcedGlide(pc);
    }

    // Scripts reloaded or crashed mid flight. Put back what we changed on him.
    public void Recover()
    {
        Safe.Run("random idles recover", RecoverIdles);
        Safe.Run("legs rate recover", RecoverLegs);
        Safe.Run("step height recover", () => RestoreStep(World.Player as RPawnPlayer));
        Safe.Run("passing recover", RecoverPassing);
        Safe.Run("held thug recover", RecoverGrip);
        Safe.Run("radio recover", RecoverRadio);
        Safe.Run("facing recover", RecoverFacing);
        Safe.Run("wind recover", RecoverRush);
        Safe.Run("run recover", RecoverRun);
        Safe.Run("blitz recover", RecoverBlitz);
        Safe.Run("worn effects recover", RecoverWorn);
        Safe.Run("owned effects recover", RecoverOwned);
        Safe.Run("boost recover", RecoverBoost);
        if (Shared.GetValue(NoMoveKey, false))
        {
            Shared.Set(NoMoveKey, false);
            Safe.Run("movement recover", () => World.Pc?.IgnoreMoveInput(false));
        }
        if (!Shared.GetValue(FlyingKey, false))
            return;

        Shared.Set(FlyingKey, false);
        if (World.Player is not { IsValid: true } pawn)
            return;

        Log.Info("Scripts reloaded mid flight, putting Batman back.");
        if (pawn.Mesh is { IsValid: true } mesh)
            mesh.bPauseAnims = false;
        RecoverMeshLift(pawn);
        EndIdleHold(pawn);
        pawn.SetRotation(new Rotator(0f, pawn.Rotation.Yaw, 0f));
        if (pawn.Physics is Actor.EPhysics.PHYS_None or Actor.EPhysics.PHYS_Flying)
            pawn.SetPhysics(Actor.EPhysics.PHYS_Falling, false);
        if (World.Pc is { } pc && InFlyingState(pc))
            pc.GotoState(FallingState);
    }

    private void Toast(string text) => Readout.Toast(Title, text, _flying);
}
