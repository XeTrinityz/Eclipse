using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using Pov = BmSDK.GameObject.FTPOV;

namespace Trinity.Eclipse;

// Our camera while flying or super running. The game's rotation is kept, only the position is ours.
internal sealed partial class Suit
{
    private const float CamShoulder = 60f;
    private const float CamShoulderFast = 35f;
    private const float CamSide = 45f;
    private const float CamFov = 12f;
    private const float CamSurgeFov = 10f;
    private const float CamTrailMost = 90f;
    private const float CamPivotRate = 16f, CamPivotRateFast = 7f;
    private const float CamBankShare = 0.25f;
    private const float CamShake = 0.12f;
    private const float CamWallGap = 25f;
    private const float CamBlendIn = 0.5f, CamBlendOut = 0.6f;

    // R3rdPersonCamera pitch limits are in game rotation units. This is about 82 degrees.
    private const int CamPitchLimit = 15000;

    private Vector3 _camPivot;
    private bool _camReady;
    private FrameTimer _viewClock;
    private float _camDist, _camFov, _camRoll, _camFast, _fovKick, _camKick, _zoom;
    private float _camBlendIn, _camBlendOut;
    private const float TwistPitch = -35f, TwistBack = 0.5f;
    private float _twistCam;
    private Pov _camFrom, _camLast;

    private nint _pinCam;
    private bool _pinned;
    private int _minPitch, _maxPitch;
    private bool _assistWas, _dragOffWas;

    private void StartView()
    {
        _camReady = false;
        _camBlendIn = CamBlendIn;
        _camBlendOut = 0f;
        _camFov = 0f;
        _camRoll = 0f;
        _fovKick = 0f;
        // Old jolts only wear off while flying, so clear them or they shake the next flight.
        _camKick = 0f;
        _zoom = 0f;
    }

    private void EndView()
    {
        Unpin();
        _camFrom = _camLast;
        _camBlendOut = _camLast.FOV > 1f ? CamBlendOut : 0f;
        _camBlendIn = 0f;
    }

