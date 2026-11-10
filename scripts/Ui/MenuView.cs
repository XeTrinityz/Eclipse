using static Trinity.Eclipse.Gfx;

namespace Trinity.Eclipse;

// A full screen eclipse. Pages orbit its left edge and rows curve round its right.
internal static class MenuView
{
    // 1080p pixels at MENU SIZE 1.
    private const float DiscR = 128f;
    private const float Orbit = 182f;
    private const float Curve = 200f;
    private const float CurveTall = 470f;
    private const float RowGap = 26f;
    private const float RowH = 40f;
    private const float ListW = 480f;
    private const float TrackW = 170f;
    private const float PageStep = 21f;
    private const float BarTop = 64f;
    private const float BarBottom = 112f;
    private const float LeftReach = 360f;
    private const float RightReach = Curve + RowGap + ListW + 20f;
    private const float Margin = 20f;

    // The font is small at 1.0, so these run larger than they look.
    private const float BrandScale = 0.84f;
    private const float LabelScale = 0.7f;
    private const float SmallScale = 0.56f;
    private const float HelpScale = 0.66f;
    private const float StateScale = 1.2f;

    private const int Rays = 96;
    private const float Rad = MathF.PI / 180f;

    // The scrim is dark enough that shapes can treat what's under them as this colour.
    private static readonly Rgba Under = new(12, 9, 8);

    private static float _size = 1f;
    private static float _rowF = -1f;
    private static float _pageF = -1f;
    private static float _glow = -1f;
    private static float _flare;
    private static bool _wasOn;
    private static float _focus;

    private static bool Fx => Opt.Effects.On;

    private static float U(float v) => S(v) * _size;

    private static float Sc(float s) => s * _size;

    public static void Opened()
    {
        _rowF = -1f;
        _pageF = -1f;
        _glow = -1f;
        _flare = 0f;
        _wasOn = Opt.Powers.On;
        _focus = Menu.Level;
    }

    public static void PageTurned() => _rowF = -1f;

    private static float FitSize()
    {
        float want = Opt.MenuSize.Number;
        float fitW = (Gfx.W - S(Margin) * 2f) / S(LeftReach + RightReach);
        float fitH = Gfx.H / S(BarTop + BarBottom + Orbit * 2f + 80f);
        return MathF.Max(0.5f, MathF.Min(want, MathF.Min(fitW, fitH)));
    }

    private static string Decode(string text, float age) => Fx ? Gfx.Decode(text, age) : text;

    public static void Draw(float anim)
    {
        _size = FitSize();
        float ease = Ease.Out(anim);
        Shapes.Under = Under;

        bool on = Opt.Powers.On;
        if (on && !_wasOn)
            _flare = 1f;
        _wasOn = on;
        _glow = Fx ? Follow(_glow, on ? 1f : 0f, 5f) : on ? 1f : 0f;
        _flare = MathF.Max(0f, _flare - Dt * 1.6f);
        _focus = Follow(_focus, Menu.Level, 12f);

        float top = U(BarTop), bottom = U(BarBottom);
        float cy = MathF.Round(top + (Gfx.H - top - bottom) * 0.5f);
        float cx = MathF.Round((Gfx.W - U(LeftReach + RightReach)) * 0.5f + U(LeftReach));

        Alpha = ease;
        Rect(0f, 0f, Gfx.W, Gfx.H, Palette.Void, 0.74f);
        Letterbox(top * ease, bottom * ease);

        float settle = 1f - ease;
        Alpha = ease;
        Pages(cx - U(24) * settle, cy);
        Rows(cx + U(36) * settle, cy, top, Gfx.H - bottom);
        Eclipse(cx, cy, ease);

        Alpha = ease;
        Header(top * ease - top);
        Footer(Gfx.H - bottom * ease);
        Alpha = 1f;
    }

    private static void Letterbox(float top, float bottom)
    {
        float line = MathF.Max(1f, S(1));
        Rect(0f, 0f, Gfx.W, top, Palette.Black, 0.94f);
        Rect(0f, Gfx.H - bottom, Gfx.W, bottom, Palette.Black, 0.94f);
        Rect(0f, top, Gfx.W, line, Palette.Ember, 0.35f);
        Rect(0f, Gfx.H - bottom - line, Gfx.W, line, Palette.Ember, 0.35f);
    }

