using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Slow time. The world all but stops while he fights at his own speed. Every hit he lands is kept, and when time
// comes back each thug he struck takes them all at once.
internal sealed partial class Suit
{
    private const float SlowWorld = 0.1f;
    private const double SlowLongest = 6.0, SlowCooldown = 3.0;
    private const string SlowKey = "eclipse.slow";
    // The launch stops getting harder past this many kept hits.
    private const int SlowHitsMost = 12;

    private sealed class Kept
    {
        public RPawnVillain Thug = null!;
        public int Hits;
        public Vector3 Push;
    }

    private readonly Dictionary<nint, Kept> _kept = new();
    private bool _slowing;
    private double _slowEnds, _slowReady;
    private float _slowAnimWas = 1f;

    public bool Slowing => _slowing;

    public bool SlowReady => !_slowing && Opt.SlowTime.On && Clock.Now >= _slowReady;

    public int SlowHits => _kept.Values.Sum(k => k.Hits);

    public float SlowLeft => _slowing ? (float)Math.Clamp((_slowEnds - Clock.Now) / SlowLongest, 0.0, 1.0) : 0f;

    // A thug held on his feet by a flurry or by slow time. His hits are kept instead of landing.
    public bool Holds(RPawnVillain thug) => FlurryHolds(thug) || (_slowing && ThugUp(thug));

    private void SlowStep(RPawnPlayer pawn, RPlayerController pc, float dt)
    {
        bool pressed = Controls.Take(Act.Slow);
        if (_slowing)
        {
            if (pressed || Clock.Now >= _slowEnds || _running)
            {
                EndSlow(pawn, pc, release: true);
                return;
            }

            if (pawn.Mesh is { IsValid: true } mesh)
                mesh.GlobalAnimRateScale = _slowAnimWas / SlowWorld;
            SlowWalk(pawn, dt);
            return;
        }

        if (pressed && SlowReady && !_blitzing && !_flurry && !_tornado && !_running)
            StartSlow(pawn);
    }

    // The game walks him on world time, so he'd crawl. The rest of his real speed is added here. Fight moves are
    // left alone, since their own timing is already sped up.
    private static void SlowWalk(RPawnPlayer pawn, float dt)
    {
        if (pawn.Physics != Actor.EPhysics.PHYS_Walking || pawn.CombatMove is { IsValid: true }
            || pawn.CurrentSpecialMove is { IsValid: true })
            return;
        var v = new Vector3(pawn.Velocity.X, pawn.Velocity.Y, 0f);
        if (v.LengthSquared() > 100f)
            pawn.Move(v * (dt * (1f - SlowWorld)));
    }

    private void StartSlow(RPawnPlayer pawn)
    {
        _slowing = true;
        _slowEnds = Clock.Now + SlowLongest;
        _kept.Clear();
        Pace.Hold(SlowKey, SlowWorld);
        if (pawn.Mesh is { IsValid: true } mesh)
        {
            _slowAnimWas = SaneRate(mesh.GlobalAnimRateScale);
            Shared.Set(SlowKey, _slowAnimWas);
            mesh.GlobalAnimRateScale = _slowAnimWas / SlowWorld;
        }

        // The game's camera runs on world time, so ours follows him through it.
        if (!_ownCam)
            StartView();
        _ownCam = true;
        _ownCamOff = double.MaxValue;
        foreach (WornFx trail in _trails)
            trail.Set(pawn, true);
        Sound(pawn, BlitzIn);
        Sound(pawn, FieldIn);
        Rumble.Play(Buzz.Burst);
    }

    // From the damage redirect, for each of his hits on a thug while time is slow.
    public void KeepHit(RPawnVillain thug)
    {
        if (!_kept.TryGetValue(thug.Ptr, out Kept? kept))
        {
            kept = new Kept { Thug = thug };
            _kept[thug.Ptr] = kept;
        }

        kept.Hits++;
        if (World.Player is not { } you)
            return;
        var away = new Vector3(thug.Location.X - you.Location.X, thug.Location.Y - you.Location.Y, 0f);
        if (away.LengthSquared() > 1f)
            kept.Push += Vector3.Normalize(away);
        _impact.At(you, Box(thug).Centre, Fx.Facing(-away));
        Rumble.Play(Buzz.Hit);
    }

    private void EndSlow(RPawnPlayer? pawn, PlayerController? pc, bool release)
    {
        if (!_slowing)
            return;

        _slowing = false;
        _slowReady = Clock.Now + SlowCooldown;
        Pace.Drop(SlowKey);
        Shared.Set(SlowKey, null);
        if (pawn?.Mesh is { IsValid: true } mesh)
            mesh.GlobalAnimRateScale = _slowAnimWas;
        _ownCamOff = Clock.Now + OwnCamAfter;
        List<Kept> kept = _kept.Values.ToList();
        _kept.Clear();
        if (pawn == null)
            return;
        if (!_running)
        {
            foreach (WornFx trail in _trails)
                trail.Set(pawn, false);
        }

        Sound(pawn, BlitzOut);
        Sound(pawn, FieldOut);
        if (!release || pc is not RPlayerController rpc)
            return;

        // Every kept hit lands at once. More hits, a harder launch.
        float force = Opt.FlashPunchForce.Number;
        int burst = 0;
        foreach (Kept k in kept)
        {
            if (k.Thug is not { IsValid: true, bDeleteMe: false } thug)
                continue;
            float hard = 0.6f + 0.15f * Math.Min(k.Hits, SlowHitsMost);
            var flat = new Vector3(k.Push.X, k.Push.Y, 0f);
            Vector3 dir = flat.LengthSquared() > 0.01f ? Vector3.Normalize(flat) : Geo.Facing(pawn.Rotation.Yaw);
            Safe.Run("slow launch", () =>
            {
                RamThug(thug, pawn, rpc, dir, WaveForce * hard * force, 1f, thug.Location);
                QueueThrow(thug, (dir + Vector3.UnitZ * PunchUp) * (StrikeThrow * hard * force));
            });
            _bursts[burst++ % _bursts.Length].At(pawn, thug.Location - Vector3.UnitZ * thug.GetCollisionHeight(), default);
            CrackleOn(thug, CrackleSeconds);
        }

        if (burst == 0)
            return;
        Sound(pawn, StrikeWhoosh);
        Sound(pawn, Fx.WaveSound);
        Rumble.Play(Buzz.Heavy);
        _camKick = MathF.Max(_camKick, 1.2f);
    }

    // Scripts reloaded in slow time. His anim speed goes back.
    private static void RecoverSlow()
    {
        if (Shared.Get(SlowKey) is not float was)
            return;
        Shared.Set(SlowKey, null);
        if (World.Player?.Mesh is { IsValid: true } mesh)
            mesh.GlobalAnimRateScale = was;
    }

    // The game scales how long its combat moves take. In slow time his are cut by the world's slow down, so his
    // strikes and lunges keep their real speed.
    [Redirect(typeof(RPlayerControllerCombat), nameof(RPlayerControllerCombat.GetCombatMoveDurationMulti))]
    private static float CombatDurationRedirect(RPlayerControllerCombat self)
    {
        float multi = self.GetCombatMoveDurationMulti();
        return I._slowing ? multi * SlowWorld : multi;
    }

    // His anim speed as read back can be garbage. Anything out of reason is taken as normal speed.
    private static float SaneRate(float rate) => rate is > 0.05f and < 20f ? rate : 1f;
}
