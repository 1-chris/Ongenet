using System;
using System.Threading.Tasks;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Au;

/// <summary>
/// Scans for installed Audio Units (once) and registers music-device plugins into the instrument
/// registry and effect plugins into the effect registry, so they appear in the Instruments sidebar and
/// the "Add effect" menu respectively. macOS only.
/// </summary>
public sealed class AuPluginProvider
{
    private readonly IInstrumentRegistry _instruments;
    private readonly IEffectRegistry _effects;
    private readonly AuPluginScanner _scanner;
    private readonly Action<string>? _log;

    public AuPluginProvider(IInstrumentRegistry instruments, IEffectRegistry effects, Action<string>? log = null)
    {
        _instruments = instruments;
        _effects = effects;
        _scanner = new AuPluginScanner(log);
        _log = log;
    }

    /// <summary>Scans (off-thread) and registers every discovered AU instrument + effect.</summary>
    public Task ScanAsync() => Task.Run(Scan);

    /// <summary>Scans synchronously and registers discovered Audio Units. Returns (instruments, effects) counts.</summary>
    public (int Instruments, int Effects) Scan()
    {
        var instruments = 0;
        var effects = 0;
        try
        {
            foreach (var d in _scanner.Scan())
            {
                var type = d.Type;
                var subType = d.SubType;
                var manufacturer = d.Manufacturer;
                var name = d.Name;
                var id = AuPluginBase.MakeId(type, subType, manufacturer);

                if (d.IsInstrument)
                {
                    _instruments.Register(new InstrumentInfo(id, name,
                        () => new AuInstrument(type, subType, manufacturer, name), "AU"));
                    instruments++;
                }

                if (d.IsEffect)
                {
                    _effects.Register(new EffectInfo(id, name,
                        () => new AuEffect(type, subType, manufacturer, name), "AU"));
                    effects++;
                }
            }

            _log?.Invoke($"AU: registered {instruments} instrument(s), {effects} effect(s).");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"AU scan failed: {ex.Message}");
        }

        return (instruments, effects);
    }
}
