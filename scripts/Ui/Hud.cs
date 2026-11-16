using System.Numerics;
using BmSDK.BmGame;
using static Trinity.Eclipse.Gfx;

namespace Trinity.Eclipse;

// Each hero's own HUD in the bottom left, up whenever his powers are, with the control legend above it.
// Superman has a shield with his flight readings and a legend cut like crystal. The Flash's is in FlashHud.
// Both hide with the game's own HUD and in photo mode.
internal static partial class Hud
{
    private const float Metre = 100f;
    private const double HintSeconds = 6.0;
    private const float RowH = 26f, RowGap = 3f, Label = 0.52f, Glyph = 0.46f;

    private static float _shown, _legend;
    private static float _speedShown = -1f;
    private static double _hintUntil, _changedUntil;
    private static string _legendWas = "";

    public static bool Showing => _shown > 0.01f || Wanted;

    private static bool Wanted => Opt.Hud.On && Opt.Powers.On && Visible();

    // Shows the legend for a while even when it's turned off, like after switching hero.
    public static void ShowControls() => _hintUntil = Clock.Now + HintSeconds;

    // What the legend says, less the seconds counting down, so a change shows it again.
    private static string LegendState()
    {
        var sb = new System.Text.StringBuilder(Legend.Mode);
        foreach (Entry e in Legend.Now())
            sb.Append('|').Append(e.Label).Append(e.Ready ? '1' : '0').Append(e.Active ? '1' : '0').Append(e.Short ? '1' : '0');
        return sb.ToString();
    }

    // Batman on his feet or flying, with the game's own HUD up.
    private static bool Visible()
    {
        if (!World.InGame || World.Paused || World.Pc is not { } pc || Hooks.InCinematic(pc))
            return false;
        if (pc.myHUD is { IsValid: true, bShowHUD: false } || pc.IsPhotoMode())
            return false;
        return World.Pawn is RPawnPlayer { Health: > 0, bHidden: false } p && p.DrivenVehicle is not { IsValid: true };
    }

    public static void Draw(BmSDK.Engine.Canvas canvas)
    {
        bool want = Wanted;
        _shown = Follow(_shown < 0f ? 0f : _shown, want ? 1f : 0f, 8f);
        if (want && Opt.Legend.On && Opt.LegendFade.On && LegendState() is var state && state != _legendWas)
        {
            _legendWas = state;
            _changedUntil = Clock.Now + HintSeconds;
        }

        bool shown = Opt.Legend.On && (!Opt.LegendFade.On || Clock.Now < _changedUntil);
        bool legend = want && (shown || Clock.Now < _hintUntil);
        _legend = Follow(_legend < 0f ? 0f : _legend, legend ? 1f : 0f, 8f);
        if (_shown < 0.01f)
            return;

        float bottom = H - S(48f);
        if (Heroes.Active == Hero.Flash)
        {
            FlashHud(canvas, bottom);
        }
        else
        {
            float top = ShieldGauge(bottom, _shown);
            if (_legend > 0.01f)
                SupermanLegend(top - S(26f), _legend * _shown);
        }

        Alpha = 1f;
    }

    // The shield with what he's doing, and his height and speed beside it in flight. Returns its top.
    private static float ShieldGauge(float bottom, float a)
    {
        Alpha = a;
        float size = S(104f);
        float x = S(48f), y = bottom - size;
        bool flying = Suit.I.Flying;
        Cut(x - S(16f), y - S(16f), S(330f), size + S(32f), S(18f), new Rgba(6, 14, 24), 0.62f * a);
        Emblems.Shield(x, y, size, 0.9f, filled: false);
        TextCentre(x + size * 0.5f, y + size * 0.42f, flying ? Suit.I.FlightWord : "GROUND", Emblems.Ice, 0.6f);

        float tx = x + size + S(16f);
        if (!flying)
        {
            Spaced(tx, y + S(30f), "SUPERMAN", Emblems.Ice, 0.62f, 4f);
            Spaced(tx, y + S(56f), Suit.I.Firing ? "HEAT VISION" : "ON FOOT", Suit.I.Firing ? Emblems.Cape : Emblems.Sky, 0.5f, 3f);
            return y;
        }

        float height = Suit.I.AirHeight;
        Reading(tx, y + S(22f), "HEIGHT", height >= 0f ? Fmt.Whole(height / Metre) : "--", "M");
        Reading(tx, y + S(56f), "SPEED", Fmt.Whole(Suit.I.GaugeSpeed / Metre), "M/S");

        float k = Math.Clamp(Suit.I.GaugeSpeed / (Opt.BoostSpeed.Number * Metre), 0f, 1f);
        int lit = (int)MathF.Ceiling(k * 6f);
        for (int i = 0; i < 6; i++)
            Rect(tx + i * S(17f), y + S(84f), S(14f), S(4f), i < lit ? Emblems.Ice : Emblems.Sky, i < lit ? 1f : 0.25f);
        return y;
    }

    private static void Reading(float x, float cy, string what, string value, string unit)
    {
        Spaced(x, cy - TextH(0.5f) * 0.5f, what, Emblems.Sky, 0.5f, 3f);
        float vx = x + S(66f);
        Text(vx, cy - TextH(1f) * 0.5f, value, Emblems.Ice, 1f);
        Spaced(vx + TextW(value, 1f) + S(6f), cy - TextH(0.5f) * 0.5f, unit, Emblems.Sky, 0.5f, 2f);
    }

    // Lines in ice, a diamond for each, the one in use lit red. Things he can't do right now are dimmed.
    private static void SupermanLegend(float bottom, float a)
    {
        Alpha = a;
        List<Entry> rows = Legend.Now();
        if (rows.Count == 0)
            return;

        (float promptW, float labelW) = Widths(rows);
        float x = S(48f), w = S(30f) + promptW + S(16f) + labelW + S(24f);
        float y = bottom - rows.Count * S(RowH + RowGap);
        float slide = (1f - a) * -S(30f);

        Spaced(x + slide, y - S(30f), "SUPERMAN  /  " + Legend.Mode, Emblems.Sky, 0.48f, 3f);
        Rect(x + slide, y - S(10f), w, MathF.Max(1f, S(1f)), Emblems.Sky, 0.35f);

        foreach (Entry e in rows)
        {
            float cy = y + S(RowH) * 0.5f;
            float rx = x + slide;
            Rect(rx, y, w, S(RowH), e.Active ? new Rgba(80, 14, 12) : new Rgba(6, 14, 24), e.Active ? 0.8f : 0.5f);
            if (e.Active)
                Rect(rx, y, w, MathF.Max(1f, S(1f)), Emblems.Ice, 0.9f);

            float on = e.Ready ? 1f : 0.35f;
            Shapes.Diamond(rx + S(14f), cy, S(4.5f), e.Active ? Emblems.Cape : Emblems.Sky, on);
            Legend.Prompt(rx + S(30f), cy, e, Glyph, on);
            Spaced(rx + S(30f) + promptW + S(16f), cy - TextH(Label) * 0.5f, e.Label, e.Ready ? Emblems.Ice : Palette.Dim, Label, 2f);
            y += S(RowH + RowGap);
        }
    }

    private static (float Prompt, float Label) Widths(List<Entry> rows)
    {
        float p = 0f, l = 0f;
        foreach (Entry e in rows)
        {
            p = MathF.Max(p, Legend.PromptWidth(e, Glyph));
            l = MathF.Max(l, SpacedW(e.Label, Label, 2f));
        }

        return (p, l);
    }
}
