namespace Trinity.Eclipse;

internal static class Opt
{
    public static readonly List<Page> Pages = new();
    public static readonly List<Option> All = new();

    public static Switch Powers = null!;
    public static Dial MenuSize = null!;
    public static Switch Effects = null!;

    public static Switch Ram = null!;
    public static Dial RamForce = null!;
    public static Switch Heavy = null!;
    public static Switch Invincible = null!;
    public static Switch NoHit = null!;
    public static Switch NoStun = null!;
    public static Pick SuperPunch = null!;
    public static Dial PunchForce = null!;
    public static Dial PowerForce = null!;

    public static Dial MoveSpeed = null!;
    public static Dial BoostSpeed = null!;
    public static Dial Coast = null!;
    public static Dial ClimbSpeed = null!;
    public static Dial PunchTime = null!;
    public static Switch HitStop = null!;

    public static Switch SuperRun = null!;
    public static Dial RunSpeed = null!;
    public static Switch SpeedVision = null!;
    public static Dial WorldSpeed = null!;

    public static Switch TargetMarks = null!;
    public static Switch Gauge = null!;
    public static Switch Tilt = null!;
    public static Dial Bank = null!;
    public static Switch HoverDrift = null!;
    public static Pick Lean = null!;
    public static Switch FlightFx = null!;
    public static Switch FlightSounds = null!;

    public static Switch Camera = null!;
    public static Dial CamDistance = null!;
    public static Dial CamFastDistance = null!;

    public static Bind FlyKey = null!;
    public static Bind MenuKey = null!;
    public static Bind PunchKey = null!;
    public static Bind GrabKey = null!;
    public static Bind FireKey = null!;
    public static Switch Controller = null!;
    public static Pick PadButton = null!;

    // The power switches only count while Eclipse itself is on.
    public static bool HeavyOn => Powers.On && Heavy.On;

    public static bool RamOn => Powers.On && Ram.On;

    public static bool InvincibleOn => Powers.On && Invincible.On;

    public static bool NoHitOn => Powers.On && NoHit.On;

    public static bool NoStunOn => Powers.On && NoStun.On;

    public static string SuperPunchMode => Powers.On ? SuperPunch.Key : "off";

