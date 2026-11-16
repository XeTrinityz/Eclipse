using BmSDK;
using BmSDK.BmGame;

namespace Trinity.Eclipse;

// Each hero's look: one of Batman's unlocked suits and the cape on or off. The suit goes on the game's own
// way, the way Knightfall's suit picker does it. The hero's suit is made the game's pick just while the game
// loads it, then the player's own pick is put straight back, so the save keeps the player's suit. When no
// hero is on, the player's own suit comes back.
internal static class Looks
{
    private const string Prefix = "Playable_";
    private const string ShowcasePage = "Players";
    private const double LoadLongest = 20.0;
    private const double CheckEvery = 0.5;
    private const string CapeKey = "eclipse.cape";
    // Whether the suit he wears is one Eclipse put on, and the last load's target and result. Kept over reloads.
    private const string OursKey = "eclipse.suit.ours", DoneKey = "eclipse.suit.done";

    private static bool _loading;
    private static string _want = "", _target = "";
    private static string? _ownPick;
    private static nint _pawn;
    private static double _since, _nextCheck;
    // Suits that wouldn't load this session. They aren't tried again until the pick changes.
    private static readonly HashSet<string> Failed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase);
    private static bool _capeHeld;

    // The hero's picked suit, or empty for the player's own.
    private static string HeroSuit() => !Opt.Powers.On ? "" : Heroes.Active == Hero.Flash ? Opt.FlashSuit.Key : Opt.SupermanSuit.Key;

    private static bool HeroCape() => Heroes.Active == Hero.Flash ? Opt.FlashCape.On : Opt.SupermanCape.On;

    public static void Step()
    {
        Cape();
        if (Clock.Now < _nextCheck && !_loading)
            return;
        _nextCheck = Clock.Now + CheckEvery;
        if (World.Pawn is not RPawnPlayer { Health: > 0 } pawn || World.Pc is not RPlayerController pc
            || pawn.DrivenVehicle is { IsValid: true } || !Game.TryGetGameRI(out RGameRI? game) || game is not { IsValid: true }
            || game.PersistentOptions is not { IsValid: true } options)
            return;

        int index = SlotOf(game, pawn);
        if (index < 0)
            return;
        ref RGameRI.FLoadedPlayerCharacter slot = ref Slot(game, index);
        string character = slot.CharacterName.ToString() ?? "";
        if (!Batman(character))
            return;

        if (_loading)
        {
            Loading(pawn, pc, options, ref slot);
            return;
        }

        string want = HeroSuit();
        if (want.Length > 0 && Failed.Contains(want))
            want = "";
        // The player's own suit is only put back over one Eclipse put on. Anything else he wears is left alone.
        bool ours = Shared.GetValue(OursKey, false);
        string target = want.Length > 0 ? want : ours ? Own(options, character) : "";
        string worn = slot.MeshName.ToString() ?? "";
        if (target.Length == 0 || Wearing(worn, target) || Shared.Get(DoneKey) as string == target + "|" + worn
            || Hooks.InCinematic(pc) || pawn.CurrentSpecialMove is { IsValid: true })
            return;

        _want = want;
        _target = target;
        _ownPick = null;
        if (want.Length > 0)
        {
            _ownPick = Own(options, character);
            options.SetActivePlayerSkin(want);
        }

        // Emptying the slot's data makes the game's loader fetch it again with the suit now picked.
        slot.CharacterData = null!;
        slot.MeshData = null!;
        game.ReallyLoadPlayableCharacters(false, false);
        _loading = true;
        _pawn = pawn.Ptr;
        _since = Clock.Now;
    }

    private static void Loading(RPawnPlayer pawn, RPlayerController pc, RPersistentOptions options, ref RGameRI.FLoadedPlayerCharacter slot)
    {
        bool ready = slot.CharacterData is not null && ((slot.MeshName.ToString() ?? "").Length == 0 || slot.MeshData is not null);
        if (!ready)
        {
            if (Clock.Now - _since <= LoadLongest)
                return;
            Log.Info("The suit " + _want + " took too long to load.");
            if (_want.Length > 0)
                Failed.Add(_want);
            Done(options);
            return;
        }

        Done(options);
        // What the load left him in, so a suit whose name the game writes differently isn't loaded again and again.
        Shared.Set(DoneKey, _target + "|" + (slot.MeshName.ToString() ?? ""));
        Shared.Set(OursKey, _want.Length > 0);
        if (pawn.Ptr == _pawn)
            Safe.Run("suit on", () => pawn.SetupPlayerMesh(true));
    }

    // The player's own pick goes back as soon as the hero's suit is in.
    private static void Done(RPersistentOptions options)
    {
        if (_ownPick != null)
            options.SetActivePlayerSkin(_ownPick);
        _ownPick = null;
        _loading = false;
    }

    // The player's own suit as the game would load it: the showcase pick, or the standard suit.
    private static string Own(RPersistentOptions options, string character)
    {
        string pick = OwnPick(options, character);
        return pick.Length > 0 ? pick : Owner(character) + "_Std";
    }

    // The showcase pick for this character, empty for none.
    private static string OwnPick(RPersistentOptions options, string character)
    {
        string id = SkinId(Owner(character));
        var picks = options.ActivePlayerSkins;
        for (int i = 0; i < picks.Count; i++)
        {
            var pick = picks[i];
            if (string.Equals(pick.Id.ToString(), id, StringComparison.OrdinalIgnoreCase))
                return pick.Skin.ToString() ?? "";
        }

        return "";
    }

    // The hero's cape, hidden or shown with the game's own switch. Put back as it was when no hero is on.
    private static void Cape()
    {
        bool hide = Opt.Powers.On && !HeroCape();
        if (World.Pawn is not RPawnPlayerBmBase { CapeComponent: { IsValid: true } cape })
            return;
        if (hide)
        {
            if (!_capeHeld)
            {
                Shared.Set(CapeKey, cape.IsCapeHiddenGame);
                _capeHeld = true;
            }

            if (!cape.IsCapeHiddenGame)
                cape.CapeSetHiddenGame(true);
            return;
        }

        if (_capeHeld)
            CapeBack(cape);
    }

    private static void CapeBack(RCapeComponent cape)
    {
        _capeHeld = false;
        bool was = Shared.GetValue(CapeKey, false);
        Shared.Set(CapeKey, null);
        if (cape.IsCapeHiddenGame != was)
            cape.CapeSetHiddenGame(was);
    }

    // Scripts unloading. The cape goes back. The suit stays, the next load picks it up.
    public static void Release()
    {
        if (_capeHeld && World.Pawn is RPawnPlayerBmBase { CapeComponent: { IsValid: true } cape })
            CapeBack(cape);
        if (_loading && Game.TryGetGameRI(out RGameRI? game) && game?.PersistentOptions is { IsValid: true } options)
            Done(options);
    }

    // A new world has its own player.
    public static void Forget()
    {
        _loading = false;
        _ownPick = null;
        _capeHeld = false;
        Shared.Set(CapeKey, null);
    }

    // Scripts reloaded with the cape hidden.
    public static void Recover()
    {
        if (Shared.Get(CapeKey) is bool was && World.Pawn is RPawnPlayerBmBase { CapeComponent: { IsValid: true } cape })
            cape.CapeSetHiddenGame(was);
        Shared.Set(CapeKey, null);
    }

    // Batman's unlocked suits from the game's showcase, as skin names with the showcase's labels. The first
    // entry is the player's own suit.
    public static List<(string Key, string Label)> Suits()
    {
        var list = new List<(string Key, string Label)> { ("", "YOUR SUIT") };
        if (!Game.TryGetGameInfo(out RGameInfo? info) || info is not { IsValid: true } || !Game.TryGetGameRI(out RGameRI? game)
            || game?.PersistentOptions is not { IsValid: true } options)
            return list;

        // Some installs list a suit more than once, with spare rows pointing at another suit's skin. So each
        // suit takes the skin no other suit uses, when it has one.
        info.ShowcaseValidateSorting();
        var suits = new List<(string Key, List<string> Skins)>();
        var users = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (RGameInfo.FShowcaseItem item in info.ShowcaseItems)
        {
            string skin = item.Skin.ToString() ?? "";
            string key = item.Item.ToString() ?? "";
            if (skin.Length == 0 || key.Length == 0
                || !string.Equals(item.Page.ToString(), ShowcasePage, StringComparison.OrdinalIgnoreCase)
                || !SkinId(skin).Equals("Batman", StringComparison.OrdinalIgnoreCase)
                || !Unlocked(options, item, key))
                continue;

            int at = suits.FindIndex(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (at < 0)
            {
                at = suits.Count;
                suits.Add((key, new List<string>()));
            }

            if (!suits[at].Skins.Exists(s => s.Equals(skin, StringComparison.OrdinalIgnoreCase)))
                suits[at].Skins.Add(skin);
            if (!users.TryGetValue(skin, out HashSet<string>? keys))
                users[skin] = keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            keys.Add(key);
        }

        foreach ((string key, List<string> skins) in suits)
        {
            string skin = skins.Find(s => users[s].Count == 1) ?? skins[0];
            if (!list.Exists(e => e.Key.Equals(skin, StringComparison.OrdinalIgnoreCase)))
                list.Add((skin, Label(key, skin)));
        }

        return list;
    }

    // A suit picked earlier that failed gets another go once it's picked again.
    public static void Picked(string skin) => Failed.Remove(skin);

    private static bool Unlocked(RPersistentOptions options, RGameInfo.FShowcaseItem item, string key)
    {
        if (item.Unlocked)
            return true;
        string needs = item.Prerequisite.ToString() ?? "";
        return options.IsOptionsFlagSet(needs.Length == 0 ? "Showcase_" + key + "_Unlocked" : needs);
    }

    // The showcase's own name for a suit, or the package name tidied up.
    private static string Label(string key, string skin)
    {
        if (Labels.TryGetValue(key, out string? known))
            return known;
        string label = GameObject.GetLocalisedString("GFxUI", "Showcase", key, false).ToString() ?? "";
        if (label.Length == 0 || label.StartsWith('?'))
            label = skin.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? skin[Prefix.Length..].Replace('_', ' ') : skin;
        label = label.Trim().ToUpperInvariant();
        Labels[key] = label;
        return label;
    }

    private static bool Batman(string character) => SkinId(Owner(character)).Equals("Batman", StringComparison.OrdinalIgnoreCase);

    // Second versions of a character end in 0 and wear the first one's suits.
    private static string Owner(string character) => character.EndsWith('0') ? character[..^1] : character;

    // The game's rule for which character a skin belongs to: the package prefix off, then the style.
    private static string SkinId(string skin)
    {
        if (skin.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            skin = skin[Prefix.Length..];
        int style = skin.LastIndexOf('_');
        return style >= 0 ? skin[..style] : skin;
    }

    // A worn standard suit has its battle damage level on the end of its name.
    private static bool Wearing(string worn, string skin)
    {
        if (!worn.StartsWith(skin, StringComparison.OrdinalIgnoreCase))
            return false;
        ReadOnlySpan<char> rest = worn.AsSpan(skin.Length);
        if (rest.Length == 0)
            return true;
        if (rest[0] != '_' || rest.Length == 1)
            return false;
        foreach (char c in rest[1..])
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return true;
    }

    private static ref RGameRI.FLoadedPlayerCharacter Slot(RGameRI game, int index)
    {
        switch (index)
        {
            case 0: return ref game.PlayerCharacters_0;
            case 1: return ref game.PlayerCharacters_1;
            case 2: return ref game.PlayerCharacters_2;
            case 3: return ref game.PlayerCharacters_3;
            case 4: return ref game.PlayerCharacters_4;
            case 5: return ref game.PlayerCharacters_5;
            case 6: return ref game.PlayerCharacters_6;
            default: return ref game.PlayerCharacters_7;
        }
    }

    // The slot the pawn was loaded from: by its own character name, or the slot whose character is its class.
    private static int SlotOf(RGameRI game, RPawnPlayer pawn)
    {
        FName name = pawn.CharacterName;
        for (int i = 0; i < 8; i++)
        {
            FName slotName = Slot(game, i).CharacterName;
            if (name.Index != 0 && slotName.Index == name.Index && slotName.Number == name.Number)
                return i;
        }

        for (int i = 0; i < 8; i++)
        {
            ref RGameRI.FLoadedPlayerCharacter slot = ref Slot(game, i);
            if (slot.CharacterName.Index != 0 && slot.CharacterData is { IsValid: true } data && data.PlayerClass is { } kind
                && kind.Ptr == pawn.Class.Ptr)
                return i;
        }

        return -1;
    }
}
