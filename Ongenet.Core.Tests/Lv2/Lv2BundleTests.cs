using System;
using System.IO;
using System.Linq;
using Ongenet.Lv2;

namespace Ongenet.Core.Tests.Lv2;

public class Lv2BundleTests : IDisposable
{
    private readonly string _dir;

    public Lv2BundleTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ongenet-lv2-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        Write("manifest.ttl", """
            @prefix lv2:  <http://lv2plug.in/ns/lv2core#> .
            @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .
            <http://example.org/amp>   a lv2:Plugin ; lv2:binary <amp.so>   ; rdfs:seeAlso <amp.ttl> .
            <http://example.org/synth> a lv2:Plugin ; lv2:binary <synth.so> ; rdfs:seeAlso <synth.ttl> .
            """);

        Write("amp.ttl", """
            @prefix doap: <http://usefulinc.com/ns/doap#> .
            @prefix lv2:  <http://lv2plug.in/ns/lv2core#> .
            <http://example.org/amp>
                a lv2:Plugin ;
                doap:name "Example Amp" ;
                lv2:port [
                    a lv2:InputPort , lv2:ControlPort ;
                    lv2:index 0 ; lv2:symbol "gain" ; lv2:name "Gain" ;
                    lv2:default 0.0 ; lv2:minimum -90.0 ; lv2:maximum 24.0 ;
                ] , [
                    a lv2:AudioPort , lv2:InputPort ;
                    lv2:index 1 ; lv2:symbol "in" ; lv2:name "In" ;
                ] , [
                    a lv2:AudioPort , lv2:OutputPort ;
                    lv2:index 2 ; lv2:symbol "out" ; lv2:name "Out" ;
                ] .
            """);

        Write("synth.ttl", """
            @prefix doap: <http://usefulinc.com/ns/doap#> .
            @prefix lv2:  <http://lv2plug.in/ns/lv2core#> .
            @prefix atom: <http://lv2plug.in/ns/ext/atom#> .
            @prefix midi: <http://lv2plug.in/ns/ext/midi#> .
            @prefix rdf:  <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .
            @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .
            <http://example.org/synth>
                a lv2:Plugin , lv2:InstrumentPlugin ;
                doap:name "Example Synth" ;
                lv2:port [
                    a lv2:InputPort , atom:AtomPort ;
                    atom:supports midi:MidiEvent ;
                    lv2:index 0 ; lv2:symbol "midi_in" ; lv2:name "MIDI In" ;
                ] , [
                    a lv2:OutputPort , lv2:AudioPort ;
                    lv2:index 1 ; lv2:symbol "out" ; lv2:name "Out" ;
                ] , [
                    a lv2:InputPort , lv2:ControlPort ;
                    lv2:index 2 ; lv2:symbol "mono" ; lv2:name "Mono" ;
                    lv2:default 0 ; lv2:minimum 0 ; lv2:maximum 1 ;
                    lv2:portProperty lv2:toggled ;
                ] , [
                    a lv2:InputPort , lv2:ControlPort ;
                    lv2:index 3 ; lv2:symbol "wave" ; lv2:name "Waveform" ;
                    lv2:default 0 ; lv2:minimum 0 ; lv2:maximum 2 ;
                    lv2:portProperty lv2:enumeration , lv2:integer ;
                    lv2:scalePoint [ rdfs:label "Sine"   ; rdf:value 0 ] ;
                    lv2:scalePoint [ rdfs:label "Saw"    ; rdf:value 1 ] ;
                    lv2:scalePoint [ rdfs:label "Square" ; rdf:value 2 ] ;
                ] .
            """);

        // Dummy binaries so the File.Exists guard passes (never loaded during discovery).
        Write("amp.so", "");
        Write("synth.so", "");
    }

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(_dir, name), content);

    [Fact]
    public void ReadsBothPluginsWithoutDuplicates()
    {
        var plugins = Lv2Bundle.Read(_dir);
        Assert.Equal(2, plugins.Count);
    }

    [Fact]
    public void ClassifiesAmpAsEffectWithControlAndAudioPorts()
    {
        var amp = Lv2Bundle.Read(_dir).Single(p => p.Uri == "http://example.org/amp");

        Assert.Equal("Example Amp", amp.Name);
        Assert.True(amp.IsEffect);
        Assert.False(amp.IsInstrument);
        Assert.True(File.Exists(amp.BinaryPath));

        var gain = amp.Ports.Single(p => p.Symbol == "gain");
        Assert.Equal(PortKind.Control, gain.Kind);
        Assert.Equal(PortDirection.Input, gain.Direction);
        Assert.Equal(-90f, gain.Min);
        Assert.Equal(24f, gain.Max);
        Assert.Equal(1, amp.Ports.Count(p => p.IsAudio && p.Direction == PortDirection.Input));
        Assert.Equal(1, amp.Ports.Count(p => p.IsAudio && p.Direction == PortDirection.Output));
    }

    [Fact]
    public void ClassifiesSynthAsInstrumentWithMidiAndEnumeratedControl()
    {
        var synth = Lv2Bundle.Read(_dir).Single(p => p.Uri == "http://example.org/synth");

        Assert.True(synth.IsInstrument);
        Assert.False(synth.IsEffect);

        var midiIn = synth.Ports.Single(p => p.Symbol == "midi_in");
        Assert.True(midiIn.IsAtomOrEvent);
        Assert.True(midiIn.SupportsMidi);

        var mono = synth.Ports.Single(p => p.Symbol == "mono");
        Assert.True(mono.Toggled);

        var wave = synth.Ports.Single(p => p.Symbol == "wave");
        Assert.True(wave.Enumeration);
        Assert.True(wave.Integer);
        Assert.Equal(new[] { "Sine", "Saw", "Square" }, wave.ScalePoints.Select(s => s.Label).ToArray());
        Assert.Equal(new[] { 0.0, 1.0, 2.0 }, wave.ScalePoints.Select(s => s.Value).ToArray());
    }

    [Fact]
    public void EffectExposesFloatParameterFromControlPortWithoutLoadingBinary()
    {
        var amp = Lv2Bundle.Read(_dir).Single(p => p.Uri == "http://example.org/amp");
        var fx = new Lv2Effect(amp);

        // Parameters are built from the descriptor alone (the dummy .so is never loaded here).
        var gain = Assert.IsType<Ongenet.Core.Audio.Parameters.FloatParameter>(Assert.Single(fx.Parameters));
        Assert.Equal("Gain", gain.Name);
        Assert.Equal(-90, gain.Min);
        Assert.Equal(24, gain.Max);

        gain.Value = 6;
        Assert.Equal(6, gain.Value); // value round-trips through the managed control array
    }

    [Fact]
    public void InstrumentExposesToggleAndChoiceParametersInPortOrder()
    {
        var synth = Lv2Bundle.Read(_dir).Single(p => p.Uri == "http://example.org/synth");
        var inst = new Lv2Instrument(synth);

        Assert.Equal(2, inst.Parameters.Count); // mono (control), wave (control); MIDI/audio ports are not params
        var mono = Assert.IsType<Ongenet.Core.Audio.Parameters.BoolParameter>(inst.Parameters[0]);
        var wave = Assert.IsType<Ongenet.Core.Audio.Parameters.ChoiceParameter>(inst.Parameters[1]);

        mono.Value = true;
        Assert.True(mono.Value);

        wave.SelectedIndex = 2;
        Assert.Equal(2, wave.SelectedIndex); // "Square"
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
