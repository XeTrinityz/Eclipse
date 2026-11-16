using static Trinity.Eclipse.Gfx;

namespace Trinity.Eclipse;

// Controller buttons as the player knows them, Xbox or PlayStation, and key caps for the keyboard.
internal static class Glyphs
{
    private static bool PlayStation => Opt.ButtonStyle.Index == 1;

    public static string Name(PadButton b) => b switch
    {
        PadButton.None => "NONE",
        PadButton.A => PlayStation ? "CROSS" : "A",
        PadButton.B => PlayStation ? "CIRCLE" : "B",
        PadButton.X => PlayStation ? "SQUARE" : "X",
        PadButton.Y => PlayStation ? "TRIANGLE" : "Y",
        PadButton.LB => PlayStation ? "L1" : "LB",
        PadButton.RB => PlayStation ? "R1" : "RB",
        PadButton.LT => PlayStation ? "L2" : "LT",
        PadButton.RT => PlayStation ? "R2" : "RT",
        PadButton.LS => PlayStation ? "L3" : "LS",
        PadButton.RS => PlayStation ? "R3" : "RS",
        PadButton.Back => PlayStation ? "TOUCHPAD" : "VIEW",
        PadButton.Start => PlayStation ? "OPTIONS" : "MENU",
        PadButton.Up => "D-PAD UP",
        PadButton.Down => "D-PAD DOWN",
        PadButton.Left => "D-PAD LEFT",
        PadButton.Right => "D-PAD RIGHT",
        _ => b.ToString().ToUpperInvariant(),
    };

    public static string LeftStick => PlayStation ? "L" : "LS";

    public static bool IsFace(PadButton b) => b is PadButton.A or PadButton.B or PadButton.X or PadButton.Y;

    public static Rgba Face(PadButton b) => PlayStation
        ? b switch
        {
            PadButton.A => new Rgba(124, 178, 232),
            PadButton.B => new Rgba(255, 102, 102),
            PadButton.X => new Rgba(230, 140, 210),
            _ => new Rgba(64, 226, 160),
        }
        : b switch
        {
            PadButton.A => new Rgba(108, 195, 108),
            PadButton.B => new Rgba(224, 98, 94),
            PadButton.X => new Rgba(90, 162, 230),
            _ => new Rgba(236, 201, 75),
        };

    // Draws a button with its left edge at x. Returns how wide it was.
    public static float Draw(float x, float cy, PadButton b, float scale, float alpha = 1f)
    {
        float h = TextH(scale) + S(4f) * scale / 0.45f;
        float r = h * 0.5f;
        if (IsFace(b))
        {
            Rgba col = Face(b);
            Shapes.Disc(x + r, cy, r, col, alpha);
            if (PlayStation)
                Symbol(x + r, cy, r * 0.46f, b, Palette.Black, alpha);
            else
                TextCentre(x + r, cy, Name(b), Palette.Black, scale, alpha);
            return h;
        }

        if (IsDpad(b))
        {
            Dpad(x + r, cy, h, b, alpha);
            return h;
        }

        return Cap(x, cy, Name(b), scale, Palette.Text, alpha);
    }

    private static bool IsDpad(PadButton b) => b is PadButton.Up or PadButton.Down or PadButton.Left or PadButton.Right;

    // A small d-pad with the pressed arm lit, so it's as narrow as a face button.
    private static void Dpad(float cx, float cy, float size, PadButton b, float alpha)
    {
        float t = MathF.Round(size * 0.36f), half = MathF.Round(size * 0.5f);
        Rect(cx - half, cy - t * 0.5f, half * 2f, t, Palette.Dim, 0.55f * alpha);
        Rect(cx - t * 0.5f, cy - half, t, half * 2f, Palette.Dim, 0.55f * alpha);
        float arm = half - t * 0.5f;
        switch (b)
        {
            case PadButton.Up:
                Rect(cx - t * 0.5f, cy - half, t, arm, Palette.Text, alpha);
                break;
            case PadButton.Down:
                Rect(cx - t * 0.5f, cy + t * 0.5f, t, arm, Palette.Text, alpha);
                break;
            case PadButton.Left:
                Rect(cx - half, cy - t * 0.5f, arm, t, Palette.Text, alpha);
                break;
            default:
                Rect(cx + t * 0.5f, cy - t * 0.5f, arm, t, Palette.Text, alpha);
                break;
        }
    }

