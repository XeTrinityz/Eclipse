namespace Trinity.Eclipse;

internal static class Opt
{
    public static readonly List<Page> Pages = new();
    public static readonly List<Option> All = new();
    public static readonly List<Page> SupermanPages = new();
    public static readonly List<Page> FlashPages = new();
    public static readonly Dictionary<Hero, Dictionary<Act, Control>> Binds = new();

    public static Switch Powers = null!;
    public static Pick HeroPick = null!;
    public static Dial MenuSize = null!;
    public static Switch Effects = null!;

    public static Switch Ram = null!;
    public static Dial RamForce = null!;
    public static Switch Heavy = null!;
    public static Switch Invincible = null!;
    public static Switch NoHit = null!;
    public static Switch NoStun = null!;
    public static Pick SuperPunch = null!;
    public static Pick HoldStyle = null!;
    public static Pick HoldPart = null!;
    public static Dial PunchForce = null!;
    public static Dial PowerForce = null!;

    public static Dial MoveSpeed = null!;
    public static Dial BoostSpeed = null!;
    public static Dial Coast = null!;
    public static Dial ClimbSpeed = null!;
    public static Dial PunchTime = null!;
    public static Switch HitStop = null!;
    public static Switch TargetMarks = null!;
    public static Switch Tilt = null!;
    public static Dial Bank = null!;
    public static Switch HoverDrift = null!;
    public static Pick Lean = null!;

    public static Switch SuperRun = null!;
    public static Dial RunSpeed = null!;
    public static Switch SpeedVision = null!;
    public static Dial WorldSpeed = null!;
    public static Pick FlashPunch = null!;
    public static Switch Blitz = null!;
    public static Switch Flurry = null!;
    public static Switch SlowTime = null!;
    public static Switch Rewind = null!;
    public static Switch InfiniteForce = null!;
    public static Listed FlashSuit = null!, SupermanSuit = null!;
    public static Switch FlashCape = null!, SupermanCape = null!;
    public static Dial BlitzSpeed = null!;
    public static Dial LightningForce = null!;
    public static Switch Tornado = null!;
    public static Switch RunLightning = null!;
    public static Dial FlashPunchForce = null!;

    public static Switch Hud = null!;
    public static Switch Legend = null!;
    public static Switch LegendFade = null!;
    public static Switch FlightFx = null!;
    public static Switch FlightSounds = null!;

    public static Switch Camera = null!;
    public static Dial CamDistance = null!;
    public static Dial CamFastDistance = null!;

    public static Bind MenuKey = null!;
    public static Switch Controller = null!;
    public static Pick ButtonStyle = null!;
    public static Switch Rumble = null!;
    public static Pick PadMenu = null!;

    // The power switches only count while Superman is on.
    public static bool HeavyOn => Heroes.Is(Hero.Superman) && Heavy.On;

    public static bool RamOn => Heroes.Is(Hero.Superman) && Ram.On;

    public static bool InvincibleOn => Heroes.Is(Hero.Superman) && Invincible.On;

    public static bool NoHitOn => Heroes.Is(Hero.Superman) && NoHit.On;

    public static bool NoStunOn => Heroes.Is(Hero.Superman) && NoStun.On;

    public static string SuperPunchMode => !Opt.Powers.On ? "off" : Heroes.Active == Hero.Flash ? FlashPunch.Key : SuperPunch.Key;

    public static float PunchForceNow => Heroes.Active == Hero.Flash ? FlashPunchForce.Number : PunchForce.Number;

    public static void Build()
    {
        Pages.Clear();
        All.Clear();
        SupermanPages.Clear();
        FlashPages.Clear();
        Binds.Clear();

        BuildSuperman();
        BuildFlash();
        BuildShared();

        PadMenu.Changed = Controls.HoldsChanged;
        Controller.Changed = Controls.HoldsChanged;
        Rumble.Changed = () =>
        {
            if (Rumble.On)
                Eclipse.Rumble.Play(Buzz.Swap);
        };
        Powers.Changed = () =>
        {
            if (!Powers.On)
                Suit.I.PowersOff();
            Controls.HoldsChanged();
        };
        HeroPick.Changed = () =>
        {
            Suit.I.PowersOff();
            Controls.HoldsChanged();
        };
    }