    private static void Header(float shift)
    {
        float pad = U(36);
        float cy = U(BarTop) * 0.5f + shift;
        float bw = Spaced(pad, cy - TextH(Sc(BrandScale)) * 0.5f, "ECLIPSE", Palette.Hud, Sc(BrandScale), 5f);
        float age = (float)(Time - Menu.OpenedAt);
        TextMid(pad + bw + U(18), cy + U(1), Decode("POWER SUITE", age), Palette.Dim, Sc(SmallScale));

        string state = !Opt.Powers.On ? "POWERS OFF" : Suit.I.Flying ? "IN FLIGHT" : "ON FOOT";
        float sw = TextRight(Gfx.W - pad, cy + U(1), "V" + App.Version, Palette.Faint, Sc(SmallScale));
        TextRight(Gfx.W - pad - sw - U(22), cy + U(1), state, Opt.Powers.On ? Palette.Text : Palette.Caution, Sc(SmallScale));
    }

    private static void Eclipse(float cx, float cy, float ease)
    {
        float r = U(DiscR) * (0.9f + 0.1f * ease);
        float g = Math.Clamp(_glow, 0f, 1f) * ease;
        float t = (float)Time;

        // Glow in steps, fading out from the rim.
        if (g > 0.01f)
        {
            const int steps = 14;
            float reach = U(30) * g;
            for (int k = steps - 1; k >= 0; k--)
            {
                float f = 1f - k / (float)steps;
                Shapes.Ring(cx, cy, r, r + reach * (k + 1) / steps, Rgba.Mix(Palette.Ember, Palette.Hud, f * f), 0.5f * f * f * g);
            }

            float thick = MathF.Max(1f, U(1.6f));
            for (int i = 0; i < Rays; i++)
            {
                float a = i * (360f / Rays) * Rad;
                float wave = Fx ? 0.5f + 0.5f * MathF.Sin(i * 2.39f + t * 0.9f + MathF.Sin(i * 0.71f + t * 0.35f) * 2f) : 0.5f;
                float len = U(6f + 22f * wave * wave) * g;
                float c = MathF.Cos(a), s = MathF.Sin(a);
                Shapes.QueueLine(cx + c * (r + U(2)), cy + s * (r + U(2)), cx + c * (r + U(2) + len), cy + s * (r + U(2) + len), thick);
            }

            Shapes.FlushLines(Rgba.Mix(Under, Palette.Hud, 0.55f * g));
        }

        if (_flare > 0f && Fx)
        {
            float f = Ease.Out(1f - _flare);
            float fr = r + U(90) * f;
            Shapes.Ring(cx, cy, fr, fr + U(3) * _flare + 1f, Palette.Flare, _flare);
        }

        Shapes.Disc(cx, cy, r, Palette.Void, 1f);
        Rgba rim = Rgba.Mix(Palette.Faint, Palette.Flare, g);
        Shapes.Ring(cx, cy, r - U(1), r + U(1.5f + 1.5f * g), rim, 1f);

        // The bead of light that shows just before totality.
        if (g > 0.05f)
        {
            float a = -48f * Rad;
            float bx = cx + MathF.Cos(a) * (r + U(1)), by = cy + MathF.Sin(a) * (r + U(1));
            float pulse = Fx ? 0.75f + 0.25f * MathF.Sin(t * 2.2f) : 1f;
            Shapes.Disc(bx, by, U(5.5f) * pulse * g, Palette.Flare, 1f);
            float spike = U(16) * pulse * g;
            Shapes.QueueLine(bx - spike, by, bx + spike, by, MathF.Max(1f, U(1.2f)));
            Shapes.QueueLine(bx, by - spike, bx, by + spike, MathF.Max(1f, U(1.2f)));
            Shapes.FlushLines(Rgba.Mix(Under, Palette.Flare, 0.8f * g));
        }

        bool on = Opt.Powers.On;
        float age = (float)(Time - Menu.OpenedAt);
        float bs = Sc(SmallScale);
        float ew = SpacedW("POWERS", bs, 4f);
        Spaced(cx - ew * 0.5f, cy - r * 0.42f - TextH(bs) * 0.5f, "POWERS", Palette.Dim, bs, 4f);
        TextCentre(cx, cy + U(2), Decode(on ? "ACTIVE" : "DORMANT", age), on ? Palette.Flare : Palette.Faint, Sc(StateScale));
        string page = (Menu.PageIndex + 1).ToString("00") + " / " + Opt.Pages.Count.ToString("00");
        TextCentre(cx, cy + r * 0.44f, page, Palette.Dim, bs);
    }