    // A word in a key cap, for keys, bumpers, triggers and sticks. Returns how wide it was.
    public static float Cap(float x, float cy, string text, float scale, Rgba col, float alpha = 1f)
    {
        float pad = S(6f) * scale / 0.45f;
        float w = TextW(text, scale) + pad * 2f;
        float h = TextH(scale) + S(4f) * scale / 0.45f;
        Rect(x, cy - h * 0.5f, w, h, Palette.Black, 0.45f * alpha);
        Outline(x, cy - h * 0.5f, w, h, col, 0.75f * alpha, S(1f));
        TextMid(x + pad, cy, text, col, scale, alpha);
        return w;
    }

    public static float Width(PadButton b, float scale) =>
        IsFace(b) || IsDpad(b) ? TextH(scale) + S(4f) * scale / 0.45f : CapWidth(Name(b), scale);

    public static float CapWidth(string text, float scale) => TextW(text, scale) + S(6f) * scale / 0.45f * 2f;

    // A control as the device in use shows it. Returns how wide it was.
    public static float Prompt(float x, float cy, Control c, float scale, float alpha = 1f)
    {
        if (!Controls.PadActive)
            return Cap(x, cy, c.KeyLabel, scale, Palette.Text, alpha);
        if (c.Button == PadButton.None)
            return Cap(x, cy, "NONE", scale, Palette.Dim, alpha);

        return Draw(x, cy, c.Button, scale, alpha);
    }

    public static float PromptWidth(Control c, float scale)
    {
        if (!Controls.PadActive)
            return CapWidth(c.KeyLabel, scale);
        if (c.Button == PadButton.None)
            return CapWidth("NONE", scale);
        return Width(c.Button, scale);
    }

    // A menu hint: the pad button or the key, whichever is in use.
    public static float Hint(float x, float cy, PadButton b, string key, float scale, float alpha = 1f) =>
        Controls.PadActive ? Draw(x, cy, b, scale, alpha) : Cap(x, cy, key, scale, Palette.Text, alpha);

    public static float HintWidth(PadButton b, string key, float scale) =>
        Controls.PadActive ? Width(b, scale) : CapWidth(key, scale);

    private static void Symbol(float cx, float cy, float r, PadButton b, Rgba col, float alpha)
    {
        float t = MathF.Max(1f, r * 0.3f);
        switch (b)
        {
            case PadButton.A:
                Shapes.QueueLine(cx - r, cy - r, cx + r, cy + r, t);
                Shapes.QueueLine(cx - r, cy + r, cx + r, cy - r, t);
                Shapes.FlushLines(col, alpha);
                break;
            case PadButton.B:
                Shapes.Ring(cx, cy, r - t, r, col, alpha);
                break;
            case PadButton.X:
                Shapes.QueueLine(cx - r, cy - r, cx + r, cy - r, t);
                Shapes.QueueLine(cx + r, cy - r, cx + r, cy + r, t);
                Shapes.QueueLine(cx + r, cy + r, cx - r, cy + r, t);
                Shapes.QueueLine(cx - r, cy + r, cx - r, cy - r, t);
                Shapes.FlushLines(col, alpha);
                break;
            default:
                Shapes.QueueLine(cx, cy - r, cx + r, cy + r * 0.75f, t);
                Shapes.QueueLine(cx + r, cy + r * 0.75f, cx - r, cy + r * 0.75f, t);
                Shapes.QueueLine(cx - r, cy + r * 0.75f, cx, cy - r, t);
                Shapes.FlushLines(col, alpha);
                break;
        }
    }
}