    private static void BuildSuperman()
    {
        const Hero h = Hero.Superman;
        Page powers = Add(h, "superman_powers", "POWERS", "What he can do, on foot and in the air.");
        Ram = Add(powers, "ram", "RAM", true,
            "He hits like the Batmobile. Flying into thugs launches them, cars and drones get knocked aside and hurt, props break. Slow, or on foot, it's a shove that clears cars and props out of his way.");
        RamForce = Add(powers, "ram_force", "RAM FORCE", 1f, 0.25f, 3f, 0.05f, Fmt.Times,
            "How hard he hits things he flies into, punches or slams down beside.");
        Heavy = Add(powers, "heavy", "HEAVY", true,
            "He weighs what the Batmobile weighs. Cars that drive into him stop dead and get dented. Cars, wrecks and loose props he walks or flies into get shoved out of the way.");
        Invincible = Add(powers, "invincible", "INVINCIBLE", true,
            "He takes no damage and his health stays full.");
        NoHit = Add(powers, "no_hit", "NO HIT REACTIONS", true,
            "Hits don't stagger him or knock him back. Thugs who charge him or grab him bounce off and go down.");
        NoStun = Add(powers, "no_stun", "NO STUNS", true,
            "Stun batons, zaps and laser grids don't daze him.");
        SuperPunch = Add(powers, "super_punch", "SUPER PUNCH ON FOOT", new[] { "every", "knockouts", "off" }, new[] { "EVERY HIT", "KNOCKOUTS", "OFF" },
            "His hits on foot launch thugs. KNOCKOUTS is only the hit that puts one down, so fights keep flowing.");
        PunchForce = Add(powers, "punch_force", "PUNCH FORCE", 1f, 0.25f, 3f, 0.05f, Fmt.Times,
            "How far his punches and throws send thugs.");
        PowerForce = Add(powers, "power_force", "HEAT VISION FORCE", 1f, 0.25f, 3f, 0.05f, Fmt.Times,
            "How far heat vision throws the thugs it burns, and how hard it pushes wrecks and loose things.");

        Page flight = Add(h, "superman_flight", "FLIGHT", "How he flies.");
        MoveSpeed = Add(flight, "move_speed", "MOVE SPEED", 12f, 4f, 30f, 1f, v => Fmt.Whole(v) + " M/S",
            "How fast he hovers around.");
        BoostSpeed = Add(flight, "boost_speed", "BOOST SPEED", 40f, 15f, 90f, 5f, v => Fmt.Whole(v) + " M/S",
            "Top speed while boosting. A dive or a sonic burst takes him past it for a while.");
        Coast = Add(flight, "coast", "COAST", 1.2f, 0.3f, 4f, 0.1f, Fmt.Secs,
            "Seconds to slow from top speed to hover speed when you let go of boost. Pulling back brakes much harder.");
        ClimbSpeed = Add(flight, "climb_speed", "CLIMB SPEED", 8f, 2f, 20f, 1f, v => Fmt.Whole(v) + " M/S",
            "How fast he goes up and down while hovering.");
        Tilt = Add(flight, "tilt", "TILT", true, "In the fast pose his body leans up or down with the way he's flying.");
        Bank = Add(flight, "bank", "BANK", 1f, 0f, 2f, 0.05f, Fmt.Times,
            "How far he rolls into turns in fast flight. 0 keeps him level.");
        HoverDrift = Add(flight, "hover_drift", "HOVER DRIFT", true,
            "He bobs and sways a little while hovering, leans into his moves and banks into turns. Turn off to hold him dead still.");
        Lean = Add(flight, "lean", "LEAN", new[] { "into", "against" }, new[] { "INTO", "AGAINST" },
            "Which way he leans when he moves while hovering. INTO tips him the way he goes, AGAINST tips him back like he's braced.");

        Page combat = Add(h, "superman_combat", "COMBAT", "Fighting in the air.");
        PunchTime = Add(combat, "punch_time", "PUNCH TIME", 0.7f, 0.3f, 1.5f, 0.05f, Fmt.SecsFine,
            "How long a punch takes in flight. The fist lands half way through. Shorter is snappier, longer is easier to see.");
        HitStop = Add(combat, "hit_stop", "HIT STOP", true,
            "Each punch that lands in flight slows the game for a blink, longer on a heavy one. A ground slam does it too.");
        HoldStyle = Add(combat, "hold_style", "THUG HOLD", new[] { "ragdoll", "animation" }, new[] { "RAGDOLL", "ANIMATION" },
            "How he holds a thug. RAGDOLL knocks him limp and carries him on a physics handle. ANIMATION keeps him standing in a grab animation.");
        HoldPart = Add(combat, "hold_part", "HOLD PART", new[] { "neck", "chest", "hips", "leg" }, new[] { "NECK", "CHEST", "HIPS", "LEG" },
            "Where he takes hold of a ragdolled thug. LEG hangs him upside down. Only for the ragdoll hold.");
        TargetMarks = Add(combat, "target_marks", "TARGET MARKS", true,
            "While flying, brackets the thug or vehicle nearest the middle of the screen, with what punch and grab do to it.");

        Page supermanLook = Add(h, "superman_look", "LOOK", "The suit Batman wears as Superman.");
        SupermanSuit = AddSuit(supermanLook, "superman_suit");
        SupermanCape = Add(supermanLook, "superman_cape", "CAPE", true,
            "Shows or hides the cape while he's Superman. It goes back as it was when no hero is on.");

        Page keys = Add(h, "superman_controls", "CONTROLS", "Superman's keys and buttons. Batman's gadgets are off while a hero is on, so their buttons are free.");
        Ctl(keys, h, Act.Fly, "fly_key", "FLY", Keys.J, null, PadButton.RB, Use.Foot,
            "Takes off. From the ground he kicks off into a hover. The key also stops flight. On a pad, hold the drop button to land.");
        Ctl(keys, h, Act.Heat, "fire_key", "HEAT VISION", Keys.MButton, null, PadButton.RT, Use.Foot,
            "Hold for heat vision. Thugs and cars it touches are destroyed and it leaves fire where it rests. The key works flying too.");
        Ctl(keys, h, Act.HeatAir, "heat_air", "HEAT VISION IN FLIGHT", Keys.None, "SAME KEY", PadButton.LB, Use.Flight,
            "Hold for heat vision while flying. The triggers climb and sink up there, so it has its own button.");
        Ctl(keys, h, Act.Swap, "superman_swap", "SWITCH HERO", Keys.H, null, PadButton.Down, Use.Any,
            "Switches to the next hero.");
        Ctl(keys, h, Act.Punch, "punch_key", "PUNCH", Keys.LButton, null, PadButton.X, Use.Flight,
            "In flight, punches the bracketed target, flying to it first if it's out of reach. Tap to keep punching, hold to wind up a heavy punch.");
        Ctl(keys, h, Act.Grab, "grab_key", "GRAB AND THROW", Keys.RButton, null, PadButton.Y, Use.Flight,
            "In flight, picks up the bracketed thug or vehicle. Press again to throw it where you look or at another bracketed target.");
        Ctl(keys, h, Act.Boost, "boost", "BOOST", Keys.None, "HOLD SHIFT", PadButton.RB, Use.Flight,
            "Hold for fast flight, steered with the camera.");
        Ctl(keys, h, Act.Burst, "burst", "BURST UP", Keys.None, "SPACE TWICE", PadButton.A, Use.Flight,
            "Hovering, a quick burst upwards. Boosting, it pulls his nose up.");
        Ctl(keys, h, Act.Drop, "drop", "DROP AND SLAM", Keys.None, "CTRL TWICE", PadButton.B, Use.Flight,
            "Close to the ground, a quick drop. Boosting, a dive. A few metres up or more, a ground slam. Hold it on a pad to land.");
        Ctl(keys, h, Act.Dash, "dash", "DASH AND SONIC BURST", Keys.Q, null, PadButton.Up, Use.Flight,
            "Hovering, a dash the way you push. Boosting, a sonic burst past his top speed. W A S D twice does it on keys too.", PadButton.LS);
        Ctl(keys, h, Act.SideLeft, "side_left", "SIDE LEFT", Keys.None, "A TWICE", PadButton.Left, Use.Flight,
            "Hovering, a dash to the left. Boosting, a barrel roll to the left.");
        Ctl(keys, h, Act.SideRight, "side_right", "SIDE RIGHT", Keys.None, "D TWICE", PadButton.Right, Use.Flight,
            "Hovering, a dash to the right. Boosting, a barrel roll to the right.");
        Ctl(keys, h, Act.Roll, "roll", "ROLL OR SIDE DASH", Keys.E, null, PadButton.None, Use.Flight,
            "On keys, a barrel roll while boosting and a side dash while hovering, toward the way you push.", PadButton.RS);
    }