    private static void Pages(float cx, float cy)
    {
        int n = Opt.Pages.Count;
        _pageF = _pageF < 0f || !Fx ? Menu.PageIndex : Follow(_pageF, Menu.PageIndex, 14f);
        float age = (float)(Time - Menu.PageChangedAt);
        float orbit = U(Orbit);
        float focus = Math.Clamp(1f - _focus, 0f, 1f);

        // The orbit track, faint, round the left side.
        Shapes.Arc(cx, cy, orbit - MathF.Max(0.5f, U(0.5f)), orbit + MathF.Max(0.5f, U(0.5f)), 100f, 260f, Palette.Faint, 0.8f);

        for (int i = 0; i < n; i++)
        {
            // Later pages sit below, so Down brings them up to the middle.
            float deg = 180f - (i - _pageF) * PageStep;
            float off = MathF.Abs(deg - 180f);
            if (off > 80f)
                continue;

            float fade = 1f - Math.Clamp((off - 30f) / 50f, 0f, 1f);
            float a = deg * Rad;
            float px = cx + MathF.Cos(a) * orbit, py = cy + MathF.Sin(a) * orbit;
            bool current = i == Menu.PageIndex;
            float was = Alpha;
            Alpha = was * fade;

            if (current)
            {
                Shapes.Disc(px, py, U(6.5f), Palette.Hud, 1f);
                Shapes.Ring(px, py, U(9.5f), U(9.5f) + MathF.Max(1f, U(1.2f)), Palette.Hud, 0.4f + 0.6f * focus);
            }
            else
            {
                Shapes.Disc(px, py, U(4.5f), Palette.Dim, 1f);
                Shapes.Disc(px, py, U(3f), Under, 1f);
            }

            string title = current ? Decode(Opt.Pages[i].Title, age) : Opt.Pages[i].Title;
            Rgba ink = current ? Rgba.Mix(Palette.Hud, Palette.Flare, focus) : Palette.Dim;
            float s = current ? Sc(LabelScale) : Sc(SmallScale);
            TextRight(px - U(20), py + U(1), title, ink, s);
            if (current && focus > 0.01f)
                TextRight(px - U(20), py + U(22), Opt.Pages[i].Options.Count + " " + Opt.Pages[i].Unit, Palette.Faint, Sc(SmallScale), focus);

            Alpha = was;
        }
    }

    private static float RowX(float cx, float dy)
    {
        float k = Math.Clamp(dy / U(CurveTall), -1f, 1f);
        return cx + U(Curve) * MathF.Sqrt(1f - k * k) + U(RowGap);
    }

    private static void Rows(float cx, float cy, float top, float bottom)
    {
        List<Option> rows = Menu.Current.Options;
        if (rows.Count == 0)
            return;

        int sel = Math.Clamp(Menu.RowIndex, 0, rows.Count - 1);
        _rowF = _rowF < 0f || !Fx ? sel : Follow(_rowF, sel, 16f);
        float rowH = U(RowH);
        float band = (bottom - top) * 0.5f - U(10);
        float age = (float)(Time - Menu.PageChangedAt);
        float dim = 0.55f + 0.45f * Math.Clamp(_focus, 0f, 1f);
        bool rowsFocused = Menu.Level == 1;

        // A short ray from the corona to the selected row.
        float hx0 = cx + U(DiscR) + U(40), hx1 = RowX(cx, 0f) - U(10);
        Rect(hx0, cy, hx1 - hx0, MathF.Max(1f, S(1)), Palette.Hud, rowsFocused ? 0.75f : 0.3f);

        for (int j = 0; j < rows.Count; j++)
        {
            float dy = (j - _rowF) * rowH;
            float y = cy + dy;
            if (y < top + rowH * 0.5f || y > bottom - rowH * 0.5f)
                continue;

            float d = MathF.Abs(dy) / MathF.Max(1f, band);
            float fade = 1f - 0.72f * Math.Clamp(d * d * 1.4f, 0f, 1f);
            int k = Math.Abs(j - sel);
            float cascade = Fx ? Ease.Out(Math.Clamp((age - k * 0.035f) / 0.2f, 0f, 1f)) : 1f;
            bool selected = rowsFocused && j == sel;
            float x0 = RowX(cx, dy) + U(28) * (1f - cascade);
            float x1 = x0 + U(ListW);

            float was = Alpha;
            Alpha = was * fade * cascade * (selected ? 1f : dim);
            Row(rows[j], x0, x1, y, selected, age - k * 0.035f);
            Alpha = was;
        }
    }

