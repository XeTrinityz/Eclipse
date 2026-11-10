using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Flight combat anims. Blended on his upper body in body space, since hip space twisted his torso.
internal sealed partial class Suit
{
    internal enum Move
    {
        Punch,
        Hold,
        Throw,
        Grab,
        Neck,
        Beam,
    }

    private const float ActBlend = 0.12f;

    private AnimSequence? _overlaySeq;
    private Move _overlayMove;
    private double _overlayUntil;

    private bool OverlayOn => _overlaySeq != null && Clock.Now < _overlayUntil + 0.1;

    // The punch has none here. It comes from his strike table, see Suit.Blows.
    private static string? AnimName(Move move) => move switch
    {
        Move.Hold => Rig.HoldAnim,
        Move.Throw => Rig.ThrowAnim,
        Move.Grab => Rig.GrabAnim,
        Move.Neck => Rig.NeckAnim,
        Move.Beam => Rig.BeamAnim,
        _ => null,
    };

    private Move HoldMove => _heldKind == Kind.Thug ? Move.Neck : Move.Hold;

    private static bool IsHold(Move move) => move is Move.Hold or Move.Neck;

    // Fingers to his cowl while beaming in a hover with his hands free.
    private bool BeamPose =>
        _firing && !Holding && _swing == null && !_charging && !Rushing && !_slamming && !_fm.Boosting
        && _autoFast != true && _fm.Velocity.Length() < _fm.MoveSpeed * 1.4f;

    private AnimSequence? AnimFor(Move move) => AnimName(move) is { } name ? FindAnim(name) : null;

    private static float Length(AnimSequence seq) => seq.SequenceLength / MathF.Max(seq.RateScale, 0.01f);

    private bool PlayMove(RPawnPlayer pawn, Move move)
    {
        if (AnimFor(move) is not { IsValid: true } seq)
            return false;
        return move == Move.Throw ? PlayOnce(pawn, seq, move) : PlayUpper(pawn, seq, move);
    }

    // Each pose's cape state is put back while idle, since flight moves left the cape stiff.
    private readonly Dictionary<string, string> _capeFor = new();
    private bool _capeClean;
    private double _capeQuietSince;
    private Every _capeCheck;
    private const double CapeSettle = 0.4;

    private void ResetCape()
    {
        _capeFor.Clear();
        _capeClean = true;
        _capeQuietSince = Clock.Now;
    }

    private void CapeStep(RPawnPlayer pawn)
    {
        if (pawn is not RPawnPlayerBmBase bm || _autoSpec is not { } spec)
            return;

        bool busy = OverlayOn || _swing != null || _charging || Holding || _rush != null;
        if (busy)
        {
            _capeClean = false;
            _capeQuietSince = Clock.Now;
            return;
        }

        if (!_capeCheck.Elapsed(0.25) || Clock.Now - _capeQuietSince < CapeSettle)
            return;

        Safe.Run("cape state", () =>
        {
            string now = bm.GetCurrentCapeStateName().ToString();
            if (!_capeFor.TryGetValue(spec, out string? good))
            {
                if (_capeClean)
                    _capeFor[spec] = now;
                return;
            }

            if (now == good)
                return;

            bm.CapeStartInState(new FName(good), 0f, 0f, bm.CapeGetPoseMirroredType());
        });
    }

    // Played as a transition so it can't loop early. His pose goes back on in OverlayStep.
    private bool PlayOnce(RPawnPlayer pawn, AnimSequence seq, Move move)
    {
        _autoSpec = HoverKey();
        _autoFast = false;
        (string stance, string weapon) = Stance();
        if (pawn.Anim is { IsValid: true } anim)
            anim.ForceAnimatedAutomaticTransitionsOff = false;
        var transition = new RPawnCharacter.FCustomTransitionConfig { Config = BodyConfig(seq, full: true) };
        pawn.ForceChangePose(new FName(stance), new FName(weapon), default, false, transition, default, default, Length(seq));
        Posed(new FName(stance));
        _overlaySeq = seq;
        _overlayMove = move;
        _overlayUntil = Clock.Now + Length(seq);
        return true;
    }

    // Played as a looping idle on his upper body, over the legs of his pose.
    private bool PlayUpper(RPawnPlayer pawn, AnimSequence seq, Move move)
    {
        // Done hovering. Swap him back, since the hold kept the pose keeper off.
        _autoSpec = HoverKey();
        _autoFast = false;

        (string stance, string weapon) = Stance();
        var idle = new RPawnCharacter.FCustomIdleConfig { Config = BodyConfig(seq) };
        pawn.ForceChangePose(new FName(stance), new FName(weapon), default, false, default, idle, default, ActBlend);
        _overlaySeq = seq;
        _overlayMove = move;
        // The holds and the beam pose last until he lets go. A grab on its own runs once.
        _overlayUntil = IsHold(move) || move == Move.Beam ? double.MaxValue : Clock.Now + Length(seq);
        return true;
    }

