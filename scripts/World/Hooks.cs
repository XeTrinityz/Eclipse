using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;
using Pov = BmSDK.GameObject.FTPOV;

namespace Trinity.Eclipse;

internal static class Hooks
{
    private static bool _povWritten;
    private static nint _povCam;
    private static Pov _povGame, _povMine;

    internal static R3rdPersonCamera? PlayerCam() =>
        World.Pc?.PlayerCamera is R3rdPersonCamera { IsValid: true, IsClassDefaultObject: false } cam ? cam : null;

    internal static bool InCinematic(PlayerController pc) =>
        pc.bCinematicMode || pc is RPlayerControllerBase { ActiveCinematicMode: { IsValid: true } };

    // Moves him after his tick, then places the view. The game's own view goes back before its update.
    [Redirect(typeof(R3rdPersonCamera), nameof(R3rdPersonCamera.UpdateCamera))]
    private static void UpdateCameraRedirect(R3rdPersonCamera self, float deltaTime)
    {
        bool ours = false;
        try
        {
            ours = Before(self);
        }
        catch (Exception ex)
        {
            Log.Error("camera before", "Camera update (before)", ex);
        }

        self.UpdateCamera(deltaTime);
        if (!ours)
            return;

        try
        {
            float dt = World.Paused ? 0f : deltaTime;
            Suit.I.CameraStep(dt);
            Safe.Run("heavy body", Body.Follow);
            After(self, dt);
        }
        catch (Exception ex)
        {
            Log.Error("camera after", "Camera update (after)", ex);
        }
    }

    private static bool Before(R3rdPersonCamera self)
    {
        if (PlayerCam() is not { } cam || cam.Ptr != self.Ptr)
        {
            _povWritten = false;
            return false;
        }

        if (_povWritten && _povCam == cam.Ptr)
        {
            ref Camera.FTCameraCache cache = ref cam.CameraCache;
            if (SamePov(cache.POV, _povMine))
                cache.POV = _povGame;
        }

        _povWritten = false;
        return true;
    }

    private static void After(R3rdPersonCamera cam, float dt)
    {
        ref Camera.FTCameraCache cache = ref cam.CameraCache;
        Pov game = cache.POV;
        Pov pov = game;
        if (!Suit.I.View(cam, game, ref pov, dt))
            return;

        cache.POV = pov;
        _povGame = game;
        _povMine = pov;
        _povCam = cam.Ptr;
        _povWritten = true;
    }

    private static bool SamePov(in Pov a, in Pov b)
    {
        Rotator ra = a.Rotation, rb = b.Rotation;
        return Vector3.DistanceSquared(a.Location, b.Location) < 0.01f
            && MathF.Abs(ra.Pitch - rb.Pitch) < 0.01f && MathF.Abs(ra.Yaw - rb.Yaw) < 0.01f && MathF.Abs(ra.Roll - rb.Roll) < 0.01f;
    }

    // Leaves the game's own view in place when scripts unload.
    public static void Shutdown()
    {
        if (_povWritten && PlayerCam() is { } cam && cam.Ptr == _povCam)
        {
            ref Camera.FTCameraCache cache = ref cam.CameraCache;
            if (SamePov(cache.POV, _povMine))
                cache.POV = _povGame;
        }

        _povWritten = false;
    }
}
