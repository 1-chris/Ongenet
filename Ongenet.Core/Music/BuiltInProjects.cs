using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Music;

/// <summary>One built-in (code-generated) project the user can open from the library's Projects tab.</summary>
public sealed record BuiltInProjectInfo(string Name, string Description,
    Func<IInstrumentRegistry, Project> Create);

/// <summary>
/// The catalog of built-in projects: deterministic factory songs shipped as code, so they need no
/// bundled files and always match the running app's instruments and presets. Building one always
/// produces a fresh, independent <see cref="Project"/>.
/// </summary>
public static class BuiltInProjects
{
    public static IReadOnlyList<BuiltInProjectInfo> All { get; } = new[]
    {
        new BuiltInProjectInfo(PreviewSongFactory.SongName,
            "Deep progressive house · C major · 128 BPM", PreviewSongFactory.Create),
        new BuiltInProjectInfo(DarkDnbSongFactory.SongName,
            "Dark drum & bass · F minor · 170 BPM", DarkDnbSongFactory.Create),
        new BuiltInProjectInfo(UpliftingTranceSongFactory.SongName,
            "Uplifting progressive trance · A minor · 138 BPM", UpliftingTranceSongFactory.Create),
        new BuiltInProjectInfo(LoFiBeatSongFactory.SongName,
            "Lo-fi hip-hop · D minor · 88 BPM", LoFiBeatSongFactory.Create),
        new BuiltInProjectInfo(HouseStarterSongFactory.SongName,
            "Four-on-the-floor house template · A minor · 124 BPM", HouseStarterSongFactory.Create),
        new BuiltInProjectInfo(TechnoStarterSongFactory.SongName,
            "Driving techno template · A minor · 130 BPM", TechnoStarterSongFactory.Create),
        new BuiltInProjectInfo(TrapBeatSongFactory.SongName,
            "Modern trap sketch · F minor · 140 BPM", TrapBeatSongFactory.Create),
        new BuiltInProjectInfo(FieldModularSongFactory.SongName,
            "Field modular synth sketch · D minor · 120 BPM", FieldModularSongFactory.Create),
        new BuiltInProjectInfo(StaticBloomSongFactory.SongName,
            "Ambient electronica · E minor · 76 BPM", StaticBloomSongFactory.Create)
    };
}
