using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Super run. The game runs him as normal and we place him each frame so it can't cap his speed.
internal sealed partial class Suit
{
    private const string RunKey = "eclipse.run";
    private const string RunPaceKey = "super run";

    private const float RunStartSpeed = 700f;
    private const float RunAccel = 1800f;
    // Turn rate in degrees a second, from a jog to top speed.
    private const float RunTurnSlow = 420f, RunTurnFast = 80f;
    // Pushing more than this far against the way he's going brakes him.
    private const float RunBrakeDot = -0.3f;
    private const float SkidFrom = 1000f;
    private const float SkidDecel = 3200f;
    private const float SkidEnd = 150f;
    private const float RunStepUp = 40f;
    private const float RunDropLeast = 40f;
    // Held under this speed by something in the way, the run gives up instead of running on the spot.
    private const float RunStuckBelow = 300f;
    private const float RunStuckSeconds = 0.25f;
    // His run cycle's own speed. Faster than that, the cycle is sped up to at most this many times.
    private const float RunAnimSpeed = 700f;
    private const float LegRateMost = 2.5f;
    // Speed vision comes on past this share of his top speed.
    private const float VisionFrom = 0.6f;
    private const float RunBankPerTurn = 0.12f, RunBankMost = 16f;
    private const float RunPoseBlend = 0.2f;
    private const float LaunchUp = 900f;
    private const float LaunchPitch = 20f;
    // Boosting this close to the ground with Ctrl held drops him into a run.
    private const float SkimHeight = 120f;

    private static readonly string[] RunStates = { "WalkingMode", "RunningMode" };
    private static readonly string[] SkidAnims = { "Slide_Floor_Stop" };

    private bool _running, _skidding, _jumpWas, _camHandOff;
    private RPawnPlayer? _runPawn;
    private Vector3 _runHeading = Vector3.UnitX, _runAt;
    // Where a run last gave up against something. It won't start again pushing the same way from there.
    private Vector3? _stuckSpot;
    private Vector3 _stuckDir;
    private float _runSpeed, _runYaw, _runBank, _runVision, _animRateWas = 1f, _stuckFor;
    private FrameTimer _runClock;
    private Every _runDust, _runBuzz;

    public bool Running => _running;

    // The game is told he's running for as long as he does, not while he skids.
    private bool RunForced => _running && !_skidding;

    private float RunTop => Opt.RunSpeed.Number * Metre;

    // On a pad the run stays on from the press until a skid, so only the stick has to be held.
    private bool _padRun;

    private bool RunHeld() => Keyboard.IsDown(Keys.ShiftKey) || _padRun;

    // The run button on a pad. A second press while he runs lets go, like a skid.
    public void PadRunPressed()
    {
        if (!CanRun)
            return;
        _padRun = !_padRun;
        if (_padRun && !_running)
            Readout.Toast("FLASH", "PUSH THE STICK TO RUN", true);
    }

    private static bool CanRun => Heroes.Is(Hero.Flash) && Opt.SuperRun.On;

    private static Vector2 RunInput()
    {
        Vector2 ls = Controls.LeftStick;
        return new Vector2(
            Math.Clamp(Axis(Keys.D, Keys.A) + ls.X, -1f, 1f),
            Math.Clamp(Axis(Keys.W, Keys.S) + ls.Y, -1f, 1f));
    }

