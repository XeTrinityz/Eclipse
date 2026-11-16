using System.Numerics;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Flight sounds and effects, all the game's own. See World/Fx.cs.
internal sealed partial class Suit
{
    // Losing this much speed in one frame is a crash. At Full it shakes the view hardest.
    private const float CrashFrom = 1800f;
    private const float CrashFull = 4500f;
    private const double CrashAgain = 0.4;

    private const string RushKey = "eclipse.rush";

    private Actor? _rushOn;
    private double _crashAt;

    private static void Puff(string path, Vector3 at, Vector3 facing = default)
    {
        if (Opt.FlightFx.On && Fx.Room(PoolSpare))
            Safe.Run("flight effect", () => Fx.Spawn(path, at, facing));
    }

    private static void Ring(string path, Vector3 at, Vector3 across)
    {
        if (Opt.FlightFx.On && Fx.Room(PoolSpare))
            Safe.Run("flight effect", () => Fx.Spawn(path, at, Fx.Across(across)));
    }

    private static void Sound(Actor on, string path)
    {
        if (Opt.FlightSounds.On)
            Safe.Run("flight sound", () => Fx.Play(on, path));
    }

    private static Vector3 Feet(Pawn pawn) => pawn.Location - Vector3.UnitZ * pawn.GetCollisionHeight();

    private void TakeOffFeel(RPawnPlayer pawn)
    {
        Rumble.Play(Buzz.TakeOff);
        Puff(Fx.Dust, Feet(pawn));
        Sound(pawn, Fx.TakeOffSound);
    }

    // The dive pose has just gone on.
    private void BoostFeel(RPawnPlayer pawn)
    {
        Vector3 back = -_fm.Heading;
        Puff(Fx.Gust, pawn.Location + back * 60f, back);
        Sound(pawn, Fx.BoostSound);
    }

    // A dash, a roll, a burst up or a drop.
    private void MoveFeel()
    {
        if (Held() is { } pawn)
            Sound(pawn, Fx.MoveSound);
    }

    private void SonicFeel(RPawnPlayer pawn)
    {
        Rumble.Play(Buzz.Burst);
        Puff(Fx.Gust, pawn.Location, -_fm.Heading);
        Ring(Fx.Dust, pawn.Location, _fm.Heading);
        Sound(pawn, Fx.WaveSound);
        _fovKick = MathF.Max(_fovKick, 16f);
        _camKick = MathF.Max(_camKick, 1.2f);
    }

    private void SlamFeel(RPawnPlayer pawn, Vector3 at, float hard)
    {
        Rumble.Play(Buzz.Slam);
        Puff(Fx.FloorSlam, at);
        Puff(Fx.Dust, at);
        Sound(pawn, Fx.SlamSound);
        Sound(pawn, Fx.SlamDebris);
        _camKick = MathF.Max(_camKick, 2f + 2f * hard);
        _fovKick = MathF.Max(_fovKick, 10f + 8f * hard);
    }

    // A heavy punch's shockwave.
    private static void WaveFeel(RPawnPlayer pawn, Vector3 at, Vector3 dir)
    {
        Puff(Fx.Gust, at, dir);
        Sound(pawn, Fx.WaveSound);
    }

    private void LandFeel(RPawnPlayer pawn)
    {
        Rumble.Play(Buzz.Land);
        Puff(Fx.Dust, Feet(pawn));
        Sound(pawn, Fx.LandSound);
    }

    // Flying into something that doesn't give. The view jolts and it bangs.
    private void CrashFeel(RPawnPlayer pawn, float lost, Vector3 dir)
    {
        Rumble.Play(Buzz.Crash);
        // Not during a slam, a punch or a fly in, since those hit things on purpose.
        if (lost < CrashFrom || _slamming || _swing != null || Rushing || Clock.Now - _crashAt < CrashAgain)
            return;

        _crashAt = Clock.Now;
        float hard = Math.Clamp((lost - CrashFrom) / (CrashFull - CrashFrom), 0f, 1f);
        _camKick = MathF.Max(_camKick, 0.8f + 1.7f * hard);
        _fovKick = MathF.Max(_fovKick, 4f + 6f * hard);
        Puff(Fx.Gust, pawn.Location + dir * pawn.GetCollisionRadius(), -dir);
        if (Opt.FlightSounds.On)
            Thud(pawn, hard > 0.5f);
    }

    // Flight has no wind loop. This only stops one left on from a super run.
    private void FeelStep(RPawnPlayer pawn) => RushSound(pawn, false);

    // The wind rushing past during the super run.
    private void RushSound(RPawnPlayer pawn, bool want)
    {
        want &= Opt.FlightSounds.On;
        if (want == (_rushOn != null))
            return;

        if (want)
        {
            _rushOn = pawn;
            Shared.Set(RushKey, true);
            Safe.Run("wind on", () => Fx.StartLoop(pawn, RunRush));
        }
        else
        {
            EndFeel();
        }
    }

    // The loop is taken off the same actor it was put on.
    private void EndFeel()
    {
        if (_rushOn is { IsValid: true } on)
            Safe.Run("wind off", () => Fx.StopLoop(on, RunRush));
        _rushOn = null;
        Shared.Set(RushKey, false);
    }

    // Scripts reloaded mid boost. Nothing else knows the wind is still on him.
    private static void RecoverRush()
    {
        Shared.Set(RushKey, false);
        if (World.Player is { IsValid: true } pawn)
            Fx.StopAllLoops(pawn, RunRush);
    }
}
