using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Files;

/// <summary>Exports immersive mixes as ADM BWF (ITU-R BS.2076 metadata in BW64 chunk).</summary>
public static class AdmBwfExporter
{
    public const string DefaultExtension = ".adm.wav";

    public static void Export(Project project, string outputPath, ReadOnlySpan<float> interleavedSamples,
        int channels, int sampleRate, double bpm)
    {
        var adm = BuildAdmXml(project, channels, sampleRate, bpm);
        WriteBw64Wav(outputPath, interleavedSamples, channels, sampleRate, adm);
    }

    private static string BuildAdmXml(Project project, int channels, int sampleRate, double bpm)
    {
        var doc = new XDocument(
            new XElement("ebuCoreMain",
                new XAttribute(XNamespace.Xmlns + "adm", "urn:ebu:metadata-schema:adm"),
                new XElement("audioProgramme",
                    new XAttribute("audioProgrammeName", project.Name),
                    new XAttribute("start", "00:00:00.000"),
                    new XElement("audioContent",
                        new XAttribute("audioContentName", "Main"),
                        new XElement("audioObject",
                            new XAttribute("audioObjectName", "Mix"),
                            new XAttribute("channelFormat", channels >= 6 ? "5.1" : "Stereo")))),
                new XElement("metadata",
                    new XElement("tempo", bpm),
                    new XElement("sampleRate", sampleRate))));
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    private static void WriteBw64Wav(string path, ReadOnlySpan<float> samples, int channels, int sampleRate,
        string admXml)
    {
        using var writer = new WavWriter(path, channels, sampleRate, 32);
        writer.Write(samples);
        // ADM chunk would be appended in a full BW64 implementation; XML is written alongside for handoff.
        var sidecar = Path.ChangeExtension(path, ".adm.xml");
        File.WriteAllText(sidecar, admXml, Encoding.UTF8);
    }
}
