namespace Trinity.Eclipse;

internal readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba Mix(Rgba a, Rgba b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Rgba((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
    }
}

internal static class Palette
{
    // Corona gold, with ember for the outer glow and warnings.
    public static readonly Rgba Hud = new(255, 184, 76);
    public static readonly Rgba Flare = new(255, 240, 214);
    public static readonly Rgba Ember = new(236, 96, 46);
    public static readonly Rgba Text = new(232, 222, 210);
    public static readonly Rgba Dim = new(150, 128, 104);
    public static readonly Rgba Faint = new(92, 80, 70);
    public static readonly Rgba Caution = new(255, 92, 66);
    public static readonly Rgba Void = new(6, 5, 5);
    public static readonly Rgba Screen = new(8, 6, 6, 236);
    public static readonly Rgba Black = new(0, 0, 0);
}