    public static void Build()
    {
        Pages.Clear();
        All.Clear();

        Page eclipse = Add("eclipse", "ECLIPSE", "Turn his powers on or off, and change how this menu looks.");
        Powers = Add(eclipse, "powers", "POWERS", true,
            "Turns all of Eclipse on or off. Off, he's plain Batman again. No flight, no heavy body, no super punches. This menu still opens.");
        MenuSize = Add(eclipse, "menu_size", "MENU SIZE", 1f, 0.75f, 1.5f, 0.05f, Fmt.Times,
            "Menu scale. It shrinks to fit if the screen is too small.");
        Effects = Add(eclipse, "effects", "MENU EFFECTS", true,
            "The burning corona, text that scrambles in and rows that slide. Turn off for a still menu.");
        Add(eclipse, "reset_all", "RESET EVERYTHING", ResetAll, true,
            "Puts every setting on every page back to its default, keys included.");

        Page moves = Add("moves", "MOVES", "Everything he can do, and the keys for it.");
        moves.Unit = "MOVES";
        Note(moves, "FLY", () => Bind.KeyName(FlyKey.Key),
            "Starts and stops flight. From the ground he kicks off into a hover. Hold Ctrl as he settles onto the ground to land on his feet.");
        Note(moves, "MOVE", "W A S D, SPACE, CTRL",
            "Hovering, he moves the way the camera faces. Space climbs and Ctrl sinks. Left stick, RT and LT on a pad.");
        Note(moves, "BOOST", "HOLD SHIFT",
            "Fast flight, steered with the camera. W is thrust and S brakes. A and D bank, Space and Ctrl pitch. RB on a pad.");
        Note(moves, "DASH", "W A S D TWICE",
            "Hovering, tap a direction twice to dash that way. LB on a pad dashes the way the stick points.");
        Note(moves, "BURST UP", "SPACE TWICE",
            "Hovering, a quick burst upwards. Boosting, it pulls his nose up. A on a pad.");
        Note(moves, "DROP", "CTRL TWICE",
            "Close to the ground, a quick drop. Boosting, it tips him into a dive. B on a pad.");
        Note(moves, "GROUND SLAM", "CTRL TWICE, UP HIGH",
            "Hovering a few metres up or more, he dives straight down and lands on his feet out of flight, throwing back everything round him. The further he falls, the harder it hits. Space pulls out on the way down.");
        Note(moves, "BARREL ROLL", "BOOSTING, A OR D TWICE", "Rolls him aside in fast flight.");
        Note(moves, "SONIC BURST", "BOOSTING, W TWICE",
            "Kicks him past his top speed for a few seconds, and the crack of it knocks down whatever he's passing. LB on a pad.");
        Note(moves, "SUPER RUN", "HOLD SHIFT ON FOOT",
            "Breaks into a run that keeps building speed. He turns wide at speed. Let go to skid to a stop. Hold RB on a pad.");
        Note(moves, "RUN INTO FLIGHT", "SPACE WHILE RUNNING",
            "Launches him off the ground into boost flight at the speed he was running. A on a pad.");
        Note(moves, "FLIGHT INTO RUN", "BOOSTING LOW, HOLD CTRL",
            "Skim the ground in boost flight and hold Ctrl to drop onto your feet into a super run. Let go of Shift as he lands to skid instead.");
        Note(moves, "PUNCH", () => Bind.KeyName(PunchKey.Key),
            "In flight, punches the bracketed thug or vehicle, flying to it first if it's out of reach. Tap again to keep punching. X on a pad.");
        Note(moves, "HEAVY PUNCH", () => "HOLD " + Bind.KeyName(PunchKey.Key),
            "Hold to wind up and let go to throw it. It launches the target, and a shockwave knocks down everything round it.");
        Note(moves, "GRAB AND THROW", () => Bind.KeyName(GrabKey.Key),
            "In flight, picks up the bracketed thug or vehicle. Press again to throw it where you look, or at another bracketed target. Holding a thug, the punch key finishes him. Y on a pad.");
        Note(moves, "HEAT VISION", () => "HOLD " + Bind.KeyName(FireKey.Key),
            "On foot or flying. Thugs and cars it touches are destroyed, and it leaves fire where it rests. R3 on a pad.");

        Page powers = Add("powers", "POWERS", "What he can do, on foot and in the air.");
        Ram = Add(powers, "ram", "RAM", true,
            "He hits like the Batmobile. Flying into thugs launches them, cars and drones get knocked aside and hurt, props break. Slow, or on foot, it's a shove that clears cars and props out of his way.");
        RamForce = Add(powers, "ram_force", "RAM FORCE", 1f, 0.25f, 3f, 0.05f, Fmt.Times,
            "How hard he hits things he flies into, punches or slams down beside.");
        Heavy = Add(powers, "heavy", "HEAVY", true,
            "He weighs what the Batmobile weighs, on foot and in flight. Cars that drive into him stop dead and get dented. Cars, wrecks and loose props he walks or flies into get shoved out of the way.");
        Invincible = Add(powers, "invincible", "INVINCIBLE", true,
            "He takes no damage and his health stays full, on foot and flying.");
        NoHit = Add(powers, "no_hit", "NO HIT REACTIONS", true,
            "Hits don't stagger him or knock him back. Thugs who charge him or grab him bounce off and go down.");
        NoStun = Add(powers, "no_stun", "NO STUNS", true,
            "Stun batons, zaps and laser grids don't daze him.");
        SuperPunch = Add(powers, "super_punch", "SUPER PUNCH", new[] { "every", "knockouts", "off" }, new[] { "EVERY HIT", "KNOCKOUTS", "OFF" },
            "His hits on foot launch thugs. every is every strike and counter. knockouts is only the hit that puts one down, so fights keep flowing.");
        PunchForce = Add(powers, "punch_force", "PUNCH FORCE", 1f, 0.25f, 3f, 0.05f, Fmt.Times,
            "How far his punches and throws send thugs.");
        PowerForce = Add(powers, "power_force", "HEAT VISION FORCE", 1f, 0.25f, 3f, 0.05f, Fmt.Times,
            "How far heat vision throws the thugs it burns, and how hard it pushes wrecks and loose things.");

        Page flight = Add("flight", "FLIGHT", "How he flies and fights in the air.");
        MoveSpeed = Add(flight, "move_speed", "MOVE SPEED", 12f, 4f, 30f, 1f, v => Fmt.Whole(v) + " M/S",
            "How fast he hovers around.");
        BoostSpeed = Add(flight, "boost_speed", "BOOST SPEED", 40f, 15f, 90f, 5f, v => Fmt.Whole(v) + " M/S",
            "Top speed while boosting. A dive or a sonic burst takes him past it for a while.");
        Coast = Add(flight, "coast", "COAST", 1.2f, 0.3f, 4f, 0.1f, Fmt.Secs,
            "Seconds to slow from top speed to hover speed when you let go of boost. Holding S brakes much harder.");
        ClimbSpeed = Add(flight, "climb_speed", "CLIMB SPEED", 8f, 2f, 20f, 1f, v => Fmt.Whole(v) + " M/S",
            "How fast he goes up and down while hovering.");
        PunchTime = Add(flight, "punch_time", "PUNCH TIME", 0.7f, 0.3f, 1.5f, 0.05f, Fmt.SecsFine,
            "How long a punch takes in flight, wind up to the hover coming back. The fist lands half way through. Shorter is snappier, longer is easier to see.");
        HitStop = Add(flight, "hit_stop", "HIT STOP", true,
            "Each punch that lands in flight slows the game for a blink, longer on a heavy one. A ground slam does it too.");

        Page run = Add("run", "RUN", "The super run.");
        SuperRun = Add(run, "super_run", "SUPER RUN", true,
            "Hold Shift on foot to run at super speed. RB on a pad.");
        RunSpeed = Add(run, "run_speed", "RUN SPEED", 35f, 15f, 80f, 5f, v => Fmt.Whole(v) + " M/S",
            "Top speed of the super run. It takes a couple of seconds to get there.");
        SpeedVision = Add(run, "speed_vision", "SPEED VISION", true,
            "Once he's running fast the world slows down round him, while he keeps going at full speed.");
        WorldSpeed = Add(run, "world_speed", "WORLD SPEED", 0.3f, 0.1f, 0.7f, 0.05f, Fmt.Times,
            "How fast the world goes during speed vision. Lower is slower.");

        Page look = Add("look", "LOOK", "How flight and the super run look and sound.");
        TargetMarks = Add(look, "target_marks", "TARGET MARKS", true,
            "While flying, brackets the thug or vehicle nearest the middle of the screen, with what the punch and grab keys do to it.");
        Gauge = Add(look, "readout", "SPEED READOUT", true,
            "His speed in the corner of the screen while flying or super running, and his height while flying.");
        Tilt = Add(look, "tilt", "TILT", true, "In the fast pose his body leans up or down with the way he's flying.");
        Bank = Add(look, "bank", "BANK", 1f, 0f, 2f, 0.05f, Fmt.Times,
            "How far he rolls into turns in fast flight. 0 keeps him level.");
        HoverDrift = Add(look, "hover_drift", "HOVER DRIFT", true,
            "He bobs and sways a little while hovering, leans into his moves and banks into turns. Turn off to hold him dead still.");
        Lean = Add(look, "lean", "LEAN", new[] { "into", "against" }, new[] { "INTO", "AGAINST" },
            "Which way he leans when he moves while hovering. into tips him the way he goes, against tips him back like he's braced.");
        FlightFx = Add(look, "flight_effects", "EFFECTS", true,
            "Dust and bursts of air when he takes off, boosts, lands, slams down, runs, skids or hits a wall.");
        FlightSounds = Add(look, "flight_sounds", "SOUNDS", true,
            "Wind rushing past while he boosts or runs, and the sounds of taking off, dashing, landing, skidding and slamming down.");

        Page camera = Add("camera", "CAMERA", "The camera for flight and the super run.");
        Camera = Add(camera, "camera", "OWN CAMERA", true,
            "Our own camera while flying or super running. Close over his shoulder when slow, further back and wider at speed. Turn off for the game's camera.");
        CamDistance = Add(camera, "hover_distance", "HOVER DISTANCE", 3.2f, 1.5f, 8f, 0.1f, v => Fmt.Tenths(v) + " M",
            "How far behind him the camera sits while hovering.");
        CamFastDistance = Add(camera, "fast_distance", "FAST DISTANCE", 5.5f, 2f, 12f, 0.1f, v => Fmt.Tenths(v) + " M",
            "How far behind him the camera sits at top speed.");

        Page controls = Add("controls", "CONTROLS", "Keys and controller. The MOVES page lists what each one does.");
        FlyKey = Add(controls, "fly_key", "FLY KEY", Keys.J, "Starts and stops flight.");
        MenuKey = Add(controls, "menu_key", "MENU KEY", Keys.F5, "Opens and closes this menu.");
        PunchKey = Add(controls, "punch_key", "PUNCH KEY", Keys.LButton,
            "In flight, punches the bracketed target. Tap to keep punching, hold for a heavy punch. Only taken from the game while flying.");
        GrabKey = Add(controls, "grab_key", "GRAB KEY", Keys.RButton,
            "In flight, picks up the bracketed thug or vehicle, and throws it. Only taken from the game while flying.");
        FireKey = Add(controls, "fire_key", "FIRE KEY", Keys.MButton,
            "Hold for heat vision, on foot or flying. Taken from the game whenever his powers are on.");
        Controller = Add(controls, "controller", "CONTROLLER", true,
            "Xbox controller. Flying, left stick moves, RT up, LT down, RB boost, LB dash, A burst up, B drop, X punch, Y grab. On foot, hold RB to super run. R3 is heat vision.");
        PadButton = Add(controls, "pad_button", "PAD MENU BUTTON", new[] { "ls+rs", "off" }, new[] { "HOLD LS + RS", "OFF" },
            "Hold this on a controller to open the menu. Press it again to close.");

        Controller.Changed = () => Pad.Enabled = Controller.On;
        PunchKey.Changed = Suit.I.KeysChanged;
        GrabKey.Changed = Suit.I.KeysChanged;
        Powers.Changed = () =>
        {
            if (!Powers.On)
                Suit.I.PowersOff();
        };
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

    private static Page Add(string key, string title, string blurb)
    {
        var page = new Page(key, title, blurb);
        Pages.Add(page);
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

    private static Act Add(Page page, string id, string label, Action run, bool confirm, string help) =>
        Register(page, new Act { Id = id, Label = label, Help = help, Run = run, Confirm = confirm });

    private static Bind Add(Page page, string id, string label, Keys key, string help) =>
        Register(page, new Bind { Id = id, Label = label, Help = help, Key = key, Default = key });

    private static void Note(Page page, string label, Func<string> text, string help) =>
        Register(page, new Info { Id = page.Key + " " + label, Label = label, Help = help, Text = text });

    private static void Note(Page page, string label, string text, string help) => Note(page, label, () => text, help);
}