    // Called from the camera update after the game's own. True when pov was changed.
    public bool View(R3rdPersonCamera cam, in Pov game, ref Pov pov, float dt)
    {
        dt = Math.Clamp(dt, 0f, 0.1f);
        // Speed vision slows the game, so the run camera keeps real time.
        float real = _viewClock.Step(0.1f);
        RPawnPlayer? pawn = _flying ? Held() : _running ? _runPawn : _blitzing || _ownCam ? World.Player : null;
        if (pawn is not { IsValid: true } || !Opt.Camera.On)
        {
            if (_flying || _running || _blitzing || _ownCam)
                _camBlendOut = 0f;
            bool blended = BlendBack(game, ref pov, dt);
            return BeamCam(ref pov, World.Player, MathF.Max(dt, 1f / 120f)) || blended;
        }

        Pin(cam);
        // While he marks targets ours follows him as normal. Once he strikes it goes up over the fight.
        if (_blitzing && !_blitzAiming)
            return BlitzView(game, ref pov, MathF.Max(real, 1f / 120f));
        float step = MathF.Max(_running || _blitzing || _ownCam ? real : dt, 1f / 120f);
        float fastWant = _running
            ? Ease.Smooth(_runSpeed / MathF.Max(RunTop, 1f))
            : Ease.Smooth((_fm.Velocity.Length() - _fm.MoveSpeed) / MathF.Max(_fm.BoostSpeed - _fm.MoveSpeed, 1f));
        float surge = _running ? 0f : _fm.Surge;
        bool pushing = _running ? !_skidding : _fm.Boosting;
        _camFast += (fastWant - _camFast) * Ease.Follow(3f, step);
        float fast = _camFast;

        // In a tornado the view rises over the funnel and looks down into it.
        _twistCam += ((_tornado ? 1f : 0f) - _twistCam) * Ease.Follow(3f, step);
        Rotator look = game.Rotation;
        if (_twistCam > 0.001f)
            look = new Rotator(Lerp(look.Pitch, TwistPitch, _twistCam), look.Yaw, look.Roll);
        Geo.Axes(new Rotator(look.Pitch, look.Yaw, 0f), out Vector3 fwd, out _, out _);
        Vector3 side = Geo.Facing(look.Yaw + 90f);

        float dist = Lerp(Opt.CamDistance.Number, Opt.CamFastDistance.Number, fast) * Metre * (1f + TwistBack * _twistCam);
        _camDist = _camDist <= 0f || !_camReady ? dist : _camDist + (dist - _camDist) * Ease.Follow(4f, step);

        // In a flurry his blows drift him off the spot he's pinned to every frame, so the view holds the spot.
        Vector3 at = _flurry && _ownCam ? _flurrySpot : pawn.Location;
        if (_twistCam > 0.001f)
            at = Vector3.Lerp(at, new Vector3(_eye.X, _eye.Y, at.Z), _twistCam);
        Vector3 target = at + Vector3.UnitZ * Lerp(CamShoulder, CamShoulderFast, fast) + side * (CamSide * (1f - fast));
        if (!_camReady)
        {
            _camPivot = target;
            _camReady = true;
        }

        // Trails him a little at speed, on a short leash so it can't drag off him.
        _camPivot += (target - _camPivot) * Ease.Follow(Lerp(CamPivotRate, CamPivotRateFast, fast), step);
        Vector3 lag = _camPivot - target;
        if (lag.Length() > CamTrailMost)
            _camPivot = target + Vector3.Normalize(lag) * CamTrailMost;

        Vector3 eye = _camPivot - fwd * _camDist;
        if (Ground.Ray(pawn, _camPivot, eye, out Vector3 wall, out Vector3 normal))
            eye = wall + normal * CamWallGap;

        float baseFov = game.FOV > 1f ? game.FOV : 70f;
        _fovKick = MathF.Max(0f, _fovKick - step * 18f);
        // A heavy punch winding up closes the view in on him.
        _zoom += (_zoomWant - _zoom) * Ease.Follow(12f, step);
        float fovWant = baseFov + CamFov * fast + CamSurgeFov * surge + _fovKick - _zoom;
        _camFov = _camFov <= 0f ? fovWant : _camFov + (fovWant - _camFov) * Ease.Follow(3f, step);

        // Follows the turn bank only, or the barrel roll would spin the view.
        float rollWant = _running ? _runBank * CamBankShare * (1f - _twistCam) : _autoFast == true ? _lean.Y * CamBankShare : 0f;
        _camRoll += (rollWant - _camRoll) * Ease.Follow(4f, step);

        Rotator rot = new(look.Pitch, look.Yaw, _camRoll);
        // A hard hit jolts the view for a moment.
        _camKick = MathF.Max(0f, _camKick - step * 3f);
        float shake = CamShake * fast * (pushing ? 1f : 0.4f) * (1f + surge) + _camKick * 1.5f;
        if (shake > 0.001f)
        {
            float t = (float)Clock.Now;
            rot = new Rotator(
                rot.Pitch + shake * (0.7f * MathF.Sin(t * 37.1f) + 0.4f * MathF.Sin(t * 61.7f + 1.3f)),
                rot.Yaw + shake * (0.5f * MathF.Sin(t * 29.3f + 0.7f) + 0.3f * MathF.Sin(t * 53.9f)),
                rot.Roll + shake * 0.9f * MathF.Sin(t * 23.7f + 2.1f));
        }

        var mine = new Pov { Location = eye, Rotation = rot, FOV = Math.Clamp(_camFov, 20f, 130f) };
        if (_camBlendIn > 0f)
        {
            _camBlendIn = MathF.Max(0f, _camBlendIn - step);
            mine = Blend(game, mine, Ease.Smooth(1f - _camBlendIn / CamBlendIn));
        }

        BeamCam(ref mine, pawn, step);
        pov = mine;
        _camLast = mine;
        return true;
    }

    // Heat vision camera sits over his right shoulder, so the beams can be seen leaving his eyes.
    private const float BeamCamBack = 150f;
    private const float BeamCamSide = 55f;
    private const float BeamCamUp = 18f;
    private const float BeamCamDropMost = 40f;
    private const float BeamCamFov = 6f;
    private float _beamCam;
    private Vector3 _beamHead;
    private bool _beamHeadSet;

