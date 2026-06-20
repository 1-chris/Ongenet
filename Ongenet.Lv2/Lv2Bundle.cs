using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ongenet.Lv2.Interop;
using Ongenet.Lv2.Turtle;

namespace Ongenet.Lv2;

/// <summary>
/// Parses one <c>*.lv2</c> bundle: reads <c>manifest.ttl</c>, follows <c>rdfs:seeAlso</c> to the
/// plugin data files, merges everything into one <see cref="TurtleGraph"/>, and resolves the plugins
/// it declares into <see cref="Lv2PluginDescriptor"/>s (URI, name, classification, ports, required
/// features). Pure metadata work — no plugin binary is loaded here, so scanning a bad bundle can fail
/// cleanly without running foreign code.
/// </summary>
public static class Lv2Bundle
{
    /// <summary>Reads every plugin described by the bundle at <paramref name="bundleDir"/>.</summary>
    public static IReadOnlyList<Lv2PluginDescriptor> Read(string bundleDir)
    {
        var manifestPath = Path.Combine(bundleDir, "manifest.ttl");
        if (!File.Exists(manifestPath)) return Array.Empty<Lv2PluginDescriptor>();

        var graph = new TurtleGraph();
        var manifestBase = new Uri(manifestPath).AbsoluteUri;
        TurtleParser.ParseInto(File.ReadAllText(manifestPath), graph, manifestBase);

        // Pull in the data files referenced by every subject's rdfs:seeAlso (the manifest is usually a
        // thin index; ports/ranges live in these files). Materialise the path list first — parsing
        // mutates the graph the seeAlso objects are enumerated from. Parse each file at most once.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var dataFiles = graph.SubjectsOfType(Lv2Api.ClassPlugin)
            .SelectMany(plugin => graph.Objects(plugin, Lv2Api.PredSeeAlso).Where(o => o.IsIri))
            .Select(see => UriToPath(see.Value, bundleDir))
            .Where(path => path != null && seen.Add(path) && File.Exists(path))
            .ToList();

        foreach (var path in dataFiles)
        {
            try { TurtleParser.ParseInto(File.ReadAllText(path!), graph, new Uri(path!).AbsoluteUri); }
            catch { /* skip an unreadable data file but keep the rest of the bundle */ }
        }

        // A plugin is often typed lv2:Plugin in both the manifest and its data file, so dedupe by URI.
        var results = new List<Lv2PluginDescriptor>();
        var processed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plugin in graph.SubjectsOfType(Lv2Api.ClassPlugin))
        {
            if (!plugin.IsIri || !processed.Add(plugin.Value)) continue;
            var desc = TryReadPlugin(graph, plugin, bundleDir);
            if (desc != null) results.Add(desc);
        }

