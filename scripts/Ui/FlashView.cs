using static Trinity.Eclipse.Gfx;
using static Trinity.Eclipse.MenuView;

namespace Trinity.Eclipse;

// The Flash's pages, all leaning forward. Slanted tabs across the top, slanted rows with tick meters, and a
// speedometer on the right that reads out whatever row is lit.
internal static class FlashView
{
    private static readonly Rgba Ink = new(29, 5, 8);
    private static readonly Rgba Rust = new(181, 138, 110);
    private static readonly Rgba Sand = new(236, 214, 196);
    private static readonly Rgba Ash = new(138, 106, 88);

    private const float Lean = 14f;
    private const float RowsX = 105f, RowsY = 210f, RowW = 1050f, RowH = 57f, RowGap = 6f;
    private const int RowsMost = 11;
    private const float GaugeX = 1530f, GaugeY = 420f, GaugeR = 180f;

    private static float _glide = -1f, _needle = -1f;

    public static void Draw()
    {
        Rect(0f, 0f, W, H, Ink, 0.86f);
        Streaks();
        Header();
        Tabs();
        Rows();
        Gauge();
        Bottom();
    }

    private static void Streaks()
    {
        float t = MathF.Max(1f, S(1f));
        float run = Opt.Effects.On ? (float)(Time * 0.6 % 1.0) : 0f;
        Shapes.QueueLine(X(0f), Y(144f), X(510f - 120f * run), Y(144f), t);
        Shapes.QueueLine(X(0f), Y(156f), X(375f - 90f * run), Y(156f), t);
        Shapes.QueueLine(X(0f), Y(168f), X(255f), Y(168f), t);
        Shapes.FlushLines(Emblems.Gold, 0.25f);
        float b = MathF.Max(1.5f, S(3f));
        Shapes.QueueLine(X(1920f), Y(0f), X(1815f), Y(135f), b);
        Shapes.QueueLine(X(1815f), Y(135f), X(1854f), Y(144f), b);
        Shapes.QueueLine(X(1854f), Y(144f), X(1725f), Y(315f), b);
        Shapes.FlushLines(Emblems.Gold, 0.22f);
    }

    private static void Header()
    {
        float x = X(84f), y = Y(54f);
        Emblems.Bolt(x, y, S(72f), 1f);
        Spaced(x + S(90f), y + S(2f), "FLASH", Emblems.Cream, Title, 8f);
        Spaced(x + S(92f), y + S(52f), "ECLIPSE  /  HERO 2 OF " + Heroes.All.Length, Emblems.Gold, Small, 4f);
    }

    private static void Tabs()
    {
        float x = X(570f), cy = Y(96f), h = S(51f), lean = S(Lean);
        x += Glyphs.Hint(x, cy, PadButton.LB, "PGUP", Small * 0.9f) + S(14f);
        for (int i = 0; i < Menu.Pages.Count; i++)
        {
            bool on = i == Menu.PageIndex;
            string title = Menu.Pages[i].Title;
            float w = SpacedW(title, Label * 0.92f, 4f) + S(54f);
            Slant(x, cy - h * 0.5f, w, h, lean, on ? Emblems.Crimson : new Rgba(255, 255, 255), on ? 1f : 0.07f);
            if (on)
                Slant(x, cy + h * 0.5f - S(4f), w, S(4f), lean * S(4f) / h, Emblems.Gold, 1f);
            Spaced(x + S(27f) + lean * 0.5f, cy - TextH(Label * 0.92f) * 0.5f, title, on ? Emblems.Cream : Sand, Label * 0.92f, 4f, on ? 1f : 0.85f);
            x += w + S(9f);
        }

        Glyphs.Hint(x + S(10f), cy, PadButton.RB, "PGDN", Small * 0.9f);
    }