    private static void BuildFlash()
    {
        const Hero h = Hero.Flash;
        Page powers = Add(h, "flash_powers", "POWERS", "What he can do on his feet.");
        FlashPunch = Add(powers, "flash_super_punch", "SPEED PUNCH", new[] { "knockouts", "every", "off" }, new[] { "KNOCKOUTS", "EVERY HIT", "OFF" },
            "His punches on foot land at speed and launch thugs. KNOCKOUTS is only the hit that puts one down.");
        FlashPunchForce = Add(powers, "flash_punch_force", "PUNCH FORCE", 0.7f, 0.25f, 3f, 0.05f, Fmt.Times,
            "How far his speed punches send thugs.");

        Page run = Add(h, "flash_run", "RUN", "The super run.");
        SuperRun = Add(run, "super_run", "SUPER RUN", true,
            "He breaks into a run that keeps building speed, turns wide at speed and skids when he stops.");
        RunSpeed = Add(run, "run_speed", "TOP SPEED", 35f, 15f, 80f, 5f, v => Fmt.Whole(v) + " M/S",
            "How fast he runs flat out. It takes a couple of seconds to get there.");
        SpeedVision = Add(run, "speed_vision", "SPEED VISION", true,
            "Once he's running fast the world slows down round him, while he keeps going at full speed.");
        WorldSpeed = Add(run, "world_speed", "WORLD SPEED", 0.3f, 0.1f, 0.7f, 0.05f, Fmt.Times,
            "How fast the world goes during speed vision. Lower is slower.");
        Tornado = Add(run, "tornado", "TORNADO", true,
            "Press the tornado button and he runs a tight circle round where he is for a few seconds, lifting the thugs, cars and loose things near him, then flings them out.");
        RunLightning = Add(run, "run_lightning", "RUN LIGHTNING", true,
            "While he runs fast, lightning jumps off him and knocks away the thugs, cars and loose things he passes. Lightning force sets how hard.");

        Page force = Add(h, "flash_force", "SPEED FORCE", "The blitz and the lightning.");
        Blitz = Add(force, "blitz", "BLITZ", true,
            "Hold the blitz button and time all but stops. Mark thugs on the crosshair with the strike button, or mark none and he picks the nearest. Let go and he hits each one, comes back to where he stood, and time snaps back as they all go flying. Started while running, he goes straight in on the nearest thugs. Uses one charge. Charges come back while he runs fast.");
        Flurry = Add(force, "flurry", "FLURRY", true,
            "The flurry button makes him hit the thug in front of him dozens of times in a second and a half, then launch him. On foot or running.");
        SlowTime = Add(force, "slow_time", "SLOW TIME", true,
            "Press slow time and the world all but stops for up to 6 seconds while he fights at full speed. Every hit is kept, and when time comes back each thug he struck takes them all at once. Press again to end it early.");
        InfiniteForce = Add(force, "infinite_speed_force", "INFINITE SPEED FORCE", false,
            "Keeps all three charges full, so the blitz and rewind never run out. Cooldowns still apply.");
        Rewind = Add(force, "rewind", "REWIND", true,
            "Press rewind and he streaks back along the path he took to where he was 3 seconds ago. Only he moves. Uses one charge.");
        BlitzSpeed = Add(force, "blitz_world_speed", "BLITZ WORLD SPEED", 0.08f, 0.03f, 0.3f, 0.01f, Fmt.Times,
            "How fast the world goes during a blitz. Lower is closer to stopped.");
        LightningForce = Add(force, "lightning_force", "LIGHTNING FORCE", 1f, 0.25f, 3f, 0.05f, Fmt.Times,
            "How hard his lightning and the tornado's last throw hit.");

        Page flashLook = Add(h, "flash_look", "LOOK", "The suit Batman wears as the Flash.");
        FlashSuit = AddSuit(flashLook, "flash_suit");
        FlashCape = Add(flashLook, "flash_cape", "CAPE", false,
            "Shows or hides the cape while he's the Flash. It goes back as it was when no hero is on.");

        Page keys = Add(h, "flash_controls", "CONTROLS", "The Flash's keys and buttons. Batman's gadgets are off while a hero is on, so their buttons are free.");
        Ctl(keys, h, Act.Run, "run", "SUPER RUN", Keys.None, "HOLD SHIFT", PadButton.RB, Use.Foot,
            "Starts the super run. On a pad he keeps running until you skid or press it again, so only the stick has to be held.");
        Ctl(keys, h, Act.Skid, "skid", "SKID", Keys.None, "LET GO OF SHIFT", PadButton.B, Use.Run,
            "Skids him to a stop.");
        Ctl(keys, h, Act.Lightning, "lightning", "LIGHTNING", Keys.MButton, null, PadButton.RT, Use.Foot,
            "Hold to charge lightning in his hand, let go to throw it where you look. It strikes the thug or car near the crosshair and blasts what's round it.");
        Ctl(keys, h, Act.Blitz, "blitz_key", "BLITZ", Keys.Q, null, PadButton.LT, Use.Foot,
            "Hold to stop time and mark thugs, let go to strike them all. The cape button calls it off.");
        Ctl(keys, h, Act.Flurry, "flurry_key", "FLURRY", Keys.F, null, PadButton.Right, Use.Foot,
            "A flurry of punches too fast to see on the thug in front of him, then a hit that launches him. Works on foot and running.");
        Ctl(keys, h, Act.Slow, "slow_key", "SLOW TIME", Keys.V, null, PadButton.Left, Use.Foot,
            "Slows the world while he fights. His hits all land when it ends. Press again to end it early.");
        Ctl(keys, h, Act.Rewind, "rewind_key", "REWIND", Keys.R, null, PadButton.Up, Use.Foot,
            "Back along his path to where he was 3 seconds ago. On foot or running. Uses one charge.");
        Ctl(keys, h, Act.Tornado, "tornado_key", "TORNADO", Keys.G, null, PadButton.Y, Use.Run,
            "While running, a tornado round where he is. The key works on foot too.");
        Ctl(keys, h, Act.Swap, "flash_swap", "SWITCH HERO", Keys.H, null, PadButton.Down, Use.Any,
            "Switches to the next hero.");
    }

