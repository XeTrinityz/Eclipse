using System.Numerics;
using BmSDK;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Projects world points onto the screen from a camera read once a frame. From Oracle.
internal static class WorldView
{
    private const float ProbeAhead = 1000f;
    private const float ProbeSide = 250f;
    private const float NearPlane = 10f;

    private static float _cx, _cy, _kx, _ky;
    private static int _frame = -1;

    public static bool Ready { get; private set; }

    public static Vector3 Eye { get; private set; }
    public static Vector3 Forward { get; private set; }
    public static Vector3 Right { get; private set; }
    public static Vector3 Up { get; private set; }

    public static float Width => _cx * 2f;
    public static float Height => _cy * 2f;

    public static void Begin(Canvas canvas)
    {
        if (Ready && _frame == Gfx.Frame)
            return;
        _frame = Gfx.Frame;

        Ready = false;
        if (World.Pc is not { } pc || !ReadCamera(pc))
            return;

        _cx = canvas.ClipX * 0.5f;
        _cy = canvas.ClipY * 0.5f;
        if (_cx <= 0f || _cy <= 0f)
            return;

        // The probe is a quarter of the way out, so its offset is the scale times 0.25.
        float ratio = ProbeSide / ProbeAhead;
        Vector3 s = canvas.Project(Eye + Forward * ProbeAhead + Right * ProbeSide + Up * ProbeSide);
        float kx = (s.X - _cx) / ratio;
        float ky = (_cy - s.Y) / ratio;
        if (float.IsFinite(kx) && float.IsFinite(ky) && kx > 1f && ky > 1f && kx < _cx * 50f && ky < _cx * 50f)
        {
            _kx = kx;
            _ky = ky;
        }
        else
        {
            float fov = Math.Clamp(pc.GetFOVAngle(), 5f, 170f);
            _kx = _ky = _cx / MathF.Tan(fov * 0.5f * MathF.PI / 180f);
        }

        Ready = true;
    }

    private static bool ReadCamera(PlayerController pc)
    {
        pc.GetPlayerViewPoint(out Vector3 eye, out Rotator rot);
        Eye = eye;
        Geo.Axes(rot, out Vector3 forward, out Vector3 right, out Vector3 up);
        Forward = forward;
        Right = right;
        Up = up;
        return float.IsFinite(Forward.X);
    }

    public static bool ToScreen(Vector3 world, out Vector2 screen)
    {
        Vector3 v = world - Eye;
        float z = Vector3.Dot(v, Forward);
        if (!Ready || z < NearPlane)
        {
            screen = default;
            return false;
        }

        screen = new Vector2(_cx + _kx * Vector3.Dot(v, Right) / z, _cy - _ky * Vector3.Dot(v, Up) / z);
        return true;
    }

    public static bool OnScreen(Vector2 p, float margin = 0f) =>
        p.X >= -margin && p.Y >= -margin && p.X <= Width + margin && p.Y <= Height + margin;

    public static bool Bounds(Vector3 min, Vector3 max, out float x0, out float y0, out float x1, out float y1)
    {
        x0 = y0 = float.MaxValue;
        x1 = y1 = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
            if (!ToScreen(c, out Vector2 p))
                return false;

            x0 = MathF.Min(x0, p.X);
            y0 = MathF.Min(y0, p.Y);
            x1 = MathF.Max(x1, p.X);
            y1 = MathF.Max(y1, p.Y);
        }

        return x1 >= 0f && y1 >= 0f && x0 <= Width && y0 <= Height;
    }
}
