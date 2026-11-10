using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// On foot heat vision uses the game's radio mode so he strafes facing the aim. Flight uses Move.Beam.
internal sealed partial class Suit
{
    private const string RadioKey = "eclipse.radio";
    private const string RadioMode = "RadioMode";
    private static readonly string[] RadioFrom = { "WalkingMode", "RunningMode" };

    private bool _radio;
    private bool _radioTried;
    private bool _radioLogged;
    private string _radioFrom = "", _radioState = "";

    private void RadioStep(RPawnPlayer? pawn)
    {
        bool beam = _firing && !_flying && !_running && pawn != null;
        if (!beam)
        {
            _radioTried = false;
            if (_radio)
                Safe.Run("radio off", EndRadio);
            return;
        }

        // A fight, a fall or the game itself took him out of it.
        if (_radio && World.Pc is { } now && now.GetStateName().ToString() != RadioMode)
        {
            _radio = false;
            Shared.Set(RadioKey, false);
        }

        // Once a press. If a fight or a fall takes him out of it, it stays off until the next one.
        if (_radio || _radioTried || World.Pc is not { } pc || !FootReady(pawn!, pc))
            return;

        _radioTried = true;
        Safe.Run("radio on", () => StartRadio(pc));
    }

    // Standing, walking or running, not in a fight or a move, and not already on a real call.
    private static bool FootReady(RPawnPlayer pawn, RPlayerController pc) =>
        pawn.Physics == Actor.EPhysics.PHYS_Walking && !InFight(pc) && pc.RadioConversationAction is not { IsValid: true }
        && RadioFrom.Contains(pc.GetStateName().ToString());

    private void StartRadio(RPlayerController pc)
    {
        string before = pc.GetStateName().ToString();
        // The state goes back to this one when it ends.
        pc.RadioModeFromState = new FName(before);
        pc.GotoState(new FName(RadioMode));
        string after = pc.GetStateName().ToString();
        _radio = after == RadioMode;
        _radioFrom = before;
        _radioState = _radio ? after : "";
        Shared.Set(RadioKey, _radio);
        if (!_radio && !_radioLogged)
        {
            _radioLogged = true;
            Log.Info("Heat vision couldn't put him in radio mode, the controller stayed in " + after + ".");
        }
    }

    private void EndRadio()
    {
        if (!_radio)
            return;

        _radio = false;
        Shared.Set(RadioKey, false);
        // A real call that started meanwhile keeps its radio mode.
        if (World.Pc is not { } pc || pc.RadioConversationAction is { IsValid: true })
            return;

        // The game's exit leaves him in the state with no call, so he's sent back.
        pc.EndRadioMode();
        if (_radioState.Length > 0 && _radioFrom.Length > 0 && pc.GetStateName().ToString() == _radioState)
            pc.GotoState(new FName(_radioFrom));
    }

    // A new world starts out of radio mode.
    private void ForgetRadio()
    {
        _radio = false;
        _radioTried = false;
        Shared.Set(RadioKey, false);
        _footFace = false;
        Shared.Set(FaceKey, false);
    }

    // Scripts reloaded mid beam.
    private static void RecoverRadio()
    {
        if (!Shared.GetValue(RadioKey, false))
            return;

        Shared.Set(RadioKey, false);
        if (World.Pc is not { } pc || pc.RadioConversationAction is { IsValid: true } || pc.GetStateName().ToString() != RadioMode)
            return;

        pc.EndRadioMode();
        if (pc.GetStateName().ToString() == RadioMode)
            pc.GotoState(new FName(RadioFrom[0]));
    }
}
