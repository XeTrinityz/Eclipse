using static Trinity.Eclipse.Gfx;
using static Trinity.Eclipse.MenuView;

namespace Trinity.Eclipse;

// Superman's pages, cut like Kryptonian crystal. Shard tabs down the left, a panel with its corners cut off,
// values as rows of diamonds and a readout pane on the right.
internal static class SupermanView
{
    private static readonly Rgba Navy = new(6, 12, 20);
    private static readonly Rgba Mist = new(125, 152, 180);
    private static readonly Rgba Deep = new(95, 122, 150);
    private static readonly Rgba Pale = new(200, 217, 234);

    private const float PanelX = 375f, PanelY = 195f, PanelW = 960f, PanelH = 720f;
    private const float RowsY = 330f, RowH = 54f;
    private const int RowsMost = 10;

    private static float _glide = -1f;

    public static void Draw()
    {
        Rect(0f, 0f, W, H, Palette.Black, 0.8f);
        Facets();
        Header();
        Shards();
        Panel();
        Readout();
        Bottom();
    }

    private static void Facets()
    {
        float t = MathF.Max(1f, S(1f));
        Shapes.QueueLine(X(0f), Y(270f), X(210f), Y(135f), t);
        Shapes.QueueLine(X(210f), Y(135f), X(315f), Y(210f), t);
        Shapes.QueueLine(X(315f), Y(210f), X(495f), Y(60f), t);
        Shapes.QueueLine(X(1920f), Y(780f), X(1725f), Y(960f), t);
        Shapes.QueueLine(X(1725f), Y(960f), X(1590f), Y(900f), t);
        Shapes.FlushLines(Emblems.Sky, 0.12f);
    }

    private static void Header()
    {
        float x = X(84f), y = Y(78f);
        Emblems.Shield(x, y, S(72f), 1f);
        Spaced(x + S(90f), y + S(4f), "SUPERMAN", Emblems.Ice, Title * 0.9f, 7f);
        Spaced(x + S(92f), y + S(50f), "ECLIPSE  /  HERO 1 OF " + Heroes.All.Length, Emblems.Sky, Small, 4f);
    }

    private static void Shards()
    {
        float x = X(66f), y = Y(225f);
        float cy = y + S(14f);
        float hx = x + S(30f);
        hx += Glyphs.Hint(hx, cy, PadButton.LB, "PGUP", Small * 0.9f) + S(6f);
        Glyphs.Hint(hx, cy, PadButton.RB, "PGDN", Small * 0.9f);

        float w = S(255f), h = S(62f);
        y += S(46f);
        for (int i = 0; i < Menu.Pages.Count; i++)
        {
            bool on = i == Menu.PageIndex;
            float sx = x + (on ? S(14f) : 0f);
            float[] pts = { sx + w * 0.06f, y, sx + w * 0.86f, y, sx + w, y + h * 0.5f, sx + w * 0.86f, y + h, sx + w * 0.06f, y + h, sx, y + h * 0.5f };
            if (on)
                Shapes.Fill(Emblems.Ice, 1f, pts);
            else
                Shapes.Loop(MathF.Max(1f, S(1f)), Emblems.Sky, 0.35f, pts);

            Rgba col = on ? Navy : Pale;
            Spaced(sx + S(36f), y + h * 0.5f - TextH(Label) * 0.5f, Menu.Pages[i].Title, col, Label, 5f);
            y += h + S(14f);
        }
    }

    private static void Panel()
    {
        float x = X(PanelX), y = Y(PanelY), w = S(PanelW), h = S(PanelH), cut = S(42f);
        float slide = Slide(Menu.PageChangedAt, S(40f) * Menu.Moved);
        Cut(x, y, w, h, cut, Navy, 0.94f);
        Shapes.Loop(MathF.Max(1f, S(1.5f)), Emblems.Sky, 0.45f,
            x + cut, y, x + w, y, x + w, y + h - cut, x + w - cut, y + h, x, y + h, x, y + cut);

        Page page = Menu.Current;
        Spaced(x + S(45f) + slide, y + S(32f), In(page.Title, Menu.PageChangedAt), Emblems.Ice, Head * 1.15f, 8f);
        string count = page.Options.Count + " ROWS";
        Spaced(x + w - S(45f) - SpacedW(count, Small, 4f), y + S(42f), count, Mist, Small, 4f);

        float ry = y + S(100f);
        Rect(x + S(45f), ry, (w - S(90f)) * 0.5f - S(14f), MathF.Max(1f, S(1f)), Emblems.Sky, 0.3f);
        Rect(x + w * 0.5f + S(14f), ry, (w - S(90f)) * 0.5f - S(14f), MathF.Max(1f, S(1f)), Emblems.Sky, 0.3f);
        Shapes.Diamond(x + w * 0.5f, ry, S(6f), Emblems.Ice, 1f);

        int first = Math.Clamp(Menu.RowIndex - RowsMost + 1, 0, Math.Max(0, page.Options.Count - RowsMost));
        float target = (Menu.RowIndex - first) * RowH;
        _glide = Follow(_glide, target, 18f);
        float rx = x + S(18f), rw = w - S(36f), top = Y(RowsY);

        // The lit row: a red glow fading into ice, with bright rules above and below.
        float gy = top + S(_glide);
        Rect(rx, gy, rw * 0.5f, S(RowH), Emblems.Cape, 0.26f);
        Rect(rx + rw * 0.5f, gy, rw * 0.5f, S(RowH), Emblems.Sky, 0.08f);
        Rect(rx, gy, rw, MathF.Max(1f, S(1f)), Emblems.Ice, 0.7f);
        Rect(rx, gy + S(RowH) - MathF.Max(1f, S(1f)), rw, MathF.Max(1f, S(1f)), Emblems.Ice, 0.7f);

        for (int i = first; i < Math.Min(page.Options.Count, first + RowsMost); i++)
        {
            Option o = page.Options[i];
            bool on = i == Menu.RowIndex;
            float cy = top + S((i - first) * RowH + RowH * 0.5f);
            float flash = Flash(o);
            if (flash > 0f)
                Rect(rx, cy - S(RowH * 0.5f), rw, S(RowH), Emblems.Ice, 0.25f * flash);
            if (!on && i < first + RowsMost - 1)
                Rect(rx + S(27f), cy + S(RowH * 0.5f), rw - S(54f), MathF.Max(1f, S(1f)), Emblems.Sky, 0.1f);

            string n = (i + 1).ToString("00");
            Text(rx + S(27f), cy - TextH(Small) * 0.5f, n, on ? new Rgba(255, 138, 128) : Deep, Small);
            Spaced(rx + S(72f) + slide, cy - TextH(Label) * 0.5f, In(o.Label, Menu.PageChangedAt), Emblems.Ice, Label, 3f);
            Value(o, rx + rw - S(27f), cy, on);
        }
    }

