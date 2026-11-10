using System.Numerics;

namespace Trinity.Eclipse;

internal struct FlightInput
{
    // X strafes right, Y goes forward.
    public Vector2 Move;

    public float Lift;

    public bool Boost;

    // Camera angles in degrees.
    public float Yaw;

    public float Pitch;

    // From his feet to the ground under him.
    public float Ground;
}

// Hover and boost flight, handing over to each other without a jump in speed.
internal sealed class FlightModel
{
    private const float Deg = Geo.Deg;

    public float MoveSpeed = 1200f;
    public float BoostSpeed = 4000f;
    public float ClimbSpeed = 800f;

    // Seconds to slow from top speed to hover speed with nothing held.
    public float CoastSeconds = 1.2f;

    // Hover, in units a second squared.
    private const float HoverAccel = 1000f;
    private const float HoverBrake = 1400f;
    private const float HardBrake = 4000f;
    private const float ClimbAccel = 1100f;
    private const float FallBrake = 2500f;

    // Boost.
    private const float BoostAccel = 2400f;
    private const float BoostDrag = 400f;
    private const float TurnSlow = 150f, TurnFast = 60f;
    private const float SteerYaw = 40f, SteerPitch = 30f;
    private const float SlopeGravity = 600f;
    private const float SideGrip = 4f;
    private const float MostClimb = 0.9f;

    // A sonic burst kicks his speed and raises top speed and thrust while it lasts.
    private const float SurgeGain = 0.6f;
    private const float SurgeSeconds = 3.5f, SurgeFade = 1.2f;
    private const float SurgeKick = 1800f;
    private const float SurgeThrust = 2f;

    // Near the ground he can't drop faster than this many times his height a second.
    private const float CushionFrom = 250f;
    private const float CushionRate = 3f, CushionSlowest = 40f;

    public Vector3 Velocity;

    public Vector3 Heading = Vector3.UnitX;

    public Vector3 Wanted { get; private set; }

    public bool Boosting { get; private set; }

    private float _surge;

    // How much of a sonic burst is left, 1 for most of it and down to 0 as it fades.
    public float Surge => Math.Clamp(_surge / SurgeFade, 0f, 1f);

    public float TopSpeed => BoostSpeed * (1f + SurgeGain * Surge);

    public void StartSurge()
    {
        _surge = SurgeSeconds;
        Velocity += Heading * SurgeKick;
    }

    public void Reset(Vector3 velocity, float yaw)
    {
        Velocity = velocity;
        Wanted = velocity;
        Boosting = false;
        _surge = 0f;
        float speed = velocity.Length();
        Heading = speed > 100f ? velocity / speed : Geo.Facing(yaw);
    }

    public void Step(float dt, in FlightInput input)
    {
        if (!(dt > 0f))
            return;

        bool boost = input.Boost && input.Move.Y > -0.2f;
        if (boost && !Boosting)
        {
            // Start the heading along his current path, or the camera's look from a hover.
            float speed = Velocity.Length();
            Heading = speed > MoveSpeed * 0.5f ? Velocity / speed : Geo.Dir(input.Yaw, input.Pitch);
        }

        Boosting = boost;
        _surge = boost ? MathF.Max(0f, _surge - dt) : 0f;
        if (boost)
            BoostStep(dt, input);
        else
            HoverStep(dt, input);

        Cushion(input.Ground);
    }

    private void BoostStep(float dt, in FlightInput input)
    {
        Vector3 aim = Geo.Dir(input.Yaw + input.Move.X * SteerYaw, Math.Clamp(input.Pitch + input.Lift * SteerPitch, -80f, 80f));
        float speed = Velocity.Length();
        float fast = Math.Clamp(speed / BoostSpeed, 0f, 1f);
        float rate = (TurnSlow + (TurnFast - TurnSlow) * fast) / Deg;
        Heading = Toward(Heading, aim, rate * dt);
        if (MathF.Abs(Heading.Z) > MostClimb)
        {
            var flat = new Vector3(Heading.X, Heading.Y, 0f);
            flat = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : Geo.Facing(input.Yaw);
            Heading = Vector3.Normalize(flat * MathF.Sqrt(1f - MostClimb * MostClimb) + Vector3.UnitZ * (MathF.Sign(Heading.Z) * MostClimb));
        }

        // Thrust stops at top speed. Only a dive or a burst goes past it.
        float top = TopSpeed;
        float along = Vector3.Dot(Velocity, Heading);
        float thrust = MathF.Max(0f, input.Move.Y);
        float push = along < top ? BoostAccel * (_surge > 0f ? SurgeThrust : 1f) * thrust : 0f;
        if (thrust <= 0f)
            push = -BoostDrag;
        along += (push - SlopeGravity * Heading.Z) * dt;
        if (along > top)
            along = top + (along - top) * MathF.Exp(-dt * 1.5f);
        along = MathF.Max(0f, along);

        // What isn't along the heading fades quickly, so a turn carves instead of sliding.
        Vector3 side = Velocity - Heading * Vector3.Dot(Velocity, Heading);
        side *= MathF.Exp(-dt * SideGrip);
        Velocity = Heading * along + side;
        Wanted = Heading * top;
    }

