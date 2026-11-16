using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Turns his body with the camera while heat vision fires on foot, so the beams leave his eyes.
internal sealed partial class Suit
{
    private const string FaceKey = "eclipse.facing";
    private const float FootTurnRate = 14f;
    private static readonly string[] FacingStates = { "WalkingMode", "RunningMode", "RadioMode" };

    private bool _footFace;

    private void FootFaceStep(float dt)
    {
        if (!(_firing || _bolt >= 0f) || _flying || _running || dt <= 0f || (PowerPawn() ?? FlashPawn()) is not { } pawn || World.Pc is not { } pc || Hooks.PlayerCam() is not { } cam
            || pawn.Physics != Actor.EPhysics.PHYS_Walking || pawn.CurrentSpecialMove is { IsValid: true }
            || !FacingStates.Contains(pc.GetStateName().ToString()))
        {
            EndFootFace();
            return;
        }

        float want = cam.CameraCache.POV.Rotation.Yaw;
        Rotator now = pawn.Rotation;
        float yaw = now.Yaw + Geo.WrapDegrees(want - now.Yaw) * Ease.Follow(FootTurnRate, dt);
        pawn.SetRotation(new Rotator(now.Pitch, yaw, now.Roll));
        pawn.DesiredRotation = new Rotator(0f, want, 0f);
        pawn.bDesiredRotationSet = true;
        pawn.bLockDesiredRotation = true;
        if (!_footFace)
        {
            _footFace = true;
            Shared.Set(FaceKey, true);
        }
    }

    private void EndFootFace()
    {
        if (!_footFace)
            return;

        _footFace = false;
        Shared.Set(FaceKey, false);
        if (World.Player is not { } pawn)
            return;

        pawn.bLockDesiredRotation = false;
        Safe.Run("facing off", pawn.ResetDesiredRotation);
    }

    // Scripts reloaded while his heading was locked.
    private static void RecoverFacing()
    {
        if (!Shared.GetValue(FaceKey, false))
            return;

        Shared.Set(FaceKey, false);
        if (World.Player is { } pawn)
        {
            pawn.bLockDesiredRotation = false;
            pawn.ResetDesiredRotation();
        }
    }
}
