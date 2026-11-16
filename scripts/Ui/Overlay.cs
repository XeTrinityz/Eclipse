using BmSDK.Engine;

namespace Trinity.Eclipse;

internal static class Overlay
{
    private const float MaxFrame = 0.1f;

    private static FrameTimer _frame;
    private static float _menuAnim;

    public static void Draw(Canvas? canvas)
    {
        if (Menu.IsOpen && (World.Paused || Shared.MenuOwner != Shared.OwnerName))
        {
            Menu.Close();
            _menuAnim = 0f;
        }

        Gfx.Dt = _frame.Step(MaxFrame);
        Gfx.Time += Gfx.Dt;
        Gfx.Frame++;
        Readout.Advance(Gfx.Dt);
        Safe.Run("menu", () => Menu.Tick(Gfx.Dt));

        float target = Menu.IsOpen ? 1f : 0f;
        float speed = Menu.IsOpen ? 5f : 8f;
        _menuAnim = _menuAnim < target ? Math.Min(target, _menuAnim + Gfx.Dt * speed) : Math.Max(target, _menuAnim - Gfx.Dt * speed);

        bool marks = Marks.Showing && _menuAnim <= 0f;
        bool hud = Hud.Showing && _menuAnim <= 0f;
        if (canvas == null || (!Readout.Showing && !marks && !hud && _menuAnim <= 0f))
            return;

        if (!Gfx.Begin(canvas, out Font? font))
            return;

        try
        {
            Gfx.Alpha = 1f;
            if (marks)
                Safe.Run("marks", () => Marks.Draw(canvas));
            if (hud)
                Safe.Run("hud", () => Hud.Draw(canvas));
            if (_menuAnim > 0f)
                Safe.Run("menu view", () => MenuView.Draw(_menuAnim));
            Gfx.Alpha = 1f;
            if (Readout.Showing)
                Readout.Draw();
        }
        finally
        {
            Gfx.Alpha = 1f;
            canvas.Font = font;
        }
    }
}
