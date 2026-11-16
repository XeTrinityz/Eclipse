using System.Numerics;
using BmSDK.BmGame;
using BmSDK.Engine;
using static Trinity.Eclipse.Gfx;

namespace Trinity.Eclipse;

// The Flash's HUD. The Speed Force dial in the bottom left with his move rows above it, one banner up top for
// every timed move, the screen edges in slow time and at speed, and the marks his moves leave in the world.
internal static partial class Hud
{
    private const float MetaScale = 0.5f, HeadScale = 0.54f, NameScale = 0.95f, ModeScale = 0.6f, InfoScale = 0.5f;
    // Share of top speed where speed vision starts, as the run has it.
    private const float VisionMark = 0.6f;
    private static float _dialW;
    private static readonly Rgba Panel = new(26, 6, 9);
    private static readonly Rgba RowFill = new(40, 8, 12);
    private static readonly Rgba Muted = new(217, 201, 180);
    private static readonly Rgba LightGold = new(255, 224, 138);
    private static readonly Rgba Coral = new(224, 122, 95);
    private static readonly Rgba Ink = new(29, 5, 8);
    private static readonly Rgba White = new(255, 255, 255);

    private static void FlashHud(BmSDK.Engine.Canvas canvas, float bottom)
    {
        Suit s = Suit.I;
        Alpha = _shown;
        if (s.Slowing)
            EdgeTint();
        else if (s.Running && Opt.SpeedVision.On && s.VisionNow > 0.05f)
            Streaks(s.VisionNow);

        float top = Dial(bottom, _shown);
        if (_legend > 0.01f)
            FlashLegend(top - S(26f), _legend * _shown);
        FlashBanner();

        Alpha = _shown;
        WorldView.Begin(canvas);
        if (!WorldView.Ready)
            return;
        if (s.Blitzing)
            BlitzMarks();
        else if (s.Slowing)
            KeptMarks();
        else if (!s.Twisting && s.RewindSpot is { } back)
            RewindMark(back);
        if (s.BoltAim is { } aim)
            BoltRing(aim, s.BoltCharge);
    }

    // Speed round the outside with a gold spark at its edge, the charges as a ring inside, the lightning charge
    // round the middle. His bolt in the centre when he's still. Returns its top.
    private static float Dial(float bottom, float a)
    {
        Alpha = a;
        Suit s = Suit.I;
        float speed = s.Running ? s.GaugeSpeed / Metre : 0f;
        _speedShown = Follow(_speedShown, speed, 8f);
        float f = Math.Clamp(_speedShown / MathF.Max(1f, Opt.RunSpeed.Number), 0f, 1f);

        float r = S(70f), cx = S(52f) + r, cy = bottom - r;
        float tx = cx + r + S(34f);
        float charges = s.Charges;
        float vision = s.VisionNow;
        bool showVision = vision > 0.05f && Opt.SpeedVision.On;
        string mode = Legend.Mode;
        string force = "SPEED FORCE  " + (int)charges + " OF " + Suit.ChargesMost;
        // The panel ends just past the widest line beside the dial.
        float column = MathF.Max(SpacedW("FLASH", NameScale, 5f) + S(24f), MathF.Max(SpacedW(mode, ModeScale, 4f),
            MathF.Max(SpacedW(force, InfoScale, 3f), showVision ? SpacedW("SPEED VISION", InfoScale, 3f) + S(80f) : 0f)));
        float panelLeft = S(30f), panelW = tx + column + S(36f) - panelLeft;
        float panelTop = cy - r - S(20f);
        Slant(panelLeft, panelTop, panelW, r * 2f + S(40f), S(14f), Panel, 0.66f);
        Slant(panelLeft + S(14f), panelTop, panelW, MathF.Max(1f, S(2f)), 0f, Emblems.Crimson, 1f);

        for (int i = 0; i < 36; i++)
            Shapes.Arc(cx, cy, r + S(6f), r + S(13f), i * 10f, i * 10f + 1.8f, Emblems.Cream, 0.4f);
        Shapes.Arc(cx, cy, r - S(12f), r, 135f, 405f, Emblems.Gold, 0.18f);
        if (f > 0.002f)
            Shapes.Arc(cx, cy, r - S(12f), r, 135f, 135f + 270f * f, Emblems.Crimson, 1f);
        if (f > 0.02f)
            Shapes.Arc(cx, cy, r - S(14f), r + S(2f), 132f + 270f * f, 135f + 270f * f, Emblems.Gold, 1f);
        float mark = 135f + 270f * VisionMark;
        Shapes.Arc(cx, cy, r + S(2f), r + S(14f), mark - 1.2f, mark + 1.2f, Emblems.Cream, 0.8f);

        for (int i = 0; i < Suit.ChargesMost; i++)
        {
            float a0 = 272f + 120f * i;
            float fill = Math.Clamp(charges - i, 0f, 1f);
            Shapes.Arc(cx, cy, r - S(26f), r - S(19f), a0, a0 + 116f, Emblems.Gold, 0.16f);
            if (fill > 0.01f)
                Shapes.Arc(cx, cy, r - S(26f), r - S(19f), a0, a0 + 116f * fill, Emblems.Gold, fill >= 1f ? 1f : 0.5f);
        }

        float bolt = s.BoltCharge;
        if (bolt > 0.01f)
            Shapes.Arc(cx, cy, r - S(34f), r - S(31f), 270f, 270f + 360f * bolt, Emblems.Cream, 1f);

        if (_speedShown < 1f)
        {
            float size = S(54f);
            Emblems.BoltOnly(cx - size * 0.51f, cy - size * 0.5f, size, Emblems.Gold, 1f);
        }
        else
        {
            TextCentre(cx, cy - S(4f), Fmt.Whole(_speedShown), Emblems.Cream, 1.3f);
            TextCentre(cx, cy + S(28f), "M/S", Emblems.Gold, 0.46f);
        }

        float nameW = Spaced(tx, cy - S(60f), "FLASH", Emblems.Cream, NameScale, 5f);
        Slant(tx + nameW + S(10f), cy - S(58f), S(10f), S(28f), S(8f), Emblems.Crimson, 1f);
        Spaced(tx, cy - S(20f), mode, Emblems.Gold, ModeScale, 4f);
        if (showVision)
        {
            float vw = Spaced(tx, cy + S(10f), "SPEED VISION", Emblems.Cream, InfoScale, 3f, vision);
            Slant(tx + vw + S(10f), cy + S(16f), S(70f) * vision, S(5f), S(3f), Emblems.Gold, vision);
        }

        // Up into the speed vision line's place when that isn't showing.
        Spaced(tx, cy + S(showVision ? 38f : 10f), force, Muted, InfoScale, 3f);
        _dialW = panelW;
        return panelTop;
    }

