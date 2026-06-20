using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Instruments.Sfz;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Tests.Sfz;

public class SfzCloneTests
{
    // A loader that fails the test if it's ever called (clone must NOT re-decode from disk).
    private sealed class CountingLoader : ISfzLoadService
    {
        public int Calls;
        public SfzLoadResult? Load(string sfzPath, IProgress<double>? progress = null) { Calls++; return null; }
        public SfzLoadResult LoadFromText(string sfzText, string sfzPath, IProgress<double>? progress = null) { Calls++; throw new InvalidOperationException(); }
    }

    private static SfzInstrument LoadedInstrument()
    {
        const string sfz = "<region> sample=a.wav key=60";
        var inst = new SfzInstrument();
        inst.Prepare(new AudioFormat(44100, 1));
        inst.ApplyLoad(new SfzLoadResult
        {
            Document = SfzParser.Parse(sfz),
            Library = new SfzSampleLibrary(new Dictionary<string, SfzSample>
            {
                ["a.wav"] = SfzSample.FromResident(new AudioSampleBuffer(Enumerable.Repeat(0.5f, 100).ToArray(), 1, 44100))
            }),
            SfzPath = "/tmp/a.sfz",
            SfzText = sfz,
            DisplayName = "a"
        });
        return inst;
    }

    [Fact]
    public void CopyRuntimeStateSharesRegionsWithoutReloading()
    {
        var loader = new CountingLoader();
        var previous = SfzInstrument.Loader;
        SfzInstrument.Loader = loader;
        try
        {
            var src = LoadedInstrument();
            var dst = new SfzInstrument();
            dst.CopyRuntimeStateFrom(src);

            Assert.Equal(src.Regions.Count, dst.Regions.Count);
            Assert.Equal(0, loader.Calls); // never touched disk
        }
        finally { SfzInstrument.Loader = previous; }
    }

    [Fact]
    public void ProjectClonerDoesNotReDecodeSfzLibrary()
    {
        var loader = new CountingLoader();
        var previous = SfzInstrument.Loader;
        SfzInstrument.Loader = loader;
        try
        {
            var project = new Project();
            project.Tracks.Add(new Track { Kind = TrackKind.Instrument, Instrument = LoadedInstrument() });

            var clone = ProjectCloner.Clone(project, new InstrumentRegistry(), new EffectRegistry());

            var cloned = Assert.IsType<SfzInstrument>(clone.Tracks[0].Instrument);
            Assert.Single(cloned.Regions);
            Assert.Equal(0, loader.Calls); // the history clone must not re-read the library from disk
        }
        finally { SfzInstrument.Loader = previous; }
    }
}