    private static void Value(Option o, float right, float cy, bool on)
    {
        switch (o)
        {
            case Switch s:
                float onW = Glyphs.CapWidth("ON", Small);
                float boxX = right - onW;
                if (s.On)
                {
                    Rect(boxX, cy - S(13f), onW, S(26f), Emblems.Ice, 0.12f);
                    Outline(boxX, cy - S(13f), onW, S(26f), Emblems.Ice, 1f, S(1f));
                    TextCentre(boxX + onW * 0.5f, cy, "ON", Emblems.Ice, Small);
                    TextRight(boxX - S(12f), cy, "OFF", Deep, Small);
                }
                else
                {
                    TextCentre(boxX + onW * 0.5f, cy, "ON", Deep, Small);
                    float offW = Glyphs.CapWidth("OFF", Small);
                    Outline(boxX - S(12f) - offW, cy - S(13f), offW, S(26f), Emblems.Ice, 1f, S(1f));
                    TextCentre(boxX - S(12f) - offW * 0.5f, cy, "OFF", Emblems.Ice, Small);
                }

                break;
            case Dial d:
                float vw = S(70f);
                TextRight(right, cy, d.Value, Emblems.Ice, Small);
                float px = right - vw - S(10f);
                int lit = (int)MathF.Round(d.Fraction * 10f);
                for (int i = 9; i >= 0; i--)
                {
                    float dx = px - (9 - i) * S(17f);
                    if (i < lit)
                        Shapes.Diamond(dx, cy, S(6.5f), Emblems.Ice, 1f);
                    else
                        Shapes.DiamondLine(dx, cy, S(6.5f), MathF.Max(1f, S(1f)), Emblems.Sky, 0.5f);
                }

                if (on)
                {
                    TextRight(px - 10 * S(17f) - S(4f), cy, "<", new Rgba(255, 138, 128), Small);
                    Text(right + S(8f), cy - TextH(Small) * 0.5f, ">", new Rgba(255, 138, 128), Small);
                }

                break;
            case Control c:
                if (Menu.Binding == c)
                    TextRight(right, cy, Words(c), Emblems.Cape, Small);
                else
                    Glyphs.Prompt(right - Glyphs.PromptWidth(c, Small), cy, c, Small);
                break;
            case Bind b:
                if (Menu.Binding == b)
                    TextRight(right, cy, Words(b), Emblems.Cape, Small);
                else
                    Glyphs.Cap(right - Glyphs.CapWidth(b.Value, Small), cy, b.Value, Small, Emblems.Ice);
                break;
            default:
                string words = Words(o);
                if (words.Length > 0)
                    Spaced(right - SpacedW(words, Small, 2f), cy - TextH(Small) * 0.5f, words, o is Command ? Emblems.Cape : Emblems.Ice, Small, 2f);
                break;
        }
    }

    private static void Readout()
    {
        if (Menu.Selected is not { } o)
            return;

        float x = X(1380f), y = Y(PanelY), w = S(465f);
        float slide = Slide(Menu.PageChangedAt, S(20f));
        Spaced(x + slide, y, "ROW " + (Menu.RowIndex + 1).ToString("00"), Emblems.Sky, Small, 4f);
        float ly = y + S(32f);
        string label = o.Label;
        while (label.Length > 0)
        {
            label = FitLine(label, w, Head, out string line);
            Spaced(x, ly, line, Emblems.Ice, Head, 4f);
            ly += TextH(Head) * 1.2f;
        }

        ly += S(10f);
        ly += Wrap(x, ly, w, o.Help, Pale, HelpSize, 1.4f);

        if (Menu.BindNote.Length > 0 && Menu.Binding != null)
        {
            ly += S(10f);
            ly += Wrap(x, ly, w, Menu.BindNote, Emblems.Cape, Small, 1.4f);
        }

        string detail = Detail(o);
        if (detail.Length > 0)
        {
            ly += S(24f);
            Rect(x, ly, w, MathF.Max(1f, S(1f)), Emblems.Sky, 0.25f);
            Spaced(x, ly + S(14f), detail, Pale, Small, 2f);
        }
    }

    private static void Bottom()
    {
        float top = Y(1000f);
        Rect(0f, top, W, H - top, Palette.Black, 0.75f);
        Rect(0f, top, W, MathF.Max(1f, S(1f)), Emblems.Sky, 0.3f);
        float cy = Y(1040f);
        Rgba text = new(196, 200, 208);
        float x = PageHints(X(1860f), cy, text, Small);
        if (Menu.Binding == null)
            HintCap(x, cy, Controls.PadActive ? Glyphs.LeftStick : "UP DOWN", "ROWS", text, Small);
    }
}
