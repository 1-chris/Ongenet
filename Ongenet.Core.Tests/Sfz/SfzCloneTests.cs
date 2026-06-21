using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Audio.Instruments.Sampler.Sfz;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Tests.Sfz;

[Collection("SamplerStaticLoader")]
public class SfzCloneTests
{
    // A loader that fails the test if it's ever called (clone must NOT re-decode from disk).
    private sealed class CountingLoader : ISamplerLoadService
    {
        public int Calls;
        public SamplerLoadResult? Load(string path, int presetIndex = -1, IProgress<double>? progress = null) { Calls++; return null; }
        public SamplerLoadResult? LoadFromText(string sourceText, string path, IProgress<double>? progress = null) { Calls++; throw new InvalidOperationException(); }
    }

    private static SamplerInstrument LoadedInstrument()
    {
        const string sfz = "<region> sample=a.wav key=60";
        var lib = new SamplerSampleLibrary(new Dictionary<string, SamplerSample>
        {
            ["a.wav"] = SamplerSample.FromResident(new AudioSampleBuffer(Enumerable.Repeat(0.5f, 100).ToArray(), 1, 44100))
        });
        var inst = new SamplerInstrument();
        inst.Prepare(new AudioFormat(44100, 1));
        inst.ApplyLoad(new SamplerLoadResult
        {
            Regions = SfzLoader.BuildRegions(SfzParser.Parse(sfz), lib),
            Library = lib,
            Path = "/tmp/a.sfz",
            DisplayName = "a",
            Format = SamplerFormat.Sfz,
            SourceText = sfz
        });
        return inst;
    }

    [Fact]
    public void CopyRuntimeStateSharesRegionsWithoutReloading()
    {
        var loader = new CountingLoader();
        var previous = SamplerInstrument.Loader;
        SamplerInstrument.Loader = loader;
        try
        {
            var src = LoadedInstrument();
            var dst = new SamplerInstrument();
            dst.CopyRuntimeStateFrom(src);

            Assert.Equal(src.Regions.Count, dst.Regions.Count);
            Assert.Equal(0, loader.Calls); // never touched disk
        }
        finally { SamplerInstrument.Loader = previous; }
    }

    [Fact]
    public void ProjectClonerDoesNotReDecodeSfzLibrary()
    {
        var loader = new CountingLoader();
        var previous = SamplerInstrument.Loader;
        SamplerInstrument.Loader = loader;
        try
        {
            var project = new Project();
            var track = new Track { Kind = TrackKind.Instrument };
            track.Instruments.Add(new InstrumentSlot(LoadedInstrument()));
            project.Tracks.Add(track);

            var clone = ProjectCloner.Clone(project, new InstrumentRegistry(), new EffectRegistry());

            var cloned = Assert.IsType<SamplerInstrument>(clone.Tracks[0].Instruments[0].Instrument);
            Assert.Single(cloned.Regions);
            Assert.Equal(0, loader.Calls); // the history clone must not re-read the library from disk
        }
        finally { SamplerInstrument.Loader = previous; }
    }
}