    private static void BuildShared()
    {
        Page camera = Add(null, "camera", "CAMERA", "The camera for flight and the super run.");
        Camera = Add(camera, "camera", "OWN CAMERA", true,
            "Our own camera while flying or super running. Close over his shoulder when slow, further back and wider at speed. Turn off for the game's camera.");
        CamDistance = Add(camera, "hover_distance", "SLOW DISTANCE", 3.2f, 1.5f, 8f, 0.1f, v => Fmt.Tenths(v) + " M",
            "How far behind him the camera sits while hovering or running slowly.");
        CamFastDistance = Add(camera, "fast_distance", "FAST DISTANCE", 5.5f, 2f, 12f, 0.1f, v => Fmt.Tenths(v) + " M",
            "How far behind him the camera sits at top speed.");

        Page pad = Add(null, "controller", "CONTROLLER", "Controller support and the keys every hero shares.");
        Controller = Add(pad, "controller", "CONTROLLER", true, "Controller support. Turn off if the mod picks up a pad you don't want it to.");
        PadMenu = Add(pad, "pad_menu", "MENU BUTTON", new[] { "hold_menu", "off" }, new[] { "HOLD MENU", "OFF" },
            "Hold the MENU button to open Eclipse. A quick tap still pauses the game.");
        ButtonStyle = Add(pad, "button_style", "BUTTON PROMPTS", new[] { "xbox", "playstation" }, new[] { "XBOX", "PLAYSTATION" },
            "Which buttons the prompts show.");
        Rumble = Add(pad, "rumble", "VIBRATION", true,
            "The pad rumbles on take off, hits, slams and skids. The game's own vibration setting has to be on too.");
        MenuKey = Add(pad, "open_key", "MENU KEY", Keys.F3, "Opens and closes this menu.");

        Page eclipse = Add(null, "eclipse", "ECLIPSE", "Powers, the HUD and how this menu looks.");
        Powers = Add(eclipse, "powers", "POWERS", true,
            "Off, he's plain Batman again. The hero screen turns this back on when you pick a hero.");
        HeroPick = Add(eclipse, "hero", "HERO", new[] { "superman", "flash" }, new[] { "SUPERMAN", "FLASH" },
            "Whose powers he has. The hero screen sets this too.");
        Hud = Add(eclipse, "readout", "HUD", true,
            "The hero's own gauge in the bottom left whenever his powers are on. Superman's shows his height and speed in flight, the Flash's his speed.");
        Legend = Add(eclipse, "control_legend", "CONTROL LEGEND", true,
            "Lists what every button does right now above the hero's gauge, and dims what he can't do yet. Off, it still shows for a few seconds after switching hero.");
        LegendFade = Add(eclipse, "control_legend_fade", "HIDE LEGEND WHEN IDLE", true,
            "The legend shows when something changes, like starting a run or a move coming off cooldown, then fades after a few seconds.");
        FlightFx = Add(eclipse, "flight_effects", "EFFECTS", true,
            "Dust and bursts of air when he takes off, boosts, lands, slams down, runs, skids or hits a wall.");
        FlightSounds = Add(eclipse, "flight_sounds", "SOUNDS", true,
            "Wind rushing past while he boosts or runs, and the sounds of taking off, dashing, landing, skidding and slamming down.");
        MenuSize = Add(eclipse, "menu_size", "MENU SIZE", 1f, 0.75f, 1.5f, 0.05f, Fmt.Times,
            "Menu scale. It shrinks to fit if the screen is too small.");
        Effects = Add(eclipse, "effects", "MENU EFFECTS", true,
            "Text that scrambles in, slides and glints. Turn off for a still menu.");
        Add(eclipse, "reset_all", "RESET EVERYTHING", ResetAll, true,
            "Puts every setting for every hero back to its default, keys and buttons included.");

        SupermanPages.Add(camera);
        SupermanPages.Add(pad);
        SupermanPages.Add(eclipse);
        FlashPages.Add(camera);
        FlashPages.Add(pad);
        FlashPages.Add(eclipse);
    }