    private (string Stance, string Weapon) Stance() =>
        _autoSpec != null && NamedPoses.TryGetValue(_autoSpec, out var named) ? named : ("StandingNoTurn", "Relaxed");

    // The anim on his upper body over the legs of his pose, or on all of him.
    private RAnimConfig.FCustomAnimConfig BodyConfig(AnimSequence seq, bool full = false)
    {
        (string stance, string weapon) = Stance();
        AnimSequence? legs = full ? null : LegsFor(stance, weapon);
        return legs != null
            ? new RAnimConfig.FCustomAnimConfig
            {
                FullBodyAnim = legs.SequenceName,
                CustomFullBodyAnim = legs,
                UpperBodyAnim = seq.SequenceName,
                CustomUpperBodyAnim = seq,
                ModelspaceUpperBody = true,
            }
            : new RAnimConfig.FCustomAnimConfig { FullBodyAnim = seq.SequenceName, CustomFullBodyAnim = seq };
    }

    // The grab plays once as a transition into the hold. False if either anim is missing.
    private bool PlayGrabInto(RPawnPlayer pawn, Move grab, Move hold)
    {
        if (AnimFor(grab) is not { IsValid: true } lead || AnimFor(hold) is not { IsValid: true } held)
            return false;

        _autoSpec = HoverKey();
        _autoFast = false;
        (string stance, string weapon) = Stance();
        if (pawn.Anim is { IsValid: true } anim)
            anim.ForceAnimatedAutomaticTransitionsOff = false;
        var transition = new RPawnCharacter.FCustomTransitionConfig { Config = BodyConfig(lead) };
        var idle = new RPawnCharacter.FCustomIdleConfig { Config = BodyConfig(held) };
        pawn.ForceChangePose(new FName(stance), new FName(weapon), default, false, transition, idle, default, Length(lead));
        Posed(new FName(stance));

        // The hold is on once the transition ends, so treat the whole thing as the hold.
        _overlaySeq = held;
        _overlayMove = hold;
        _overlayUntil = double.MaxValue;
        return true;
    }

    // The hover legs while standing, or the stance's own idle like the dive.
    private AnimSequence? LegsFor(string stance, string weapon)
    {
        string name = IsStanding(_autoSpec) ? Rig.HoverLegs : _idleAnimOf.TryGetValue(stance + "/" + weapon, out string? idle) ? idle : "";
        return name.Length > 0 ? FindAnim(name) : null;
    }

    // Each flight frame. Ends a finished anim, puts his pose back and keeps the hold on.
    private void OverlayStep(RPawnPlayer pawn)
    {
        CapeStep(pawn);
        if (_overlaySeq == null)
        {
            if (Holding && !_throwing)
                PlayMove(pawn, HoldMove);
            else if (BeamPose)
                PlayMove(pawn, Move.Beam);
            return;
        }

        bool done = _overlayMove == Move.Beam ? !BeamPose : IsHold(_overlayMove) ? !Holding || _throwing : Clock.Now >= _overlayUntil;
        if (!done)
            return;

        // His hand comes down from his cowl, it doesn't snap.
        if (_overlayMove == Move.Beam)
            _overlayCut = true;
        _overlaySeq = null;
        if (Holding && !_throwing && PlayMove(pawn, HoldMove))
            return;
        if (_autoSpec != null)
        {
            // A throw cut short after it lets go blends back, rather than snapping out of the swing.
            if (_overlayCut)
                ApplySpec(pawn, _autoSpec, force: false, SettleBlend);
            else
                ApplySpec(pawn, _autoSpec, force: true);
        }

        _overlayCut = false;
    }

    // The throw anim runs long after the release, so he goes back to the hover shortly after.
    private const double ThrowFollow = 0.45;
    private bool _overlayCut;

    private void CutThrow()
    {
        if (_overlaySeq == null || _overlayMove != Move.Throw)
            return;
        double until = Clock.Now + ThrowFollow;
        if (until < _overlayUntil)
        {
            _overlayUntil = until;
            _overlayCut = true;
        }
    }

    // Flight ending puts his pose right itself, so this only forgets the anim.
    private void StopOverlay() => _overlaySeq = null;
}
