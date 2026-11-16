using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Heat vision on foot and in flight while the fire key is held.
internal sealed partial class Suit
{
    private const string PowerKeys = "powers";

    // His head, and where his eyes sit off it, facing what he's looking at.
    private static readonly FName[] HeadBones = { new("Bip01_Head") };
    private const float EyesAhead = 9f, EyesUp = 5f, EyesApart = 3.2f;

    private bool _fireWas;
    private bool _firing;
    private bool _humming;
    private Actor? _humOn;
    private Keys _powerKey = Keys.None;

    // Batman on his feet or flying, out of the Batmobile and not in a cutscene.
    private static RPawnPlayer? PowerPawn()
    {
        if (!Heroes.Is(Hero.Superman) || !World.InGame || World.Paused || World.Pc is not { } pc || Hooks.InCinematic(pc))
            return null;
        return World.Pawn is RPawnPlayer { Health: > 0, bHidden: false, bDeleteMe: false } pawn ? pawn : null;
    }

    private static bool FireDown() => Controls.Down(Act.Heat) || Controls.Down(Act.HeatAir);

    // From the game tick. The key, the hum and radio mode. The beams are placed in BeamStep.
    private void PowersStep()
    {
        KeepPowerKey();
        RPawnPlayer? pawn = PowerPawn();
        bool held = FireDown();
        bool down = pawn != null && Listening && held;
        bool pressed = down && !_fireWas;
        _fireWas = held;

        if (_firing && !down)
            StopFiring();

        if (pressed && pawn != null)
        {
            _firing = true;
            StartHum(pawn);
        }

        SearedStep();
        RadioStep(pawn);
    }

    // After he moves, so the beams don't trail his head at speed.
    private void BeamStep()
    {
        if (!_firing)
            return;

        if (PowerPawn() is { } pawn && World.Pc is { } pc)
            HeatStep(pawn, pc);
        else
            HideBeams();
    }

    private void StopFiring()
    {
        _firing = false;
        HideBeams();
        StopHum();
    }

    // The beam hums for as long as it's held.
    private void StartHum(RPawnPlayer pawn)
    {
        StopHum();
        _humOn = pawn;
        _humming = true;
        Safe.Run("beam hum", () => Fx.StartLoop(pawn, Fx.BeamHum));
    }

    private void StopHum()
    {
        if (_humming && _humOn is { IsValid: true } on)
            Safe.Run("beam hum", () => Fx.StopLoop(on, Fx.BeamHum));
        _humming = false;
        _humOn = null;
    }

    // The fire key is kept from the game while his powers can be used.
    private void KeepPowerKey()
    {
        bool want = Heroes.Is(Hero.Superman) && World.InGame && World.Pawn is RPawnPlayer;
        Keys key = want ? Controls.Key(Act.Heat) : Keys.None;
        if (key == _powerKey)
            return;

        _powerKey = key;
        if (key == Keys.None)
            GameKeys.Release(PowerKeys);
        else
            GameKeys.Hold(PowerKeys, new[] { key });
    }

    private void ResetPowers()
    {
        StopFiring();
        _fireWas = FireDown();
        _seared.Clear();
        _fires.Clear();
        _beamLeft.Drop();
        _beamRight.Drop();
        ForgetRadio();
        Fx.Forget();
    }

    // Where the middle of the screen looks from and along.
    private bool AimRay(out Vector3 eye, out Vector3 look)
    {
        if (Hooks.PlayerCam() is not { } cam)
        {
            eye = look = default;
            return false;
        }

        BmSDK.GameObject.FTPOV view = ViewPov(cam);
        eye = view.Location;
        look = Geo.Dir(view.Rotation.Yaw, Geo.WrapDegrees(view.Rotation.Pitch));
        return true;
    }

    private static Vector3 HeadAt(RPawnPlayer pawn)
    {
        if (pawn.Mesh is { IsValid: true } mesh && FindBone(mesh, "head", HeadBones, "Head") is { } bone)
            return mesh.GetBoneLocation(bone, 0);
        return pawn.Location + Vector3.UnitZ * (pawn.GetCollisionHeight() * 0.85f);
    }

    // His eyes, placed off his head towards the aim point.
    private static void EyePoints(RPawnPlayer pawn, Vector3 head, Vector3 toward, out Vector3 left, out Vector3 right)
    {
        Vector3 fwd = toward - head;
        fwd = fwd.LengthSquared() > 1f ? Vector3.Normalize(fwd) : Geo.Facing(pawn.Rotation.Yaw);
        Vector3 side = Vector3.Cross(fwd, Vector3.UnitZ);
        side = side.LengthSquared() > 0.0001f ? Vector3.Normalize(side) : Geo.Facing(pawn.Rotation.Yaw + 90f);
        Vector3 eyes = head + fwd * EyesAhead + Vector3.UnitZ * EyesUp;
        left = eyes + side * EyesApart;
        right = eyes - side * EyesApart;
    }
}
