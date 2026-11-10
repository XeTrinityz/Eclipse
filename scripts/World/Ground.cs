using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

internal static class Ground
{
    private static readonly FName StandingPose = new("Standing");
    private static readonly FName RelaxedPose = new("Relaxed");

    // Every trace goes here. Some breakables make it throw, so that counts as a miss.
    public static Actor? Trace(Actor from, Vector3 start, Vector3 end, Component.ECollisionFilter filter, Vector3 extent, out Vector3 at, out Vector3 normal)
    {
        at = default;
        normal = default;
        try
        {
            Actor? hit = from.Trace(out at, out normal, end, start, filter, extent, out _, 0);
            return hit is { IsValid: true } ? hit : null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }

    public static bool Ray(Actor from, Vector3 start, Vector3 end, out Vector3 hit, out Vector3 normal) =>
        Trace(from, start, end, Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out hit, out normal) != null && hit != Vector3.Zero;

    // Distance from his feet to whatever is under him, or reach if there's nothing.
    public static float Below(Pawn pawn, float reach)
    {
        Vector3 at = pawn.Location;
        if (!Ray(pawn, at, at - new Vector3(0f, 0f, reach), out Vector3 hit, out _))
            return reach;
        return MathF.Max(0f, at.Z - hit.Z - pawn.GetCollisionHeight());
    }

    public static void Settle(Pawn mover)
    {
        if (mover is RPawnPlayer batman)
            Safe.Run("player pose", () => batman.ForceChangePose(StandingPose, RelaxedPose, default, false, default, default, default, 0.001f));
        Safe.Run("player moved", () => World.Pc?.OnPlayerHasBeenMoved());
    }
}
