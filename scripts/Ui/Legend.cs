namespace Trinity.Eclipse;

// One line of the control legend. Ready is false when it can't be used right now, Active when it's in use.
// Meta is the short note at the row's end, like seconds left or what it costs. Short means he lacks the charge.
internal readonly record struct Entry(string Label, string Key, PadButton[] Pad, string PadWord, bool Ready, bool Active,
    string Meta = "", bool Short = false);

// What the hero can do right now and how, on foot, flying or running, built from his binds as they are.
internal static class Legend
{
    private static readonly PadButton[] Triggers = { PadButton.RT, PadButton.LT };

    public static string Mode => Suit.I.Blitzing ? "BLITZ" : Suit.I.Flying ? "FLYING" : Suit.I.Running ? "RUNNING" : "ON FOOT";

    public static List<Entry> Now()
    {
        var rows = new List<Entry>();
        if (!Opt.Binds.TryGetValue(Heroes.Active, out var map))
            return rows;

        if (Heroes.Active == Hero.Flash)
            Flash(rows, map);
        else
            Superman(rows, map);

        if (map.TryGetValue(Act.Swap, out Control? swap))
            rows.Add(Of(swap, "SWITCH HERO"));
        return rows;
    }

    private static void Superman(List<Entry> rows, Dictionary<Act, Control> map)
    {
        Suit s = Suit.I;
        if (!s.Flying)
        {
            Add(rows, map, Act.Fly, "TAKE OFF");
            Add(rows, map, Act.Heat, "HEAT VISION", active: s.Firing);
            return;
        }

        bool fast = s.FastPose;
        Add(rows, map, Act.Boost, "BOOST", active: fast);
        rows.Add(new Entry("CLIMB AND SINK", "SPACE  CTRL", Triggers, "", true, false));
        Add(rows, map, Act.Burst, fast ? "PULL UP" : "BURST UP");
        Add(rows, map, Act.Drop, fast ? "DIVE" : s.SlamReady ? "GROUND SLAM" : "DROP");
        Add(rows, map, Act.Dash, fast ? "SONIC BURST" : "DASH", ready: !fast || s.SonicReady);
        Add(rows, map, Act.Roll, "BARREL ROLL", ready: fast);
        if (map.TryGetValue(Act.Drop, out Control? drop))
            rows.Add(new Entry("LAND", Key(map, Act.Fly), new[] { drop.Button }, "HOLD", !fast, false));
        Add(rows, map, Act.Punch, s.HoldingThug ? "FINISH" : "PUNCH  HOLD FOR HEAVY");
        Add(rows, map, Act.Grab, s.Holding ? "THROW" : "GRAB");
        if (map.TryGetValue(Act.HeatAir, out Control? air))
            rows.Add(new Entry("HEAT VISION", Key(map, Act.Heat), new[] { air.Button }, "", true, s.Firing));
    }

