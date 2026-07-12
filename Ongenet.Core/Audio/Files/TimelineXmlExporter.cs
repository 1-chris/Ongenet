using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Timeline XML handoff export for video/post workflows. This is a custom XML timeline summary —
/// not binary AAF/OMF. Use for reference or tooling that accepts this schema; for pro interchange
/// import a real AAF writer or external conversion.
/// </summary>
public static class TimelineXmlExporter
{
    public const string DefaultExtension = ".ongen-timeline.xml";

    public static void Export(Project project, string outputPath, double bpm)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<OngenetTimeline xmlns=\"https://ongenet.dev/timeline-xml/1\">");
        sb.AppendLine($"  <Header project=\"{Escape(project.Name)}\" tempo=\"{bpm:F2}\" format=\"timeline-xml-v1\"/>");
        sb.AppendLine("  <Timeline>");

        foreach (var track in project.Tracks.Where(t => !t.IsBus))
        {
            sb.AppendLine($"    <Track name=\"{Escape(track.Name)}\" id=\"{track.Id}\">");
            foreach (var clip in track.Clips)
            {
                if (clip.IsAudio && clip.Samples is not null)
                {
                    sb.AppendLine(
                        $"      <AudioClip name=\"{Escape(clip.Name)}\" startBeat=\"{clip.StartBeat:F4}\" lengthBeat=\"{clip.LengthBeats:F4}\" sampleRate=\"{clip.Samples.SampleRate}\" channels=\"{clip.Samples.Channels}\"/>");
                }
                else if (clip.IsMidi)
                {
                    sb.AppendLine(
                        $"      <MidiClip name=\"{Escape(clip.Name)}\" startBeat=\"{clip.StartBeat:F4}\" lengthBeat=\"{clip.LengthBeats:F4}\" noteCount=\"{clip.Notes.Count}\"/>");
                }
            }
            sb.AppendLine("    </Track>");
        }

        sb.AppendLine("  </Timeline>");
        sb.AppendLine("</OngenetTimeline>");
        File.WriteAllText(outputPath, sb.ToString());
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>Obsolete alias — use <see cref="TimelineXmlExporter"/>.</summary>
[Obsolete("TimelineXmlExporter replaces the misnamed AafExporter (custom XML, not binary AAF).")]
public static class AafExporter
{
    public static void Export(Project project, string outputPath, double bpm)
        => TimelineXmlExporter.Export(project, outputPath, bpm);
}