    private static RPawnPlayer? RunPawn()
    {
        if (!CanRun || !World.Ready || World.Paused || World.Pc is not { } pc || Hooks.InCinematic(pc))
            return null;
        return World.Pawn is RPawnPlayer { Health: > 0, bHidden: false, bDeleteMe: false } pawn && pawn.DrivenVehicle is not { IsValid: true } ? pawn : null;
    }

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.IsRunning))]
    private static bool PcIsRunningRedirect(RPlayerController self) => (I.RunForced && IsYours(self)) || self.IsRunning();

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.IsActuallyRunning))]
    private static bool PcIsActuallyRunningRedirect(RPlayerController self) => (I.RunForced && IsYours(self)) || self.IsActuallyRunning();

    [Redirect(typeof(RPawnPlayer), nameof(RPawnPlayer.IsRunning))]
    private static bool PawnIsRunningRedirect(RPawnPlayer self) => (I.RunForced && IsBatman(self)) || self.IsRunning();

    // On foot each frame. Starts the run when Shift is held and he's moving.
    private void RunWatch()
    {
        // A held run key would start the run again under the flurry, which then stops it as stuck.
        if (!Listening || _flurry || _slowing || _rewinding || !RunHeld() || RunInput().LengthSquared() < 0.04f || RunPawn() is not { } pawn || World.Pc is not { } pc)
            return;
        if (pawn.Physics != Actor.EPhysics.PHYS_Walking || pawn.CurrentSpecialMove is { IsValid: true } || InFight(pc)
            || !RunStates.Contains(pc.GetStateName().ToString()))
            return;
        if (StillStuck(pawn))
            return;

        var v = new Vector3(pawn.Velocity.X, pawn.Velocity.Y, 0f);
        StartRun(pawn, pc, v.LengthSquared() > 1f ? v : Geo.Facing(pawn.Rotation.Yaw) * RunStartSpeed);
    }

    private bool StillStuck(RPawnPlayer pawn)
    {
        if (_stuckSpot is not { } spot)
            return false;
        Vector3 wish = WishDir(pawn);
        if (Vector3.DistanceSquared(pawn.Location, spot) < 150f * 150f && Vector3.Dot(wish, _stuckDir) > 0.3f)
            return true;
        _stuckSpot = null;
        return false;
    }

    // The way the keys or the stick push, flat, from the camera. Zero when nothing is pushed.
    private static Vector3 WishDir(RPawnPlayer pawn)
    {
        Vector2 move = RunInput();
        float yaw = Hooks.PlayerCam() is { } cam ? cam.CameraCache.POV.Rotation.Yaw : pawn.Rotation.Yaw;
        Vector3 wish = Geo.Facing(yaw) * move.Y + Geo.Facing(yaw + 90f) * move.X;
        return wish.LengthSquared() > 0.04f ? Vector3.Normalize(wish) : Vector3.Zero;
    }

    private void StartRun(RPawnPlayer pawn, RPlayerController pc, Vector3 velocity)
    {
        Safe.Run("radio off", EndRadio);
        Safe.Run("facing off", EndFootFace);

        var flat = new Vector3(velocity.X, velocity.Y, 0f);
        float speed = flat.Length();
        _runHeading = speed > 1f ? flat / speed : Geo.Facing(pawn.Rotation.Yaw);
        _runSpeed = MathF.Max(speed, RunStartSpeed);
        _runYaw = MathF.Atan2(_runHeading.Y, _runHeading.X) * Geo.Deg;
        _runBank = 0f;
        _runVision = 0f;
        _skidding = false;
        _stuckFor = 0f;
        _speedHeld = 0f;
        _jumpWas = Keyboard.IsDown(Keys.Space);

        float below = Ground.Below(pawn, SkimHeight * 2f);
        if (below > 1f && below < SkimHeight * 2f)
            pawn.Move(-Vector3.UnitZ * below);
        _runAt = pawn.Location;

        // Out of flight, put him on his feet walking and let the game's own run start take over.
        if (pawn.Physics != Actor.EPhysics.PHYS_Walking)
            pawn.SetPhysics(Actor.EPhysics.PHYS_Walking, false);
        if (_camHandOff)
            Safe.Run("flight pose off", () => Ground.Settle(pawn));
        // Flight kept his own speed at nothing. The game has to see him moving to run him.
        pawn.Velocity = _runHeading * MathF.Min(_runSpeed, RunAnimSpeed);
        _running = true;
        _runPawn = pawn;
        if (!RunStates.Contains(pc.GetStateName().ToString()))
            Safe.Run("run state", () => pc.GotoState(new FName(RunStates[0])));
        Safe.Run("game run", pc.StartRun);

        // The pad isn't parked, since the game runs him from its stick.
        GameKeys.Hold("run", GameKeys.ButtonKeys);
        if (pawn.Mesh is { IsValid: true } mesh)
        {
            _animRateWas = SaneRate(mesh.GlobalAnimRateScale);
            Shared.Set(RunKey, BitConverter.SingleToInt32Bits(_animRateWas));
        }

        _runClock.Step(0.1f);
        // Straight from flight or a flurry the camera carries on as it was.
        if (_ownCam)
            _ownCam = false;
        else if (!_camHandOff)
            StartView();
        Sound(pawn, RunWhoosh);
        FlashPuff(Fx.Dust, Feet(pawn) - _runHeading * 60f);
    }

    // From Frame, after the game has moved him this frame.
    private void RunStep()
    {
        float dt = _runClock.Step(0.1f);
        if (_runPawn is not { IsValid: true, bDeleteMe: false } pawn || RunPawn() is not { } now || now.Ptr != pawn.Ptr
            || World.Pc is not { } pc)
        {
            EndRun(RunEnd.Stop);
            return;
        }

        // The game climbs, vaults and drops him by itself while he runs. He's left to it and runs on after.
        if (InTraversal(pawn))
        {
            Traverse(pawn, dt);
            return;
        }

        if (_traversing)
            EndTraverse(pawn);
        // Held for a moment, so a ledge that stops him dead doesn't cost the speed he had.
        _speedHeld = MathF.Max(_runSpeed, _speedHeld - RunAccel * dt);

        if (pawn.CurrentSpecialMove is { IsValid: true } || !RunStates.Contains(pc.GetStateName().ToString())
            || Vector3.DistanceSquared(pawn.Location, _runAt) > MovedAway * MovedAway)
        {
            EndRun(RunEnd.Stop);
            return;
        }

        if (_reassert.Elapsed(0.5))
            GameKeys.Reassert();
        if (dt <= 0f)
            return;

        // Superman launches into flight off a run. The Flash's jumps are the game's own, while he runs.
        if (Jumped() && Heroes.Is(Hero.Superman))
        {
            Launch();
            return;
        }

        KeepRunning(pawn, pc);
        if (!RunSpeedStep(pawn, pc, dt))
            return;

        if (_tornado)
            TornadoPlace(pawn, dt);
        else if (!RunPlace(pawn, pc, dt))
            return;

        RunLook(pawn, pc, dt);
        RunFeel(pawn);
    }

    // Keeps the game in its running state. Sent once he's moving, or it drops straight back out.
    private void KeepRunning(RPawnPlayer pawn, RPlayerController pc)
    {
        if (_skidding || pc.GetStateName().ToString() == RunStates[1] || pawn.ActualMovementSpeed == 0)
            return;

        pc.GotoState(new FName(RunStates[1]));
    }

    private bool Jumped()
    {
        // Superman launches into flight off it, the Flash leaps.
        bool down = Listening && Keyboard.IsDown(Keys.Space);
        bool pressed = down && !_jumpWas;
        _jumpWas = down;
        return pressed && !_skidding && _runSpeed > SkidFrom;
    }

    // Speed and heading from the keys. False when the run is over.
    private bool RunSpeedStep(RPawnPlayer pawn, RPlayerController pc, float dt)
    {
        if (TornadoStep(pawn))
            return true;
        if (Controls.Take(Act.Skid))
            _padRun = false;
        Vector2 move = Listening ? RunInput() : Vector2.Zero;
        float yaw = Hooks.PlayerCam() is { } cam ? cam.CameraCache.POV.Rotation.Yaw : pawn.Rotation.Yaw;
        Vector3 wish = Geo.Facing(yaw) * move.Y + Geo.Facing(yaw + 90f) * move.X;
        bool go = Listening && RunHeld() && wish.LengthSquared() > 0.04f;
        float top = RunTop;

        if (go)
        {
            if (_skidding)
                EndSkid(pawn);

            Vector3 want = Vector3.Normalize(wish);
            if (Vector3.Dot(want, _runHeading) < RunBrakeDot && _runSpeed > SkidFrom)
            {
                _runSpeed = MathF.Max(0f, _runSpeed - SkidDecel * dt);
            }
            else
            {
                float frac = Math.Clamp(_runSpeed / top, 0f, 1f);
                float rate = (RunTurnSlow + (RunTurnFast - RunTurnSlow) * frac) * dt;
                _runHeading = TurnFlat(_runHeading, want, rate);
                if (_runSpeed < top)
                    _runSpeed = MathF.Min(top, _runSpeed + RunAccel * (1f - 0.5f * frac) * dt);
                else
                    _runSpeed = MathF.Max(top, _runSpeed - SkidDecel * 0.5f * dt);
            }

            return true;
        }

        if (!_skidding)
        {
            if (_runSpeed <= SkidFrom)
            {
                EndRun(RunEnd.Stop);
                return false;
            }

            StartSkid(pawn);
        }

        _runSpeed = MathF.Max(0f, _runSpeed - SkidDecel * dt);
        if (_runSpeed > SkidEnd)
            return true;

        EndRun(RunEnd.Stop);
        return false;
    }

    // Turns a flat direction towards another by at most this many degrees.
    private static Vector3 TurnFlat(Vector3 from, Vector3 to, float most)
    {
        float a = MathF.Atan2(from.Y, from.X) * Geo.Deg;
        float b = MathF.Atan2(to.Y, to.X) * Geo.Deg;
        float turn = Math.Clamp(Geo.WrapDegrees(b - a), -most, most);
        return Geo.Facing(a + turn);
    }

    // Moves him for the frame. False when he ran off an edge and the run is over.
    private bool RunPlace(RPawnPlayer pawn, RPlayerController pc, float dt)
    {
        Vector3 step = _runHeading * (_runSpeed * dt);
        if (Opt.RamOn && _runSpeed > RamTouch)
            RamAlong(pawn, pc, _runHeading * _runSpeed, step, thugs: true);

        // Back to where he should be, then up a step and across.
        pawn.Move(_runAt - pawn.Location + Vector3.UnitZ * RunStepUp);
        float rose = pawn.Location.Z - _runAt.Z;
        Vector3 from = pawn.Location;
        pawn.Move(step);
        Vector3 got = pawn.Location - from;
        float want = step.Length();
        if (want > 1f && got.Length() < want * 0.9f)
        {
            // Blocked. What's left is tried one way at a time, so he slides along walls.
            Vector3 left = step - got;
            pawn.Move(new Vector3(left.X, 0f, 0f));
            pawn.Move(new Vector3(0f, left.Y, 0f));
            got = pawn.Location - from;
            if (!RamPassing)
            {
                float kept = Math.Clamp(Vector3.Dot(got, _runHeading) / want, 0f, 1f);
                float lost = _runSpeed * (1f - kept);
                if (lost > CrashFrom)
                    CrashFeel(pawn, lost, _runHeading);
                _runSpeed *= kept;
                if (got.LengthSquared() > 1f && kept < 0.95f)
                    _runHeading = Vector3.Normalize(new Vector3(got.X, got.Y, 0f));
            }
        }

        // Down onto whatever is there, a step plus however far a slope drops over the move.
        Vector3 across = pawn.Location;
        float drop = rose + MathF.Max(RunDropLeast, want * 1.2f);
        pawn.Move(-Vector3.UnitZ * drop);
        if (across.Z - pawn.Location.Z >= drop - 1f)
        {
            // Nothing under him. Off he goes, at the speed he had, and on a pad the run picks up when he lands.
            bool latched = _padRun;
            EndRun(RunEnd.Fall);
            _padRun = latched;
            return false;
        }

        if (pawn.Physics != Actor.EPhysics.PHYS_Walking)
            pawn.SetPhysics(Actor.EPhysics.PHYS_Walking, false);
        _runAt = pawn.Location;

        _stuckFor = !_skidding && _runSpeed < RunStuckBelow ? _stuckFor + dt : 0f;
        if (_stuckFor < RunStuckSeconds)
            return true;

        // Pinned against something. He stops with a jolt.
        _stuckSpot = pawn.Location;
        _stuckDir = _runHeading;
        Rumble.Play(Buzz.Crash);
        _camKick = MathF.Max(_camKick, 0.6f);
        EndRun(RunEnd.Stop);
        return false;
    }

    // Faces the way he's running, leaning into turns, with his run cycle sped up to his speed.
    private void RunLook(RPawnPlayer pawn, RPlayerController pc, float dt)
    {
        float yaw = MathF.Atan2(_runHeading.Y, _runHeading.X) * Geo.Deg;
        float turn = Geo.WrapDegrees(yaw - _runYaw) / MathF.Max(dt, 1e-3f);
        _runYaw = yaw;
        float bankWant = _skidding ? 0f : Math.Clamp(turn * RunBankPerTurn, -RunBankMost, RunBankMost);
        _runBank += (bankWant - _runBank) * Ease.Follow(8f, dt);

        pawn.SetRotation(new Rotator(0f, yaw, _runBank));
        pawn.DesiredRotation = new Rotator(0f, yaw, 0f);
        pawn.bDesiredRotationSet = true;
        pawn.bLockDesiredRotation = true;

        // Speed and the run button both say run. Both are off while skidding.
        if (_skidding)
        {
            pawn.Velocity = Vector3.Zero;
            pawn.Acceleration = Vector3.Zero;
        }
        else
        {
            pawn.Velocity = _runHeading * MathF.Min(_runSpeed, RunAnimSpeed);
            pawn.Acceleration = _runHeading * MathF.Max(pawn.AccelRate, RunAnimSpeed);
        }

        float top = RunTop;
        bool vision = Opt.SpeedVision.On && !_skidding && _runSpeed > top * VisionFrom;
        float was = _runVision;
        _runVision += ((vision ? 1f : 0f) - _runVision) * Ease.Follow(vision ? 4f : 7f, dt);
        if (_runVision > 0.01f)
            Pace.Hold(RunPaceKey, 1f + (Opt.WorldSpeed.Number - 1f) * _runVision);
        else
            Pace.Drop(RunPaceKey);
        if (was < 0.5f && _runVision >= 0.5f)
        {
            // Its shockwave sound is in BoomFeel. The sonic burst's sound is the Batmobile's jump wind, a loop that
            // never stops.
            BoomFeel(pawn);
        }

        // Sped up to match his speed and the world's slow down, so he looks normal in it.
        if (pawn.Mesh is { IsValid: true } mesh)
        {
            float legs = _skidding ? 1f : Math.Clamp(_runSpeed / RunAnimSpeed, 1f, LegRateMost);
            mesh.GlobalAnimRateScale = _animRateWas * legs / MathF.Max(Pace.Speed, 0.05f);
        }
    }

    private void RunFeel(RPawnPlayer pawn)
    {
        float every = _skidding ? 0.08f : 0.14f;
        if ((_skidding || _runSpeed > RunTop * 0.5f) && _runDust.Elapsed(every))
            FlashPuff(Fx.Dust, Feet(pawn) - _runHeading * (_skidding ? -40f : 60f));
        if (!_skidding && _runSpeed > RunTop * 0.4f && _runBuzz.Elapsed(0.28))
            Rumble.Play(_runSpeed > RunTop * 0.75f ? Buzz.RunFast : Buzz.Run);
        FlashRunFx(pawn);
    }

    private void StartSkid(RPawnPlayer pawn)
    {
        Rumble.Play(Buzz.Skid);
        _skidding = true;
        _runBank = 0f;
        Pace.Drop(RunPaceKey);
        Sound(pawn, SkidWhoosh);

        AnimSequence? seq = FindAnim(Rig.SkidAnim);
        foreach (string name in SkidAnims)
            seq ??= FindAnim(name);
        if (seq is { IsValid: true })
            Safe.Run("skid anim", () => PlayIntoStand(pawn, seq));
    }

    private void EndSkid(RPawnPlayer? pawn) => _skidding = false;

    // Space at speed launches him into boost flight, keeping his speed and the camera.
    private void Launch()
    {
        _camHandOff = true;
        try
        {
            EndRun(RunEnd.Carry);
            Toggle();
        }
        finally
        {
            _camHandOff = false;
        }

        if (!_flying)
        {
            Safe.Run("camera end", EndView);
            return;
        }

        _fm.Velocity += Vector3.UnitZ * LaunchUp;
        _fm.PitchHeading(LaunchPitch);
    }

    // Skimming the ground in boost flight with Ctrl held, he drops onto his feet into a run.
    private bool SkimToRun(RPawnPlayer pawn, in FlightInput input)
    {
        if (!CanRun || _autoFast != true || _slamming || Holding || Rushing || _swing != null || input.Lift > -0.5f
            || _ground > SkimHeight)
            return false;
        if (World.Pc is not { } pc)
            return false;

        Vector3 v = _fm.Velocity;
        _camHandOff = true;
        try
        {
            End("RUNNING", fall: false);
            Safe.Run("run start", () => StartRun(pawn, pc, new Vector3(v.X, v.Y, 0f)));
        }
        finally
        {
            _camHandOff = false;
        }

        if (!_running)
            Safe.Run("camera end", EndView);
        return true;
    }

    private enum RunEnd
    {
        // Handed back to the game standing or jogging.
        Stop,
        // Ran off an edge, falling with his speed.
        Fall,
        // Into flight, which takes his speed.
        Carry,
    }

    private void EndRun(RunEnd how)
    {
        _padRun = false;
        _traversing = false;
        if (!_running)
            return;

        if (_tornado)
            EndTornado(_runPawn);
        FlashRunFxOff();

        RPawnPlayer? pawn = _runPawn is { IsValid: true, bDeleteMe: false } p ? p : null;
        EndSkid(pawn);
        _running = false;
        _runPawn = null;
        Safe.Run("flight sounds off", EndFeel);
        Pace.Drop(RunPaceKey);
        _runVision = 0f;
        Safe.Run("run keys", () => GameKeys.Release("run"));
        if (!_camHandOff)
            Safe.Run("camera end", EndView);
        Shared.Set(RunKey, null);
        if (pawn == null)
            return;

        if (pawn.Mesh is { IsValid: true } mesh)
            mesh.GlobalAnimRateScale = _animRateWas;
        pawn.bLockDesiredRotation = false;
        Safe.Run("facing off", pawn.ResetDesiredRotation);
        pawn.SetRotation(new Rotator(0f, _runYaw, 0f));
        pawn.Acceleration = Vector3.Zero;

        Vector3 v = _runHeading * _runSpeed;
        switch (how)
        {
            case RunEnd.Fall:
                pawn.SetPhysics(Actor.EPhysics.PHYS_Falling, false);
                pawn.Velocity = v;
                break;
            // Flight takes over from here.
            case RunEnd.Carry:
                pawn.Velocity = v;
                break;
            default:
                pawn.Velocity = _runHeading * MathF.Min(_runSpeed, RunAnimSpeed);
                break;
        }
    }

    // Scripts reloaded mid run. His anim rate and heading lock are put back.
    private static void RecoverRun()
    {
        if (Shared.Get(RunKey) is not int bits)
            return;

        Shared.Set(RunKey, null);
        if (World.Player is not { IsValid: true } pawn)
            return;

        if (pawn.Mesh is { IsValid: true } mesh)
            mesh.GlobalAnimRateScale = BitConverter.Int32BitsToSingle(bits);
        pawn.bLockDesiredRotation = false;
        pawn.ResetDesiredRotation();
    }
}