    private static void ResetAll()
    {
        foreach (Option o in All)
            o.Reset();
        ApplyAll();
        Settings.MarkDirty();
        Readout.Toast("ECLIPSE", "EVERY SETTING BACK TO DEFAULT", true);
    }

    public static void ApplyAll()
    {
        foreach (Option o in All)
            Safe.Run(o.Id + " apply", () => o.Changed?.Invoke());
    }

    private static Page Add(Hero? hero, string key, string title, string blurb)
    {
        var page = new Page(key, title, blurb);
        Pages.Add(page);
        if (hero == Hero.Superman)
            SupermanPages.Add(page);
        else if (hero == Hero.Flash)
            FlashPages.Add(page);
        return page;
    }

    private static T Register<T>(Page page, T option) where T : Option
    {
        option.Page = page;
        page.Options.Add(option);
        All.Add(option);
        return option;
    }

    private static Switch Add(Page page, string id, string label, bool on, string help) =>
        Register(page, new Switch { Id = id, Label = label, Help = help, On = on, Default = on });

    private static Dial Add(Page page, string id, string label, float value, float min, float max, float stride, Func<float, string> format, string help) =>
        Register(page, new Dial { Id = id, Label = label, Help = help, Number = value, Default = value, Min = min, Max = max, Stride = stride, Format = format });

