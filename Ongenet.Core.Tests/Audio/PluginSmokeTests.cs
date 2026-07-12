using System;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Instruments;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

/// <summary>Headless smoke tests for built-in instruments (no external VST/CLAP required).</summary>
public sealed class PluginSmokeTests
{
    [Theory]
    [InlineData(typeof(OscillatorInstrument))]
    [InlineData(typeof(FmSynthInstrument))]
    [InlineData(typeof(TripleOscInstrument))]
    public void BuiltInInstrument_ProcessesSilenceWithoutThrowing(Type instrumentType)
    {
        var instrument = (IInstrument)Activator.CreateInstance(instrumentType)!;
        var format = new AudioFormat(44100, 2);
        instrument.Prepare(format);

        var buffer = new float[512];
        instrument.Render(buffer.AsSpan());

        Assert.True(true);
    }
}
