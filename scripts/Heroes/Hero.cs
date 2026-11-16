namespace Trinity.Eclipse;

internal enum Hero
{
    Superman,
    Flash,
}

// Which hero's powers Batman has. Each hero has his own pages, binds, menu look and HUD.
internal static class Heroes
{
    public static readonly Hero[] All = { Hero.Superman, Hero.Flash };

    public static Hero Active => (Hero)Math.Clamp(Opt.HeroPick.Index, 0, All.Length - 1);

    // Powers on and this hero picked.
    public static bool Is(Hero hero) => Opt.Powers.On && Active == hero;

    public static string Name(Hero hero) => hero switch
    {
        Hero.Flash => "FLASH",
        _ => "SUPERMAN",
    };

    public static string Tagline(Hero hero) => hero switch
    {
        Hero.Flash => "FASTEST MAN ALIVE",
        _ => "MAN OF STEEL",
    };

    public static string Blurb(Hero hero) => hero switch
    {
        Hero.Flash => "Runs faster than anything in Gotham, throws lightning, slows time to take a crowd apart and rewinds when it goes wrong.",
        _ => "Flies, stops cars dead, punches them across the street and burns through thugs with heat vision.",
    };

    public static string[] Powers(Hero hero) => hero switch
    {
        Hero.Flash => new[] { "SUPER RUN", "LIGHTNING THROW", "BLITZ AND FLURRY", "SLOW TIME AND REWIND", "TORNADO" },
        _ => new[] { "FLIGHT", "HEAVY BODY", "FLYING PUNCHES AND THROWS", "HEAT VISION", "GROUND SLAM" },
    };

    public static List<Page> Pages(Hero hero) => hero == Hero.Flash ? Opt.FlashPages : Opt.SupermanPages;

    // Puts Batman in this hero's powers, with whatever the old hero had going ended first.
    public static void Become(Hero hero)
    {
        bool changed = Active != hero || !Opt.Powers.On;
        if (Active != hero)
            Suit.I.PowersOff();

        Opt.HeroPick.Index = (int)hero;
        if (!Opt.Powers.On)
        {
            Opt.Powers.On = true;
            Safe.Run("powers on", () => Opt.Powers.Changed?.Invoke());
        }

        Settings.MarkDirty();
        if (changed)
        {
            Rumble.Play(Buzz.Swap);
            Hud.ShowControls();
            Readout.Toast("ECLIPSE", Name(hero), true);
        }
    }

    // The next hero along, from its button or key.
    public static void Next()
    {
        int i = (int)Active;
        Become(All[(i + 1) % All.Length]);
    }
}