    // Slanted strips with a gold notch when ready, crimson and underlined in gold when in use, and a note at
    // the end saying why one isn't ready.
    private static void FlashLegend(float bottom, float a)
    {
        Alpha = a;
        List<Entry> rows = Legend.Now();
        if (rows.Count == 0)
            return;

        (float promptW, float labelW) = Widths(rows);
        float metaW = rows.Max(e => e.Meta.Length > 0 ? SpacedW(e.Meta, MetaScale, 2f) : 0f);
        float lean = S(10f), notch = S(4f), rowH = S(RowH);
        // As wide as the dial's panel below it, so their right edges line up.
        float x = S(30f), w = MathF.Max(notch + S(12f) + promptW + S(14f) + labelW + S(20f) + metaW + S(16f), _dialW);
        float y = bottom - rows.Count * S(RowH + RowGap);
        float slide = (1f - a) * -S(30f);

        float left = x + slide + lean;
        float headY = y - S(34f);
        float headW = Spaced(left, headY, "MOVES", Emblems.Gold, HeadScale, 4f);
        string mode = Legend.Mode;
        float modeW = SpacedW(mode, HeadScale, 3f);
        float right = x + slide + w + lean;
        Spaced(right - modeW, headY, mode, Muted, HeadScale, 3f);
        float ruleX = left + headW + S(12f);
        float ruleY = headY + TextH(HeadScale) * 0.5f - S(1f);
        Slant(ruleX, ruleY, MathF.Max(S(10f), right - modeW - S(12f) - ruleX), MathF.Max(2f, S(3f)), S(2f), Emblems.Crimson, 1f);

        foreach (Entry e in rows)
        {
            float rx = x + slide, cy = y + rowH * 0.5f;
            bool lit = e.Ready || e.Active;
            Slant(rx, y, w, rowH, lean, e.Active ? Emblems.Crimson : RowFill, e.Active ? 0.9f : lit ? 0.72f : 0.55f);
            Slant(rx, y, notch, rowH, lean, Emblems.Gold, lit ? 1f : 0.25f);
            if (e.Active)
                Slant(rx, y + rowH - S(2f), w, S(2f), lean * 2f / RowH, Emblems.Gold, 1f);

            float on = lit ? 1f : 0.4f;
            float px = rx + notch + S(12f) + lean * 0.5f;
            Legend.Prompt(px, cy, e, Glyph, on);
            Rgba text = e.Active ? White : e.Ready ? Emblems.Cream : Palette.Dim;
            Spaced(px + promptW + S(14f), cy - TextH(Label) * 0.5f, e.Label, text, Label, 2f);
            if (e.Meta.Length > 0)
            {
                Rgba note = e.Short ? Coral : e.Active ? LightGold : e.Ready ? Emblems.Gold : Palette.Dim;
                float mx = rx + w + lean * 0.5f - S(16f) - SpacedW(e.Meta, MetaScale, 2f);
                Spaced(mx, cy - TextH(MetaScale) * 0.5f, e.Meta, note, MetaScale, 2f);
            }

            y += S(RowH + RowGap);
        }
    }