    private void HoverStep(float dt, in FlightInput input)
    {
        Vector3 fwd = Geo.Facing(input.Yaw);
        Vector3 right = Geo.Facing(input.Yaw + 90f);
        Vector3 dir = fwd * input.Move.Y + right * input.Move.X;
        if (dir.LengthSquared() > 1f)
            dir = Vector3.Normalize(dir);

        var wish = new Vector2(dir.X, dir.Y) * MoveSpeed;
        var flat = new Vector2(Velocity.X, Velocity.Y);
        float speed = flat.Length();
        float accel;
        if (wish.LengthSquared() > 1f && Vector2.Dot(wish, flat) < 0f)
            accel = HardBrake;
        else if (wish.Length() > speed)
            accel = HoverAccel;
        else if (speed > MoveSpeed * 1.05f)
            accel = (BoostSpeed - MoveSpeed) / MathF.Max(CoastSeconds, 0.1f);
        else
            accel = HoverBrake;
        flat += Limit(wish - flat, accel * dt);

        // Coming out of a dive he's still dropping fast, so that's braked harder.
        float wantUp = input.Lift * ClimbSpeed;
        float upAccel = MathF.Abs(Velocity.Z) > ClimbSpeed * 1.2f ? FallBrake : ClimbAccel;
        float up = Velocity.Z + Math.Clamp(wantUp - Velocity.Z, -upAccel * dt, upAccel * dt);

        Velocity = new Vector3(flat.X, flat.Y, up);
        Wanted = new Vector3(wish.X, wish.Y, wantUp);
        if (Velocity.LengthSquared() > 100f * 100f)
            Heading = Vector3.Normalize(Velocity);
    }

    // Turns the boost heading up or down by this many degrees, for a quick pull up or dive.
    public void PitchHeading(float degrees)
    {
        var flat = new Vector3(Heading.X, Heading.Y, 0f);
        if (flat.LengthSquared() < 1e-6f)
            return;

        flat = Vector3.Normalize(flat);
        float pitch = MathF.Asin(Math.Clamp(Heading.Z, -1f, 1f)) * Deg;
        float most = MathF.Asin(MostClimb) * Deg;
        float next = Math.Clamp(pitch + degrees, -most, most) / Deg;
        Heading = Vector3.Normalize(flat * MathF.Cos(next) + Vector3.UnitZ * MathF.Sin(next));
    }

    // Near the ground he settles onto it instead of dropping straight in.
    private void Cushion(float ground)
    {
        if (ground >= CushionFrom || Velocity.Z >= 0f)
            return;

        float most = -(MathF.Max(ground, 0f) * CushionRate + CushionSlowest);
        if (Velocity.Z < most)
            Velocity = new Vector3(Velocity.X, Velocity.Y, most);
    }

    private static Vector2 Limit(Vector2 v, float most)
    {
        float size = v.Length();
        return size > most && size > 1e-4f ? v * (most / size) : v;
    }

    // Turns a towards b by at most the given angle in radians.
    private static Vector3 Toward(Vector3 a, Vector3 b, float most)
    {
        float dot = Math.Clamp(Vector3.Dot(a, b), -1f, 1f);
        float angle = MathF.Acos(dot);
        if (angle <= most || angle < 1e-4f)
            return b;

        Vector3 axis = Vector3.Cross(a, b);
        if (axis.LengthSquared() < 1e-8f)
            axis = Vector3.UnitZ;
        Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), most);
        return Vector3.Normalize(Vector3.Transform(a, turn));
    }
}
