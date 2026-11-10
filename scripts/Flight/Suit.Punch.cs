using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Super punches. Each hit becomes a heavy bat hit, and the thug is ragdolled and thrown.
internal sealed partial class Suit
{
    private const float PunchPush = 3000f;
    private const float RagdollForceSeconds = 0.25f;
    private const float PunchSpeed = 1400f;
    private const float PunchUp = 0.35f;
    private const int ThrowFrames = 3;
    private const double ThrowWaitMost = 1.5;
    // Force a ragdoll if the hit hasn't by then. Any sooner and it doesn't take.
    private const double ForceRagdollAfter = 0.2;

    private sealed class Throw
    {
        public RPawnVillain Thug = null!;
        public Vector3 Speed;
        public int Frames;
        public double Until;
        public double ForceAt;
        public int Tries;
    }

    private static readonly List<Throw> _throws = new();
    private static RPawnVillain? _forcing;

    // During his hits the game's ragdoll check says yes, the way a bat hit does.
    [Redirect(typeof(RPawnVillain), nameof(RPawnVillain.ShouldGoRagdoll))]
    private static bool ShouldGoRagdollRedirect(RPawnVillain self, Class dmgType, float damageAmount)
    {
        if (_forcing is { } thug && thug.Ptr == self.Ptr)
            return true;

        return self.ShouldGoRagdoll(dmgType, damageAmount);
    }

    [Redirect(typeof(RPawnVillain), nameof(RPawnVillain.ShouldGoRagdollWhenHitByBaseballBat))]
    private static bool ShouldGoRagdollBatRedirect(RPawnVillain self)
    {
        if (_forcing is { } thug && thug.Ptr == self.Ptr)
            return true;

        return self.ShouldGoRagdollWhenHitByBaseballBat();
    }

    [Redirect(typeof(RPawnVillain), nameof(RPawnVillain.DamagedBy))]
    private static RPawnCombat.DamageResult VillainDamagedByRedirect(RPawnVillain self, RPawnCombat.FDamageInfo info)
    {
        Vector3 launch = Vector3.Zero;
        try
        {
            info = SuperPunch(self, info, out launch);
        }
        catch (Exception ex)
        {
            Log.Error("super punch", "Super punch", ex);
        }

        _forcing = launch != Vector3.Zero ? self : null;
        RPawnCombat.DamageResult result;
        try
        {
            result = self.DamagedBy(info);
        }
        finally
        {
            _forcing = null;
        }

        if (launch != Vector3.Zero)
            Safe.Run("super punch launch", () => Launch(self, info, launch));
        return result;
    }

    private static RPawnCombat.FDamageInfo SuperPunch(RPawnVillain thug, RPawnCombat.FDamageInfo info, out Vector3 launch)
    {
        launch = Vector3.Zero;
        string mode = Opt.SuperPunchMode;
        // A flight jab is meant to leave him standing for the next one.
        if (_jabbing || mode == "off" || !IsBatman(info.Attacker) || !ThugUp(thug) || World.Player is not { } batman)
            return info;
        if (mode == "knockouts" && info.DamageAmount < thug.Health)
            return info;

        Vector3 away = thug.Location - batman.Location;
        away.Z = 0f;
        Vector3 dir = away.LengthSquared() > 1f ? Vector3.Normalize(away) : Geo.Facing(batman.Rotation.Yaw);
        Vector3 up = dir + Vector3.UnitZ * PunchUp;
        Vector3 push = up * (PunchPush * Opt.PunchForce.Number);

        // Keep a harder push the hit already had, like the ram's at speed.
        if (info.Impulse.LengthSquared() > push.LengthSquared())
            push = info.Impulse;

        // The game's own switch for a thug who ragdolls from every hit.
        thug.bNeverRagdollWhenHit = false;
        thug.bAlwaysRagdollWhenHit = true;

        // The game's own ragdoll launch from its big hits, applied once the body is live.
        info.RagdollForce = push;
        info.RagdollForceDuration = RagdollForceSeconds;
        info.ARF = RPawnCombat.AdditionalRagdollForce.ARF_HeadBackFeetUp;

        info.Impulse = push;
        info.ForceHitReactionHeading = push;
        info.DamageType = RDmgType_BaseballBat.StaticClass();
        info.bHeavyHit = true;
        info.bWillKnockOverOtherPawns = true;
        launch = up * (PunchSpeed * Opt.PunchForce.Number);
        return info;
    }

