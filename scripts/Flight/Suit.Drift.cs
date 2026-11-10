using System.Numerics;
using BmSDK.BmGame;

namespace Trinity.Eclipse;

// Idle drift. He bobs, sways and leans into moves on a soft spring. The bob only moves his mesh.
internal sealed partial class Suit
{
    private const string MeshLiftKey = "eclipse.meshlift";

    private const float BobSize = 9f;
    private const float SwayRoll = 2.2f, SwayPitch = 1.6f;
    private const float CalmSpeed = 300f, GoneSpeed = 1200f;
    private const float DriftFade = 1.5f;

    // Degrees of lean per unit of speed, and per unit of speed still to gain.
    private const float LeanPerSpeed = 1.1f / Metre;
    private const float PushLean = 0.6f / Metre;
    private const float LeanMost = 14f;

    // Banks by turn rate plus steering, so he leans in before the turn builds.
    private const float BankPerTurn = 0.6f;
    private const float SteerBank = 30f;
    private const float BankMost = 65f;

    private float _steer;

    private const float SpringRate = 4f, SpringDamp = 0.55f;

    // Pose swaps pitch the body against the crossfade, so it reads as one smooth move.
    private const float DiveAngle = 80f;
    private const float PoseBlend = 0.6f;
    private const float SwapSeconds = PoseBlend;

    private float _poseComp;
    private float _swapT = -1f, _swapFrom, _animBefore, _animAfter;

    private void PoseSwap(bool toFast)
    {
        float animNow = _swapT >= 0f ? AnimPitch(_swapT) : _animAfter;
        _swapFrom = _lean.X + _poseComp + animNow;
        _animBefore = animNow;
        _animAfter = toFast ? -DiveAngle : 0f;
        _swapT = 0f;
        _lean.X = 0f;
        _leanVel.X = 0f;
        _poseComp = _swapFrom - _animBefore;
    }

    private float AnimPitch(float t) => _animBefore + (_animAfter - _animBefore) * Ease.Smooth(t / PoseBlend);

    private void CompStep(float dt)
    {
        if (_swapT < 0f)
            return;

        _swapT += dt;
        float seen = _swapFrom + (_animAfter - _swapFrom) * Ease.Smooth(_swapT / SwapSeconds);
        _poseComp = seen - AnimPitch(_swapT);
        if (_swapT >= MathF.Max(SwapSeconds, PoseBlend))
        {
            _swapT = -1f;
            _poseComp = 0f;
        }
    }

    private const float ClimbLag = 0.02f, ClimbLagMost = 25f;

    private float _driftT, _driftWeight;
    private Vector2 _lean, _leanVel;
    private float _lift, _liftVel;
    private float _meshLift, _setLift;
    private float _lastYaw;

    private void ResetDrift()
    {
        _driftT = Random.Shared.NextSingle() * 100f;
        _driftWeight = 0f;
        _lean = _leanVel = Vector2.Zero;
        _lift = _liftVel = 0f;
        _lastYaw = _faceYaw;
    }

