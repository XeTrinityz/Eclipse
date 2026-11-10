using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Random idles are switched off on every pose while flying and put back exactly on landing.
internal sealed partial class Suit
{
    private const string IdlesOffKey = "eclipse.idlesoff";
    private const float NoIdleTime = 1e6f;

    private readonly record struct IdleWas(nint Config, int Index, nint Set, nint AddSet, float Min, float Max);

    private readonly List<IdleWas> _idlesWas = new();
    private bool _autoOffWas;
    private nint _autoOffNode;

    private static List<RPoseConfig> PoseConfigsOf(RPawnPlayer pawn)
    {
        var configs = new List<RPoseConfig>();
        var seen = new HashSet<nint>();
        void From(TArray<RPoseConfig>? list)
        {
            if (list == null)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is { IsValid: true } c && seen.Add(c.Ptr))
                    configs.Add(c);
            }
        }

        From(pawn.AnimationConfig is { IsValid: true } ac ? ac.PoseConfigs : null);
        From(pawn.UnarmedWeaponConfig is { IsValid: true } uw ? uw.PoseConfigs : null);
        From(pawn.OverrideUnarmedWeaponConfig is { IsValid: true } ow ? ow.PoseConfigs : null);
        return configs;
    }

    private void StartIdleHold(RPawnPlayer pawn)
    {
        IdlesOff(pawn);
        pawn.StopIdleOverlay();
        if (pawn.Anim is { IsValid: true } anim)
        {
            anim.StopAllOverlaysInstantly();
            _autoOffNode = anim.Ptr;
            _autoOffWas = anim.ForceAnimatedAutomaticTransitionsOff;
            anim.ForceAnimatedAutomaticTransitionsOff = true;
        }
    }

    private void IdlesOff(RPawnPlayer pawn)
    {
        RestoreIdles();
        foreach (RPoseConfig config in PoseConfigsOf(pawn))
        {
            Span<RPoseConfig.FPose> poses = config.Poses.AsSpan();
            for (int i = 0; i < poses.Length; i++)
            {
                ref RPoseConfig.FPose pose = ref poses[i];
                RPoseConfig.FRandomIdleOverlays idles = pose.RandomIdleOverlays;
                AnimSet? set = idles.AnimSet, add = idles.AdditiveAnimSet;
                if (set is not { IsValid: true } && add is not { IsValid: true })
                    continue;

                _idlesWas.Add(new IdleWas(config.Ptr, i, set?.Ptr ?? 0, add?.Ptr ?? 0, idles.MinTime, idles.MaxTime));
                idles.AnimSet = null!;
                idles.AdditiveAnimSet = null!;
                idles.MinTime = NoIdleTime;
                idles.MaxTime = NoIdleTime;
                pose.RandomIdleOverlays = idles;
            }
        }

        SaveIdlesOff();
    }

    // Puts back every pose's random idles from the list, live or saved before a reload.
    private void RestoreIdles()
    {
        if (_idlesWas.Count == 0)
            return;

        foreach (IdleWas was in _idlesWas)
        {
            if (FromPtr<RPoseConfig>(was.Config) is not { IsValid: true } config)
                continue;

            Span<RPoseConfig.FPose> poses = config.Poses.AsSpan();
            if (was.Index >= poses.Length)
                continue;

            ref RPoseConfig.FPose pose = ref poses[was.Index];
            RPoseConfig.FRandomIdleOverlays idles = pose.RandomIdleOverlays;
            idles.AnimSet = FromPtr<AnimSet>(was.Set)!;
            idles.AdditiveAnimSet = FromPtr<AnimSet>(was.AddSet)!;
            idles.MinTime = was.Min;
            idles.MaxTime = was.Max;
            pose.RandomIdleOverlays = idles;
        }

        _idlesWas.Clear();
        SaveIdlesOff();
    }

    private void SaveIdlesOff()
    {
        var flat = new long[_idlesWas.Count * 6];
        for (int i = 0; i < _idlesWas.Count; i++)
        {
            IdleWas w = _idlesWas[i];
            flat[i * 6] = w.Config;
            flat[i * 6 + 1] = w.Index;
            flat[i * 6 + 2] = w.Set;
            flat[i * 6 + 3] = w.AddSet;
            flat[i * 6 + 4] = BitConverter.SingleToInt32Bits(w.Min);
            flat[i * 6 + 5] = BitConverter.SingleToInt32Bits(w.Max);
        }

        Shared.Set(IdlesOffKey, flat);
    }

    // Scripts reloaded mid flight. The old list only survives in the shared state.
    private void RecoverIdles()
    {
        if (Shared.Get(IdlesOffKey) is not long[] { Length: > 0 } flat)
            return;

        _idlesWas.Clear();
        for (int i = 0; i + 5 < flat.Length; i += 6)
        {
            _idlesWas.Add(new IdleWas((nint)flat[i], (int)flat[i + 1], (nint)flat[i + 2], (nint)flat[i + 3],
                BitConverter.Int32BitsToSingle((int)flat[i + 4]), BitConverter.Int32BitsToSingle((int)flat[i + 5])));
        }

        RestoreIdles();
    }

    private static unsafe T? FromPtr<T>(nint ptr) where T : GameObject
    {
        if (ptr == 0)
            return null;
        nint held = ptr;
        return MarshalUtil.ToManaged<T>((IntPtr)(&held));
    }

    private void HoldIdles(RPawnPlayer pawn)
    {
        if (pawn.Anim is not { IsValid: true } anim)
            return;

        // A strike of ours plays as a transition, which this would stop.
        if (OverlayOn)
            return;

        if (!anim.ForceAnimatedAutomaticTransitionsOff)
            anim.ForceAnimatedAutomaticTransitionsOff = true;

        bool fidget = anim.IdleOverlays.PlayingOverlays.Count > 0 || anim.WeaponOverlays.PlayingOverlays.Count > 0
            || anim.AdditiveOverlays.PlayingOverlays.Count > 0 || pawn.IsInAnyAutomaticTransition(default, out _);
        if (!fidget)
            return;

        anim.StopAllOverlaysInstantly();
        pawn.StopIdleOverlay();
    }

    private void EndIdleHold(RPawnPlayer pawn)
    {
        Safe.Run("random idles back", RestoreIdles);
        Safe.Run("legs rate back", FreeLegs);
        Safe.Run("idle overlays back", pawn.AllowIdleOverlaysAgain);
        if (pawn.Anim is { IsValid: true } anim && anim.Ptr == _autoOffNode)
            anim.ForceAnimatedAutomaticTransitionsOff = _autoOffWas;
        _autoOffNode = 0;
    }
}
