using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Watches him after flight. Turns force glide off once he glides and checks he isn't stuck.
internal sealed partial class Suit
{
    private const double WatchSeconds = 0.1;
    private const double AfterSeconds = 3.0;

    private Every _watchEvery;
    private double _afterUntil = -1.0;
    private double _forceGlideUntil = -1.0;

    private void Watch()
    {
        if (!_watchEvery.Elapsed(WatchSeconds) || !World.Ready || World.Paused)
            return;
        if (World.Player is not { IsValid: true } pawn || World.Pc is not { } pc)
            return;

        ForcedGlideStep(pawn, pc);
        AfterCheck(pawn, pc);
    }

    // Force glide stays on only until he glides, or it keeps pulling him back into one.
    private void ForcedGlideStep(RPawnPlayer pawn, RPlayerController pc)
    {
        if (_forceGlideUntil < 0.0)
            return;

        if (pawn.bGliding || Clock.Now >= _forceGlideUntil)
            StopForcedGlide(pc);
    }

    private void StopForcedGlide(RPlayerController pc)
    {
        if (_forceGlideUntil < 0.0)
            return;

        _forceGlideUntil = -1.0;
        Safe.Run("force glide off", () => pc.bForceGlide = _forceGlideWas);
    }

    private void AfterCheck(RPawnPlayer pawn, RPlayerController pc)
    {
        if (_afterUntil <= 0.0 || Clock.Now < _afterUntil)
            return;

        _afterUntil = -1.0;

        // Flying again or super running, so this check belongs to the last flight.
        if (_flying || _running)
            return;

        // The game's own glide uses PHYS_Flying, so that's only stuck when he isn't gliding.
        bool stuck = pawn.Physics == Actor.EPhysics.PHYS_None || (pawn.Physics == Actor.EPhysics.PHYS_Flying && !pawn.bGliding);
        if (stuck && pawn.CurrentSpecialMove is not { IsValid: true } && !pawn.bHidden)
        {
            Log.Info("He looked stuck in " + pawn.Physics + " after flight, put him in falling.");
            pawn.SetPhysics(Actor.EPhysics.PHYS_Falling, false);
        }

        if (InFlyingState(pc))
        {
            Log.Info("Controller was still in " + FlyingState + " after flight, sent it to " + FallingState + ".");
            pc.GotoState(FallingState);
        }
    }
}