    // Extra pitch and roll for this frame in degrees, and the bob on his mesh.
    private Vector2 DriftStep(RPawnPlayer pawn, float dt)
    {
        _driftT += dt;
        // Holding a thug he stays still, or leaning swings his hand off the thug's neck.
        bool on = Opt.HoverDrift.On && !(Holding && _heldKind == Kind.Thug);
        bool fast = _autoFast == true;
        Vector3 v = _fm.Velocity;
        Vector3 push = _fm.Wanted - v;
        Vector3 fwd = Geo.Facing(_faceYaw), right = Geo.Facing(_faceYaw + 90f);

        float flat = MathF.Sqrt(v.X * v.X + v.Y * v.Y) + MathF.Abs(v.Z) * 0.5f;
        float calm = 1f - Ease.Smooth((flat - CalmSpeed) / (GoneSpeed - CalmSpeed));
        float want = on && !fast ? calm : 0f;
        _driftWeight += (want - _driftWeight) * Ease.Follow(DriftFade, dt);

        Vector2 target = Vector2.Zero;
        if (on && !fast)
        {
            float pitch = Vector3.Dot(v, fwd) * LeanPerSpeed + Vector3.Dot(push, fwd) * PushLean;
            float roll = Vector3.Dot(v, right) * LeanPerSpeed + Vector3.Dot(push, right) * PushLean;
            float way = Opt.Lean.Key == "against" ? -1f : 1f;
            target = new Vector2(-Math.Clamp(pitch, -LeanMost, LeanMost), Math.Clamp(roll, -LeanMost, LeanMost)) * way;
        }
        else if (on)
        {
            float turn = Geo.WrapDegrees(_faceYaw - _lastYaw) / MathF.Max(dt, 1e-3f);
            float bank = (turn * BankPerTurn + _steer * SteerBank) * Opt.Bank.Number;
            target = new Vector2(0f, Math.Clamp(bank, -BankMost, BankMost));
        }

        _lastYaw = _faceYaw;
        _leanVel += ((target - _lean) * (SpringRate * SpringRate) - _leanVel * (2f * SpringDamp * SpringRate)) * dt;
        _lean += _leanVel * dt;

        float liftWant = on ? Math.Clamp(-v.Z * ClimbLag, -ClimbLagMost, ClimbLagMost) : 0f;
        _liftVel += ((liftWant - _lift) * (SpringRate * SpringRate) - _liftVel * (2f * SpringDamp * SpringRate)) * dt;
        _lift += _liftVel * dt;

        float w = _driftWeight, t = _driftT;
        float bob = BobSize * w * Wander(t + 13f, 1.3f, 2.3f, 0.5f);
        SetMeshLift(pawn, bob + _lift);

        return new Vector2(
            _lean.X + SwayPitch * w * Wander(t + 31f, 0.41f, 1.13f, 1.97f),
            _lean.Y + SwayRoll * w * Wander(t, 0.53f, 1.31f, 2.27f));
    }

    // Layered slow waves at odd ratios so it never visibly repeats. Stays within -1 to 1.
    private static float Wander(float t, float a, float b, float c) =>
        (MathF.Sin(t * a) + 0.6f * MathF.Sin(t * b + 1.7f) + 0.35f * MathF.Sin(t * c + 4.1f)) / 1.95f;

    // Moves his mesh from its base. If the game moved the mesh, that becomes the base.
    private void SetMeshLift(RPawnPlayer pawn, float lift)
    {
        if (pawn.Mesh is not { IsValid: true } mesh)
            return;

        Vector3 now = mesh.Translation;
        float baseZ = MathF.Abs(now.Z - _setLift) < 0.1f ? now.Z - _meshLift : now.Z;
        _meshLift = lift;
        _setLift = baseZ + lift;
        mesh.SetTranslation(new Vector3(now.X, now.Y, _setLift));
        Shared.Set(MeshLiftKey, _meshLift);
    }

    private void ClearMeshLift(RPawnPlayer pawn)
    {
        if (_meshLift != 0f && pawn.Mesh is { IsValid: true } mesh && MathF.Abs(mesh.Translation.Z - _setLift) < 0.1f)
        {
            Vector3 now = mesh.Translation;
            mesh.SetTranslation(new Vector3(now.X, now.Y, now.Z - _meshLift));
        }

        _meshLift = _setLift = 0f;
        Shared.Set(MeshLiftKey, 0f);
    }

    // Scripts reloaded mid flight. Take the bob back off his mesh.
    private static void RecoverMeshLift(RPawnPlayer pawn)
    {
        float lift = Shared.GetValue(MeshLiftKey, 0f);
        Shared.Set(MeshLiftKey, 0f);
        if (lift != 0f && pawn.Mesh is { IsValid: true } mesh)
        {
            Vector3 now = mesh.Translation;
            mesh.SetTranslation(new Vector3(now.X, now.Y, now.Z - lift));
        }
    }
}
