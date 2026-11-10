using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// His own straight strikes from AutoStrikeList, sped up to land on time and blended back into the hover.
internal sealed partial class Suit
{
    private static readonly FName HoverMove = new("StandingNoTurn");
    private static readonly FName HoverWeapon = new("Relaxed");
    // Where in the anim the fist lands when the game's own contact mark can't be read.
    private const float ContactFallback = 0.45f;

    // Straight strikes that read wrong in flight. Forward_06 swings round to the side.
    private static readonly HashSet<string> Dropped = new(StringComparer.OrdinalIgnoreCase) { "Attack_Close_Forward_06" };

    private sealed record Blow(AnimSequence Seq, string Hand, bool Strong, bool Mirrorable, float Contact, string Bone);

    private const float RateLeast = 0.7f, RateMost = 2f;
    // How far the wind up goes towards contact before it's held, and how fast it blends in.
    private const float CockedShare = 0.8f;
    private const float WindBlend = 0.25f;
    // How fast the held strike runs on when let go, and how long it follows through.
    private const float HeavyRate = 1.6f;
    private const float HeavyFollow = 0.45f;
    // The one strike that reads right as a wind up. The others were tried and looked wrong.
    private const string HeavyStrike = "Attack_Close_Forward_02";
    // Longer strikes are the showy spinning ones.
    private const float BlowLongest = 2.2f;

    private readonly List<Blow> _blows = new();
    private nint _blowsPawn;
    private Blow? _lastBlow;
    private string _lastHand = "";
    private Blow? _windBlow;
    private AnimSequence? _windSeq;
    private float _windRate = 1f;

    private List<Blow> Blows(RPawnPlayer pawn)
    {
        if (pawn.Ptr == _blowsPawn && _blows.Count > 0 && _blows.All(b => b.Seq.IsValid))
            return _blows;

        _blowsPawn = pawn.Ptr;
        _blows.Clear();
        // Strict first. If the table has too few of those, the rest join in.
        for (int pass = 0; pass < 2 && _blows.Count < 3; pass++)
        {
            _blows.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RPawnPlayerCombat.FStrikeInfo strike in pawn.AutoStrikeList)
            {
                if (strike.StrikeAnimName.ToString() is not { Length: > 0 } name || name == "None" || !seen.Add(name))
                    continue;
                if (!StraightPunch(strike, name, strict: pass == 0) || Dropped.Contains(name))
                    continue;

                AnimSequence? seq = strike.StrikeAnimset is { IsValid: true } set ? set.FindAnimSequence(strike.StrikeAnimName) : null;
                seq ??= FindAnim(name);
                if (seq is not { IsValid: true } || seq.SequenceLength <= 0.2f || (pass == 0 && Length(seq) > BlowLongest))
                    continue;

                string hand = strike.StrikeHand.ToString();
                hand = Has(hand, "Left") ? "Left" : Has(hand, "Right") ? "Right" : "";
                string bone = strike.Strike_PlayerStrikingBone.ToString();
                _blows.Add(new Blow(seq, hand, Has(strike.StrikeStrength.ToString(), "Strong"), strike.bCanBeMirrored, ContactFraction(pawn, seq), bone));
            }
        }

