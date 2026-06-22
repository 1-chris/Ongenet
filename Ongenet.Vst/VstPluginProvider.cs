using System;
using System.Threading;
using System.Threading.Tasks;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Vst.Vst2;
using Ongenet.Vst.Vst3;

namespace Ongenet.Vst;

/// <summary>
/// Scans for VST2 + VST3 plugins (once) and registers instrument plugins into the instrument registry
/// and audio-effect plugins into the effect registry, so they appear in the Instruments sidebar and the
/// "Add effect" menu respectively. Mirrors <c>ClapPluginProvider</c> / <c>Lv2PluginProvider</c>.
/// </summary>
public sealed class VstPluginProvider
{
    private readonly IInstrumentRegistry _instruments;
    private readonly IEffectRegistry _effects;
    private readonly VstPluginScanner _scanner;
    private readonly Action<string>? _log;

    public VstPluginProvider(IInstrumentRegistry instruments, IEffectRegistry effects, Action<string>? log = null)
    {
        _instruments = instruments;
        _effects = effects;
        _scanner = new VstPluginScanner(log);
        _log = log;
    }

    /// <summary>Scans (off-thread) and registers every discovered VST instrument + effect.</summary>
    public Task ScanAsync() => Task.Run(Scan);

    /// <summary>
    /// Scans and registers discovered VST plugins, probing up to <see cref="VstPluginScanner.MaxConcurrency"/>
    /// modules at once (each read can spin up a Wine host, so serial scanning is very slow). Each plugin is
    /// registered the moment it is read, so the library fills in progressively. Returns (instruments, effects).
    /// </summary>
    public (int Instruments, int Effects) Scan()
    {
        var instruments = 0;
        var effects = 0;
        try
        {
            var candidates = _scanner.FindCandidates();
            var options = new ParallelOptions { MaxDegreeOfParallelism = VstPluginScanner.MaxConcurrency };
            Parallel.ForEach(candidates, options, c =>
            {
                foreach (var d in _scanner.ReadModule(c.Format, c.Path))
                {
                    var id = MakeId(d);
                    var category = d.Format == VstFormat.Vst3 ? "VST3" : "VST2";
                    if (d.IsInstrument)
                    {
                        _instruments.Register(new InstrumentInfo(id, d.Name, () => CreateInstrument(d), category));
                        Interlocked.Increment(ref instruments);
                    }

                    if (d.IsEffect)
                    {
                        _effects.Register(new EffectInfo(id, d.Name, () => CreateEffect(d), category));
                        Interlocked.Increment(ref effects);
                    }
                }
            });

            _log?.Invoke($"VST: registered {instruments} instrument(s), {effects} effect(s).");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"VST scan failed: {ex.Message}");
        }

        return (instruments, effects);
    }

    private static string MakeId(VstPluginDescriptor d) => d.Format == VstFormat.Vst3
        ? Vst3PluginBase.MakeId(d.Path, d.Uid)
        : Vst2PluginBase.MakeId(d.Path, d.Uid);

    private static IInstrument CreateInstrument(VstPluginDescriptor d) => d.Format == VstFormat.Vst3
        ? new Vst3Instrument(d.Path, d.Uid, d.Name)
        : new Vst2Instrument(d.Path, d.Uid, d.Name);

    private static IAudioEffect CreateEffect(VstPluginDescriptor d) => d.Format == VstFormat.Vst3
        ? new Vst3Effect(d.Path, d.Uid, d.Name)
        : new Vst2Effect(d.Path, d.Uid, d.Name);
}
