using BmSDK;
using BmSDK.Engine;
using Sample = BmSDK.Engine.ForceFeedbackWaveform.FWaveformSample;
using Shape = BmSDK.Engine.ForceFeedbackWaveform.EWaveformFunction;

namespace Trinity.Eclipse;

internal enum Buzz
{
    Swap,
    TakeOff,
    Land,
    Hit,
    Heavy,
    Slam,
    Crash,
    Skid,
    Burst,
    // A short hum, played over and over while the Flash runs. Fast is near top speed.
    Run,
    RunFast,
}

// Controller rumble through the game's own force feedback, so it works on any pad the game supports and
// follows the game's vibration setting. The waveforms are made once and kept from the garbage collector,
// since the game keeps playing one after the call that started it.
internal static class Rumble
{
    private static readonly Dictionary<Buzz, ForceFeedbackWaveform> _waves = new();
    private static bool _warned;

    public static void Play(Buzz kind)
    {
        if (!Opt.Rumble.On || !Controls.PadActive || World.Pc is not { } pc)
            return;

        Safe.Run("rumble", () =>
        {
            if (Wave(kind) is { } wave)
                pc.ClientPlayForceFeedbackWaveform(wave, null!);
        });
    }

    public static void Release()
    {
        foreach (ForceFeedbackWaveform wave in _waves.Values)
        {
            if (wave.IsValid)
                Safe.Run("rumble let go", wave.RemoveFromRoot);
        }

        _waves.Clear();
    }

    private static ForceFeedbackWaveform? Wave(Buzz kind)
    {
        if (_waves.TryGetValue(kind, out ForceFeedbackWaveform? have) && have.IsValid)
            return have;

        try
        {
            var wave = new ForceFeedbackWaveform(Package.GetTransientPackage());
            wave.AddToRoot();
            foreach (Sample s in Samples(kind))
                wave.Samples.Push(s);
            _waves[kind] = wave;
            return wave;
        }
        catch (Exception ex)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Info("Rumble: couldn't make a waveform, rumble is off: " + ex.Message);
            }

            return null;
        }
    }

    // Left is the heavy motor, right the light one.
    private static IEnumerable<Sample> Samples(Buzz kind) => kind switch
    {
        Buzz.Swap => new[] { S(40, 90, Shape.WF_Sin0to180, 0.22f) },
        Buzz.TakeOff => new[] { S(150, 90, Shape.WF_LinearDecreasing, 0.3f) },
        Buzz.Land => new[] { S(140, 70, Shape.WF_LinearDecreasing, 0.22f) },
        Buzz.Hit => new[] { S(120, 200, Shape.WF_Noise, 0.12f) },
        Buzz.Heavy => new[] { S(220, 220, Shape.WF_Noise, 0.18f), S(140, 60, Shape.WF_LinearDecreasing, 0.3f) },
        Buzz.Slam => new[] { S(255, 255, Shape.WF_Noise, 0.3f), S(200, 100, Shape.WF_LinearDecreasing, 0.6f) },
        Buzz.Crash => new[] { S(230, 200, Shape.WF_Noise, 0.25f), S(120, 60, Shape.WF_LinearDecreasing, 0.4f) },
        Buzz.Skid => new[] { S(60, 140, Shape.WF_Noise, 0.45f) },
        Buzz.Burst => new[] { S(90, 60, Shape.WF_LinearIncreasing, 0.15f), S(160, 110, Shape.WF_LinearDecreasing, 0.35f) },
        Buzz.Run => new[] { S(18, 40, Shape.WF_Constant, 0.3f) },
        Buzz.RunFast => new[] { S(45, 80, Shape.WF_Noise, 0.3f) },
        _ => Array.Empty<Sample>(),
    };

    private static Sample S(int left, int right, Shape shape, float seconds) => new()
    {
        LeftAmplitude = (byte)left,
        RightAmplitude = (byte)right,
        LeftFunction = shape,
        RightFunction = shape,
        Duration = seconds,
    };
}
