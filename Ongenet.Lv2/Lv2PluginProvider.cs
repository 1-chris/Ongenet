using System;
using System.Threading.Tasks;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Lv2;

/// <summary>
/// Scans for LV2 plugins (once) and registers instrument plugins into the instrument registry and
/// audio-effect plugins into the effect registry, so they appear in the Instruments sidebar and the
/// "Add effect" menu respectively. Plugins requiring features this host doesn't provide are skipped.
/// </summary>
public sealed class Lv2PluginProvider
{
    private readonly IInstrumentRegistry _instruments;
    private readonly IEffectRegistry _effects;
    private readonly Lv2PluginScanner _scanner;
    private readonly Action<string>? _log;

    public Lv2PluginProvider(IInstrumentRegistry instruments, IEffectRegistry effects, Action<string>? log = null)
    {
        _instruments = instruments;
        _effects = effects;
        _scanner = new Lv2PluginScanner(log);
        _log = log;
    }

    /// <summary>Scans (off-thread) and registers every discovered LV2 instrument + effect.</summary>
    public Task ScanAsync() => Task.Run(Scan);

    /// <summary>Scans synchronously and registers discovered LV2 plugins. Returns (instruments, effects) counts.</summary>
    public (int Instruments, int Effects) Scan()
    {
        var instruments = 0;
        var effects = 0;
        try
        {
            foreach (var d in _scanner.Scan())
            {
                if (!Lv2PluginBase.SupportsRequiredFeatures(d.RequiredFeatures))
                {
                    var missing = string.Join(", ", Lv2PluginBase.UnsupportedFeatures(d.RequiredFeatures));
                    _log?.Invoke($"LV2: skipped '{d.Name}' ({d.Uri}): unsupported required feature(s): {missing}");
                    continue;
                }

                var id = Lv2PluginBase.MakeId(d.Uri);
                var descriptor = d;

                if (d.IsInstrument)
                {
                    _instruments.Register(new InstrumentInfo(id, d.Name, () => new Lv2Instrument(descriptor), "LV2"));
                    instruments++;
                }

                if (d.IsEffect)
                {
                    _effects.Register(new EffectInfo(id, d.Name, () => new Lv2Effect(descriptor), "LV2"));
                    effects++;
                }
            }

            _log?.Invoke($"LV2: registered {instruments} instrument(s), {effects} effect(s).");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"LV2 scan failed: {ex.Message}");
        }

        return (instruments, effects);
    }
}
