using System;
using System.Collections.Generic;
using System.IO;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Services;

namespace Ongenet.Scripting;

public sealed partial class ScriptingApi
{
    public string ExportProjectAsScript(ExportScriptOptions? options = null)
    {
        if (_projectExporter is null)
            throw new InvalidOperationException("Project script export is not available.");
        return _projectExporter.Export(_project.Current, options);
    }

    public string ExportInstrumentSlotAsScript(Guid trackId, int slotIndex, ExportScriptOptions? options = null)
    {
        if (_presetExporter is null)
            throw new InvalidOperationException("Preset script export is not available.");
        return _presetExporter.ExportInstrumentSlot(_project.Current, trackId, slotIndex, options);
    }

    public string ExportEffectChainAsScript(Guid trackId, int instrumentSlotIndex = -1, ExportScriptOptions? options = null)
    {
        if (_presetExporter is null)
            throw new InvalidOperationException("Preset script export is not available.");
        return _presetExporter.ExportEffectChain(_project.Current, trackId, instrumentSlotIndex, options);
    }

    public string ExportPresetAsScript(Guid trackId, int? slotIndex, int? effectIndex, ExportScriptOptions? options = null)
    {
        if (_presetExporter is null)
            throw new InvalidOperationException("Preset script export is not available.");
        return _presetExporter.ExportPreset(_project.Current, trackId, slotIndex, effectIndex, options);
    }

    /// <inheritdoc cref="IScriptingApi.ExportAudio"/>
    public void ExportAudio(string path, string? deliveryPlatform = null, bool normalizeLoudness = false,
        bool applyDither = false, bool bypassMasterFx = false, bool analyzeLoudness = true,
        int bitDepth = 0, ScriptDitherMode ditherMode = ScriptDitherMode.Tpdf, int targetSampleRate = 0,
        bool exportComparisonPair = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Export path is required.", nameof(path));

        var export = _export ?? new ExportService(new NullVideoCompositor(), new NullVideoMuxer());
        var format = _engine?.Format ?? new AudioFormat(48000, 2);
        var bpm = _transport.Tempo.BeatsPerMinute;
        var platform = deliveryPlatform ?? _deliveryTarget?.PlatformName;
        var resolvedBitDepth = bitDepth > 0 ? bitDepth : applyDither ? 16 : 24;
        var options = new ExportOptions
        {
            Kind = ExportKind.Master,
            AudioFormat = ResolveAudioFormat(path),
            DeliveryPlatform = platform,
            TargetIntegratedLufs = _deliveryTarget?.TargetIntegratedLufs ?? -14,
            TargetTruePeakDbTp = _deliveryTarget?.TargetTruePeakDbTp ?? -1,
            NormalizeLoudness = normalizeLoudness,
            ApplyDither = applyDither,
            BypassMasterFx = bypassMasterFx,
            AnalyzeLoudness = analyzeLoudness,
            BitDepth = resolvedBitDepth,
            DitherMode = ToDitherMode(ditherMode),
            TargetSampleRate = targetSampleRate,
            ExportComparisonPair = exportComparisonPair
        };
        if (DeliveryPlatformPresets.TryGet(platform) is { } preset)
        {
            options.TargetIntegratedLufs = preset.Lufs;
            options.TargetTruePeakDbTp = preset.DbTp;
        }

        export.Export(_project.Current, format, bpm, path, options);
        if (options.LoudnessReport is { } lr)
            Log(lr.Summary);
        else
            Log($"Exported master to {path}");
    }

    /// <inheritdoc cref="IScriptingApi.GetDeliveryTarget"/>
    public (string PlatformName, double TargetLufs, double TargetTruePeakDbTp) GetDeliveryTarget()
    {
        if (_deliveryTarget is null)
            return ("Spotify", -14, -1);
        return (_deliveryTarget.PlatformName, _deliveryTarget.TargetIntegratedLufs,
            _deliveryTarget.TargetTruePeakDbTp);
    }

    /// <inheritdoc cref="IScriptingApi.SetDeliveryTarget"/>
    public void SetDeliveryTarget(string? platformName, double? targetLufs = null, double? targetTruePeakDbTp = null)
    {
        if (_deliveryTarget is null) return;
        if (!string.IsNullOrWhiteSpace(platformName) &&
            !string.Equals(platformName, "Custom", StringComparison.OrdinalIgnoreCase) &&
            DeliveryPlatformPresets.TryGet(platformName) is not null)
        {
            _deliveryTarget.ApplyPlatform(platformName);
            return;
        }

        if (!string.IsNullOrWhiteSpace(platformName))
            _deliveryTarget.PlatformName = platformName;
        if (targetLufs is { } lufs)
            _deliveryTarget.TargetIntegratedLufs = lufs;
        if (targetTruePeakDbTp is { } tp)
            _deliveryTarget.TargetTruePeakDbTp = tp;
    }

    /// <inheritdoc cref="IScriptingApi.MatchAlbumLoudness"/>
    public void MatchAlbumLoudness(string[] wavPaths, double targetLufs = -14, double targetTp = -1)
    {
        ArgumentNullException.ThrowIfNull(wavPaths);
        var tracks = new List<(string WavPath, float[] Samples, AudioFormat Format)>(wavPaths.Length);
        foreach (var path in wavPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Album WAV was not found.", path);
            using var stream = File.OpenRead(path);
            var buffer = WavParser.Parse(stream);
            tracks.Add((path, buffer.Samples, new AudioFormat(buffer.SampleRate, buffer.Channels)));
        }

        ExportService.MatchAlbumLoudness(tracks, targetLufs, targetTp);
        foreach (var track in tracks)
        {
            using (var writer = new WavWriter(track.WavPath, track.Format.Channels, track.Format.SampleRate, 24))
                writer.Write(track.Samples);
            var report = LoudnessAnalyzer.Analyze(track.Samples, track.Format, targetLufs, targetTp);
            WavLoudnessMetadata.Append(track.WavPath, report.IntegratedLufs, report.TruePeakDbTp);
        }
        Log($"Matched {tracks.Count} WAV file(s) to album target {targetLufs:0.#} LUFS / {targetTp:0.#} dBTP.");
    }

    private static ExportAudioFormat ResolveAudioFormat(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".flac", StringComparison.OrdinalIgnoreCase)) return ExportAudioFormat.Flac;
        if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)) return ExportAudioFormat.Mp3;
        if (ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase)) return ExportAudioFormat.Ogg;
        return ExportAudioFormat.Wav;
    }

    private static DitherMode ToDitherMode(ScriptDitherMode mode) =>
        mode == ScriptDitherMode.NoiseShaped ? DitherMode.NoiseShaped : DitherMode.Tpdf;
}
