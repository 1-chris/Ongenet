using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Persistence.Import.Bitwig;

/// <summary>
/// Experimental native Bitwig <c>.bwproject</c> importer.
/// Extracts sample paths and track-like names from the proprietary binary / nested ZIP payload.
/// Full arrangement mapping remains a research track — prefer <c>.dawproject</c> for reliable import.
/// </summary>
public sealed class BwprojectImporter : IProjectImporter
{
    private static readonly Regex AudioPathRegex = new(
        @"[A-Za-z]:\\(?:[^\x00-\x1F""<>|*\?]{1,240})\.(?:wav|wave|aif|aiff|flac|mp3|ogg|oga|opus|m4a|aac)|" +
        @"/(?:[^\x00-\x1F""<>|*\?]{1,240})\.(?:wav|wave|aif|aiff|flac|mp3|ogg|oga|opus|m4a|aac)|" +
        @"(?:Samples|samples|audio|Audio|recordings|Recordings)/[^\x00-\x1F""<>|*\?]{1,200}\.(?:wav|wave|aif|aiff|flac|mp3|ogg|oga|opus|m4a|aac)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TrackNameRegex = new(
        @"\b(?:Audio|Instrument|Hybrid|Group|Effect|Drum|Bass|Lead|Pad|Vocal|Vox|Kick|Snare|Hat|Perc|FX)\b[^\x00-\x1F]{0,40}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ImportMapper _mapper;

    public BwprojectImporter(IInstrumentRegistry instruments, IEffectRegistry effects, IAudioFileService? audioFiles = null)
    {
        _mapper = new ImportMapper(instruments, effects, audioFiles);
    }

    public string FormatId => "bwproject";

    public bool CanImport(string path) =>
        path.EndsWith(".bwproject", StringComparison.OrdinalIgnoreCase);

    public ImportResult Import(string path)
    {
        var doc = BuildDocument(path);
        return _mapper.Map(doc, FormatId, path);
    }

    internal static ImportDocument BuildDocument(string path)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var doc = new ImportDocument
        {
            Name = Path.GetFileNameWithoutExtension(path)
        };
        doc.Warnings.Add(
            "Native .bwproject import is experimental. Prefer exporting DAWproject from Bitwig for fuller conversion.");

        doc.Tracks.Add(new ImportTrack
        {
            Id = "master",
            Name = "Master",
            Kind = ImportTrackKind.Master,
            Volume = 1.0
        });

        var blobs = CollectBlobs(path);
        var samplePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trackNames = new List<string>();

        foreach (var blob in blobs)
        {
            foreach (var s in ExtractStrings(blob))
            {
                foreach (Match m in AudioPathRegex.Matches(s))
                    samplePaths.Add(NormalizePath(m.Value, projectDir));

                // Also catch bare relative sample filenames with extensions.
                if (LooksLikeAudioFile(s))
                    samplePaths.Add(NormalizePath(s.Trim(), projectDir));

                foreach (Match m in TrackNameRegex.Matches(s))
                {
                    var name = m.Value.Trim();
                    if (name.Length is >= 3 and <= 48 && !trackNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        trackNames.Add(name);
                }
            }
        }

        // Also scan project folder for common Bitwig sample subfolders.
        foreach (var sub in new[] { "samples", "Samples", "audio", "recordings", "bounce" })
        {
            var dir = Path.Combine(projectDir, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                         .Where(LooksLikeAudioFile))
                samplePaths.Add(file);
        }

        var index = 0;
        foreach (var sample in samplePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            index++;
            var name = Path.GetFileNameWithoutExtension(sample);
            if (string.IsNullOrWhiteSpace(name)) name = $"Sample {index}";
            doc.Tracks.Add(new ImportTrack
            {
                Id = $"bw-sample:{index}",
                Name = name,
                Kind = ImportTrackKind.Audio,
                Clips =
                {
                    new ImportClip
                    {
                        Name = name,
                        StartBeat = (index - 1) * 4,
                        LengthBeats = 4,
                        IsAudio = true,
                        SamplePath = sample,
                        StretchToTempo = true
                    }
                }
            });
            if (!File.Exists(sample))
                doc.Warnings.Add($"Bitwig sample path not found: {sample}");
        }

        // Named tracks without samples (structure hint only).
        foreach (var name in trackNames.Take(32))
        {
            if (doc.Tracks.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                continue;
            doc.Tracks.Add(new ImportTrack
            {
                Id = $"bw-name:{name}",
                Name = name,
                Kind = ImportTrackKind.Instrument
            });
        }

        if (samplePaths.Count == 0)
            doc.Warnings.Add("No sample paths extracted from .bwproject; arrangement data was not decoded.");

        return doc;
    }

    private static List<byte[]> CollectBlobs(string path)
    {
        var blobs = new List<byte[]> { File.ReadAllBytes(path) };

        // Some Bitwig packages are ZIP-like; try listing nested entries.
        try
        {
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries.Take(64))
            {
                if (entry.Length is <= 0 or > 32_000_000) continue;
                using var s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                blobs.Add(ms.ToArray());
            }
        }
        catch (InvalidDataException)
        {
            // Not a ZIP — binary project only.
        }
        catch (IOException)
        {
            // Ignore unreadable nested members.
        }

        return blobs;
    }

    private static IEnumerable<string> ExtractStrings(byte[] data)
    {
        // UTF-8 / ASCII runs
        var sb = new StringBuilder();
        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            if (b is >= 32 and <= 126)
                sb.Append((char)b);
            else
            {
                if (sb.Length >= 6) yield return sb.ToString();
                sb.Clear();
            }
        }
        if (sb.Length >= 6) yield return sb.ToString();

        // UTF-16LE runs
        sb.Clear();
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            var c = (char)BitConverter.ToUInt16(data, i);
            if (c is >= (char)32 and <= (char)126)
                sb.Append(c);
            else
            {
                if (sb.Length >= 6) yield return sb.ToString();
                sb.Clear();
            }
        }
        if (sb.Length >= 6) yield return sb.ToString();
    }

    private static bool LooksLikeAudioFile(string s)
    {
        var ext = Path.GetExtension(s);
        return ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".wave", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".flac", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".aif", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".aiff", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string raw, string projectDir)
    {
        var s = raw.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(s)) return s;
        return Path.GetFullPath(Path.Combine(projectDir, s));
    }
}
