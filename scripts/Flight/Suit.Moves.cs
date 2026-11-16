using System.Numerics;
using BmSDK.BmGame;

namespace Trinity.Eclipse;

// Quick moves off double taps of the flight keys, like dashes, rolls, bursts and the slam.
internal sealed partial class Suit
{
    private const double TapGap = 0.28;
    private const double MoveCooldown = 0.45;
    private const double TrailFlare = 0.4;

    private const float DashSpeed = 1500f;
    private const float DashLean = 260f;
    private const float RollSpeed = 1400f;
    private const float RollSeconds = 0.55f;
    private const float BurstSpeed = 1200f;
    private const float DropSpeed = 1600f;
    private const float PullUp = 30f, DiveDown = 35f;
    private const float FovKick = 6f;

    private const float TakeOffSpeed = 650f;
    private const float LandHeight = 40f;
    private const double LandHold = 0.15;
    private const float LandSkidFrom = 600f;

    private static readonly Keys[] TapKeys = { Keys.W, Keys.A, Keys.S, Keys.D, Keys.Space, Keys.ControlKey };

    private readonly Dictionary<Keys, (bool Down, double Tapped)> _taps = new();
    private double _dropHeld = -1.0;
    private double _moveReady;
    private float _rollT = -1f, _rollWay;
    private double _landSince = -1.0;

    private void ResetMoves()
    {
        _taps.Clear();
        foreach (Keys key in TapKeys)
            _taps[key] = (Keyboard.IsDown(key), -1.0);
        foreach (Act a in new[] { Act.Burst, Act.Drop, Act.Dash, Act.Roll, Act.SideLeft, Act.SideRight, Act.Punch, Act.Grab })
            Controls.Clear(a);
        _dropHeld = -1.0;

        _moveReady = 0.0;
        _rollT = -1f;
        _landSince = -1.0;
    }

    // From the ground he pops up first, so the hover isn't scraping the floor.
    private void TakeOff(RPawnPlayer pawn)
    {
        if (_ground > 60f || _fm.Velocity.Z > TakeOffSpeed)
            return;

        _fm.Velocity = new Vector3(_fm.Velocity.X, _fm.Velocity.Y, TakeOffSpeed);
        TakeOffFeel(pawn);
    }

    private bool DoubleTap(Keys key)
    {
        bool down = Keyboard.IsDown(key);
        (bool was, double tapped) = _taps.TryGetValue(key, out var t) ? t : (false, -1.0);
        bool hit = false;
        if (down && !was)
        {
            double now = Clock.Now;
            hit = tapped > 0.0 && now - tapped <= TapGap;
            tapped = hit ? -1.0 : now;
        }

        _taps[key] = (down, tapped);
        return hit;
    }

    // Holding the drop button on a pad lands him.
    private const double PadLandHold = 0.45;

    private bool PadLand()
    {
        if (!Controls.PadActive || !Controls.Down(Act.Drop))
        {
            _dropHeld = -1.0;
            return false;
        }

        if (_dropHeld < 0.0)
            _dropHeld = Clock.Now;
        return !_slamming && Clock.Now - _dropHeld >= PadLandHold;
    }

