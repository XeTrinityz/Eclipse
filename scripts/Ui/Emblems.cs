namespace Trinity.Eclipse;

// The hero crests, drawn from points on a 100 unit square. size is how big that square is on screen.
internal static class Emblems
{
    public static readonly Rgba Ice = new(232, 244, 255);
    public static readonly Rgba Sky = new(159, 210, 255);
    public static readonly Rgba Cape = new(216, 52, 44);
    public static readonly Rgba Gold = new(255, 199, 44);
    public static readonly Rgba Crimson = new(200, 16, 46);
    public static readonly Rgba Cream = new(255, 244, 224);

    // A shield with five sides, a white rim and a red middle.
    public static void Shield(float x, float y, float size, float alpha, bool filled = true)
    {
        float k = size / 100f;
        float t = MathF.Max(1.5f, 5f * k);
        Shapes.Loop(t, Ice, alpha, P(x, y, k, 22, 12, 78, 12, 96, 34, 50, 92, 4, 34));
        if (filled)
            Shapes.Fill(Cape, alpha, P(x, y, k, 34, 24, 66, 24, 78, 36, 50, 72, 22, 36));
        else
            Shapes.Loop(MathF.Max(1f, 2f * k), Cape, alpha, P(x, y, k, 30, 20, 70, 20, 84, 36, 50, 80, 16, 36));
    }

    // A lightning bolt on a white disc.
    public static void Bolt(float x, float y, float size, float alpha, bool disc = true)
    {
        float k = size / 100f;
        if (disc)
            Shapes.Disc(x + 50f * k, y + 50f * k, 45f * k, Cream, alpha);
        else
            Shapes.Ring(x + 50f * k, y + 50f * k, 43f * k, 45f * k, Gold, alpha);
        BoltOnly(x, y, size, Gold, alpha);
    }

    public static void BoltOnly(float x, float y, float size, Rgba col, float alpha)
    {
        float k = size / 100f;
        Shapes.Fill(col, alpha, P(x, y, k, 58, 6, 66, 6, 54, 40, 48, 54, 26, 54));
        Shapes.Fill(col, alpha, P(x, y, k, 48, 54, 54, 40, 76, 40, 38, 94));
    }

    private static float[] P(float x, float y, float k, params float[] pts)
    {
        var o = new float[pts.Length];
        for (int i = 0; i < pts.Length; i += 2)
        {
            o[i] = x + pts[i] * k;
            o[i + 1] = y + pts[i + 1] * k;
        }

        return o;
    }
}
