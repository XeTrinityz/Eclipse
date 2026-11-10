using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// The hover uses hanging legs with toes pointed, held on one frame so the loop doesn't twitch.
internal sealed partial class Suit
{
    private const string LegsRateKey = "eclipse.legsrate";

    private AnimSequence? _stillLegs;
    private float _stillRate;

    private static bool IsStanding(string? spec) => string.Equals(spec, "standing", StringComparison.OrdinalIgnoreCase);

    // Puts on the hover pose with hanging legs. False when the legs anim isn't loaded.
    private bool StandWithLegs(RPawnPlayer pawn, FName move, FName weapon, float blend)
    {
        if (FindAnim(Rig.HoverLegs) is not { } legs)
        {
            FreeLegs();
            return false;
        }

        HoldLegsStill(legs);
        if (!HoverIdle(out RPawnCharacter.FCustomIdleConfig idle))
            return false;

        pawn.ForceChangePose(move, weapon, default, false, default, idle, default, blend);
        Posed(move);
        return true;
    }

    // The hover's custom idle on its own. A strike's transition ends in it too.
    private bool HoverIdle(out RPawnCharacter.FCustomIdleConfig idle)
    {
        idle = default;
        if (FindAnim(Rig.HoverLegs) is not { } legs || FindAnim(Rig.HoverUpper) is not { } upper)
            return false;

        idle = new RPawnCharacter.FCustomIdleConfig
        {
            Config = new RAnimConfig.FCustomAnimConfig
            {
                FullBodyAnim = legs.SequenceName,
                CustomFullBodyAnim = legs,
                UpperBodyAnim = upper.SequenceName,
                CustomUpperBodyAnim = upper,
            },
        };
        return true;
    }

    private void HoldLegsStill(AnimSequence legs)
    {
        if (_stillLegs is { IsValid: true } held && held.Ptr == legs.Ptr)
            return;

        FreeLegs();
        _stillLegs = legs;
        _stillRate = legs.RateScale;
        legs.RateScale = 0f;
        Shared.Set(LegsRateKey, new long[] { legs.Ptr, BitConverter.SingleToInt32Bits(_stillRate) });
    }

    private void FreeLegs()
    {
        if (_stillLegs is { IsValid: true } held)
            held.RateScale = _stillRate;
        _stillLegs = null;
        Shared.Set(LegsRateKey, null);
    }

    private static void RecoverLegs()
    {
        if (Shared.Get(LegsRateKey) is long[] { Length: 2 } saved && FromPtr<AnimSequence>((nint)saved[0]) is { IsValid: true } legs)
        {
            legs.RateScale = BitConverter.Int32BitsToSingle((int)saved[1]);
            Log.Info("Scripts reloaded mid flight, put " + legs.SequenceName + " back to its normal rate.");
        }

        Shared.Set(LegsRateKey, null);
    }
}
