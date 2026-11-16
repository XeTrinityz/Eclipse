using BmSDK.Engine;
using FLinearColor = BmSDK.GameObject.FLinearColor;

namespace Trinity.Eclipse;

internal static class Gfx
{
    private const int MaxCachedWidths = 4096;
    private const float DecodeSeconds = 0.32f;
    private const string Glyphs = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789#%/<>";
    private const string FontPath = "BmFonts.EngineSmallFont";
    private const string WhitePath = "EngineResources.WhiteSquareTexture";

    private static readonly float[] Linear = BuildLinear();
    private static readonly Dictionary<(string, float), float> _widths = new();
    private static readonly Dictionary<float, float> _heights = new();
    private static Texture2D? _white;
    private static Font? _font;

    public static Canvas C = null!;
    public static float Scale = 1f;
    public static float Alpha = 1f;
    public static float Dt;
    public static double Time;
    public static int Frame;

    public static float W => C.ClipX;

    public static float H => C.ClipY;

    public static float S(float v) => v * Scale;

    public static Texture2D? WhiteTexture => _white is { IsValid: true } ? _white : null;

    public static bool Begin(Canvas canvas, out Font? previous)
    {
        previous = null;
        if (_white is not { IsValid: true })
            _white = Game.FindObject<Texture2D>(WhitePath);
        if (_font is not { IsValid: true })
            _font = Game.FindObject<Font>(FontPath);
        if (_white == null)
            return false;

        C = canvas;
        Scale = Math.Clamp(canvas.ClipY / 1080f, 0.75f, 2.5f);
        Alpha = 1f;
        previous = canvas.Font;
        if (_font != null)
            canvas.Font = _font;
        return true;
    }

    public static void Rect(float x, float y, float w, float h, Rgba col, float alpha = 1f)
    {
        if (w <= 0f || h <= 0f)
            return;

        C.SetPos(MathF.Round(x), MathF.Round(y), 0f);
        var color = new FLinearColor { R = Linear[col.R], G = Linear[col.G], B = Linear[col.B], A = col.A / 255f * alpha * Alpha };
        C.DrawTile(_white!, MathF.Round(w), MathF.Round(h), 0f, 0f, 32f, 32f, color, false, EngineTypes.EBlendMode.BLEND_Translucent);
    }

    public static void Outline(float x, float y, float w, float h, Rgba col, float alpha, float t = 1f)
    {
        t = MathF.Max(1f, MathF.Round(t));
        Rect(x, y, w, t, col, alpha);
        Rect(x, y + h - t, w, t, col, alpha);
        Rect(x, y + t, t, h - t * 2f, col, alpha);
        Rect(x + w - t, y + t, t, h - t * 2f, col, alpha);
    }

    // A see-through box leaning right by lean across its height, drawn in thin bands since only boxes can fade.
    public static void Slant(float x, float y, float w, float h, float lean, Rgba col, float alpha = 1f)
    {
        float band = MathF.Max(1f, MathF.Round(S(2f)));
        for (float yy = 0f; yy < h; yy += band)
        {
            float bh = MathF.Min(band, h - yy);
            float shift = lean * (1f - (yy + bh * 0.5f) / h);
            Rect(x + shift, y + yy, w, bh, col, alpha);
        }
    }

    // A box with its corners cut off, top left and bottom right, see-through like Slant.
    public static void Cut(float x, float y, float w, float h, float cut, Rgba col, float alpha = 1f)
    {
        float band = MathF.Max(1f, MathF.Round(S(2f)));
        for (float yy = 0f; yy < h; yy += band)
        {
            float bh = MathF.Min(band, h - yy);
            float mid = yy + bh * 0.5f;
            float left = mid < cut ? cut - mid : 0f;
            float right = mid > h - cut ? mid - (h - cut) : 0f;
            Rect(x + left, y + yy, w - left - right, bh, col, alpha);
        }
    }

    public static float TextW(string text, float scale)
    {
        if (_widths.Count >= MaxCachedWidths)
            _widths.Clear();

        float size = scale * Scale;
        if (_widths.TryGetValue((text, size), out float w))
            return w;

        C.TextSize(text, out w, out _, size, size);
        _widths[(text, size)] = w;
        return w;
    }

    public static float TextH(float scale)
    {
        float size = scale * Scale;
        if (_heights.TryGetValue(size, out float h))
            return h;

        C.TextSize("AQ", out _, out h, size, size);
        _heights[size] = h;
        return h;
    }

    public static void Text(float x, float y, string text, Rgba col, float scale, float alpha = 1f)
    {
        C.SetDrawColor(col.R, col.G, col.B, (byte)Math.Clamp(col.A * alpha * Alpha, 0f, 255f));
        C.SetPos(MathF.Round(x), MathF.Round(y), 0f);
        float size = scale * Scale;
        C.DrawText(text, false, size, size, out _);
    }

    public static void TextMid(float x, float cy, string text, Rgba col, float scale, float alpha = 1f) =>
        Text(x, cy - TextH(scale) * 0.5f, text, col, scale, alpha);

    public static float TextRight(float right, float cy, string text, Rgba col, float scale, float alpha = 1f)
    {
        float w = TextW(text, scale);
        TextMid(right - w, cy, text, col, scale, alpha);
        return w;
    }

    public static void TextCentre(float cx, float cy, string text, Rgba col, float scale, float alpha = 1f) =>
        TextMid(cx - TextW(text, scale) * 0.5f, cy, text, col, scale, alpha);

    public static float Spaced(float x, float y, string text, Rgba col, float scale, float spacing, float alpha = 1f)
    {
        float start = x;
        foreach (char ch in text)
        {
            string letter = ch.ToString();
            Text(x, y, letter, col, scale, alpha);
            x += TextW(letter, scale) + S(spacing);
        }

        return x - start - S(spacing);
    }

    public static float SpacedW(string text, float scale, float spacing)
    {
        float w = 0f;
        foreach (char ch in text)
            w += TextW(ch.ToString(), scale) + S(spacing);
        return w - S(spacing);
    }

    public static string FitLine(string text, float width, float scale, out string line)
    {
        if (TextW(text, scale) <= width)
        {
            line = text;
            return "";
        }

        int cut = text.Length;
        while (cut > 0)
        {
            int space = text.LastIndexOf(' ', cut - 1);
            if (space <= 0)
                break;

            cut = space;
            if (TextW(text.Substring(0, cut), scale) <= width)
                break;
        }

        line = text.Substring(0, cut);
        return text.Substring(cut).TrimStart();
    }

    // Text that scrambles in from the left over its first moments on screen.
    public static string Decode(string text, float age)
    {
        if (age >= DecodeSeconds || text.Length == 0)
            return text;

        int resolved = Math.Max(0, (int)(text.Length * (age / DecodeSeconds)));
        char[] c = text.ToCharArray();
        int tick = (int)(Time * 28.0);
        for (int i = resolved; i < c.Length; i++)
        {
            if (c[i] != ' ')
                c[i] = Glyphs[(int)((uint)(i * 7919 + tick * 104729 + text.Length * 31) % (uint)Glyphs.Length)];
        }

        return new string(c);
    }

    public static float Follow(float value, float target, float rate) =>
        value < 0f ? target : value + (target - value) * Ease.Follow(rate, Dt);

    private static float[] BuildLinear()
    {
        var table = new float[256];
        for (int i = 0; i < table.Length; i++)
            table[i] = MathF.Pow(i / 255f, 2.2f);
        return table;
    }
}
