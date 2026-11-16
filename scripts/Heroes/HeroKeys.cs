namespace Trinity.Eclipse;

// The hero's one-press controls, from keys or pad buttons.
internal static class HeroKeys
{
    public static void Step()
    {
        if (Controls.Take(Act.Swap))
            Heroes.Next();

        switch (Heroes.Active)
        {
            case Hero.Superman:
                if (Controls.Take(Act.Fly))
                    Suit.I.Toggle();
                break;
            case Hero.Flash:
                if (Controls.Take(Act.Run))
                    Suit.I.PadRunPressed();
                break;
        }
    }
}
