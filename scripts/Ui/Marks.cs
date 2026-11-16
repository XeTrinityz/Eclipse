using System.Numerics;
using BmSDK.Engine;
using static Trinity.Eclipse.Gfx;

namespace Trinity.Eclipse;

// Target marks while flying, plus a small dot to aim with.
internal static class Marks
{
    private const int DotsMost = 16;

    public static bool Showing => Opt.TargetMarks.On && Suit.I.Flying;

    public static void Draw(Canvas canvas)
    {
        WorldView.Begin(canvas);
        if (!WorldView.Ready)
            return;

        Rect(W * 0.5f - S(2), H * 0.5f - S(2), S(4), S(4), Palette.Hud, 0.7f);

        // A heavy punch winding up fills a bar under the dot.
        float charge = Suit.I.Charge;
        if (charge > 0f)
        {
            float bw = S(56), bh = MathF.Max(1f, S(3));
            float bx = W * 0.5f - bw * 0.5f, by = H * 0.5f + S(9);
            Rect(bx, by, bw, bh, Palette.Dim, 0.5f);
            Rect(bx, by, bw * charge, bh, charge >= 1f ? Palette.Caution : Palette.Hud, 0.9f);
        }

        Suit.Mark? aim = Suit.I.Aim;
        int dots = 0;
        foreach (Suit.Mark m in Suit.I.Marks)
        {
            if (aim is { } a && a.Thing.Ptr == m.Thing.Ptr)
                continue;
            if (dots >= DotsMost)
                break;
            if (!WorldView.ToScreen(m.Centre, out Vector2 p) || !WorldView.OnScreen(p))
                continue;

            dots++;
            Rect(p.X - S(3), p.Y - S(3), S(6), S(6), Palette.Hud, 0.4f);
        }

        if (aim is { } target)
            Brackets(target);
        else if (Suit.I.Holding)
            TextCentre(W * 0.5f, H * 0.5f + S(28), HoldHint(" THROW"), Palette.Text, 0.42f, 0.9f);
    }

    private static void Brackets(Suit.Mark m)
    {
        if (!WorldView.Bounds(m.Centre - m.Half, m.Centre + m.Half, out float x0, out float y0, out float x1, out float y1))
            return;

        float least = S(28);
        float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
        float w = MathF.Max(x1 - x0, least) + S(8), h = MathF.Max(y1 - y0, least) + S(8);
        x0 = cx - w * 0.5f;
        y0 = cy - h * 0.5f;
        x1 = x0 + w;
        y1 = y0 + h;

        float arm = Math.Clamp(MathF.Min(w, h) * 0.25f, S(8), S(22));
        float t = MathF.Max(1f, S(2));
        Rgba col = Palette.Hud;
        Rect(x0, y0, arm, t, col);
        Rect(x0, y0, t, arm, col);
        Rect(x1 - arm, y0, arm, t, col);
        Rect(x1 - t, y0, t, arm, col);
        Rect(x0, y1 - t, arm, t, col);
        Rect(x0, y1 - arm, t, arm, col);
        Rect(x1 - arm, y1 - t, arm, t, col);
        Rect(x1 - t, y1 - arm, t, arm, col);

        TextCentre(cx, y0 - S(12), Name(m.Kind) + "  " + Fmt.Whole(m.Distance / 100f) + " M", Palette.Text, 0.44f);
        TextCentre(cx, y1 + S(12), Hint(m), Palette.Dim, 0.38f, 0.9f);
    }

    private static string Name(Suit.Kind kind) => kind switch
    {
        Suit.Kind.Thug => "THUG",
        Suit.Kind.Car => "VEHICLE",
        _ => "CAR",
    };

    // Holding a thug, the punch key finishes him.
    private static string HoldHint(string thrown) =>
        (Suit.I.HoldingThug ? Said(Act.Punch) + " FINISH   " : "") + Said(Act.Grab) + thrown;

    // The key or the button for a control, whichever is in use.
    private static string Said(Act a) => Controls.Of(a) is { } c ? (Controls.PadActive ? Glyphs.Name(c.Button) : c.KeyLabel) : "";

    private static string Hint(Suit.Mark m)
    {
        string punch = Said(Act.Punch) + " PUNCH, HOLD FOR HEAVY";
        if (Suit.I.Holding)
            return HoldHint(" THROW AT IT");
        return punch + "   " + Said(Act.Grab) + " GRAB";
    }
}