        return results;
    }

    private static Lv2PluginDescriptor? TryReadPlugin(TurtleGraph graph, Node plugin, string bundleDir)
    {
        if (!plugin.IsIri) return null;

        var binaryNode = graph.GetResource(plugin, Lv2Api.PredBinary);
        var binaryPath = binaryNode is { } b ? UriToPath(b.Value, bundleDir) : null;
        if (binaryPath == null || !File.Exists(binaryPath)) return null;

        var name = graph.GetString(plugin, Lv2Api.PredDoapName);
        if (string.IsNullOrWhiteSpace(name)) name = LastSegment(plugin.Value);

        var ports = graph.Objects(plugin, Lv2Api.PredPort)
            .Select(p => ReadPort(graph, p))
            .Where(p => p != null)
            .Select(p => p!)
            .OrderBy(p => p.Index)
            .ToList();

        var requiredFeatures = graph.GetIris(plugin, Lv2Api.PredRequiredFeature);

        var hasAudioIn = ports.Any(p => p.IsAudio && p.Direction == PortDirection.Input);
        var hasAudioOut = ports.Any(p => p.IsAudio && p.Direction == PortDirection.Output);
        var hasMidiIn = ports.Any(p => p.IsAtomOrEvent && p.Direction == PortDirection.Input && p.SupportsMidi);

        var isInstrument = (graph.HasType(plugin, Lv2Api.ClassInstrument) || hasMidiIn) && hasAudioOut;
        var isEffect = hasAudioIn && hasAudioOut;

        if (!isInstrument && !isEffect) return null; // nothing we can host (e.g. analyser, MIDI filter)

        return new Lv2PluginDescriptor
        {
            BundlePath = bundleDir,
            BinaryPath = binaryPath,
            Uri = plugin.Value,
            Name = name!,
            IsInstrument = isInstrument,
            IsEffect = isEffect,
            Ports = ports,
            RequiredFeatures = requiredFeatures,
            Ui = ReadUi(graph, plugin, bundleDir),
        };
    }

    private static Lv2UiInfo? ReadUi(TurtleGraph graph, Node plugin, string bundleDir)
    {
        var uiNode = graph.GetResource(plugin, Lv2Api.PredUiUi);
        if (uiNode is not { } ui) return null;

        var binNode = graph.GetResource(ui, Lv2Api.PredUiBinary);
        var binaryPath = binNode is { } b ? UriToPath(b.Value, bundleDir) : null;
        if (binaryPath == null || !File.Exists(binaryPath)) return null;

        return new Lv2UiInfo
        {
            Uri = ui.Value,
            BinaryPath = binaryPath,
            BundlePath = bundleDir,
            IsX11 = graph.HasType(ui, Lv2Api.ClassX11Ui),
            RequiredFeatures = graph.GetIris(ui, Lv2Api.PredRequiredFeature),
        };
    }

    private static PortDescriptor? ReadPort(TurtleGraph graph, Node port)
    {
        var types = graph.GetIris(port, Lv2Api.PredType);
        var kind =
            types.Contains(Lv2Api.ClassAudioPort) ? PortKind.Audio :
            types.Contains(Lv2Api.ClassCvPort) ? PortKind.Cv :
            types.Contains(Lv2Api.ClassControlPort) ? PortKind.Control :
            types.Contains(Lv2Api.ClassAtomPort) ? PortKind.Atom :
            types.Contains(Lv2Api.ClassEventPort) ? PortKind.Event :
            PortKind.Unknown;
        if (kind == PortKind.Unknown) return null;

        var direction = types.Contains(Lv2Api.ClassOutputPort) ? PortDirection.Output : PortDirection.Input;

        var index = graph.GetInt(port, Lv2Api.PredIndex) ?? int.MaxValue;
        var symbol = graph.GetString(port, Lv2Api.PredSymbol) ?? $"port{index}";
        var name = graph.GetString(port, Lv2Api.PredName) ?? symbol;

        var props = graph.GetIris(port, Lv2Api.PredPortProperty);
        var min = graph.GetDouble(port, Lv2Api.PredMinimum);
        var max = graph.GetDouble(port, Lv2Api.PredMaximum);
        var def = graph.GetDouble(port, Lv2Api.PredDefault);
        var hasRange = min.HasValue && max.HasValue;

        var supports = graph.GetIris(port, Lv2Api.PredAtomSupports);
        // Atom ports advertise MIDI via atom:supports; legacy event input ports are treated as MIDI.
        var supportsMidi = supports.Contains(Lv2Api.MidiEvent)
                           || (kind == PortKind.Event && direction == PortDirection.Input);

        var scalePoints = graph.Objects(port, Lv2Api.PredScalePoint)
            .Select(sp => new ScalePoint(
                graph.GetString(sp, Lv2Api.PredLabel) ?? string.Empty,
                graph.GetDouble(sp, Lv2Api.PredValue) ?? 0))
            .OrderBy(sp => sp.Value)
            .ToList();

        return new PortDescriptor
        {
            Index = index,
            Symbol = symbol,
            Name = name,
            Kind = kind,
            Direction = direction,
            Default = (float)(def ?? min ?? 0),
            Min = (float)(min ?? 0),
            Max = (float)(max ?? 1),
            HasRange = hasRange,
            Toggled = props.Contains(Lv2Api.PropToggled),
            Integer = props.Contains(Lv2Api.PropInteger),
            Enumeration = props.Contains(Lv2Api.PropEnumeration),
            SampleRate = props.Contains(Lv2Api.PropSampleRate),
            Logarithmic = props.Contains(Lv2Api.PropLogarithmic),
            ConnectionOptional = props.Contains(Lv2Api.PropConnectionOptional),
            SupportsMidi = supportsMidi,
            ScalePoints = scalePoints,
        };
    }

    // Converts a (possibly file://) IRI or raw relative path to a local filesystem path under the bundle.
    private static string? UriToPath(string iri, string bundleDir)
    {
        if (string.IsNullOrEmpty(iri)) return null;
        if (iri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            try { return new Uri(iri).LocalPath; } catch { return null; }
        }

        if (iri.Contains("://")) return null; // a non-file absolute IRI is not a local binary
        return Path.IsPathRooted(iri) ? iri : Path.Combine(bundleDir, iri);
    }

    private static string LastSegment(string uri)
    {
        var trimmed = uri.TrimEnd('/', '#');
        var idx = trimmed.LastIndexOfAny(new[] { '/', '#', ':' });
        return idx >= 0 && idx < trimmed.Length - 1 ? trimmed[(idx + 1)..] : trimmed;
    }
}
