using static Trinity.Eclipse.Gfx;

namespace Trinity.Eclipse;

// Speed and height gauge while flying or super running.
internal static class Gauge
{
    private const float BarW = 150f;
    // The bar's full width is this many times his boost speed.
    private const float BarSpan = 1.6f;

    private static float _speed = -1f, _height = -1f;
    private static int _lastFrame = -2;

    public static bool Showing => Opt.Gauge.On && (Suit.I.Flying || Suit.I.Running);

    public static void Draw()
    {
        // Back after a gap, so start from the real numbers.
        if (Gfx.Frame - _lastFrame > 1)
            _speed = _height = -1f;
        _lastFrame = Gfx.Frame;

        float height = Suit.I.AirHeight / 100f;
        _speed = Follow(_speed, Suit.I.GaugeSpeed / 100f, 10f);
        _height = height < 0f ? -1f : Follow(_height, height, 8f);

        float right = W - S(48), y = H - S(96);
        float bw = S(BarW), bh = MathF.Max(1f, S(2));
        float top = MathF.Max(1f, Suit.I.GaugeMark);
        float fill = Math.Clamp(_speed / (top * BarSpan), 0f, 1f);
        bool past = _speed > top * 1.03f;

        Rect(right - bw, y, bw, bh, Palette.Dim, 0.5f);
        Rect(right - bw, y, bw * fill, bh, past ? Palette.Flare : Palette.Hud, 0.9f);
        Rect(MathF.Round(right - bw + bw / BarSpan), y - S(3), MathF.Max(1f, S(1)), bh + S(6), Palette.Dim, 0.9f);

        TextRight(right, y - S(13), Fmt.Whole(_speed * 3.6f) + " KM/H", past ? Palette.Flare : Palette.Text, 0.5f);
        if (!Suit.I.Flying)
            return;
        TextMid(right - bw, y + S(13), "HEIGHT", Palette.Dim, 0.38f, 0.9f);
        TextRight(right, y + S(13), _height < 0f ? "---" : Fmt.Whole(_height) + " M", Palette.Text, 0.38f, 0.9f);
    }
}