    private static void FlashBanner()
    {
        Suit s = Suit.I;
        if (s.Blitzing)
        {
            bool aiming = s.BlitzAiming;
            string line = aiming
                ? "MARKED " + s.BlitzTotal
                : "STRIKE " + Math.Min(s.BlitzStruck + 1, s.BlitzTotal) + " OF " + s.BlitzTotal;
            Banner("BLITZ", aiming ? 1f : s.BlitzLeft, line);
        }
        else if (s.Slowing)
        {
            int hits = s.SlowHits, thugs = s.KeptThugs;
            string line = (hits == 1 ? "1 HIT KEPT" : hits + " HITS KEPT")
                + (thugs > 0 ? "  /  " + thugs + (thugs == 1 ? " THUG" : " THUGS") : "");
            Banner("SLOW TIME", s.SlowLeft, line);
        }
        else if (s.Twisting)
        {
            Banner("TORNADO", s.TornadoLeft, s.TornadoLifting > 0 ? "LIFTING " + s.TornadoLifting : "");
        }
    }

    // The same banner for every timed move: its name between crimson dashes, the time left, and a count.
    private static void Banner(string title, float left, string line)
    {
        Alpha = _shown;
        // Below the game's compass and the markers that hang off it.
        float cx = W * 0.5f, y = S(150f);
        const string over = "SPEED FORCE";
        Spaced(cx - SpacedW(over, 0.42f, 6f) * 0.5f, y, over, Muted, 0.42f, 6f);

        float ty = y + S(24f);
        float tw = SpacedW(title, 1.1f, 6f);
        Spaced(cx - tw * 0.5f, ty, title, Emblems.Cream, 1.1f, 6f);
        float dy = ty + TextH(1.1f) * 0.5f;
        Slant(cx - tw * 0.5f - S(56f), dy - S(2f), S(38f), S(4f), S(2f), Emblems.Crimson, 1f);
        Slant(cx + tw * 0.5f + S(18f), dy - S(2f), S(38f), S(4f), S(2f), Emblems.Crimson, 1f);

        float bw = S(520f), bx = cx - bw * 0.5f, by = ty + TextH(1.1f) + S(10f);
        Slant(bx, by, bw, S(9f), S(5f), Emblems.Gold, 0.18f);
        float fw = bw * Math.Clamp(left, 0f, 1f);
        if (fw > 1f)
        {
            Slant(bx, by, fw, S(9f), S(5f), Emblems.Crimson, 1f);
            Slant(bx + fw - S(4f), by, S(4f), S(9f), S(5f), Emblems.Gold, 1f);
        }

        if (line.Length > 0)
            Spaced(cx - SpacedW(line, 0.52f, 4f) * 0.5f, by + S(20f), line, Emblems.Gold, 0.52f, 4f);
    }

    // Slow time tints the screen's edges crimson and closes it in with thin bars.
    private static void EdgeTint()
    {
        float band = S(18f);
        for (int i = 0; i < 4; i++)
        {
            float k = 0.16f - 0.035f * i, at = band * i;
            Rect(0f, at, W, band, Emblems.Crimson, k);
            Rect(0f, H - at - band, W, band, Emblems.Crimson, k);
            Rect(at, 0f, band, H, Emblems.Crimson, k);
            Rect(W - at - band, 0f, band, H, Emblems.Crimson, k);
        }

        float bar = S(26f);
        Rect(0f, 0f, W, bar, Palette.Void, 0.85f);
        Rect(0f, bar, W, MathF.Max(1f, S(2f)), Emblems.Crimson, 1f);
        Rect(0f, H - bar, W, bar, Palette.Void, 0.85f);
        Rect(0f, H - bar - MathF.Max(1f, S(2f)), W, MathF.Max(1f, S(2f)), Emblems.Crimson, 1f);
    }