    private static void Row(Option o, float x0, float x1, float cy, bool selected, float age)
    {
        if (selected)
        {
            Rect(x0, cy - U(RowH) * 0.5f + U(3), x1 - x0 + U(14), U(RowH) - U(6), Palette.Hud, 0.07f);
            Shapes.Disc(x0, cy, U(4f), Palette.Flare, 1f);
        }
        else
        {
            Shapes.Disc(x0, cy, U(1.8f), Palette.Faint, 1f);
        }

        Rgba ink = selected ? Palette.Flare : Palette.Text;
        string label = selected ? o.Label : Decode(o.Label, age);
        float lx = x0 + U(16);
        TextMid(lx, cy + U(1), label, ink, Sc(LabelScale));
        if (o is Act)
            TextMid(lx + TextW(label, Sc(LabelScale)) + U(10), cy + U(1), ">", selected ? Palette.Hud : Palette.Faint, Sc(LabelScale));

        Value(o, x1, cy, selected);
        if (!o.IsDefault && o is not Act)
            Shapes.Disc(x1 + U(12), cy, U(2.5f), Palette.Ember, 1f);
    }

    private static void Value(Option o, float x1, float cy, bool selected)
    {
        bool flash = Menu.Flashed == o && Time - Menu.FlashAt < 0.45;
        Rgba col = flash ? Palette.Flare : selected ? Palette.Hud : Palette.Text;
        bool blink = (int)(Time * 3.0) % 2 == 0;
        float ls = Sc(LabelScale), ss = Sc(SmallScale);
        switch (o)
        {
            case Switch s:
                Phase(x1 - U(9), cy, s.On, flash);
                TextRight(x1 - U(26), cy + U(1), s.On ? "ON" : "OFF", s.On ? col : Palette.Dim, ls);
                break;
            case Pick p:
                float pw = TextW(p.Value, ls);
                TextRight(x1 - U(16), cy - (p.Labels.Length is > 1 and <= 8 ? U(3) : 0f), p.Value, col, ls);
                if (selected)
                {
                    TextMid(x1 - U(8), cy, ">", Palette.Hud, ls);
                    TextRight(x1 - U(24) - pw, cy, "<", Palette.Hud, ls);
                }

                if (p.Labels.Length is > 1 and <= 8)
                {
                    float gap = U(11);
                    float dx = x1 - U(16) - pw * 0.5f - gap * (p.Labels.Length - 1) * 0.5f;
                    for (int i = 0; i < p.Labels.Length; i++)
                        Shapes.Disc(dx + gap * i, cy + U(14), U(i == p.Index ? 2.8f : 1.8f), i == p.Index ? Palette.Hud : Palette.Faint, 1f);
                }

                break;
            case Dial d:
                float vw = U(78);
                float tx1 = x1 - vw - U(14), tx0 = tx1 - U(TrackW);
                float line = MathF.Max(1f, U(2));
                Rect(tx0, cy - line * 0.5f, tx1 - tx0, line, Palette.Dim, 0.55f);
                Rect(tx0, cy - line * 0.5f, (tx1 - tx0) * d.Fraction, line, col, 1f);
                if (!d.IsDefault)
                    Rect(MathF.Round(tx0 + (tx1 - tx0) * d.DefaultFraction), cy - U(6), MathF.Max(1f, U(1)), U(12), Palette.Dim, 0.9f);
                float kx = tx0 + (tx1 - tx0) * d.Fraction;
                Shapes.Disc(kx, cy, U(selected ? 5.5f : 4f), col, 1f);
                if (selected)
                    Shapes.Ring(kx, cy, U(8f), U(8f) + MathF.Max(1f, U(1)), Palette.Hud, 0.5f);
                TextRight(x1, cy + U(1), d.Value, col, ls);
                break;
            case Bind b:
                if (Menu.Binding == b)
                {
                    string prompt = Menu.BindRefused ? "THAT KEY IS TAKEN" : "PRESS A KEY";
                    TextRight(x1, cy + U(1), prompt, Palette.Caution, ss, blink ? 1f : 0.45f);
                    break;
                }

                Chip(x1, cy, b.Value, col);
                break;
            case Act a:
                if (Menu.Confirming == a)
                {
                    TextRight(x1, cy + U(1), "PRESS AGAIN TO CONFIRM", Palette.Caution, ss, blink ? 1f : 0.5f);
                    float bw = U(TrackW);
                    Rect(x1 - bw, cy + U(11), bw * Menu.ConfirmLeft, MathF.Max(1f, U(1.5f)), Palette.Caution, 0.8f);
                    break;
                }

                if (a.Value.Length > 0)
                    TextRight(x1, cy + U(1), a.Value, selected ? Palette.Hud : Palette.Dim, ss);
                break;
            case Info n:
                TextRight(x1, cy + U(1), n.Value, selected ? Palette.Hud : Palette.Dim, ss);
                break;
        }
    }

