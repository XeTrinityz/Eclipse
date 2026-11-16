using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Invincible, no hit reactions and no stuns. The redirects chain so Oracle can use its own too.
internal sealed partial class Suit
{
    private const float BounceForce = 1500f;
    private const string GodKey = "eclipse.god";

    private static bool NoHit => Opt.NoHitOn;

    private static bool NoStun => Opt.NoStunOn;

    // Damage to him is dropped in the redirects below and his health kept full. No game flag is used. With the
    // game's god mode on, the city stopped sending thugs out on foot. GodOff clears the flags an older load set.
    private static void GodStep()
    {
        GodOff();
        if (!Opt.InvincibleOn || !World.InGame)
            return;

        if (World.Player is { } p && p.Health > 0 && p.Health < p.HealthMax)
        {
            p.Health = p.HealthMax;
            p.HealthUpdated();
        }
    }

    // Kill zones still kill, or he's left stuck out of bounds.
    private static bool Invincible(Pawn self, Class? damageType) => Opt.InvincibleOn && IsBatman(self) && !KillZone(damageType);

    private static bool KillZone(Class? damageType)
    {
        if (damageType is not { IsValid: true })
            return false;
        foreach (Class type in damageType.EnumerateSupersAndSelf())
        {
            if (type.Name.ToString() == "RDmgType_KillZone")
                return true;
        }

        return false;
    }

    // A hit in a fight lands with nothing on it.
    [Redirect(typeof(RPawnPlayer), nameof(RPawnPlayer.DamagedBy))]
    private static RPawnCombat.DamageResult PlayerDamagedByRedirect(RPawnPlayer self, RPawnCombat.FDamageInfo info)
    {
        if (Invincible(self, info.DamageType))
            info.DamageAmount = 0;
        return self.DamagedBy(info);
    }

    private static void GodOff()
    {
        if (!Shared.GetValue(GodKey, false))
            return;

        Shared.Set(GodKey, false);
        if (World.Pc is { } pc)
            pc.bGodMode = false;
        if (World.Cheats is { } cm)
            cm.bIgnoreDamage = false;
    }

    private static bool IsYours(PlayerController pc) => World.Pc is { } you && you.Ptr == pc.Ptr;

    private static bool IsBatman(Actor? who) => who is { IsValid: true } && World.Player is { } you && you.Ptr == who.Ptr;

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.IgnoreHitReaction))]
    private static bool IgnoreHitReactionRedirect(RPlayerController self) =>
        (NoHit && IsYours(self)) || self.IgnoreHitReaction();

    [Redirect(typeof(RPlayerControllerCombat), nameof(RPlayerControllerCombat.Stunned))]
    private static void StunnedRedirect(RPlayerControllerCombat self, bool dontPlaySfx)
    {
        if (!NoStun || !IsYours(self))
            self.Stunned(dontPlaySfx);
    }

    [Redirect(typeof(RPlayerControllerCombat), nameof(RPlayerControllerCombat.Zapped))]
    private static void ZappedRedirect(RPlayerControllerCombat self)
    {
        if (!NoStun || !IsYours(self))
            self.Zapped();
    }

    [Redirect(typeof(RPlayerControllerCombat), nameof(RPlayerControllerCombat.Lasered))]
    private static void LaseredRedirect(RPlayerControllerCombat self)
    {
        if (!NoStun || !IsYours(self))
            self.Lasered();
    }

    // A charging thug only grabs him if the game thinks he can be grabbed.
    [Redirect(typeof(RCombatMove_VillainChargeAttack), nameof(RCombatMove_VillainChargeAttack.CanTargetBeGrabbed))]
    private static bool CanTargetBeGrabbedRedirect(RCombatMove_VillainChargeAttack self) =>
        !(NoHit && IsBatman(self.Target)) && self.CanTargetBeGrabbed();

    [Redirect(typeof(RCombatMove_VillainChargeAttack), nameof(RCombatMove_VillainChargeAttack.ChargeContact))]
    private static void ChargeContactRedirect(RCombatMove_VillainChargeAttack self)
    {
        if (!Bounce(self))
            self.ChargeContact();
    }

    [Redirect(typeof(RCombatMove_VillainChargeAttack), nameof(RCombatMove_VillainChargeAttack.Impact))]
    private static void ChargeImpactRedirect(RCombatMove_VillainChargeAttack self)
    {
        if (!Bounce(self))
            self.Impact();
    }

    // A grab from behind gives up before it starts. If one lands, the grabber goes down.
    [Redirect(typeof(RCombatMove_VillainUnarmedGrab), nameof(RCombatMove_VillainUnarmedGrab.ShouldCancelAttack))]
    private static bool ShouldCancelGrabRedirect(RCombatMove_VillainUnarmedGrab self) =>
        (NoHit && IsBatman(self.Target)) || self.ShouldCancelAttack();

    [Redirect(typeof(RCombatMove_VillainUnarmedGrab), nameof(RCombatMove_VillainUnarmedGrab.DoImpact))]
    private static bool GrabDoImpactRedirect(RCombatMove_VillainUnarmedGrab self) =>
        !Bounce(self) && self.DoImpact();

    [Redirect(typeof(RCombatMove_VillainUnarmedGrab), nameof(RCombatMove_VillainUnarmedGrab.Impact))]
    private static void GrabImpactRedirect(RCombatMove_VillainUnarmedGrab self)
    {
        if (!Bounce(self))
            self.Impact();
    }

    private static readonly Dictionary<nint, double> _bounced = new();

    // Knocks the attacker off him. True when it did, so the game's move is skipped.
    private static bool Bounce(RCombatMove_VillainAttack move)
    {
        if (!NoHit || !IsBatman(move.Target) || move.Villain is not { IsValid: true } thug || World.Pc is not { } pc
            || World.Player is not { IsValid: true } batman)
            return false;

        // One knock per attack, even though the game asks at contact and again at impact.
        double now = Clock.Now;
        if (_bounced.TryGetValue(thug.Ptr, out double at) && now - at < 1.0)
            return true;
        _bounced[thug.Ptr] = now;

        Vector3 away = thug.Location - batman.Location;
        away.Z = 0f;
        Vector3 dir = away.LengthSquared() > 1f ? Vector3.Normalize(away) : Geo.Facing(batman.Rotation.Yaw);
        Safe.Run("bounce", () =>
        {
            if (!ThugUp(thug))
                return;
            RamThug(thug, batman, pc, dir, BounceForce * Opt.RamForce.Number, 0.3f, thug.Location);
        });
        return true;
    }
}
