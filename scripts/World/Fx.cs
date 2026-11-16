using System.Numerics;
using BmSDK;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// The game's own effects and sounds by name, spawned from the world's emitter pool.
internal static class Fx
{
    public const string LaserBeam = "VFX_Security.Particles.Watchtower_Laser_P";
    public const string BeamSparks = "VFX_Gadgets.FX_Batarang_Ricochet_P";
    public const string Ignite = "VFX_Riot.Particles.Molotov_Ignition";
    // These burn for a few seconds and go out on their own.
    public const string GroundFire = "VFX_Weapon.Particles.Horizontal_Grate_Fire";
    public const string BodyFire = "VFX_Vehicles.Particles.Damage_States.FireTrail_Car";
    public const string Fireball = "VFX_Riot.Particles.Vehicle.Fire_Explosion";

    // Flight. A puff of air, a ring of dust and the ground breaking under a slam.
    public const string Gust = "FFX_Combat.fx_GlideKick1_p";
    public const string Dust = "PFX_Batman.fx_BatmanFeetshockwave_p";
    public const string FloorSlam = "PFX_Batman.Particles.VFX_FloorSlam";

    // The hum has a Stop_ twin. The rush has none, so it's put on as a looping sound.
    public const string BeamHum = "Ww_BML_DroneHacker.Play_Batmobile_DroneHacker_Loop";
    public const string Rush = "Ww_CHR_BatmanMovementFoley.Play_Batman_Glide";
    public const string SlideLoop = "Ww_CHR_BatmanBodySlide.Play_Batman_Bodyslide";

    public const string BurnSound = "Ww_VFX_Molotov.Play_Molotov_Fire_Small";
    public const string TakeOffSound = "ww_sbj_jump_subj.Play_SBJ_Whoosh_Med02";
    public const string QuickWhoosh = "Ww_CHR_Combat.Play_TD_Whoosh_Quick";
    public const string SonicIn = "Ww_SBJ_Whoosh_2D.Play_SBJ_Whoosh_Tunnel_In";
    public const string ChargeUp = "Ww_CHR_BatmanShockwaveUpgrade.Play_BatmanLandShockwave_ChargeUp";
    public const string GrabWhoosh = "Ww_CHR_Combat.Play_TD_Whoosh_Slow";
    public const string ThrowWhoosh = "Ww_CHR_Combat.Play_Strike_Whoosh03a_Stirong";
    public const string CarCrash = "Ww_OBJ_Dest_MetalsVehicles.Play_Metal_Heavy_Break";
    public const string RollWhoosh = "Ww_CHR_Combat.Play_TD_Whoosh02";
    public const string LowWhoosh = "Ww_CHR_Combat.Play_TD_Whoosh_Low";
    public const string BoostSound = "Ww_CHR_BatmanFoleyCape.Play_BM_CAPE_GLIDE_BOOST_IN";
    public const string MoveSound = "Ww_CHR_BatmanMovementFoley.Play_Batman_Cape_Turn";
    public const string DiveSound = "Ww_CHR_BatmanMovementFoley.Play_Batman_Cape_Dive";
    // A body hitting the ground hard, and the concrete under it breaking up.
    public const string SlamSound = "Ww_CHR_Combat_Brute.Play_NPC_Brute_HitGround_ANIM";
    public const string SlamDebris = "Ww_OBJ_Dest_ConcreteStoneBrick.Play_Concrete_Impacts";
    public const string WaveSound = "Ww_CHR_Combat.Play_Shockwave";
    public const string LandSound = "Ww_CHR_Combat.Play_TD_Whoosh_Low";
    public const string RunStartSound = "Ww_CHR_BatmanFoleyCape.Play_BM_CAPE_SPRINT_BOOST";
    public const string SkidStopSound = "Ww_CHR_BatmanFootsteps.Play_BM_Footstep_Slidestop";

    private const double RetrySeconds = 5.0;

    private static readonly Dictionary<string, (ParticleSystem? Found, double At)> _systems = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (AkEvent? Found, double At)> _sounds = new(StringComparer.Ordinal);

    // Not every effect is loaded everywhere, so a miss is looked for again a little later.
    private static T? Find<T>(Dictionary<string, (T? Found, double At)> cache, string path) where T : GameObject
    {
        double now = Clock.Now;
        if (cache.TryGetValue(path, out var hit) && (hit.Found is { IsValid: true } || (hit.Found == null && now - hit.At < RetrySeconds)))
            return hit.Found;

        T? found = Game.FindObject<T>(path);
        cache[path] = (found is { IsValid: true } ? found : null, now);
        return cache[path].Found;
    }

    public static ParticleSystem? System(string path) => Find(_systems, path);

    // The pool holds 45 effects and drops the oldest, so extras only spawn with room to spare.
    public static bool Room(int spare)
    {
        if (!Game.TryGetWorldInfo(out WorldInfo? world) || world?.MyEmitterPool is not { IsValid: true } pool)
            return false;
        return pool.MaxActiveEffects <= 0 || pool.ActiveComponents.Count + spare < pool.MaxActiveEffects;
    }

    public static ParticleSystemComponent? Spawn(string path, Vector3 at, Vector3 facing = default) => Spawn(path, at, Facing(facing));

    public static ParticleSystemComponent? Spawn(string path, Vector3 at, Rotator turn)
    {
        if (Find(_systems, path) is not { } system || !Game.TryGetWorldInfo(out WorldInfo? world) || world?.MyEmitterPool is not { IsValid: true } pool)
            return null;

        return pool.SpawnEmitter(system, at, turn, null, null, 0, false);
    }