    private static void Flash(List<Entry> rows, Dictionary<Act, Control> map)
    {
        Suit s = Suit.I;
        bool charged = s.Charges >= 1f && Opt.Blitz.On;
        if (s.BlitzAiming)
        {
            rows.Add(new Entry("MARK THE THUG IN YOUR SIGHTS", "LEFT CLICK", new[] { PadButton.X }, "", true, s.BlitzAim != null));
            PadButton blitz = map.TryGetValue(Act.Blitz, out Control? b) ? b.Button : PadButton.None;
            rows.Add(new Entry("LET GO TO STRIKE", Key(map, Act.Blitz), blitz == PadButton.None ? Array.Empty<PadButton>() : new[] { blitz }, "", true, false));
            rows.Add(new Entry("CALL IT OFF", "RIGHT CLICK", new[] { PadButton.B }, "", true, false));
            return;
        }

        if (s.Blitzing)
            return;

        bool bolting = s.BoltCharge > 0f;
        string boltMeta = bolting ? "CHARGING" : "HOLD";
        string blitzMeta = s.Charges >= 1f ? "1 CHARGE" : "NO CHARGE";
        string flurryMeta = s.Flurrying ? "ON" : Wait(s.FlurryWait) ?? "READY";
        string rewindMeta = s.Rewinding ? "ON" : s.Charges < 1f ? "NO CHARGE" : Wait(s.RewindWait) ?? "1 CHARGE";
        if (!s.Running)
        {
            Add(rows, map, Act.Run, "SUPER RUN", ready: Opt.SuperRun.On);
            Add(rows, map, Act.Lightning, "LIGHTNING", active: bolting, meta: boltMeta);
            Add(rows, map, Act.Blitz, "BLITZ", ready: charged, meta: blitzMeta, lacks: s.Charges < 1f);
            Add(rows, map, Act.Flurry, "FLURRY", ready: s.FlurryReady, active: s.Flurrying, meta: flurryMeta);
            Add(rows, map, Act.Slow, s.Slowing ? "END SLOW TIME" : "SLOW TIME", ready: s.SlowReady || s.Slowing, active: s.Slowing,
                meta: s.Slowing ? "ON" : Wait(s.SlowWait) ?? "READY");
            Add(rows, map, Act.Rewind, "REWIND", ready: s.RewindReady, active: s.Rewinding, meta: rewindMeta, lacks: s.Charges < 1f);
            return;
        }

        Add(rows, map, Act.Lightning, "LIGHTNING", active: bolting, meta: boltMeta);
        Add(rows, map, Act.Blitz, "BLITZ", ready: charged, meta: blitzMeta, lacks: s.Charges < 1f);
        Add(rows, map, Act.Flurry, "FLURRY", ready: s.FlurryReady, active: s.Flurrying, meta: flurryMeta);
        Add(rows, map, Act.Tornado, "TORNADO", ready: s.TornadoReady, active: s.Twisting,
            meta: s.Twisting ? "ON" : Wait(s.TornadoWait) ?? "READY");
        Add(rows, map, Act.Rewind, "REWIND", ready: s.RewindReady, active: s.Rewinding, meta: rewindMeta, lacks: s.Charges < 1f);
    }

    // Seconds left on a cooldown, or null once it's done.
    private static string? Wait(double seconds) => seconds > 0.05 ? seconds.ToString("0.0") + " S" : null;

    private static void Add(List<Entry> rows, Dictionary<Act, Control> map, Act act, string label, bool ready = true, bool active = false,
        string meta = "", bool lacks = false)
    {
        if (map.TryGetValue(act, out Control? c))
            rows.Add(Of(c, label, ready, active) with { Meta = meta, Short = lacks });
    }

    private static Entry Of(Control c, string label, bool ready = true, bool active = false) =>
        new(label, c.KeyLabel, c.Button == PadButton.None ? Array.Empty<PadButton>() : new[] { c.Button }, "", ready, active);

    private static string Key(Dictionary<Act, Control> map, Act act) => map.TryGetValue(act, out Control? c) ? c.KeyLabel : "";

    // Draws the key or the buttons for the device in use, from x. Returns how wide it was.
    public static float Prompt(float x, float cy, in Entry e, float scale, float alpha)
    {
        if (!Controls.PadActive || e.Pad.Length == 0)
            return Glyphs.Cap(x, cy, e.Key.Length > 0 ? e.Key : "NONE", scale, Palette.Text, alpha);

        float w = 0f;
        if (e.PadWord.Length > 0)
            w += Glyphs.Cap(x, cy, e.PadWord, scale * 0.85f, Palette.Text, alpha) + Gfx.S(4f);
        foreach (PadButton b in e.Pad)
            w += Glyphs.Draw(x + w, cy, b, scale, alpha) + Gfx.S(4f);
        return w - Gfx.S(4f);
    }

    public static float PromptWidth(in Entry e, float scale)
    {
        if (!Controls.PadActive || e.Pad.Length == 0)
            return Glyphs.CapWidth(e.Key.Length > 0 ? e.Key : "NONE", scale);

        float w = e.PadWord.Length > 0 ? Glyphs.CapWidth(e.PadWord, scale * 0.85f) + Gfx.S(4f) : 0f;
        foreach (PadButton b in e.Pad)
            w += Glyphs.Width(b, scale) + Gfx.S(4f);
        return w - Gfx.S(4f);
    }
}
