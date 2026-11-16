using BmSDK;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// The Flash's crackle is the game's own green electrocution effect, turned gold while he's on. Its two arc layers
// take the game's yellow arc material and its green glow and spark layers are switched off. The effect is shared,
// so the game's own use of it turns gold too, and it all goes back whenever the Flash isn't on.
internal static class GoldCrackle
{
    public const string Effect = "PFX_Batman.fx_playerElectricution_p";
    private const string Yellow = "XFX_Yanni.Materials.Other.INST_Electric_Ark_PREM_YEL";
    private const string Key = "eclipse.gold";
    private static readonly int[] Arcs = { 1, 5 };
    private static readonly int[] Green = { 0, 3, 4, 7 };

    private static readonly List<(ParticleModuleRequired Req, MaterialInterface? Was)> _swapped = new();
    private static readonly List<(ParticleLODLevel Lod, bool Was)> _off = new();

    public static bool On => _swapped.Count > 0 || _off.Count > 0;

    public static void Apply()
    {
        if (On || Fx.System(Effect) is not { } ps || Game.FindObject<MaterialInstanceConstant>(Yellow) is not { IsValid: true } yellow)
            return;

        Safe.Run("gold crackle", () =>
        {
            foreach (int e in Arcs)
            {
                foreach (ParticleLODLevel? lod in Layer(ps, e))
                {
                    if (lod?.RequiredModule is { IsValid: true } req)
                    {
                        _swapped.Add((req, req.Material));
                        req.Material = yellow;
                    }
                }
            }

            foreach (int e in Green)
            {
                foreach (ParticleLODLevel? lod in Layer(ps, e))
                {
                    if (lod is { IsValid: true })
                    {
                        _off.Add((lod, lod.bEnabled));
                        lod.bEnabled = false;
                    }
                }
            }

            // What it was, by name, so a reload of the scripts can still put it back.
            Shared.Set(Key, string.Join("|", _swapped.Select(s => s.Was?.GetPathName(null) ?? "")) + ";"
                + string.Concat(_off.Select(o => o.Was ? '1' : '0')));
        });
    }

    public static void Restore()
    {
        if (!On)
            return;

        Safe.Run("green crackle", () =>
        {
            for (int i = _swapped.Count - 1; i >= 0; i--)
            {
                if (_swapped[i].Req.IsValid)
                    _swapped[i].Req.Material = _swapped[i].Was;
            }

            foreach ((ParticleLODLevel lod, bool was) in _off)
            {
                if (lod.IsValid)
                    lod.bEnabled = was;
            }
        });
        _swapped.Clear();
        _off.Clear();
        Shared.Set(Key, null);
    }

    // Scripts reloaded while it was gold. The last load's changes are undone from what it noted.
    public static void Recover()
    {
        if (Shared.Get(Key) is not string noted)
            return;
        Shared.Set(Key, null);
        if (Fx.System(Effect) is not { } ps)
            return;

        string[] parts = noted.Split(';');
        string[] mats = parts[0].Split('|');
        string enabled = parts.Length > 1 ? parts[1] : "";
        Safe.Run("green crackle recover", () =>
        {
            int m = 0;
            foreach (int e in Arcs)
            {
                foreach (ParticleLODLevel? lod in Layer(ps, e))
                {
                    if (lod?.RequiredModule is not { IsValid: true } req || m >= mats.Length)
                        continue;
                    if (mats[m++] is { Length: > 0 } path && Game.FindObject<MaterialInterface>(path) is { IsValid: true } was)
                        req.Material = was;
                }
            }

            int b = 0;
            foreach (int e in Green)
            {
                foreach (ParticleLODLevel? lod in Layer(ps, e))
                {
                    if (lod is { IsValid: true } && b < enabled.Length)
                        lod.bEnabled = enabled[b++] == '1';
                }
            }
        });
    }

    private static IEnumerable<ParticleLODLevel?> Layer(ParticleSystem ps, int emitter) =>
        emitter < ps.Emitters.Count && ps.Emitters[emitter] is { IsValid: true } em ? em.LODLevels : Enumerable.Empty<ParticleLODLevel?>();
}