    // Fixed to a bone, so it goes where the body goes.
    public static ParticleSystemComponent? SpawnOn(string path, SkeletalMeshComponent mesh, FName bone)
    {
        if (Find(_systems, path) is { } system && Game.TryGetWorldInfo(out WorldInfo? world) && world?.MyEmitterPool is { IsValid: true } pool)
            return pool.SpawnEmitterMeshAttachment(system, mesh, bone, false, Vector3.Zero, default);
        return null;
    }

    // Stops an effect we hold and clears what it already drew.
    public static void Kill(ParticleSystemComponent? fx)
    {
        if (fx is not { IsValid: true })
            return;
        Safe.Run("effect off", () =>
        {
            fx.DeactivateSystem();
            fx.KillParticlesForced();
        });
    }

    public static void Play(Actor on, string path)
    {
        if (on.IsValid && Find(_sounds, path) is { } sound)
            on.StartAudioEvent(sound, default, 0);
    }

    private static string StopOf(string play) => play.Replace(".Play_", ".Stop_");

    // A looping sound, ended with its stop event or taken off the actor.
    public static void StartLoop(Actor on, string path)
    {
        if (!on.IsValid || Find(_sounds, path) is not { } sound)
            return;

        if (Find(_sounds, StopOf(path)) != null)
            on.StartAudioEvent(sound, default, 0);
        else
            on.AddLoopingAudioEvent(sound);
    }

    public static void StopLoop(Actor on, string path)
    {
        if (!on.IsValid)
            return;

        if (Find(_sounds, StopOf(path)) != null)
            Play(on, StopOf(path));
        else if (Find(_sounds, path) is { } sound)
            on.RemoveLoopingAudioEvent(sound);
    }

    // Takes off every copy of a looping sound, since adding it again stacks another.
    public static void StopAllLoops(Actor on, string path)
    {
        if (!on.IsValid || Find(_sounds, path) is not { } sound)
            return;
        for (int i = 0; i < 16 && on.HasLoopingAudioEvent(sound); i++)
            on.RemoveLoopingAudioEvent(sound);
    }

    public static Rotator Facing(Vector3 dir)
    {
        if (dir.LengthSquared() < 0.0001f)
            return default;
        dir = Vector3.Normalize(dir);
        return new Rotator(MathF.Asin(Math.Clamp(dir.Z, -1f, 1f)) * Geo.Deg, MathF.Atan2(dir.Y, dir.X) * Geo.Deg, 0f);
    }

    // Turns a ground effect to face this direction, like a ring he flies through.
    public static Rotator Across(Vector3 normal)
    {
        Rotator facing = Facing(normal);
        return new Rotator(facing.Pitch - 90f, facing.Yaw, 0f);
    }

    public static void Forget()
    {
        _systems.Clear();
        _sounds.Clear();
    }
}

// A laser held between two points, on a component he owns so a full effect pool can't skip or take it. The
// effect is moved, turned and sized each time it's shown.
internal sealed class FxBeam
{
    private static readonly FName Length = new("BeamLength");

    private readonly string _path;
    private ParticleSystemComponent? _fx;
    private Actor? _owner;
    private bool _on;

    public FxBeam(string path) => _path = path;

    public string Template => _path[(_path.LastIndexOf('.') + 1)..];

    public void Show(Actor owner, Vector3 from, Vector3 to)
    {
        if (!Ready(owner) || _fx is not { } fx)
            return;

        float length = Vector3.Distance(from, to);
        fx.SetTranslation(from);
        fx.SetRotation(Fx.Facing(to - from));
        fx.SetFloatParameter(Length, length);
        // The laser has two beam emitters, the core and its glow.
        for (int i = 0; i < 2; i++)
            fx.SetBeamDistance(i, length);
        if (_on)
            return;

        _on = true;
        Safe.Run("beam on", () => fx.ActivateSystem(true, false));
    }

    public void Hide()
    {
        // Killed outright, or it hangs in place.
        if (_on && _fx is { IsValid: true } fx)
        {
            Safe.Run("beam off", () =>
            {
                for (int i = 0; i < 2; i++)
                    fx.SetKillOnDeactivate(i, true);
                fx.DeactivateSystem();
                fx.KillParticlesForced();
            });
        }

        _on = false;
    }

    // Taken off him for good, when the world changes or the scripts unload.
    public void Drop()
    {
        ParticleSystemComponent? fx = _fx;
        Actor? owner = _owner;
        _fx = null;
        _owner = null;
        _on = false;
        if (fx is not { IsValid: true })
            return;
        Safe.Run("beam off", () =>
        {
            fx.DeactivateSystem();
            if (owner is { IsValid: true })
                owner.DetachComponent(fx);
        });
    }

    private bool Ready(Actor owner)
    {
        if (_fx is { IsValid: true } && _owner is { IsValid: true } && _owner.Ptr == owner.Ptr)
            return true;
        Drop();
        if (!owner.IsValid || Fx.System(_path) is not { } template)
            return false;

        Safe.Run("beam", () =>
        {
            var c = new ParticleSystemComponent(owner) { bAutoActivate = false };
            c.SetTemplate(template);
            owner.AttachComponent(c);
            c.SetAbsolute(true, true, true);
            _fx = c;
            _owner = owner;
        });
        return _fx != null;
    }
}
