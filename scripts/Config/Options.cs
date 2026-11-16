using System.Globalization;

namespace Trinity.Eclipse;

internal sealed record Page(string Key, string Title, string Blurb)
{
    public readonly List<Option> Options = new();

    // What the menu calls its rows when it counts them.
    public string Unit = "SETTINGS";
}

internal abstract class Option
{
    public string Id = "";
    public string Label = "";
    public string Help = "";
    public Page Page = null!;

    public Action? Changed;

    public abstract string Value { get; }

    public abstract bool IsDefault { get; }

    public abstract bool Step(int dir);

    public virtual bool Press() => Step(1);

    public abstract void Reset();

    public abstract void Read(TomlTable table);

    // Null for a row that isn't saved.
    public abstract string? Toml { get; }
}

internal sealed class Switch : Option
{
    public bool On;
    public bool Default;

    public override string Value => On ? "ON" : "OFF";

    public override bool IsDefault => On == Default;

    public override bool Step(int dir)
    {
        On = !On;
        return true;
    }

    public override void Reset() => On = Default;

    public override void Read(TomlTable table) => On = table.TryGetValue(Id, out object? v) && v is bool b ? b : Default;

    public override string Toml => On ? "true" : "false";
}

internal sealed class Dial : Option
{
    public float Number;
    public float Default;
    public float Min;
    public float Max;
    public float Stride;
    public Func<float, string> Format = v => v.ToString("0.##", CultureInfo.InvariantCulture);

    public override string Value => Format(Number);

    public override bool IsDefault => MathF.Abs(Number - Default) <= 0.0001f;

    public float Fraction => Max > Min ? Math.Clamp((Number - Min) / (Max - Min), 0f, 1f) : 0f;

    public float DefaultFraction => Max > Min ? Math.Clamp((Default - Min) / (Max - Min), 0f, 1f) : 0f;

    public override bool Step(int dir)
    {
        float next = MathF.Round(Math.Clamp(Number + dir * Stride, Min, Max) / Stride) * Stride;
        next = MathF.Round(next, 3);
        if (MathF.Abs(next - Number) < 0.0001f)
            return false;

        Number = next;
        return true;
    }

    public override bool Press() => false;

    public override void Reset() => Number = Default;

    public override void Read(TomlTable table)
    {
        Number = Default;
        if (table.TryGetValue(Id, out object? v))
        {
            if (v is double d)
                Number = (float)d;
            else if (v is long l)
                Number = l;
        }

        Number = Math.Clamp(Number, Min, Max);
    }

    public override string Toml => Number.ToString("0.0##", CultureInfo.InvariantCulture);
}

internal sealed class Pick : Option
{
    public int Index;
    public string[] Keys = Array.Empty<string>();
    public string[] Labels = Array.Empty<string>();

    public override string Value => Labels.Length == 0 ? "" : Labels[Math.Clamp(Index, 0, Labels.Length - 1)];

    public string Key => Keys.Length == 0 ? "" : Keys[Math.Clamp(Index, 0, Keys.Length - 1)];

    public override bool IsDefault => Index == 0;

    public override bool Step(int dir)
    {
        if (Labels.Length < 2)
            return false;

        Index = (Index + dir + Labels.Length) % Labels.Length;
        return true;
    }

    public override void Reset() => Index = 0;

    public override void Read(TomlTable table)
    {
        string saved = table.TryGetValue(Id, out object? v) && v is string s ? s : "";
        Index = Math.Max(0, Array.FindIndex(Keys, k => k.Equals(saved, StringComparison.OrdinalIgnoreCase)));
    }

    public override string Toml => "\"" + Key + "\"";
}

internal sealed class Bind : Option
{
    public Keys Key;
    public Keys Default;

    public override string Value => Key == Keys.None ? "NONE" : KeyName(Key);

    public override bool IsDefault => Key == Default;

    public override bool Step(int dir) => false;

    public override bool Press() => false;

    public bool Take(Keys key)
    {
        if (key == Key)
            return false;
        Key = key;
        return true;
    }

    public override void Reset() => Key = Default;

    public override void Read(TomlTable table) =>
        Key = table.TryGetValue(Id, out object? v) && v is string s && Enum.TryParse(s, true, out Keys k) ? k : Default;

    public override string Toml => "\"" + Key + "\"";

    public static string KeyName(Keys key) => key switch
    {
        Keys.LButton => "MOUSE 1",
        Keys.RButton => "MOUSE 2",
        Keys.MButton => "MOUSE 3",
        Keys.XButton1 => "MOUSE 4",
        Keys.XButton2 => "MOUSE 5",
        Keys.Back => "BACKSPACE",
        Keys.Return => "ENTER",
        Keys.ControlKey => "CTRL",
        Keys.ShiftKey => "SHIFT",
        Keys.Menu => "ALT",
        Keys.Oemtilde => "~",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.OemSemicolon => ";",
        Keys.OemQuotes => "'",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemQuestion => "/",
        Keys.OemPipe => "\\",
        >= Keys.D0 and <= Keys.D9 => ((int)key - (int)Keys.D0).ToString(CultureInfo.InvariantCulture),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "NUM " + ((int)key - (int)Keys.NumPad0).ToString(CultureInfo.InvariantCulture),
        _ => key.ToString().ToUpperInvariant(),
    };
}

// A pick from a list the game gives at run time, like the suits the player has unlocked. A saved key that
// isn't in the list right now is kept, so a suit from a DLC that's missing for a moment isn't lost.
internal sealed class Listed : Option
{
    public string Key = "";
    public string Blank = "";
    public Func<List<(string Key, string Label)>> Source = () => new();
    private List<(string Key, string Label)> _list = new();
    private double _listedAt = double.NegativeInfinity;

