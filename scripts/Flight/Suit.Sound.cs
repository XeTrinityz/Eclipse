using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Flight punch sounds, all the game's own.
internal sealed partial class Suit
{
    // Looked for in this order. Any part of an event's path counts.
    private static readonly string[][] ThudWords =
    {
        new[] { "Batmobile", "Impact", "Vehicle" },
        new[] { "Batmobile", "Impact" },
        new[] { "Punch", "Impact" },
        new[] { "Impact", "Metal" },
        new[] { "Body", "Impact" },
        new[] { "Impact" },
    };

    private AkEvent? _thud;
    private bool _thudLooked;

    private static void Whoosh(RPawnPlayer pawn, bool heavy)
    {
        Safe.Run("whoosh", () =>
        {
            AkEvent? sound = heavy ? pawn.StrikeWoosh : pawn.StrikeWooshWeak;
            sound ??= pawn.StrikeWoosh;
            if (sound is { IsValid: true })
                pawn.StartAudioEvent(sound, 0, 0);
        });
    }

    private static void HitSound(RPawnPlayer pawn, RPawnVillain thug, bool finishing, bool heavy)
    {
        Safe.Run("hit sound", () => pawn.PlaySound_Impact(thug, true, finishing, false, true, heavy, false, true, !heavy, true, false));
    }

    private void Thud(RPawnPlayer pawn, bool heavy)
    {
        Safe.Run("thud", () =>
        {
            if (FindThud() is not { IsValid: true } sound)
                return;
            pawn.StartAudioEvent(sound, 0, 0);
            if (heavy)
                pawn.StartAudioEvent(sound, 0, 0);
        });
    }

    private AkEvent? FindThud()
    {
        if (_thudLooked && (_thud == null || _thud.IsValid))
            return _thud;

        _thudLooked = true;
        _thud = null;
        var all = new List<(AkEvent Sound, string Path)>();
        foreach (AkEvent sound in Game.FindObjects<AkEvent>())
        {
            if (sound is { IsValid: true, IsClassDefaultObject: false })
                all.Add((sound, sound.GetPathName(null)));
        }

        foreach (string[] words in ThudWords)
        {
            _thud = all.FirstOrDefault(a => words.All(w => a.Path.Contains(w, StringComparison.OrdinalIgnoreCase))).Sound;
            if (_thud != null)
                break;
        }

        return _thud;
    }
}