    private static Pick Add(Page page, string id, string label, string[] keys, string[] labels, string help) =>
        Register(page, new Pick { Id = id, Label = label, Help = help, Keys = keys, Labels = labels });

    // One of Batman's unlocked suits, put on when the hero is. YOUR SUIT keeps the player's own.
    private static Listed AddSuit(Page page, string id)
    {
        Listed suit = Register(page, new Listed
        {
            Id = id,
            Label = "SUIT",
            Blank = "YOUR SUIT",
            Source = Looks.Suits,
            Help = "One of the suits you have unlocked, put on while this hero is. YOUR SUIT keeps the one you wear. Yours comes back when no hero is on, and your save keeps it.",
        });
        suit.Changed = () => Looks.Picked(suit.Key);
        return suit;
    }

    private static Command Add(Page page, string id, string label, Action run, bool confirm, string help) =>
        Register(page, new Command { Id = id, Label = label, Help = help, Run = run, Confirm = confirm });

    private static Bind Add(Page page, string id, string label, Keys key, string help) =>
        Register(page, new Bind { Id = id, Label = label, Help = help, Key = key, Default = key });

    private static void Ctl(Page page, Hero hero, Act act, string id, string label, Keys key, string? keyText, PadButton button, Use use, string help, PadButton retired = PadButton.None)
    {
        Control c = Register(page, new Control
        {
            Id = id, Label = label, Help = help, For = act, Hero = hero, Use = use,
            Key = key, KeyDefault = key, KeyText = keyText, Button = button, ButtonDefault = button, Retired = retired,
        });
        if (!Binds.TryGetValue(hero, out Dictionary<Act, Control>? map))
            Binds[hero] = map = new Dictionary<Act, Control>();
        map[act] = c;
        if (keyText == null)
            c.Changed = Controls.HoldsChanged;
    }
}
