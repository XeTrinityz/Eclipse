using static Trinity.Eclipse.Gfx;
using static Trinity.Eclipse.MenuView;

namespace Trinity.Eclipse;

// The hero screen. The eclipse sits in the middle with a plate for each hero either side, and the corona
// takes the colours of the hero you point at.
internal static class RosterView
{
    private static readonly Rgba Corona = new(214, 178, 94);
    private static readonly Rgba Grey = new(163, 169, 179);
    private const float DiscX = 960f, DiscY = 510f, DiscR = 180f;
    private const float PlateY = 225f, PlateW = 570f, PlateH = 600f;
    private const int HowToMost = 3;
    private static float _lean = -1f;

    public static void Draw()
    {
        Hero hero = Menu.Hero;
        bool sup = hero == Hero.Superman;
        _lean = Follow(_lean, sup ? 0f : 1f, 9f);

        Rect(0f, 0f, W, H, Palette.Black, 0.78f);
        Bars();
        Disc(hero);
        SupermanPlate(Menu.Hero == Hero.Superman);
        FlashPlate(Menu.Hero == Hero.Flash);
        Plain();
        Bottom(hero);
    }

    private static void Bars()
    {
        Rect(0f, 0f, W, Y(90f), Palette.Black, 0.92f);
        Rect(0f, Y(90f), W, MathF.Max(1f, S(1f)), Corona, 0.45f);
        Spaced(X(60f), Y(45f) - TextH(Head) * 0.5f, "ECLIPSE", Corona, Head, 10f);
        string mid = In("CHOOSE A HERO", Menu.OpenedAt);
        Spaced(X(960f) - SpacedW(mid, Small, 4f) * 0.5f, Y(45f) - TextH(Small) * 0.5f, mid, Grey, Small, 4f);
        string count = "HERO " + ((int)Menu.Hero + 1) + " / " + Heroes.All.Length;
        Spaced(X(1860f) - SpacedW(count, Small, 4f), Y(45f) - TextH(Small) * 0.5f, count, Grey, Small, 4f);
    }

    private static void Disc(Hero hero)
    {
        float cx = X(DiscX), cy = Y(DiscY), r = S(DiscR);
        float t = _lean;
        Rgba inner = Rgba.Mix(new Rgba(80, 150, 255), Emblems.Gold, t);
        Rgba outer = Rgba.Mix(Emblems.Cape, Emblems.Crimson, t);
        float pulse = Opt.Effects.On ? 0.08f * MathF.Sin((float)Time * 2.2f) : 0f;

        // The glow is rings fading outwards, since triangles can't be see-through.
        const int steps = 16;
        for (int i = steps; i >= 1; i--)
        {
            float k = i / (float)steps;
            Shapes.Ring(cx, cy, r, r + S(10f + 74f * k), Rgba.Mix(inner, outer, k), (0.34f + pulse) * (1f - k) * (1f - k) + 0.04f);
        }

        Shapes.Disc(cx, cy, r, Palette.Black, 1f);
        Shapes.Ring(cx, cy, r, r + MathF.Max(2f, S(3f)), Rgba.Mix(Corona, Emblems.Gold, t), 1f);

        // A dotted orbit, turning slowly.
        float spin = Opt.Effects.On ? (float)Time * 6f : 0f;
        for (int a = 0; a < 360; a += 10)
            Shapes.Arc(cx, cy, S(254f), S(256f), a + spin, a + spin + 4f, Corona, 0.35f);

        float e = S(165f);
        if (hero == Hero.Superman)
            Emblems.Shield(cx - e * 0.5f, cy - e * 0.5f, e, 0.55f, filled: false);
        else
            Emblems.Bolt(cx - e * 0.5f, cy - e * 0.5f, e, 0.7f, disc: false);
    }