    // Streaks running in along the screen's sides while speed vision is on.
    private static readonly Rgba[] StreakColours = { Emblems.Gold, Emblems.Cream, Emblems.Crimson };

    private static void Streaks(float vision)
    {
        for (int i = 0; i < 8; i++)
        {
            bool leftSide = (i & 1) == 0;
            float y = H * (0.14f + 0.1f * i);
            float phase = (float)((Clock.Now * 1.4 + i * 0.37) % 1.0);
            float len = S(120f + 50f * (i % 3));
            float reach = S(240f) * phase;
            float x1 = leftSide ? reach : W - reach;
            float x0 = leftSide ? x1 - len : x1 + len;
            float tilt = S(14f) * (i % 2 == 0 ? 1f : -1f);
            Shapes.Line(x0, y + tilt, x1, y, MathF.Max(1f, S(i % 3 == 0 ? 3f : 2f)), StreakColours[i % 3], 0.55f * (1f - phase) * vision);
        }
    }

    // Numbered diamonds over the blitz's marks: gold with a cream ring for the next, crimson for the rest,
    // an outline once hit. A gold ring on the thug in his sights while marking.
    private static void BlitzMarks()
    {
        int n = 0;
        foreach ((RPawnVillain thug, bool hit, bool next) in Suit.I.BlitzMarks)
        {
            n++;
            if (!WorldView.ToScreen(thug.Location + Vector3.UnitZ * 120f, out System.Numerics.Vector2 p))
                continue;

            float r = S(next ? 20f : 15f);
            if (next)
                Shapes.DiamondLine(p.X, p.Y, r + S(7f), MathF.Max(1.5f, S(2f)), Emblems.Cream, 0.9f);
            if (hit)
                Shapes.DiamondLine(p.X, p.Y, r, MathF.Max(1.5f, S(2f)), Emblems.Cream, 0.35f);
            else
                Shapes.Diamond(p.X, p.Y, r, next ? Emblems.Gold : Emblems.Crimson, 1f);
            TextCentre(p.X, p.Y, n.ToString(), next ? Ink : Emblems.Cream, next ? 0.7f : 0.56f, hit ? 0.4f : 1f);
        }

        if (Suit.I.BlitzAiming && Suit.I.BlitzAim is { IsValid: true } aim
            && WorldView.ToScreen(aim.Location + Vector3.UnitZ * 120f, out System.Numerics.Vector2 at))
            Shapes.DiamondLine(at.X, at.Y, S(28f), MathF.Max(2f, S(3f)), Emblems.Gold, 1f);
    }

    // In slow time, the hits kept on each thug over his head.
    private static void KeptMarks()
    {
        foreach ((RPawnVillain thug, int hits) in Suit.I.KeptMarks)
        {
            if (!WorldView.ToScreen(thug.Location + Vector3.UnitZ * 120f, out System.Numerics.Vector2 p))
                continue;
            Shapes.Diamond(p.X, p.Y, S(20f), Emblems.Gold, 1f);
            TextCentre(p.X, p.Y, "x" + hits, Ink, 0.62f);
        }
    }

    // Where a rewind would take him: a gold outline with a stem down to the ground.
    private static void RewindMark(Vector3 spot)
    {
        if (!WorldView.ToScreen(spot + Vector3.UnitZ * 30f, out System.Numerics.Vector2 p))
            return;
        if (WorldView.ToScreen(spot - Vector3.UnitZ * 90f, out System.Numerics.Vector2 g))
            Shapes.Line(p.X, p.Y + S(16f), g.X, g.Y, MathF.Max(1f, S(2f)), Emblems.Gold, 0.35f);
        Shapes.DiamondLine(p.X, p.Y, S(13f), MathF.Max(1.5f, S(2f)), Emblems.Gold, 0.6f);
        const string word = "REWIND";
        Spaced(p.X - SpacedW(word, 0.4f, 3f) * 0.5f, p.Y - S(40f), word, Emblems.Gold, 0.4f, 3f, 0.8f);
    }

    // A ring on whoever the bolt would hit, closing in as it charges.
    private static void BoltRing(Actor aim, float charge)
    {
        if (!WorldView.ToScreen(Suit.Box(aim).Centre, out System.Numerics.Vector2 p))
            return;
        float r = S(34f) - S(10f) * charge;
        Shapes.Ring(p.X, p.Y, r, r + MathF.Max(2f, S(3f)), Emblems.Gold, 0.9f);
        if (charge >= 0.99f)
            Shapes.Ring(p.X, p.Y, r + S(7f), r + S(8.5f), Emblems.Cream, 0.7f);
    }
}