    // Runs every frame. Throws each waiting thug once his ragdoll is simulating.
    private static void ThrowStep()
    {
        for (int i = _throws.Count - 1; i >= 0; i--)
        {
            Throw t = _throws[i];
            if (!t.Thug.IsValid || t.Thug.bDeleteMe || t.Thug.Mesh is not { IsValid: true } mesh || Clock.Now > t.Until)
            {
                _throws.RemoveAt(i);
                continue;
            }

            bool live = t.Thug.IsRagdoll() || t.Thug.Physics == Actor.EPhysics.PHYS_RigidBody
                || (t.Thug.RagdollProcessor is { IsValid: true } processor && processor.IsRagdollOn());
            if (!live)
            {
                // Tried again until he's down, or he can be left standing at no health.
                if (Clock.Now > t.ForceAt && !t.Thug.bHidden && !InCar(t.Thug))
                {
                    t.ForceAt = Clock.Now + ForceRagdollAfter;
                    Drop(t.Thug, t.Tries++);
                }

                continue;
            }

            mesh.WakeRigidBody();
            mesh.SetRBLinearVelocity(t.Speed, false);
            if (++t.Frames >= ThrowFrames)
                _throws.RemoveAt(i);
        }
    }

    // Each way the game can put a thug down, harder each try.
    private static void Drop(RPawnVillain thug, int attempt)
    {
        var info = new RPawnCombat.FDamageInfo
        {
            Attacker = World.Player,
            DamageAmount = 1f,
            DamageType = RDmgType_GlideKick.StaticClass(),
            bHeavyHit = true,
        };

        switch (attempt % 5)
        {
            case 0:
                if (thug.Health > 0)
                    thug.GenericGoRagdoll();
                else
                    Safe.Run("ragdoll kill", thug.RagdollKill);
                break;
            case 1:
                if (thug.Controller is RBMAIController { IsValid: true } ai)
                    Safe.Run("drop ai", () => ai.SetStunnedRagdoll(true));
                Safe.Run("drop ragdoll", () => thug.PlayRagdoll(info, RGameInfo.RagdollStunType.RAGSTUN_GlideBoostTackle, false));
                break;
            case 2:
                Safe.Run("drop target", thug.TargetGoRagdoll);
                break;
            case 3:
                Safe.Run("drop generic", thug.GenericGoRagdoll);
                break;
            default:
                if (World.Pc is { } pc)
                    Safe.Run("drop died", () => thug.Died(pc, RDmgType_GlideKick.StaticClass(), thug.Location));
                break;
        }
    }

    // Ragdolls him if needed and throws every part at the same speed.
    private static void Launch(RPawnVillain thug, RPawnCombat.FDamageInfo info, Vector3 launch)
    {
        // The tackle ragdoll is the fallback when the plain one doesn't take.
        if (!thug.IsRagdoll())
            thug.GenericGoRagdoll();
        if (!thug.IsRagdoll())
            thug.PlayRagdoll(info, RGameInfo.RagdollStunType.RAGSTUN_GlideBoostTackle, false);

        _throws.RemoveAll(t => t.Thug.Ptr == thug.Ptr);
        _throws.Add(new Throw { Thug = thug, Speed = launch, Until = Clock.Now + ThrowWaitMost, ForceAt = Clock.Now + ForceRagdollAfter });
    }
}
