using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Hover pose when slow, fast pose while boosting. Falls back to a loaded stance to avoid a T-pose.
internal sealed partial class Suit
{
    private static readonly Dictionary<string, (string Move, string Weapon)> NamedPoses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["standing"] = ("StandingNoTurn", "Relaxed"),
        ["falling"] = ("Falling", "Relaxed"),
        ["line_launcher_dive"] = ("LineLauncherDive", "None"),
        ["line_launcher_loop"] = ("LineLauncherLoop", "Relaxed"),
        ["cape_glide"] = ("CapeGlide", "Relaxed"),
        ["glide_boost"] = ("GlideBoost", "Relaxed"),
    };

    // The glide poses are chest down, so they aren't fallbacks for the hover.
    private static readonly string[] HoverFallbacks = { "standing", "falling" };

    private const double AnimLookAgain = 5.0;

    // A tap of boost shouldn't flick him through both poses.
    private const double FastDelay = 0.12;

    // Each stance's idle anim name from the pose configs, and the loaded anims found.
    private readonly Dictionary<string, string> _idleAnimOf = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AnimSequence> _loadedAnims = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _missingSince = new(StringComparer.OrdinalIgnoreCase);
    private nint _scannedPawn;

    private bool _posed;
    private bool? _autoFast;
    private string? _autoSpec;
    private string? _wantMove;
    private double _poseCheckAt;
    private double _fastFlipSince = -1.0;

    private void ResetPoses()
    {
        _posed = false;
        _autoFast = null;
        _autoSpec = null;
        _wantMove = null;
        _fastFlipSince = -1.0;
    }

    // Swaps to the fast pose once boost gets going, and back near hover speed.
    private void AutoPose(RPawnPlayer pawn)
    {
        // A combat anim is on. His pose goes back on when it ends.
        if (OverlayOn)
            return;

        float speed = _fm.Velocity.Length();
        bool wasFast = _autoFast == true;
        bool boost = _fm.Boosting || Rushing || (_slamming && !_slamFlaring);
        bool fastNow = !_slamFlaring && (wasFast
            ? boost || speed > _fm.MoveSpeed * 1.4f
            : boost && speed > _fm.MoveSpeed * 0.5f);
        bool fast = wasFast;
        if (fastNow != wasFast)
        {
            if (_fastFlipSince < 0.0)
                _fastFlipSince = Clock.Now;
            if (_autoFast == null || Clock.Now - _fastFlipSince >= FastDelay)
                fast = fastNow;
        }
        else
        {
            _fastFlipSince = -1.0;
        }

        bool swapped = _autoFast != null && fast != wasFast;
        _autoFast = fast;
        string spec = fast ? Rig.FastPose : HoverKey();
        if (!swapped && spec == _autoSpec)
            return;

        if (swapped)
        {
            PoseSwap(fast);
            if (fast && _fm.Boosting)
                BoostFeel(pawn);
        }

        ApplySpec(pawn, spec, force: false);
    }

    private string HoverKey()
    {
        const string chosen = Rig.HoverPose;
        if (!NamedPoses.ContainsKey(chosen) || AnimLoaded(chosen))
            return chosen;

        foreach (string key in HoverFallbacks)
        {
            if (key != chosen && AnimLoaded(key))
                return key;
        }

        return "standing";
    }

    private bool AnimLoaded(string poseKey)
    {
        if (!NamedPoses.TryGetValue(poseKey, out (string Move, string Weapon) p) || !_idleAnimOf.TryGetValue(p.Move + "/" + p.Weapon, out string? anim))
            return true;
        return FindAnim(anim) != null;
    }

    // Puts on a pose by name. Forced is instant, otherwise it crossfades.
    private void ApplySpec(RPawnPlayer pawn, string spec, bool force, float blend = -1f)
    {
        _autoSpec = spec;
        if (!NamedPoses.TryGetValue(spec, out (string Move, string Weapon) named))
        {
            ClearPose(pawn);
            return;
        }

        if (blend < 0f)
            blend = force ? 0f : PoseBlend;
        if (!IsStanding(spec) || !StandWithLegs(pawn, named.Move, named.Weapon, blend))
            SetStance(pawn, named.Move, named.Weapon, blend);
    }

    private void SetStance(RPawnPlayer pawn, FName move, FName weapon, float blend)
    {
        if (blend <= 0f)
            pawn.ForceChangePose(move, weapon);
        else
            pawn.ForceChangePose(move, weapon, default, false, default, default, default, blend);
        Posed(move);
    }

    private void Posed(FName move)
    {
        _posed = true;
        _wantMove = move.ToString();
        _poseCheckAt = Clock.Now + 0.3;
    }

    // Takes off what we forced and puts him in his falling stance.
    private void ClearPose(RPawnPlayer pawn)
    {
        if (!_posed)
            return;

        _posed = false;
        _wantMove = null;
        (string Move, string Weapon) fall = NamedPoses["falling"];
        Safe.Run("pose clear", () => pawn.ForceChangePose(fall.Move, fall.Weapon));
    }

    // The game may put its own stance back. If ours doesn't stick, set it again.
    private void HoldPose(RPawnPlayer pawn)
    {
        if (!_posed || _wantMove == null || _autoSpec == null || Clock.Now < _poseCheckAt)
            return;

        // Our punch is a transition into the hover, and putting the pose on would cut it off.
        if (OverlayOn)
            return;

        _poseCheckAt = Clock.Now + 0.25;
        string now = pawn.GetMovementStance().ToString();
        if (now == _wantMove)
            return;

        ApplySpec(pawn, _autoSpec, force: true);
    }

    // Learns each stance's idle anim from his pose configs, once per pawn.
    private void ScanIdleAnims(RPawnPlayer pawn)
    {
        if (pawn.Ptr == _scannedPawn)
            return;

        _scannedPawn = pawn.Ptr;
        _idleAnimOf.Clear();
        foreach (RPoseConfig config in PoseConfigsOf(pawn))
        {
            TArray<RPoseConfig.FPose> poses = config.Poses;
            for (int i = 0; i < poses.Count; i++)
            {
                RPoseConfig.FPose p = poses[i];
                _idleAnimOf.TryAdd(p.MovementStance + "/" + p.WeaponStance, p.Idle.FullBodyAnim.ToString());
            }
        }
    }

    // The set's own name from an anim's path, like BM_Attack.
    private static string SetName(string path)
    {
        int colon = path.LastIndexOf(':');
        string set = colon > 0 ? path[..colon] : path;
        int dot = set.LastIndexOf('.');
        return dot >= 0 ? set[(dot + 1)..] : set;
    }

    // Finds a loaded anim by name, preferring Batman's. Misses are tried again later as anims load.
    private AnimSequence? FindAnim(string name)
    {
        if (name.Length == 0 || name == "None")
            return null;
        if (_loadedAnims.TryGetValue(name, out AnimSequence? known) && known.IsValid)
            return known;
        if (_missingSince.TryGetValue(name, out double at) && Clock.Now - at < AnimLookAgain)
            return null;

        // Paired anims share a name, so his own set wins, then a Batman package, then anything.
        AnimSequence? found = null;
        int best = -1;
        foreach (AnimSequence seq in Game.FindObjects<AnimSequence>())
        {
            if (!seq.IsValid || seq.IsClassDefaultObject || !string.Equals(seq.SequenceName.ToString(), name, StringComparison.OrdinalIgnoreCase))
                continue;

            string path = seq.GetPathName(null);
            string set = SetName(path);
            int score = (path.Contains("Batman", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                + (set.StartsWith("BM_", StringComparison.OrdinalIgnoreCase) ? 2 : 0)
                - (set.Contains("_BM", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            if (score > best)
            {
                best = score;
                found = seq;
            }
        }

        if (found == null)
        {
            _missingSince[name] = Clock.Now;
            _loadedAnims.Remove(name);
            return null;
        }

        _missingSince.Remove(name);
        _loadedAnims[name] = found;
        return found;
    }
}
