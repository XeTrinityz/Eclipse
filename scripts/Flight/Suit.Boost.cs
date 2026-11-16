using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Boost flight's trails and wind. The trails are his own components, so a full effect pool can't skip them.
internal sealed partial class Suit
{
    private const string FlightTrail = "FFX_Combat.Particles.Trails.fx_SpeedTrails1_p";
    private const string FlightTrailName = "fx_SpeedTrails1_p";
    // Trails show from this share of boost speed. The Flash's gold trail is a different effect.
    private const float FlightTrailFrom = 0.6f;
    private const string BoostWind = "Ww_CHR_WhooshSweetener.Play_Combat_Far_Attack";
    private const string BoostWindKey = "eclipse.boostwind";

    private readonly WornFx[] _flightTrails =
    {
        new(FlightTrail, "Bip01_R_Hand"), new(FlightTrail, "Bip01_L_Hand"), new(FlightTrail, "Bip01_R_Foot"), new(FlightTrail, "Bip01_L_Foot"),
    };
    private bool _boostWind;
    // A dash, roll or burst flares the trails for a moment, even when he is slow.
    private double _trailUntil;
    private RPawnPlayer? _boostOn;

    // Each frame. Trails while the fast pose is on and he's moving quickly, wind while boosting in it.
    private void BoostStep(RPawnPlayer pawn)
    {
        bool fast = _autoFast == true;
        bool trails = (fast && _fm.Velocity.Length() > _fm.BoostSpeed * FlightTrailFrom) || Clock.Now < _trailUntil;
        foreach (WornFx trail in _flightTrails)
            trail.Set(pawn, trails);

        bool wind = fast && _fm.Boosting && Opt.FlightSounds.On;
        if (wind == _boostWind)
            return;

        _boostWind = wind;
        Shared.Set(BoostWindKey, wind);
        if (wind)
        {
            _boostOn = pawn;
            Safe.Run("boost wind on", () => Fx.StartLoop(pawn, BoostWind));
        }
        else
        {
            StopBoostWind();
        }
    }

    private void StopBoostWind()
    {
        if (_boostOn is { IsValid: true } on)
            Safe.Run("boost wind off", () => Fx.StopLoop(on, BoostWind));
        _boostOn = null;
        _boostWind = false;
        Shared.Set(BoostWindKey, false);
    }

    // Flight ended or the world changed. Trails come off and the wind stops.
    private void EndBoost()
    {
        foreach (WornFx trail in _flightTrails)
            trail.Drop();
        StopBoostWind();
    }

    // Scripts reloaded mid boost. Components from the last load are swept and the wind loop is taken off.
    private static void RecoverBoost()
    {
        if (World.Player is { IsValid: true } player)
        {
            if (player.Mesh is SkeletalMeshComponent { IsValid: true } mesh)
                WornFx.Sweep(mesh, FlightTrailName, null);
            if (Shared.GetValue(BoostWindKey, false))
                Fx.StopAllLoops(player, BoostWind);
        }

        Shared.Set(BoostWindKey, false);
    }
}