    private static void SupermanPlate(bool on)
    {
        float a = on ? 1f : 0.42f;
        float x = X(135f + (on ? 36f : 0f)), y = Y(PlateY), w = S(PlateW), h = S(PlateH), cut = S(50f);
        Rgba fill = on ? new Rgba(16, 32, 54) : new Rgba(18, 22, 30);
        Cut(x, y, w, h, cut, fill, 0.92f * a);
        Shapes.Loop(MathF.Max(1f, S(1.5f)), Emblems.Sky, 0.7f * a,
            x + cut, y, x + w, y, x + w, y + h - cut, x + w - cut, y + h, x, y + h, x, y + cut);

        float px = x + S(50f), py = y + S(45f);
        Emblems.Shield(px, py, S(66f), a);
        Spaced(px + S(84f), py + S(2f), "SUPERMAN", Emblems.Ice, Title, 6f, a);
        Spaced(px + S(86f), py + S(48f), Heroes.Tagline(Hero.Superman), Emblems.Sky, Small, 4f, a);
        Rect(px, py + S(96f), w - S(100f), MathF.Max(1f, S(1f)), Emblems.Sky, 0.35f * a);

        float ry = py + S(130f);
        foreach (string power in Heroes.Powers(Hero.Superman))
        {
            Shapes.Diamond(px + S(7f), ry, S(6f), power == "HEAT VISION" ? Emblems.Cape : Emblems.Sky, a);
            Spaced(px + S(26f), ry - TextH(Label) * 0.5f, power, Emblems.Ice, Label, 3f, a);
            ry += S(45f);
        }

        HowTo(Hero.Superman, px, y + h - S(190f), 0f, y, Emblems.Sky, Emblems.Ice, a);
        if (Heroes.Active == Hero.Superman && Opt.Powers.On)
            Badge(x + w - S(40f), y + h - S(48f), "PLAYING", Emblems.Sky, a);
    }

    // The hero's main controls at the foot of his plate, on the device in use. lean shifts each line left
    // the further down it is, so the Flash's lines follow his plate.
    private static void HowTo(Hero hero, float x, float y, float lean, float plateTop, Rgba head, Rgba text, float a)
    {
        if (!Opt.Binds.TryGetValue(hero, out var map))
            return;

        Spaced(x - lean * (y - plateTop), y, "HOW TO PLAY", head, Small, 4f, a);
        float cy = y + S(48f);
        // Only the first few fit on the plate. The legend and the controls page list the rest.
        foreach (Control c in map.Values.Where(c => c.Use == Use.Foot).Take(HowToMost))
        {

            float lx = x - lean * (cy - plateTop);
            float w = Glyphs.Prompt(lx, cy, c, Small, a);
            Spaced(lx + w + S(14f), cy - TextH(Small) * 0.5f, c.Label, text, Small, 3f, a);
            cy += S(40f);
        }
    }

