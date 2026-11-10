namespace Trinity.Eclipse;

internal static class KeyInput
{
    public static void Pressed(Keys key)
    {
        Pad.KeyboardUsed();

        if (Menu.IsOpen)
        {
            Menu.Press(key, false);
            return;
        }

        if (!Shared.ScreenFree)
            return;

        if (key == Opt.MenuKey.Key)
        {
            Menu.Toggle();
            return;
        }

        if (!World.InGame || World.Paused)
            return;

        if (key == Opt.FlyKey.Key)
            Suit.I.Toggle();
    }
}
