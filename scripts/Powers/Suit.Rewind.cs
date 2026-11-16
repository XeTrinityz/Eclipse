using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Rewind. He outruns time and goes back along the path he took to where he was a few seconds ago. Only he
// moves. Thugs stay where they are.
internal sealed partial class Suit
{
    private const double RewindSpan = 3.0, RewindTake = 0.5, RewindEvery = 0.05, RewindCooldown = 6.0;
    // Shorter than this behind him and there's nowhere worth going back to.
    private const double RewindLeast = 0.5;
    private const float RewindWorld = 0.2f;
    // A gap this big between two marks is a teleport or a load, not a path he ran.
    private const float RewindGap = 1500f;
    private const string RewindKey = "eclipse.rewind";
    private const string RewindBack = "Ww_CHR_Combat.Play_TD_Whoosh05_BWD";
    private const string RewindLand = "Ww_CHR_Combat.Play_TD_Whoosh06_BWD";

    private readonly List<(double At, Vector3 Spot, float Yaw)> _path = new();
    private (double At, Vector3 Spot, float Yaw)[] _going = Array.Empty<(double, Vector3, float)>();
    private bool _rewinding;
    private double _rewindFrom, _rewindReady, _pathNext;

    public bool Rewinding => _rewinding;

    public bool RewindReady => !_rewinding && Opt.Rewind.On && _charges >= 1f && Clock.Now >= _rewindReady
        && _path.Count > 1 && Clock.Now - _path[0].At >= RewindLeast;

    // Where he'd land, for the HUD's mark.
    public Vector3? RewindSpot => RewindReady ? _path[0].Spot : null;

    private void RewindStep(RPawnPlayer pawn, RPlayerController pc)
    {
        if (_rewinding)
        {
            RewindMove(pawn, pc);
            return;
        }

        RecordPath(pawn);
        if (Controls.Take(Act.Rewind) && RewindReady && !_blitzing && !_tornado && !_flurry && !_slowing)
            StartRewind(pawn, pc);
    }

    private void RecordPath(RPawnPlayer pawn)
    {
        double now = Clock.Now;
        while (_path.Count > 0 && now - _path[0].At > RewindSpan)
            _path.RemoveAt(0);
        if (now < _pathNext)
            return;
        _pathNext = now + RewindEvery;
        if (_path.Count > 0 && Vector3.Distance(_path[^1].Spot, pawn.Location) > RewindGap)
            _path.Clear();
        _path.Add((now, pawn.Location, pawn.Rotation.Yaw));
    }

    private void StartRewind(RPawnPlayer pawn, RPlayerController pc)
    {
        if (_running)
        {
            _camHandOff = true;
            try
            {
                EndRun(RunEnd.Stop);
            }
            finally
            {
                _camHandOff = false;
            }
        }
        else if (!_ownCam)
        {
            StartView();
        }

        _ownCam = true;
        _ownCamOff = double.MaxValue;
        _charges -= 1f;
        _rewinding = true;
        _rewindFrom = Clock.Now;
        // Newest first, so it plays back the way he came.
        _going = _path.AsEnumerable().Reverse().ToArray();
        _path.Clear();
        Pace.Hold(RewindKey, RewindWorld);
        NoMove(pc, true);
        TrailPath();
        foreach (WornFx trail in _trails)
            trail.Set(pawn, true);
        Sound(pawn, BlitzIn);
        Sound(pawn, RewindBack);
        Rumble.Play(Buzz.Burst);
    }

    // Lightning laid along the way back, in a few long pieces.
    private void TrailPath()
    {
        int pieces = _runBeams.Length;
        int last = _going.Length - 1;
        if (last < 1)
            return;
        for (int i = 0; i < pieces; i++)
        {
            Vector3 from = _going[last * i / pieces].Spot;
            Vector3 to = _going[last * (i + 1) / pieces].Spot;
            if (Vector3.DistanceSquared(from, to) > 100f)
                Arc(from, to, RewindTake + 0.3, run: true);
        }
    }

    private void RewindMove(RPawnPlayer pawn, RPlayerController pc)
    {
        float t = (float)Math.Clamp((Clock.Now - _rewindFrom) / RewindTake, 0.0, 1.0);
        float along = Ease.Smooth(t) * (_going.Length - 1);
        int i = Math.Min((int)along, _going.Length - 2);
        float f = along - i;
        (_, Vector3 a, float yawA) = _going[i];
        (_, Vector3 b, float yawB) = _going[i + 1];
        Vector3 spot = Vector3.Lerp(a, b, f);
        pawn.SetLocation(spot);
        pawn.SetRotation(new Rotator(0f, yawA + Geo.WrapDegrees(yawB - yawA) * f, 0f));
        pawn.Velocity = Vector3.Zero;
        if (t >= 1f)
            EndRewind(pawn, pc);
    }

    private void EndRewind(RPawnPlayer? pawn, PlayerController? pc)
    {
        if (!_rewinding)
            return;

        _rewinding = false;
        _rewindReady = Clock.Now + RewindCooldown;
        _going = Array.Empty<(double, Vector3, float)>();
        Pace.Drop(RewindKey);
        NoMove(pc, false);
        _ownCamOff = Clock.Now + OwnCamAfter;
        if (pawn == null)
            return;

        Safe.Run("rewind settle", () => Ground.Settle(pawn));
        foreach (WornFx trail in _trails)
            trail.Set(pawn, false);
        CrackleOn(pawn, 0.6);
        Sound(pawn, RewindLand);
        Sound(pawn, BlitzOut);
        Rumble.Play(Buzz.Land);
    }

    private void ForgetPath()
    {
        _path.Clear();
        EndRewind(World.Player, World.Pc);
    }
}