    // A switch drawn as a moon. Full and haloed when on, a dark new moon when off.
    private static void Phase(float cx, float cy, bool on, bool flash)
    {
        float r = U(7f);
        if (on)
        {
            Shapes.Ring(cx, cy, r + U(2), r + U(3.5f), Palette.Ember, 0.8f);
            Shapes.Disc(cx, cy, r, flash ? Palette.Flare : Palette.Hud, 1f);
            return;
        }

        Shapes.Disc(cx, cy, r, Palette.Dim, 1f);
        Shapes.Disc(cx, cy, r - MathF.Max(1f, U(1.4f)), Under, 1f);
    }

    private static void Chip(float right, float cy, string text, Rgba col)
    {
        float s = Sc(SmallScale);
        float w = TextW(text, s) + U(14);
        float h = TextH(s) + U(6);
        float x = right - w;
        Rect(x, cy - h * 0.5f, w, h, col, 0.08f);
        Outline(x, cy - h * 0.5f, w, h, col, 0.6f, S(1));
        TextMid(x + U(7), cy + U(1), text, col, s);
    }

    private static void Footer(float y)
    {
        float bottom = U(BarBottom);
        float hs = Sc(HelpScale);
        float width = MathF.Min(Gfx.W - U(72), U(1100));
        string help = Menu.Binding is { } b
            ? "Press the key for " + b.Label.ToLowerInvariant() + ". Backspace cancels. Keys the menu, flight or another bind uses can't be taken."
            : Menu.Level == 0 ? Menu.Current.Blurb : Menu.Selected?.Help ?? "";
        bool adjusts = Menu.Level == 1 && Menu.Selected is not (null or Info or Act or Bind);

        // Help reads like a subtitle, centred in the bottom bar.
        float ty = y + U(12);
        string rest = FitLine(help, width, hs, out string line1);
        TextCentre(Gfx.W * 0.5f, ty + TextH(hs) * 0.5f, line1, Palette.Text, hs);
        if (rest.Length > 0)
        {
            string more = FitLine(rest, width, hs, out string line2);
            if (more.Length > 0 && line2.Length > 3)
                line2 = line2.Substring(0, line2.Length - 3) + "...";
            TextCentre(Gfx.W * 0.5f, ty + TextH(hs) * 1.5f + U(3), line2, Palette.Text, hs);
        }

        var hints = new List<(string Key, string Action)>();
        if (Pad.InUse)
        {
            hints.Add(("D-PAD", Menu.Level == 0 ? "PAGE" : adjusts ? "ROW / ADJUST" : "ROW"));
            hints.Add(("A", "SELECT"));
            hints.Add(("B", Menu.Level == 0 ? "CLOSE" : "PAGES"));
            hints.Add(("LB RB", "PAGE"));
            hints.Add(("X", "DEFAULT"));
        }
        else
        {
            hints.Add(("UP DOWN", Menu.Level == 0 ? "PAGE" : "ROW"));
            if (adjusts)
                hints.Add(("LEFT RIGHT", "ADJUST"));
            hints.Add(("ENTER", "SELECT"));
            hints.Add(("BACKSPACE", Menu.Level == 0 ? "CLOSE" : "PAGES"));
            hints.Add(("TAB", "PAGE"));
            hints.Add(("DEL", "DEFAULT"));
            hints.Add((Opt.MenuKey.Value, "CLOSE"));
        }

        float s = Sc(SmallScale);
        float gap = U(18);
        float total = 0f;
        foreach ((string key, string action) in hints)
            total += TextW(key, s) + U(14) + U(6) + TextW(action, s) + gap;
        total -= gap;

        float cy = y + bottom - U(24);
        float hx = Gfx.W * 0.5f - total * 0.5f;
        foreach ((string key, string action) in hints)
        {
            float kw = TextW(key, s) + U(14);
            Chip(hx + kw, cy, key, Palette.Dim);
            TextMid(hx + kw + U(6), cy + U(1), action, Palette.Dim, s);
            hx += kw + U(6) + TextW(action, s) + gap;
        }
    }
}
