using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Tests.Effects;

/// <summary>
/// Guards against Prepare/Process races when track changes (e.g. "Render clip to new track") overlap
/// with live playback — the pattern that crashed Delay and Multiband OTT.
/// </summary>
public class EffectPrepareConcurrencyTests
{
    private static readonly AudioFormat FormatA = new(44100, 2);
    private static readonly AudioFormat FormatB = new(48000, 2);

    public static IEnumerable<object[]> BuiltInEffects()
    {
        var registry = new EffectRegistry();
        foreach (var info in registry.Available)
            yield return new object[] { info.Id };
    }

    [Theory]
    [MemberData(nameof(BuiltInEffects))]
    public async Task BuiltInEffect_ConcurrentPrepareAndProcess_DoesNotThrow(string typeId)
    {
        var fx = new EffectRegistry().Create(typeId);
        fx.Prepare(FormatA);
        var buf = new float[512 * 2];
        buf[0] = 0.25f;
        buf[1] = -0.25f;
        Exception? caught = null;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
        var audio = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try { fx.Process(buf); }
                catch (Exception ex) { caught = ex; return; }
            }
        });

        var ui = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                fx.Prepare(FormatB);
                fx.Prepare(FormatA);
            }
        });

        await Task.WhenAll(audio, ui);
        Assert.Null(caught);
    }
}
