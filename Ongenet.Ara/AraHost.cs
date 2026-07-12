using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Ara;

/// <summary>
/// Celemony ARA SDK integration entry point. Compiles as a structured stub when ENABLE_ARA is
/// undefined; with ENABLE_ARA and ARA_SDK_PATH set, real document-controller hosting can be wired in.
/// </summary>
public sealed class AraHost : IAraHost
{
    /// <summary>VST3 factory category for ARA::IMainFactory (Celemony ARAVST3.h).</summary>
    public const string AraMainFactoryClass = "ARA Main Factory Class";

    /// <summary>VST3 sub-category flag for ARA-only plug-ins.</summary>
    public const string OnlyAraSubCategory = "OnlyARA";

    private readonly Dictionary<string, IAraDocument> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, IAraDocument> _clipDocuments = new();

#if ENABLE_ARA
    private readonly SdkDocumentRegistry _sdk = new();
#endif

    public bool IsSdkAvailable =>
#if ENABLE_ARA
        _sdk.IsConfigured;
#else
        false;
#endif

    public bool TryDiscoverAraPlugin(string modulePath, string audioClassName)
    {
        if (string.IsNullOrWhiteSpace(modulePath) || string.IsNullOrWhiteSpace(audioClassName))
            return false;
        try
        {
            return Vst3AraDiscoveryImpl.ReadAraFactoryNames(modulePath)
                .Any(n => string.Equals(n, audioClassName, StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    public IAraDocument BindPlugin(string pluginId, IAraCapable plugin)
    {
        if (!_documents.TryGetValue(pluginId, out var doc))
        {
#if ENABLE_ARA
            doc = _sdk.TryCreateDocument(pluginId, plugin) ?? new MonophonicPitchAraDocument(pluginId);
#else
            doc = new MonophonicPitchAraDocument(pluginId);
#endif
            _documents[pluginId] = doc;
        }

        plugin.BindAraDocument(doc);
        return doc;
    }

    public IAraDocument BindClip(Clip clip, string pluginId, IAraCapable plugin)
    {
        if (!_clipDocuments.TryGetValue(clip.Id, out var doc))
        {
#if ENABLE_ARA
            doc = _sdk.TryCreateClipDocument(pluginId, plugin, clip.Id, clip.AraPitchOffsetSemitones)
                  ?? _sdk.TryCreateDocument(pluginId, plugin)
                  ?? new MonophonicPitchAraDocument(pluginId, clip.AraPitchOffsetSemitones, clip.Id);
#else
            doc = new MonophonicPitchAraDocument(pluginId, clip.AraPitchOffsetSemitones, clip.Id);
#endif
            _clipDocuments[clip.Id] = doc;
        }

        if (doc is MonophonicPitchAraDocument mono)
        {
            mono.BindRegion(clip.StartBeat, clip.LengthBeats);
            mono.SourceSemitoneOffset = clip.AraPitchOffsetSemitones;
        }
        else if (doc is SdkAraDocument sdk)
        {
            sdk.BindRegion(clip.StartBeat, clip.LengthBeats);
            sdk.SourceSemitoneOffset = clip.AraPitchOffsetSemitones;
        }

        plugin.BindAraDocument(doc);
        return doc;
    }

    public void OpenEditor(IAraDocument document)
    {
        if (!document.IsActive) return;
#if ENABLE_ARA
        if (_sdk.TryOpenEditor(document)) return;
#endif
        document.OpenEditor();
    }

#if ENABLE_ARA
    /// <summary>Registry for real ARA SDK document controllers when ENABLE_ARA and ARA_SDK_PATH are set.</summary>
    private sealed class SdkDocumentRegistry
    {
        private readonly Dictionary<string, SdkAraDocument> _byPlugin = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, SdkAraDocument> _byClip = new();

        public bool IsConfigured =>
            TryResolveSdkRoot(out _);

        public IAraDocument? TryCreateDocument(string pluginId, IAraCapable plugin)
        {
            if (!IsConfigured) return null;

            _ = plugin;
            if (_byPlugin.TryGetValue(pluginId, out var existing))
                return existing;

            // Real SDK wiring: ARA::HostDocumentController::create, bind plug-in extensions, persist state.
            var doc = new SdkAraDocument(pluginId);
            _byPlugin[pluginId] = doc;
            return doc;
        }

        public IAraDocument? TryCreateClipDocument(string pluginId, IAraCapable plugin, Guid clipId, double pitchOffset)
        {
            if (!IsConfigured) return null;

            _ = plugin;
            if (_byClip.TryGetValue(clipId, out var existing))
                return existing;

            var doc = new SdkAraDocument(pluginId, pitchOffset, clipId);
            _byClip[clipId] = doc;
            _byPlugin.TryAdd(pluginId, doc);
            return doc;
        }

        public bool TryOpenEditor(IAraDocument document)
        {
            if (!IsConfigured || !document.IsActive) return false;
            document.OpenEditor();
            return true;
        }

        public void Release(string pluginId)
        {
            _byPlugin.Remove(pluginId);
            foreach (var key in _byClip.Where(kv => kv.Value.PluginId == pluginId).Select(kv => kv.Key).ToList())
                _byClip.Remove(key);
        }

        private static bool TryResolveSdkRoot(out string? sdkRoot)
        {
            sdkRoot = Environment.GetEnvironmentVariable("ARA_SDK_PATH");
            if (string.IsNullOrWhiteSpace(sdkRoot)) return false;
            return Directory.Exists(sdkRoot);
        }
    }
#endif
}