    // Asked again every couple of seconds at most, since reading it walks the game's lists.
    private List<(string Key, string Label)> List()
    {
        if (Clock.Now - _listedAt > 2.0)
        {
            _listedAt = Clock.Now;
            try
            {
                _list = Source();
            }
            catch (Exception ex)
            {
                Log.Error(Id + " list", "Listing " + Label, ex);
            }
        }

        return _list;
    }

    public override string Value
    {
        get
        {
            List<(string Key, string Label)> list = List();
            int at = list.FindIndex(e => e.Key.Equals(Key, StringComparison.OrdinalIgnoreCase));
            return at >= 0 ? list[at].Label : Key.Length == 0 ? Blank : Key.Replace('_', ' ').ToUpperInvariant();
        }
    }

    public override bool IsDefault => Key.Length == 0;

    public override bool Step(int dir)
    {
        List<(string Key, string Label)> list = List();
        if (list.Count < 2)
            return false;
        int at = Math.Max(0, list.FindIndex(e => e.Key.Equals(Key, StringComparison.OrdinalIgnoreCase)));
        Key = list[(at + dir + list.Count) % list.Count].Key;
        return true;
    }

    public override void Reset() => Key = "";

    public override void Read(TomlTable table) => Key = table.TryGetValue(Id, out object? v) && v is string s ? s : "";

    public override string Toml => "\"" + Key.Replace("\"", "") + "\"";
}

// A row that does something rather than holding a setting.
internal sealed class Command : Option
{
    public Action Run = () => { };

    public bool Confirm;

    public override string Value => "";

    public override bool IsDefault => true;

    public override bool Step(int dir) => false;

    public override bool Press()
    {
        Run();
        return true;
    }

    public override void Reset() { }

    public override void Read(TomlTable table) { }

    public override string? Toml => null;
}

// A row that only tells you something, like the keys for a move.
internal sealed class Info : Option
{
    public Func<string> Text = () => "";

    public override string Value => Text();

    public override bool IsDefault => true;

    public override bool Step(int dir) => false;

    public override bool Press() => false;

    public override void Reset() { }

    public override void Read(TomlTable table) { }

    public override string? Toml => null;
}

// Where a pad button counts: on foot (running too), anywhere, only in flight, or only in a super run.
internal enum Use
{
    Foot,
    Any,
    Flight,
    Run,
}

// One thing a hero can do, on a key and a pad button. Some keys are fixed moves like a double tap, so only
// the button can change.
internal sealed class Control : Option
{
    public Act For;
    public Hero Hero;
    public Use Use;
    public Keys Key;
    public Keys KeyDefault;
    public string? KeyText;
    public PadButton Button;
    public PadButton ButtonDefault;
    // A button this row used to default to. Settings saved with it get the new default.
    public PadButton Retired;

    public bool KeyFixed => KeyText != null;

    public string KeyLabel => KeyText ?? (Key == Keys.None ? "NONE" : Bind.KeyName(Key));

    public string PadLabel => Button == PadButton.None ? "NONE" : Glyphs.Name(Button);

    public override string Value => Controls.PadActive ? PadLabel : KeyLabel;

    public override bool IsDefault => Key == KeyDefault && Button == ButtonDefault;

    public override bool Step(int dir) => false;

    public override bool Press() => false;

    // The pad buttons this row can sit on. On foot the face buttons and LB stay the game's, and only the buttons
    // Batman's gadgets used are free.
    public bool Fits(PadButton b) => Use switch
    {
        Use.Foot => b is PadButton.RB or PadButton.RT or PadButton.LT or PadButton.Up or PadButton.Down or PadButton.Left or PadButton.Right,
        Use.Any => b is PadButton.Up or PadButton.Down or PadButton.Left or PadButton.Right,
        Use.Flight => b is PadButton.A or PadButton.B or PadButton.X or PadButton.Y or PadButton.LB or PadButton.RB or PadButton.LS or PadButton.RS
            or PadButton.Up or PadButton.Left or PadButton.Right,
        _ => b is PadButton.A or PadButton.B or PadButton.X or PadButton.Y or PadButton.RB or PadButton.RT or PadButton.LT or PadButton.LS or PadButton.RS,
    };

    // Two rows clash when both can be pressed at the same time.
    public bool Shares(Control other) => Use == other.Use || Use == Use.Any || other.Use == Use.Any;

    public override void Reset()
    {
        Key = KeyDefault;
        Button = ButtonDefault;
    }

    // Older versions saved a plain key name here.
    public override void Read(TomlTable table)
    {
        Reset();
        if (!table.TryGetValue(Id, out object? v))
            return;

        if (v is string old)
        {
            if (!KeyFixed && Enum.TryParse(old, true, out Keys k))
                Key = k;
            return;
        }

        if (v is not TomlTable t)
            return;
        // A key can't be cleared in the menu, so a saved NONE is from before the row had a default.
        if (!KeyFixed && t.TryGetValue("key", out object? key) && key is string ks && Enum.TryParse(ks, true, out Keys parsed) && parsed != Keys.None)
            Key = parsed;
        if (t.TryGetValue("pad", out object? pad) && pad is string ps && Enum.TryParse(ps, true, out PadButton b) && b != Retired && (b == PadButton.None || Fits(b)))
            Button = b;
    }

    public override string Toml => "{ key = \"" + Key + "\", pad = \"" + Button + "\" }";
}
