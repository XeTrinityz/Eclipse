namespace Trinity.Eclipse;

internal static class KeyInput
{
    public static void Pressed(Keys key)
    {
        Controls.KeyboardUsed();

        if (Menu.IsOpen)
        {
            Menu.Press(key);
            return;
        }

        if (!Shared.ScreenFree)
            return;

        if (key == Opt.MenuKey.Key)
        {
            Menu.Toggle();
            return;
        }

        if (World.InGame && !World.Paused)
            Controls.KeyPressed(key);
    }
}