        return _blows;
    }

    // Close range, forward, no turn, not low, no railings, walls or weapons. Falls back to the anim name.
    private static bool StraightPunch(RPawnPlayerCombat.FStrikeInfo s, string name, bool strict)
    {
        if ((int)s.StrikeWeaponGrabbed != 0 || (int)s.StrikePickupWeaponType != 0 || (int)s.StrikeRailingDir != 0 || (int)s.StrikeWallDir != 0)
            return false;
        if (s.bHitsLow || Has(name, "Low") || Has(name, "Floor") || Has(name, "Beatdown") || Has(name, "Aerial") || Has(name, "Stun") || Has(name, "Run"))
            return false;

        string range = s.StrikeRange.ToString(), dir = s.StrikeDirection.ToString(), turn = s.StrikeTurnMotion.ToString();
        string strength = s.StrikeStrength.ToString(), target = s.StrikeTargetType.ToString();
        bool close = Named(range) ? Has(range, "Close") : Has(name, "Close");
        bool front = Named(dir) ? Has(dir, "Front") : Has(name, "Forward") || Has(name, "Front");
        bool straight = Named(turn) ? Has(turn, "None") : !(Has(name, "Left") || Has(name, "Right") || Has(name, "Back"));
        bool plain = !Named(strength) || Has(strength, "Weak") || Has(strength, "Strong");
        bool bare = !strict || !Named(target) || Has(target, "Unarmed") || Has(target, "MAX");
        string bone = s.Strike_PlayerStrikingBone.ToString();
        bool hand = !strict || bone.Length == 0 || bone == "None" || Has(bone, "Hand") || Has(bone, "Fist") || Has(bone, "Arm");
        return close && front && straight && plain && bare && hand;
    }

    private static bool Named(string value) => value.Length > 0 && !char.IsDigit(value[0]);

    private static bool Has(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);

    // How far through the anim the fist lands, from the game's own strike contact mark on it.
    private static float ContactFraction(RPawnPlayer pawn, AnimSequence seq)
    {
        float length = seq.SequenceLength;
        float at = 0f;
        Safe.Run("strike contact time", () => at = pawn.GetStrikeContactTime(seq));
        if (at > 0f && at < length)
            return at / length;
        return ContactFallback;
    }

    // Plays a strike timed so the fist lands on contact. Hands alternate. False if there's nothing to play.
    private bool PlayBlow(RPawnPlayer pawn, Swing swing)
    {
        List<Blow> blows = Blows(pawn);
        if (blows.Count == 0)
            return false;

        string want = _lastHand == "Left" ? "Right" : "Left";
        List<Blow> pool = swing.Heavy && blows.Any(b => b.Strong) ? blows.Where(b => b.Strong).ToList() : blows;
        List<Blow> fits = pool.Where(b => b != _lastBlow && (b.Hand == want || b.Hand.Length == 0 || b.Mirrorable)).ToList();
        if (fits.Count == 0)
            fits = pool;
        Blow blow = fits[Random.Shared.Next(fits.Count)];
        // A heavy punch carries on with the strike its wind up started.
        if (swing.Heavy && _windBlow is { } wound)
            blow = wound;
        _windBlow = null;
        // The throw punch is a right hand strike, picked by ThrowBlow.
        if (swing.Throw && ThrowBlow(blows) is { } thrown)
        {
            blow = thrown;
            want = "Right";
        }
        bool mirrored = blow.Hand.Length == 0 ? want == "Right" : blow.Hand != want && blow.Mirrorable;
        string hand = blow.Hand.Length == 0 || mirrored ? want : blow.Hand;

        // The last argument is the transition time, set so the contact mark lands on time.
        float contact = blow.Contact * Length(blow.Seq);
        float rate = Math.Clamp(contact / MathF.Max(swing.ContactAt, 0.05f), RateLeast, RateMost);
        float duration = Length(blow.Seq) / rate;
        var transition = new RPawnCharacter.FCustomTransitionConfig
        {
            Config = new RAnimConfig.FCustomAnimConfig { FullBodyAnim = blow.Seq.SequenceName, CustomFullBodyAnim = blow.Seq },
        };

        if (pawn.Anim is { IsValid: true } anim)
            anim.ForceAnimatedAutomaticTransitionsOff = false;
        HoverIdle(out RPawnCharacter.FCustomIdleConfig idle);
        pawn.ForceChangePose(HoverMove, HoverWeapon, default, mirrored, transition, idle, mirrored ? RAnimUtil.EMirrorChoice.MC_Yes : default, duration);
        Posed(HoverMove);
        _autoSpec = HoverKey();
        _autoFast = false;

        swing.Blow = blow;
        swing.Mirrored = mirrored;
        swing.Since = Clock.Now;
        _lastBlow = blow;
        _lastHand = hand;

        // Our pose and idle keepers leave him alone until the swing is done.
        _overlaySeq = blow.Seq;
        _overlayMove = Move.Punch;
        _overlayUntil = Clock.Now + swing.Seconds + 0.5;
        return true;
    }

    // Heavy wind up, played as a custom idle since only those honour a paused rate.
    private void PlayWind(RPawnPlayer pawn, string hand)
    {
        List<Blow> blows = Blows(pawn);
        Blow? strong = blows.FirstOrDefault(b => string.Equals(b.Seq.SequenceName.ToString(), HeavyStrike, StringComparison.OrdinalIgnoreCase))
            ?? blows.Where(b => b.Strong).OrderBy(_ => Random.Shared.Next()).FirstOrDefault() ?? blows.FirstOrDefault();
        if (strong == null)
            return;

        ThawWind();
        AnimSequence seq = strong.Seq;
        bool mirrored = strong.Hand.Length > 0 && strong.Hand != hand && strong.Mirrorable;
        float was = seq.RateScale > 0.01f ? seq.RateScale : 1f;
        float cocked = strong.Contact * CockedShare * seq.SequenceLength;
        float rate = Math.Clamp(cocked / (float)ChargeFull, 0.1f, 2f);
        _windBlow = strong;
        _windSeq = seq;
        _windRate = was;
        seq.RateScale = was * rate;

        var idle = new RPawnCharacter.FCustomIdleConfig
        {
            Config = new RAnimConfig.FCustomAnimConfig { FullBodyAnim = seq.SequenceName, CustomFullBodyAnim = seq },
        };
        if (pawn.Anim is { IsValid: true } anim)
            anim.ForceAnimatedAutomaticTransitionsOff = false;
        pawn.ForceChangePose(HoverMove, HoverWeapon, default, mirrored, default, idle, mirrored ? RAnimUtil.EMirrorChoice.MC_Yes : default, WindBlend);
        Posed(HoverMove);
        _overlaySeq = seq;
        _overlayMove = Move.Punch;
        _overlayUntil = Clock.Now + 600.0;
    }

    // The right hand strike that throws a held thug, falling back to any right or mirrored one.
    private static Blow? ThrowBlow(List<Blow> blows) =>
        blows.FirstOrDefault(b => b.Hand == "Right" && !b.Strong) ?? blows.FirstOrDefault(b => b.Hand == "Right")
        ?? blows.FirstOrDefault(b => b.Mirrorable);

    // Lets the held wind up play on through the hit. False when there's no wind up.
    private bool ReleaseWind(Swing swing, float charge)
    {
        if (_windSeq is not { IsValid: true } seq || _windBlow is not { } blow)
            return false;

        float natural = seq.SequenceLength / _windRate;
        float contact = blow.Contact * natural;
        float at = contact * CockedShare * Math.Clamp(charge, 0f, 1f);
        seq.RateScale = _windRate * HeavyRate;

        swing.Blow = blow;
        swing.Since = Clock.Now;
        swing.ContactAt = MathF.Max(0.05f, (contact - at) / HeavyRate);
        // Before the idle comes round again.
        float left = (natural - at) / HeavyRate;
        swing.Seconds = MathF.Min(swing.ContactAt + HeavyFollow, MathF.Max(swing.ContactAt + 0.1f, left - 0.05f));
        _overlaySeq = seq;
        _overlayMove = Move.Punch;
        _overlayUntil = Clock.Now + swing.Seconds + 0.5;
        _lastBlow = blow;
        return true;
    }

    // Holds the wind up in place, every frame in case the game put the rate back.
    private void HoldWind()
    {
        if (_windSeq is { IsValid: true } seq && seq.RateScale != 0f)
            seq.RateScale = 0f;
    }

    // Puts the strike anim's rate back, since the anim is the game's own.
    private void ThawWind()
    {
        if (_windSeq is { IsValid: true } seq)
            seq.RateScale = _windRate;
        _windSeq = null;
    }
}
