using System;
using System.Threading.Tasks;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Clap;

/// <summary>
/// Scans for CLAP plugins (once) and registers instrument plugins into the instrument registry and
/// audio-effect plugins into the effect registry, so they appear in the Instruments sidebar and the
/// "Add effect" menu respectively.
/// </summary>
public sealed class ClapPluginProvider
{
    private readonly IInstrumentRegistry _instruments;
    private readonly IEffectRegistry _effects;
    private readonly ClapPluginScanner _scanner;
    private readonly Action<string>? _log;

    public ClapPluginProvider(IInstrumentRegistry instruments, IEffectRegistry effects, Action<string>? log = null)
    {
        _instruments = instruments;
        _effects = effects;
        _scanner = new ClapPluginScanner(log);
        _log = log;
    }

    /// <summary>Scans (off-thread) and registers every discovered CLAP instrument + effect.</summary>
    public Task ScanAsync() => Task.Run(Scan);

    /// <summary>Scans synchronously and registers discovered CLAP plugins. Returns (instruments, effects) counts.</summary>
    public (int Instruments, int Effects) Scan()
    {
        var instruments = 0;
        var effects = 0;
        try
        {
            foreach (var d in _scanner.Scan())
            {
                var path = d.ModulePath;
                var pluginId = d.PluginId;
                var name = d.Name;
                var id = ClapPluginBase.MakeId(path, pluginId);

                if (d.IsInstrument)
                {
                    _instruments.Register(new InstrumentInfo(id, name, () => new ClapInstrument(path, pluginId, name), "CLAP"));
                    instruments++;
                }

                if (d.IsEffect)
                {
                    _effects.Register(new EffectInfo(id, name, () => new ClapEffect(path, pluginId, name), "CLAP"));
                    effects++;
                }
            }

            _log?.Invoke($"CLAP: registered {instruments} instrument(s), {effects} effect(s).");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"CLAP scan failed: {ex.Message}");
        }

        return (instruments, effects);
    }
}
