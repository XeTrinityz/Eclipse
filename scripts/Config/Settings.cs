using System.Text;

namespace Trinity.Eclipse;

internal static class Settings
{
    private const string FileName = "settings.toml";
    private const float SaveDelay = 0.75f;
    private const double PollSeconds = 1.0;

    private static DateTime _stamp = DateTime.MinValue;
    private static bool _dirty;
    private static float _saveLeft;
    private static Every _poll;
    private static string _lastError = "";

    private static string PathOf => Path.Combine(App.Dir, FileName);

    public static void MarkDirty()
    {
        _dirty = true;
        _saveLeft = SaveDelay;
    }

    public static void Tick(float dt)
    {
        if (_dirty)
        {
            _saveLeft -= dt;
            if (_saveLeft <= 0f)
                Flush();
            return;
        }

        if (_poll.Elapsed(PollSeconds))
            Load(first: false);
    }

    public static void Flush()
    {
        if (!_dirty)
            return;

        _dirty = false;
        try
        {
            string temp = PathOf + ".tmp";
            File.WriteAllText(temp, Compose());
            File.Move(temp, PathOf, overwrite: true);
            _stamp = File.GetLastWriteTimeUtc(PathOf);
        }
        catch (Exception ex)
        {
            Log.Info("Could not save " + FileName + ": " + ex.Message);
        }
    }

    public static void Load(bool first)
    {
        string path = PathOf;
        if (!File.Exists(path))
        {
            if (first)
            {
                foreach (Option o in Opt.All)
                    o.Reset();
                MarkDirty();
                Flush();
                Log.Info("Created " + FileName + ".");
            }

            return;
        }

        DateTime stamp = File.GetLastWriteTimeUtc(path);
        if (!first && stamp == _stamp)
            return;

        TomlTable root;
        try
        {
            root = Tomlyn.TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path))!;
        }
        catch (Exception ex)
        {
            if (ex.Message != _lastError)
            {
                _lastError = ex.Message;
                Log.Info(FileName + " failed to load, keeping current settings: " + ex.Message);
            }

            return;
        }

        _lastError = "";
        _stamp = stamp;
        int changed = 0;
        int missing = 0;
        int known = 0;
        int moved = 0;
        foreach (Option o in Opt.All)
        {
            if (o.Toml == null)
                continue;

            TomlTable section = Section(root, o.Page.Key);
            if (section.ContainsKey(o.Id))
            {
                known++;
            }
            else if (Elsewhere(root, o.Id) is { } old)
            {
                // The setting moved to another page, so its saved value goes under the new one.
                section = old;
                known++;
                moved++;
            }
            else
            {
                missing++;
            }

            string before = o.Value;
            o.Read(section);
            if (o.Value != before)
            {
                changed++;
                if (!first)
                    Safe.Run(o.Id + " change", () => o.Changed?.Invoke());
            }
        }

        if (!first && changed > 0)
            Log.Info("Settings reloaded from " + FileName + " (" + changed + " changed).");

        // A newer version added or dropped settings, so write the file out again.
        int stale = CountKeys(root) - known;
        if (missing > 0 || stale > 0 || moved > 0)
        {
            Log.Info("Updating " + FileName + ": " + missing + " new settings, " + moved + " moved, " + stale + " old ones dropped.");
            MarkDirty();
        }
    }

    private static string Compose()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Eclipse settings. Edits made here apply while you play.");
        sb.AppendLine("# Switches are true/false, dials are numbers, picks are names, keys are key names (J, K, F5, Insert, MButton...).");
        foreach (Page page in Opt.Pages)
        {
            // A page of notes has nothing to save.
            if (page.Options.All(o => o.Toml == null))
                continue;

            sb.AppendLine();
            sb.AppendLine("[" + page.Key + "]");
            foreach (Option o in page.Options)
            {
                if (o.Toml is not { } value)
                    continue;
                sb.AppendLine("# " + o.Label + ": " + Short(o));
                sb.AppendLine(o.Id + " = " + value);
            }
        }

        return sb.ToString();
    }

    private static string Short(Option o) => o switch
    {
        Pick p => string.Join(", ", p.Keys),
        Dial d => d.Format(d.Min) + " to " + d.Format(d.Max),
        Bind => "a key name",
        Listed => "a suit's package name, empty for your own",
        _ => "true or false",
    };

    private static int CountKeys(TomlTable root)
    {
        int count = 0;
        foreach ((string _, object? value) in root)
        {
            if (value is TomlTable section)
                count += section.Count;
            else
                count++;
        }

        return count;
    }

    private static TomlTable? Elsewhere(TomlTable root, string id)
    {
        foreach ((string _, object? value) in root)
        {
            if (value is TomlTable section && section.ContainsKey(id))
                return section;
        }

        return null;
    }

    private static TomlTable Section(TomlTable root, string name) =>
        root.TryGetValue(name, out object? raw) && raw is TomlTable table ? table : new TomlTable();
}
