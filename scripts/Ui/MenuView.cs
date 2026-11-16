using static Trinity.Eclipse.Gfx;

namespace Trinity.Eclipse;

// Draws whichever menu screen is up. Everything is laid out on a 1920 by 1080 board, scaled to the screen and
// centred, so the hero views only think in board units.
internal static class MenuView
{
    public const float BoardW = 1920f, BoardH = 1080f;

    public const float Title = 1.5f, Head = 1.05f, Label = 0.74f, Small = 0.56f, HelpSize = 0.66f;

    private static float _ox, _oy;

    // Board units to screen.
    public static float X(float u) => _ox + S(u);

    public static float Y(float u) => _oy + S(u);

    public static float Age(double since) => (float)(Time - since);

    public static void Draw(float anim)
    {
        float scale = Scale;
        Rgba under = Shapes.Under;
        try
        {
            float fit = MathF.Min(W / BoardW, H / BoardH);
            Scale = Math.Clamp(fit * Opt.MenuSize.Number, 0.4f, fit);
            _ox = (W - S(BoardW)) * 0.5f;
            _oy = (H - S(BoardH)) * 0.5f;
            Alpha = Ease.Out(anim);

            // Shapes fade by mixing toward this, so it's each screen's own backdrop.
            if (Menu.Roster)
            {
                Shapes.Under = new Rgba(5, 6, 9);
                RosterView.Draw();
            }
            else if (Menu.Hero == Hero.Flash)
            {
                Shapes.Under = new Rgba(24, 6, 9);
                FlashView.Draw();
            }
            else
            {
                Shapes.Under = new Rgba(5, 10, 16);
                SupermanView.Draw();
            }
        }
        finally
        {
            Scale = scale;
            Alpha = 1f;
            Shapes.Under = under;
        }
    }

    // Text wrapped to a width, one line under another. Returns the height used.
    public static float Wrap(float x, float y, float width, string text, Rgba col, float scale, float gap = 1.35f)
    {
        float lineH = TextH(scale) * gap;
        float top = y;
        string rest = text;
        int lines = 0;
        while (rest.Length > 0 && lines < 12)
        {
            rest = FitLine(rest, width, scale, out string line);
            Text(x, y, line, col, scale);
            y += lineH;
            lines++;
        }

        return y - top;
    }

    // Text that scrambles in, when menu effects are on.
    public static string In(string text, double since) => Opt.Effects.On ? Decode(text, Age(since)) : text;

    // Slides in from dx over the first moments after since.
    public static float Slide(double since, float dx, float seconds = 0.28f) =>
        Opt.Effects.On ? dx * (1f - Ease.Out(Age(since) / seconds)) : 0f;

    // How strongly a row that just changed should glow.
    public static float Flash(Option o) => Menu.Flashed == o ? Math.Clamp(1f - Age(Menu.FlashAt) / 0.5f, 0f, 1f) : 0f;

    // What the right hand side of a row says, for rows that show words.
    public static string Words(Option o) => o switch
    {
        Command c when Menu.Confirming == c => "PRESS AGAIN",
        Command => "",
        Pick p => "<  " + p.Value + "  >",
        Bind b when Menu.Binding == b => "PRESS A KEY",
        Control c when Menu.Binding == c => Controls.PadActive ? "PRESS A BUTTON" : "PRESS A KEY",
        _ => o.Value,
    };

    // The hints along the bottom for the hero pages, right to left from right. Returns where they end.
    public static float PageHints(float right, float cy, Rgba text, float scale)
    {
        float x = right;
        if (Menu.Binding != null)
        {
            x = Hint(x, cy, PadButton.Back, "BACKSPACE", "CANCEL", text, scale);
            return x;
        }

        x = Hint(x, cy, PadButton.B, Bind.KeyName(Opt.MenuKey.Key), "CLOSE", text, scale);
        x = Hint(x, cy, PadButton.Y, "TAB", "HEROES", text, scale);
        if (Menu.Selected is { } o && o is not Command && !o.IsDefault)
            x = Hint(x, cy, PadButton.X, "DEL", "DEFAULT", text, scale);
        if (Menu.Selected is Control or Bind)
            x = Hint(x, cy, PadButton.A, "ENTER", "REBIND", text, scale);
        else if (Menu.Selected is Switch or Command)
            x = Hint(x, cy, PadButton.A, "ENTER", Menu.Selected is Switch ? "TOGGLE" : "SELECT", text, scale);
        else if (Menu.Selected is Dial or Pick or Listed)
            x = HintCap(x, cy, Controls.PadActive ? "< >" : "LEFT RIGHT", "CHANGE", text, scale);
        return x;
    }

    // One hint drawn to the left of x. Returns its left edge, with a gap.
    public static float Hint(float x, float cy, PadButton b, string key, string what, Rgba text, float scale)
    {
        float tw = SpacedW(what, scale, 2f);
        x -= tw;
        Spaced(x, cy - TextH(scale) * 0.5f, what, text, scale, 2f);
        x -= S(8f) + Glyphs.HintWidth(b, key, scale * 0.9f);
        Glyphs.Hint(x, cy, b, key, scale * 0.9f);
        return x - S(30f);
    }

    public static float HintCap(float x, float cy, string cap, string what, Rgba text, float scale)
    {
        float tw = SpacedW(what, scale, 2f);
        x -= tw;
        Spaced(x, cy - TextH(scale) * 0.5f, what, text, scale, 2f);
        x -= S(8f) + Glyphs.CapWidth(cap, scale * 0.9f);
        Glyphs.Cap(x, cy, cap, scale * 0.9f, Palette.Text);
        return x - S(30f);
    }

    // What a row says under the help text, like where its default is.
    public static string Detail(Option o) => o switch
    {
        Dial d => "DEFAULT " + d.Format(d.Default) + "      NOW " + d.Value,
        Switch s => "DEFAULT " + (s.Default ? "ON" : "OFF") + "      NOW " + s.Value,
        Pick p => "DEFAULT " + p.Labels[0] + "      NOW " + p.Value,
        Control c => "KEY " + c.KeyLabel + "      PAD " + c.PadLabel,
        Bind b => "DEFAULT " + Bind.KeyName(b.Default) + "      NOW " + b.Value,
        _ => "",
    };
}