    private void Moves(RPawnPlayer pawn, in FlightInput input)
    {
        if (!Listening)
        {
            ResetMoves();
            return;
        }

        // Every key is read each frame so the taps stay in step.
        bool w = DoubleTap(Keys.W), a = DoubleTap(Keys.A), s = DoubleTap(Keys.S), d = DoubleTap(Keys.D);
        bool up = DoubleTap(Keys.Space) || Controls.Take(Act.Burst);
        bool down = DoubleTap(Keys.ControlKey) || Controls.Take(Act.Drop);
        bool padDash = Controls.Take(Act.Dash);
        bool padRoll = Controls.Take(Act.Roll);
        float side = Controls.Take(Act.SideRight) ? 1f : 0f;
        if (Controls.Take(Act.SideLeft))
            side = -1f;

        if (_slamming || Clock.Now < _moveReady)
            return;

        bool fast = _autoFast == true;
        // The roll key is a side dash while hovering.
        if (padRoll && !fast && side == 0f)
            side = input.Move.X < 0f ? -1f : 1f;
        Vector3 fwd = Geo.Facing(input.Yaw), right = Geo.Facing(input.Yaw + 90f);

        if (fast && (a || d || padRoll || side != 0f))
        {
            BarrelRoll(side != 0f ? side : padRoll ? (input.Move.X < 0f ? -1f : 1f) : d ? 1f : -1f);
            return;
        }

        if (fast && (w || padDash))
        {
            if (CanSonic)
            {
                SonicBurst(pawn);
                Moved(quiet: true);
            }

            return;
        }

        if (!fast && (w || a || s || d || padDash || side != 0f))
        {
            Vector2 way = side != 0f ? new Vector2(side, 0f) : padDash
                ? (input.Move.LengthSquared() > 0.04f ? input.Move : new Vector2(0f, 1f))
                : new Vector2((d ? 1f : 0f) - (a ? 1f : 0f), (w ? 1f : 0f) - (s ? 1f : 0f));
            if (way.LengthSquared() > 0.01f)
            {
                Dash(Vector3.Normalize(fwd * way.Y + right * way.X));
                return;
            }
        }

        if (up)
        {
            if (fast)
                _fm.PitchHeading(PullUp);
            else
                _fm.Velocity = new Vector3(_fm.Velocity.X, _fm.Velocity.Y, MathF.Max(_fm.Velocity.Z, 0f) + BurstSpeed);
            Moved(sound: Fx.QuickWhoosh);
            return;
        }

        if (!down)
            return;

        if (!fast && CanSlam)
        {
            StartSlam(pawn);
            return;
        }

        if (fast)
            _fm.PitchHeading(-DiveDown);
        else
            _fm.Velocity = new Vector3(_fm.Velocity.X, _fm.Velocity.Y, MathF.Min(_fm.Velocity.Z, 0f) - DropSpeed);
        Moved(sound: Fx.LowWhoosh);
    }

    private void Dash(Vector3 way)
    {
        _fm.Velocity += way * DashSpeed;

        // A hard lean the way he goes, which the drift spring rocks back out of.
        Vector3 fwd = Geo.Facing(_faceYaw), right = Geo.Facing(_faceYaw + 90f);
        float lean = Opt.Lean.Key == "against" ? -1f : 1f;
        _leanVel += new Vector2(-Vector3.Dot(way, fwd), Vector3.Dot(way, right)) * (DashLean * lean);
        Moved(sound: Fx.QuickWhoosh);
    }

    private void BarrelRoll(float way)
    {
        Vector3 side = Geo.Facing(_faceYaw + 90f) * way;
        _fm.Velocity += side * RollSpeed;
        _rollT = 0f;
        _rollWay = way;
        Moved(sound: Fx.RollWhoosh);
    }

    // Quiet when the move has a sound of its own.
    private void Moved(bool quiet = false, string? sound = null)
    {
        _moveReady = Clock.Now + MoveCooldown;
        _fovKick = MathF.Max(_fovKick, FovKick);
        _trailUntil = Clock.Now + TrailFlare;
        if (!quiet)
            MoveFeel(sound);
    }

    // A full turn over the roll time, eased so it starts and ends gently.
    private float RollStep(float dt)
    {
        if (_rollT < 0f)
            return 0f;

        _rollT += dt;
        float p = Ease.Smooth(_rollT / RollSeconds);
        if (_rollT >= RollSeconds)
        {
            _rollT = -1f;
            return 0f;
        }

        return 360f * p * _rollWay;
    }

    // Ctrl held near the ground lands him, or drops him into a super run. True if flight ended.
    private bool LandCheck(RPawnPlayer pawn, in FlightInput input)
    {
        if (SkimToRun(pawn, input))
            return true;

        if (PadLand())
        {
            LandNow(pawn);
            return true;
        }

        bool settling = _autoFast != true && !_slamming && input.Lift < -0.5f && _ground < LandHeight;
        if (!settling)
        {
            _landSince = -1.0;
            return false;
        }

        if (_landSince < 0.0)
            _landSince = Clock.Now;
        if (Clock.Now - _landSince < LandHold)
            return false;

        LandNow(pawn);
        return true;
    }

    // Steps out of the hover onto his feet. A skid if he was still moving, a soft touch if not.
    private void LandNow(RPawnPlayer pawn)
    {
        var flat = new Vector2(_fm.Velocity.X, _fm.Velocity.Y);
        bool skid = flat.Length() > LandSkidFrom;
        LandFeel(pawn);
        End("LANDED", fall: true);
        if (World.Pc is not { } pc || Ground.Below(pawn, 80f) >= 60f)
            return;

        // Played into his standing pose, the way the slam's landing is.
        Safe.Run("landing", () => LandOnFeet(pawn, pc, skid ? Rig.LandSkidAnim : Rig.LandSoftAnim, Rig.LandSoftAnim));
    }
}
