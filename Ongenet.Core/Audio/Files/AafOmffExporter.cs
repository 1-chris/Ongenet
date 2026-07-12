using System;
using System.IO;
using System.Text;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Files;

/// <summary>AAF/OMF interchange exporter — structured XML handoff until binary AAF SDK is integrated.</summary>
public static class AafOmffExporter
{
    public const string AafExtension = ".aaf.xml";
    public const string OmfExtension = ".omf.xml";

    public static void ExportAaf(Project project, string path, double bpm)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<AAFHandoff project=\"{Escape(project.Name)}\" bpm=\"{bpm:F2}\">");
        foreach (var track in project.Tracks)
        {
            sb.AppendLine($"  <Track name=\"{Escape(track.Name)}\" kind=\"{track.Kind}\">");
            foreach (var clip in track.Clips)
                sb.AppendLine(
                    $"    <Clip name=\"{Escape(clip.Name)}\" start=\"{clip.StartBeat:F3}\" length=\"{clip.LengthBeats:F3}\" audio=\"{clip.IsAudio}\"/>");
            sb.AppendLine("  </Track>");
        }
        sb.AppendLine("</AAFHandoff>");
        File.WriteAllText(path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? path : path + AafExtension,
            sb.ToString(), Encoding.UTF8);
    }

    public static void ExportOmf(Project project, string path, double bpm)
        => ExportAaf(project, path.Replace(".aaf", ".omf", StringComparison.OrdinalIgnoreCase), bpm);

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");
}
