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
            "Uplifting progressive trance · A minor · 138 BPM", UpliftingTranceSongFactory.Create)
    };
}
