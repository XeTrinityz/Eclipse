using BmSDK.BmGame;

namespace Trinity.Eclipse;

// Batman's gadgets are off while a hero is on, so the hero's powers can have their buttons. The game's own
// switch stops a gadget being readied or picked and leaves counters alone. Four calls reach the gadget in hand
// without checking it, so they're skipped too.
internal static class NoGadgets
{
    private const string WasKey = "eclipse.nogadgets";
    // The Flash has no grapple either, so its prompts don't show and its button is free.
    private const string GrappleKey = "eclipse.nograpple";
    // Nor does he glide or catch a fall with the cape. The game's own switches, as Knightfall's villains use.
    private const string GlideKey = "eclipse.noglide", CapeKey = "eclipse.nocape";

    private static bool _held, _grappleHeld, _glideHeld;

    public static bool On => _held;

    public static void Step()
    {
        bool want = Opt.Powers.On && World.InGame;
        if (!want)
        {
            Release();
            return;
        }

        if (!Game.TryGetGameInfo(out RGameInfo? info) || info is not { IsValid: true })
            return;

        if (!_held)
        {
            Shared.Set(WasKey, info.bNoGadgets);
            _held = true;
        }

        if (!info.bNoGadgets)
            info.bNoGadgets = true;

        if (Heroes.Is(Hero.Flash))
        {
            if (!_grappleHeld)
            {
                Shared.Set(GrappleKey, info.bNoGrapple);
                _grappleHeld = true;
            }

            if (!info.bNoGrapple)
                info.bNoGrapple = true;
            HoldGlide();
        }
        else
        {
            ReleaseGrapple(info);
            ReleaseGlide();
        }

        // The aim button also raises a flag the gadget in hand reads for itself.
        if (World.Pc?.PlayerInput is RPlayerInput { IsValid: true } input && input.bReadyGadgetButton != 0)
            input.bReadyGadgetButton = 0;
    }

    private static void ReleaseGrapple(RGameInfo info)
    {
        if (!_grappleHeld)
            return;
        _grappleHeld = false;
        info.bNoGrapple = Shared.GetValue(GrappleKey, false);
        Shared.Set(GrappleKey, null);
    }

    private static void HoldGlide()
    {
        if (World.Pc is not RPlayerController { IsValid: true } pc || World.Player is not RPawnPlayer { IsValid: true } pawn)
            return;

        if (!_glideHeld)
        {
            Shared.Set(GlideKey, pc.bCapeGlideDisabled);
            Shared.Set(CapeKey, pawn.bCanCapeGlide);
            _glideHeld = true;
        }

        if (!pc.bCapeGlideDisabled)
            pc.bCapeGlideDisabled = true;
        if (pawn.bCanCapeGlide)
            pawn.bCanCapeGlide = false;
    }

    private static void ReleaseGlide()
    {
        if (!_glideHeld)
            return;
        _glideHeld = false;
        PutGlideBack();
    }

    private static void PutGlideBack()
    {
        if (Shared.Get(GlideKey) is bool off && World.Pc is RPlayerController { IsValid: true } pc)
            pc.bCapeGlideDisabled = off;
        if (Shared.Get(CapeKey) is bool can && World.Player is RPawnPlayer { IsValid: true } pawn)
            pawn.bCanCapeGlide = can;
        Shared.Set(GlideKey, null);
        Shared.Set(CapeKey, null);
    }

    public static void Release()
    {
        ReleaseGlide();
        if (_grappleHeld && Game.TryGetGameInfo(out RGameInfo? held) && held is { IsValid: true })
            ReleaseGrapple(held);
        if (!_held)
            return;

        _held = false;
        bool was = Shared.GetValue(WasKey, false);
        Shared.Set(WasKey, null);
        if (Game.TryGetGameInfo(out RGameInfo? info) && info is { IsValid: true })
            info.bNoGadgets = was;
    }

    // A new world has its own game info, with its own switch.
    public static void Forget()
    {
        _held = false;
        _grappleHeld = false;
        _glideHeld = false;
        Shared.Set(WasKey, null);
        Shared.Set(GrappleKey, null);
        Shared.Set(GlideKey, null);
        Shared.Set(CapeKey, null);
    }

    // Scripts reloaded while the gadgets were off.
    public static void Recover()
    {
        PutGlideBack();
        if (Shared.Get(GrappleKey) is bool grapple)
        {
            Shared.Set(GrappleKey, null);
            if (Game.TryGetGameInfo(out RGameInfo? held) && held is { IsValid: true })
                held.bNoGrapple = grapple;
        }

        if (Shared.Get(WasKey) is not bool was)
            return;

        Shared.Set(WasKey, null);
        if (Game.TryGetGameInfo(out RGameInfo? info) && info is { IsValid: true })
            info.bNoGadgets = was;
    }

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.ReadyGadget))]
    private static void ReadyGadget(RPlayerController self, bool cantQuickThrow)
    {
        if (!_held)
            self.ReadyGadget(cantQuickThrow);
    }

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.GadgetPressed))]
    private static void GadgetPressed(RPlayerController self)
    {
        if (!_held)
            self.GadgetPressed();
    }

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.SecondaryFireGadget))]
    private static void SecondaryFireGadget(RPlayerController self)
    {
        if (!_held)
            self.SecondaryFireGadget();
    }

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.FireQuickGadgetType))]
    private static void FireQuickGadgetType(RPlayerController self, RPawnPlayer.EQuickGadgetType gadgetType, bool overrideCombatMove)
    {
        if (!_held)
            self.FireQuickGadgetType(gadgetType, overrideCombatMove);
    }
}