    private static void Rows()
    {
        Page page = Menu.Current;
        int first = Math.Clamp(Menu.RowIndex - RowsMost + 1, 0, Math.Max(0, page.Options.Count - RowsMost));
        float step = RowH + RowGap;
        _glide = Follow(_glide, (Menu.RowIndex - first) * step, 20f);
        float lean = S(Lean);
        float x = X(RowsX), w = S(RowW), h = S(RowH);
        float slide = Slide(Menu.PageChangedAt, S(60f) * Menu.Moved);

        for (int i = first; i < Math.Min(page.Options.Count, first + RowsMost); i++)
        {
            Option o = page.Options[i];
            bool on = i == Menu.RowIndex;
            float y = Y(RowsY) + S((i - first) * step);
            float rx = x + slide;
            if (!on)
            {
                Slant(rx, y, w, h, lean, new Rgba(255, 255, 255), 0.04f);
                Slant(rx, y + h - S(1f), w, S(1f), 0f, Emblems.Gold, 0.12f);
            }

            float flash = Flash(o);
            if (flash > 0f)
                Slant(rx, y, w, h, lean, Emblems.Gold, 0.3f * flash);
        }

        // The lit row slides between rows: red into dark with a gold edge leading it.
        float gy = Y(RowsY) + S(_glide);
        Slant(x, gy, w * 0.7f, h, lean, Emblems.Crimson, 1f);
        Slant(x + w * 0.7f, gy, w * 0.3f, h, lean, new Rgba(140, 11, 32), 0.6f);
        Slant(x, gy, S(6f), h, lean, Emblems.Gold, 1f);

        for (int i = first; i < Math.Min(page.Options.Count, first + RowsMost); i++)
        {
            Option o = page.Options[i];
            bool on = i == Menu.RowIndex;
            float y = Y(RowsY) + S((i - first) * step);
            float cy = y + h * 0.5f;
            float shift = lean * 0.5f + slide;
            Text(x + S(33f) + shift, cy - TextH(Small) * 0.5f, (i + 1).ToString("00"), on ? new Rgba(255, 224, 138) : Rust, Small);
            Spaced(x + S(84f) + shift, cy - TextH(Label) * 0.5f, In(o.Label, Menu.PageChangedAt), on ? Emblems.Cream : Sand, Label, 3f);
            Value(o, x + w - S(30f) + shift, cy, on);
        }
    }

    private static void Value(Option o, float right, float cy, bool on)
    {
        switch (o)
        {
            case Switch s:
                float onW = Glyphs.CapWidth("ON", Small);
                if (s.On)
                {
                    Slant(right - onW, cy - S(13f), onW, S(26f), S(6f), Emblems.Gold, 1f);
                    TextCentre(right - onW * 0.5f + S(3f), cy, "ON", Ink, Small);
                    TextRight(right - onW - S(14f), cy, "OFF", Ash, Small);
                }
                else
                {
                    TextCentre(right - onW * 0.5f, cy, "ON", Ash, Small);
                    float offW = Glyphs.CapWidth("OFF", Small);
                    Slant(right - onW - S(14f) - offW, cy - S(13f), offW, S(26f), S(6f), Emblems.Cream, 0.9f);
                    TextCentre(right - onW - S(14f) - offW * 0.5f + S(3f), cy, "OFF", Ink, Small);
                }

                break;
            case Dial d:
                float vw = S(84f);
                TextRight(right, cy, d.Value, on ? Emblems.Cream : Sand, Small);
                int lit = (int)MathF.Round(d.Fraction * 10f);
                float tx = right - vw - S(10f) - 10 * S(15f);
                for (int i = 0; i < 10; i++)
                    Slant(tx + i * S(15f), cy - S(10f), S(9f), S(20f), S(5f), Emblems.Gold, i < lit ? 1f : 0.18f);
                if (on)
                {
                    TextRight(tx - S(8f), cy, "<", new Rgba(255, 224, 138), Small);
                    Text(right + S(8f), cy - TextH(Small) * 0.5f, ">", new Rgba(255, 224, 138), Small);
                }

                break;
            case Control c:
                if (Menu.Binding == c)
                    TextRight(right, cy, Words(c), Emblems.Gold, Small);
                else
                    Glyphs.Prompt(right - Glyphs.PromptWidth(c, Small), cy, c, Small);
                break;
            case Bind b:
                if (Menu.Binding == b)
                    TextRight(right, cy, Words(b), Emblems.Gold, Small);
                else
                    Glyphs.Cap(right - Glyphs.CapWidth(b.Value, Small), cy, b.Value, Small, Emblems.Cream);
                break;
            default:
                string words = Words(o);
                if (words.Length > 0)
                    Spaced(right - SpacedW(words, Small, 2f), cy - TextH(Small) * 0.5f, words, o is Command ? Emblems.Gold : Emblems.Cream, Small, 2f);
                break;
        }
    }