    private static void FlashPlate(bool on)
    {
        float a = on ? 1f : 0.42f;
        float lean = S(84f);
        float x = X(1215f - (on ? 36f : 0f)) - lean, y = Y(PlateY), w = S(PlateW), h = S(PlateH);
        Rgba fill = on ? new Rgba(58, 10, 16) : new Rgba(30, 16, 18);
        Slant(x, y, w, h, lean, fill, 0.92f * a);
        Shapes.Loop(MathF.Max(1f, S(1.5f)), Emblems.Gold, 0.75f * a, x + lean, y, x + lean + w, y, x + w, y + h, x, y + h);

        // Two streaks of lightning across the top corner.
        float sx = x + lean + w;
        Shapes.Line(sx - S(330f), y + S(150f), sx - S(10f), y + S(10f), MathF.Max(1.5f, S(3f)), Emblems.Gold, 0.7f * a);
        Shapes.Line(sx - S(260f), y + S(170f), sx - S(20f), y + S(66f), MathF.Max(1f, S(1.5f)), Emblems.Cream, 0.5f * a);

        float px = x + lean + S(40f), py = y + S(45f);
        Emblems.Bolt(px, py, S(66f), a);
        Spaced(px + S(84f), py + S(2f), "FLASH", Emblems.Cream, Title, 6f, a);
        Spaced(px + S(86f), py + S(48f), Heroes.Tagline(Hero.Flash), Emblems.Gold, Small, 4f, a);

        // The rule and the rows lean with the plate.
        float k = lean / h;
        float ruleY = py + S(96f);
        Rect(px - k * (ruleY - y), ruleY, w - S(120f), MathF.Max(1f, S(2f)), Emblems.Gold, 0.6f * a);
        float ry = py + S(130f);
        foreach (string power in Heroes.Powers(Hero.Flash))
        {
            float rx = px - k * (ry - y);
            Spaced(rx, ry - TextH(Label) * 0.5f, "//", Emblems.Gold, Label, 1f, a);
            Spaced(rx + S(34f), ry - TextH(Label) * 0.5f, power, Emblems.Cream, Label, 3f, a);
            ry += S(45f);
        }

        HowTo(Hero.Flash, px, y + h - S(190f), k, y, Emblems.Gold, Emblems.Cream, a);
        if (Heroes.Active == Hero.Flash && Opt.Powers.On)
            Badge(x + w - S(40f), y + h - S(48f), "PLAYING", Emblems.Gold, a);
    }

    private static void Badge(float right, float cy, string text, Rgba col, float a)
    {
        float tw = SpacedW(text, Small, 3f);
        Spaced(right - tw, cy - TextH(Small) * 0.5f, text, col, Small, 3f, a);
        Shapes.Diamond(right - tw - S(14f), cy, S(5f), col, a);
    }

    private static void Plain()
    {
        float cy = Y(795f);
        string text = Opt.Powers.On ? "PLAIN BATMAN" : "POWERS ARE OFF";
        float tw = SpacedW(text, Small, 3f) + S(8f) + Glyphs.HintWidth(PadButton.X, "DEL", Small);
        float x = X(DiscX) - tw * 0.5f;
        x += Glyphs.Hint(x, cy, PadButton.X, "DEL", Small) + S(8f);
        Spaced(x, cy - TextH(Small) * 0.5f, text, Opt.Powers.On ? Grey : Palette.Caution, Small, 3f);
    }

    private static void Bottom(Hero hero)
    {
        float top = Y(990f);
        Rect(0f, top, W, H - top, Palette.Black, 0.92f);
        Rect(0f, top, W, MathF.Max(1f, S(1f)), Corona, 0.45f);
        float cy = Y(1035f);

        Wrap(X(60f), cy - TextH(HelpSize) * 0.7f, S(700f), Heroes.Blurb(hero), new Rgba(196, 200, 208), HelpSize, 1.25f);

        Rgba text = new(196, 200, 208);
        float x = X(1860f);
        x = Hint(x, cy, PadButton.B, Bind.KeyName(Opt.MenuKey.Key), "CLOSE", text, Small);
        x = Hint(x, cy, PadButton.Y, "TAB", "CUSTOMISE", text, Small);
        x = Hint(x, cy, PadButton.A, "ENTER", "PLAY AS", text, Small);
        if (Controls.PadActive)
        {
            float tw = SpacedW("HERO", Small, 2f);
            x -= tw;
            Spaced(x, cy - TextH(Small) * 0.5f, "HERO", text, Small, 2f);
            x -= S(8f) + Glyphs.Width(PadButton.RB, Small * 0.9f);
            Glyphs.Draw(x, cy, PadButton.RB, Small * 0.9f);
            x -= S(4f) + Glyphs.Width(PadButton.LB, Small * 0.9f);
            Glyphs.Draw(x, cy, PadButton.LB, Small * 0.9f);
        }
        else
        {
            HintCap(x, cy, "LEFT RIGHT", "HERO", text, Small);
        }
    }
}
