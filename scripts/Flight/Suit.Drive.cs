using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// We place him with sweeps under PHYS_None, since the game overrode every physics mode we tried.
internal sealed partial class Suit
{
    private const float BlockedBy = 1f;
    private const float StepUp = 35f;
    private const float HeadingFrom = 100f;
    private const float Heading = 12f;

    private static readonly Vector3[] Axes = { Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY };

    // Where he should be. Each frame starts here, since the game nudges him between frames.
    private Vector3 _at;

    private const string FlyingName = "PlayerFlying";
    private static readonly FName FlyingState = new(FlyingName);
    private static readonly FName FallingState = new("FallingMode");

    private static bool InFlyingState(StateObject pc) => pc.GetStateName().ToString() == FlyingName;

    private void StartCarry(RPawnPlayer pawn, RPlayerController pc)
    {
        _at = pawn.Location;
        pc.GotoState(FlyingState);
        pawn.SetPhysics(Actor.EPhysics.PHYS_None, false);
        pawn.Velocity = Vector3.Zero;
        pawn.Acceleration = Vector3.Zero;
    }

    private void Drive(RPawnPlayer pawn, RPlayerController pc, float dt)
    {
        bool special = pawn.CurrentSpecialMove is { IsValid: true };
        KeepState(pc, special);
        if (pawn.Physics != Actor.EPhysics.PHYS_None && !special)
            pawn.SetPhysics(Actor.EPhysics.PHYS_None, false);

        Vector3 from = pawn.Location;
        Vector3 want = _at + _fm.Velocity * dt;
        Vector3 delta = want - from;
        RamStep(pawn, delta);
        if (delta.LengthSquared() > 0.0001f)
            pawn.Move(delta);

        Vector3 left = want - pawn.Location;
        var flatLeft = new Vector3(left.X, left.Y, 0f);
        if (flatLeft.LengthSquared() > BlockedBy * BlockedBy && StepOver(pawn, flatLeft))
            left = want - pawn.Location;

        if (left.LengthSquared() > BlockedBy * BlockedBy)
        {
            // Blocked. Try what's left one axis at a time so he slides along walls and floors.
            foreach (Vector3 axis in Axes)
            {
                Vector3 part = axis * Vector3.Dot(left, axis);
                if (part.LengthSquared() > 0.01f)
                    pawn.Move(part);
            }

            Vector3 got = pawn.Location - from;

            // Stop pushing into what stopped him, unless a ram is still moving it away.
            Vector3 fv = _fm.Velocity;
            bool keep = RamPassing;
            if (!keep && MathF.Abs(delta.X) > 1f && MathF.Abs(got.X) < MathF.Abs(delta.X) * 0.3f)
                fv.X = 0f;
            if (!keep && MathF.Abs(delta.Y) > 1f && MathF.Abs(got.Y) < MathF.Abs(delta.Y) * 0.3f)
                fv.Y = 0f;
            if (!keep && MathF.Abs(delta.Z) > 1f && MathF.Abs(got.Z) < MathF.Abs(delta.Z) * 0.3f)
                fv.Z = 0f;
            float lost = _fm.Velocity.Length() - fv.Length();
            if (lost > CrashFrom)
                CrashFeel(pawn, lost, Vector3.Normalize(_fm.Velocity));
            _fm.Velocity = fv;
        }

        _at = pawn.Location;

        // Kept near zero, or the standing pose plays walk and run cycles in mid air. The city only
        // sends street crowds ahead of a pawn whose own velocity shows a heading, and with none
        // the streets empty while he flies. So while he flies fast the pawn holds a speed just
        // past the 10 the game asks for, along his heading.
        var level = new Vector3(_fm.Velocity.X, _fm.Velocity.Y, 0f);
        pawn.Velocity = level.Length() > HeadingFrom ? Vector3.Normalize(level) * Heading : Vector3.Zero;
        pawn.Acceleration = Vector3.Zero;
    }

    // Steps him over kerbs. Only the world is stepped over, barriers and crates get smashed.
    private static bool StepOver(RPawnPlayer pawn, Vector3 flat)
    {
        Vector3 start = pawn.Location;
        float radius = pawn.GetCollisionRadius(), half = pawn.GetCollisionHeight();
        Vector3 feet = start - Vector3.UnitZ * (half - 15f);
        Actor? low = Ground.Trace(pawn, feet, feet + Vector3.Normalize(flat) * (flat.Length() + radius + 10f),
            Component.ECollisionFilter.COF_TraceAll, new Vector3(radius * 0.8f, radius * 0.8f, 10f), out _, out _);
        if (low is not (null or Volume) && !low.bWorldGeometry)
            return false;

        pawn.Move(Vector3.UnitZ * StepUp);
        float rose = pawn.Location.Z - start.Z;
        if (rose < 5f)
        {
            pawn.Move(start - pawn.Location);
            return false;
        }

        Vector3 lifted = pawn.Location;
        pawn.Move(flat);
        Vector3 went = pawn.Location - lifted;
        if (Vector3.Dot(went, flat) < flat.LengthSquared() * 0.5f)
        {
            pawn.Move(start - pawn.Location);
            return false;
        }

        pawn.Move(-Vector3.UnitZ * rose);
        return true;
    }

    private void KeepState(RPlayerController pc, bool special)
    {
        // An attack or gadget can take the controller for a moment. Let it finish.
        if (InFlyingState(pc) || special)
            return;

        pc.GotoState(FlyingState);
    }
}