    // A speedometer that reads out the lit row. Dials fill it, switches peg it on or off.
    private static void Gauge()
    {
        if (Menu.Selected is not { } o)
            return;

        float cx = X(GaugeX), cy = Y(GaugeY), r = S(GaugeR);
        float f = o switch
        {
            Dial d => d.Fraction,
            Switch s => s.On ? 1f : 0f,
            Pick p => p.Labels.Length > 1 ? p.Index / (float)(p.Labels.Length - 1) : 0f,
            _ => 0f,
        };
        _needle = Follow(_needle, f, 10f);

        Shapes.Arc(cx, cy, r - S(21f), r, 135f, 405f, Emblems.Gold, 0.15f);
        for (int a = 135; a <= 405; a += 27)
            Shapes.Arc(cx, cy, r + S(9f), r + S(18f), a - 1f, a + 1f, Emblems.Cream, 0.4f);
        if (_needle > 0.002f)
        {
            Shapes.Arc(cx, cy, r - S(21f), r, 135f, 135f + 270f * _needle, Emblems.Crimson, 1f);
            Shapes.Arc(cx, cy, r - S(12f), r - S(8f), 135f, 135f + 270f * _needle, Emblems.Gold, 1f);
        }

        float ang = (135f + 270f * _needle) * MathF.PI / 180f;
        Shapes.Line(cx, cy, cx + MathF.Cos(ang) * (r - S(30f)), cy + MathF.Sin(ang) * (r - S(30f)), MathF.Max(2f, S(4f)), Emblems.Cream, 1f);
        Shapes.Disc(cx, cy, S(10f), Emblems.Gold, 1f);

        string big = o switch
        {
            Dial d => d.Value,
            Switch s => s.Value,
            Pick p => p.Value,
            Control c => Controls.PadActive ? c.PadLabel : c.KeyLabel,
            Bind b => b.Value,
            _ => "",
        };
        TextCentre(cx, cy + r * 0.86f, big, Emblems.Cream, Head * 1.1f);

        float x = X(1350f), y = cy + r + S(60f), w = S(480f);
        string label = o.Label;
        while (label.Length > 0)
        {
            label = FitLine(label, w, Head, out string line);
            Spaced(x, y, line, Emblems.Cream, Head, 4f);
            y += TextH(Head) * 1.2f;
        }

        y += S(8f);
        y += Wrap(x, y, w, o.Help, Sand, HelpSize, 1.4f);
        if (Menu.BindNote.Length > 0 && Menu.Binding != null)
            y += S(8f) + Wrap(x, y + S(8f), w, Menu.BindNote, Emblems.Gold, Small, 1.4f);

        string detail = Detail(o);
        if (detail.Length > 0)
        {
            y += S(18f);
            Rect(x, y, w, MathF.Max(1f, S(1f)), Emblems.Gold, 0.3f);
            Spaced(x, y + S(14f), detail, Rust, Small, 2f);
        }
    }

    private static void Bottom()
    {
        float top = Y(1000f), h = H - top;
        Slant(-S(30f), top, W + S(60f), h, S(Lean), Palette.Black, 0.7f);
        Slant(-S(30f), top, W + S(60f), MathF.Max(2f, S(3f)), 0f, Emblems.Crimson, 1f);
        float cy = Y(1040f);
        float x = PageHints(X(1860f), cy, Sand, Small);
        if (Menu.Binding == null)
            HintCap(x, cy, Controls.PadActive ? Glyphs.LeftStick : "UP DOWN", "ROWS", Sand, Small);
    }
}