    private bool BeamCam(ref Pov pov, RPawnPlayer? pawn, float step)
    {
        bool on = _firing && pawn != null;
        _beamCam += ((on ? 1f : 0f) - _beamCam) * Ease.Follow(6f, step);
        if (pawn == null || _beamCam < 0.002f)
        {
            _beamCam = 0f;
            _beamHeadSet = false;
            return false;
        }

        // His head bobs with his anims, so its place on him is smoothed.
        Vector3 head = HeadAt(pawn) - pawn.Location;
        _beamHead = _beamHeadSet ? _beamHead + (head - _beamHead) * Ease.Follow(10f, step) : head;
        _beamHeadSet = true;

        Rotator look = pov.Rotation;
        Geo.Axes(new Rotator(look.Pitch, look.Yaw, 0f), out Vector3 fwd, out _, out _);
        Vector3 side = Geo.Facing(look.Yaw + 90f);
        Vector3 anchor = pawn.Location + _beamHead;
        Vector3 want = anchor + side * BeamCamSide + Vector3.UnitZ * BeamCamUp - fwd * BeamCamBack;
        // Looking up would swing it down into his back.
        want.Z = MathF.Max(want.Z, anchor.Z - BeamCamDropMost);
        if (Ground.Ray(pawn, anchor, want, out Vector3 wall, out Vector3 normal))
            want = wall + normal * CamWallGap;

        float k = Ease.Smooth(_beamCam);
        pov.Location = Vector3.Lerp(pov.Location, want, k);
        pov.FOV = MathF.Max(20f, pov.FOV - BeamCamFov * k);
        return true;
    }

    private bool BlendBack(in Pov game, ref Pov pov, float dt)
    {
        if (_camBlendOut <= 0f)
            return false;

        _camBlendOut = MathF.Max(0f, _camBlendOut - MathF.Max(dt, 1f / 120f));
        pov = Blend(_camFrom, game, Ease.Smooth(1f - _camBlendOut / CamBlendOut));
        return true;
    }

    private static Pov Blend(in Pov a, in Pov b, float t)
    {
        Rotator ra = a.Rotation, rb = b.Rotation;
        return new Pov
        {
            Location = Vector3.Lerp(a.Location, b.Location, t),
            Rotation = new Rotator(
                ra.Pitch + Geo.WrapDegrees(rb.Pitch - ra.Pitch) * t,
                ra.Yaw + Geo.WrapDegrees(rb.Yaw - ra.Yaw) * t,
                ra.Roll + Geo.WrapDegrees(rb.Roll - ra.Roll) * t),
            FOV = a.FOV + (b.FOV - a.FOV) * t,
        };
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // The view the player sees. Ours while flying, so aiming matches the crosshair.
    private Pov ViewPov(R3rdPersonCamera cam) =>
        (_flying || _running || _blitzing || _ownCam) && Opt.Camera.On && _camReady && _camLast.FOV > 1f ? _camLast : cam.CameraCache.POV;

    // Lets him look nearly straight up and down while flying. On a pad the game also swings the camera after the
    // way he moves (assist and drag), and since flight steers by the camera that turned the left stick into a
    // second camera stick. Both are off while flying or running. The game's settings come back after.
    private void Pin(R3rdPersonCamera cam)
    {
        if (!_pinned || _pinCam != cam.Ptr)
        {
            _pinned = true;
            _pinCam = cam.Ptr;
            _minPitch = cam.MinPitch;
            _maxPitch = cam.maxPitch;
            _assistWas = cam.EnableCameraAssist;
            _dragOffWas = cam.bTemporarilyDisableCameraDrag;
        }

        if (cam.EnableCameraAssist)
            cam.EnableCameraAssist = false;
        if (cam.CurrentCameraAssistStrength != 0f)
            cam.CurrentCameraAssistStrength = 0f;
        if (!cam.bTemporarilyDisableCameraDrag)
            cam.bTemporarilyDisableCameraDrag = true;

        if (cam.MinPitch != -CamPitchLimit)
            cam.MinPitch = -CamPitchLimit;
        if (cam.maxPitch != CamPitchLimit)
            cam.maxPitch = CamPitchLimit;
    }

    private void Unpin()
    {
        if (!_pinned)
            return;

        _pinned = false;
        Safe.Run("camera pitch back", () =>
        {
            if (Hooks.PlayerCam() is not { } cam || cam.Ptr != _pinCam)
                return;
            if (cam.MinPitch == -CamPitchLimit)
                cam.MinPitch = _minPitch;
            if (cam.maxPitch == CamPitchLimit)
                cam.maxPitch = _maxPitch;
            cam.EnableCameraAssist = _assistWas;
            cam.bTemporarilyDisableCameraDrag = _dragOffWas;
        });
    }
}
