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
    public const string TakeOffSound = "Ww_CHR_BatmanMovementFoley.Play_Batman_Glide_OneShot";
    public const string BoostSound = "Ww_CHR_BatmanFoleyCape.Play_BM_CAPE_GLIDE_BOOST_IN";
    public const string MoveSound = "Ww_CHR_BatmanMovementFoley.Play_Batman_Cape_Turn";
    public const string DiveSound = "Ww_CHR_BatmanMovementFoley.Play_Batman_Cape_Dive";
    // A body hitting the ground hard, and the concrete under it breaking up.
    public const string SlamSound = "Ww_CHR_Combat_Brute.Play_NPC_Brute_HitGround_ANIM";
    public const string SlamDebris = "Ww_OBJ_Dest_ConcreteStoneBrick.Play_Concrete_Impacts";
    public const string WaveSound = "Ww_CHR_Combat.Play_Shockwave";
    public const string LandSound = "Ww_CHR_BatmanFoleyBelt.Play_BM_Gear_Glide_Boost_Land_a";
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

// A laser held between two points. The effect is moved, turned and sized each time.
internal sealed class FxBeam
{
    private static readonly FName Length = new("BeamLength");
    // Which beam each effect belongs to, since the pool can hand a freed one to the other beam.
    private static readonly Dictionary<nint, FxBeam> Owners = new();

    private readonly string _path;
    private ParticleSystemComponent? _fx;

    public FxBeam(string path) => _path = path;

    // The pool can take back the beam when it's full, so check it's still ours.
    private bool StillMine(ParticleSystemComponent fx) =>
        fx.IsValid && !fx.bWasDeactivated && !fx.bWasCompleted
        && fx.Template is { IsValid: true } template && Fx.System(_path) is { } system && template.Ptr == system.Ptr
        && Owners.TryGetValue(fx.Ptr, out FxBeam? owner) && ReferenceEquals(owner, this);

    public void Show(Vector3 from, Vector3 to)
    {
        if (_fx == null || !StillMine(_fx))
        {
            Forget();
            _fx = Fx.Spawn(_path, from, to - from);
            if (_fx != null)
            {
                _fx.SetAbsolute(true, true, true);
                Owners[_fx.Ptr] = this;
            }
        }

        if (_fx is not { } fx)
            return;

        float length = Vector3.Distance(from, to);
        fx.SetTranslation(from);
        fx.SetRotation(Fx.Facing(to - from));
        fx.SetFloatParameter(Length, length);
        // The laser has two beam emitters, the core and its glow.
        for (int i = 0; i < 2; i++)
            fx.SetBeamDistance(i, length);
    }

    public void Hide()
    {
        // Killed outright, or it hangs in place. Not if the pool already took it.
        if (_fx is { } fx && StillMine(fx))
        {
            Safe.Run("beam off", () =>
            {
                for (int i = 0; i < 2; i++)
                    fx.SetKillOnDeactivate(i, true);
                fx.DeactivateSystem();
                fx.KillParticlesForced();
            });
        }

        Forget();
    }

    // A new world took the old one's effects with it.
    public void Drop() => Forget();

    private void Forget()
    {
        if (_fx != null && Owners.TryGetValue(_fx.Ptr, out FxBeam? owner) && ReferenceEquals(owner, this))
            Owners.Remove(_fx.Ptr);
        _fx = null;
    }
}
