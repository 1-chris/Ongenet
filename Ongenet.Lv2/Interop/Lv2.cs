using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ongenet.Lv2.Interop;

/// <summary>
/// Hand-transcribed LV2 C ABI (lv2plug.in), plus the host-side features Ongenet provides. Structs
/// mirror the C layouts exactly (sequential, natural alignment). Function-pointer fields we call are
/// typed <c>delegate* unmanaged[Cdecl]</c>. Plugin binaries are loaded by path via
/// <see cref="System.Runtime.InteropServices.NativeLibrary"/> and the <c>lv2_descriptor</c> export;
/// all plugin <i>metadata</i> (ports, ranges, classes) comes from the bundle's Turtle, not the binary.
///
/// The URID map is a process-wide string↔integer table shared by every plugin instance (URIDs only
/// need to be stable within a host run), exposed to plugins through the <c>urid:map</c>/<c>unmap</c>
/// features. MIDI is delivered to instruments as <c>midi:MidiEvent</c> atoms forged into an
/// <c>LV2_Atom_Sequence</c> on the plugin's atom input port (see <see cref="Lv2.Lv2PluginBase"/>).
/// </summary>
public static unsafe class Lv2Api
{
    // --- Entry symbol ---
    public const string EntrySymbol = "lv2_descriptor";

    // --- Namespace prefixes / URIs (used by both the bundle parser and feature building) ---
    public const string NsLv2 = "http://lv2plug.in/ns/lv2core#";
    public const string NsAtom = "http://lv2plug.in/ns/ext/atom#";
    public const string NsEvent = "http://lv2plug.in/ns/ext/event#";
    public const string NsMidi = "http://lv2plug.in/ns/ext/midi#";
    public const string NsUrid = "http://lv2plug.in/ns/ext/urid#";
    public const string NsBufSize = "http://lv2plug.in/ns/ext/buf-size#";
    public const string NsParams = "http://lv2plug.in/ns/ext/parameters#";
    public const string NsOptions = "http://lv2plug.in/ns/ext/options#";
    public const string NsWorker = "http://lv2plug.in/ns/ext/worker#";
    public const string NsState = "http://lv2plug.in/ns/ext/state#";
    public const string NsPortProps = "http://lv2plug.in/ns/ext/port-props#";
    public const string NsUi = "http://lv2plug.in/ns/extensions/ui#";
    public const string NsDoap = "http://usefulinc.com/ns/doap#";
    public const string NsRdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    public const string NsRdfs = "http://www.w3.org/2000/01/rdf-schema#";

    // Class / type URIs.
    public const string ClassPlugin = NsLv2 + "Plugin";
    public const string ClassInstrument = NsLv2 + "InstrumentPlugin";
    public const string ClassAudioPort = NsLv2 + "AudioPort";
    public const string ClassControlPort = NsLv2 + "ControlPort";
    public const string ClassCvPort = NsLv2 + "CVPort";
    public const string ClassInputPort = NsLv2 + "InputPort";
    public const string ClassOutputPort = NsLv2 + "OutputPort";
    public const string ClassAtomPort = NsAtom + "AtomPort";
    public const string ClassEventPort = NsEvent + "EventPort";

    // Predicate URIs.
    public const string PredType = NsRdf + "type";
    public const string PredValue = NsRdf + "value";
    public const string PredSeeAlso = NsRdfs + "seeAlso";
    public const string PredLabel = NsRdfs + "label";
    public const string PredDoapName = NsDoap + "name";
    public const string PredPort = NsLv2 + "port";
    public const string PredIndex = NsLv2 + "index";
    public const string PredSymbol = NsLv2 + "symbol";
    public const string PredName = NsLv2 + "name";
    public const string PredDefault = NsLv2 + "default";
    public const string PredMinimum = NsLv2 + "minimum";
    public const string PredMaximum = NsLv2 + "maximum";
    public const string PredPortProperty = NsLv2 + "portProperty";
    public const string PredRequiredFeature = NsLv2 + "requiredFeature";
    public const string PredOptionalFeature = NsLv2 + "optionalFeature";
    public const string PredBinary = NsLv2 + "binary";
    public const string PredScalePoint = NsLv2 + "scalePoint";
    public const string PredAtomSupports = NsAtom + "supports";
    public const string PredAtomBufferType = NsAtom + "bufferType";

    // Port-property URIs.
    public const string PropToggled = NsLv2 + "toggled";
    public const string PropInteger = NsLv2 + "integer";
    public const string PropEnumeration = NsLv2 + "enumeration";
    public const string PropSampleRate = NsLv2 + "sampleRate";
    public const string PropConnectionOptional = NsLv2 + "connectionOptional";
    public const string PropLogarithmic = NsPortProps + "logarithmic";

    // Atom / unit type URIs (mapped to URIDs at runtime).
    public const string MidiEvent = NsMidi + "MidiEvent";
    public const string AtomSequence = NsAtom + "Sequence";
    public const string AtomChunk = NsAtom + "Chunk";
    public const string AtomInt = NsAtom + "Int";
    public const string AtomFloat = NsAtom + "Float";

    // Feature URIs.
    public const string FeatureUridMap = NsUrid + "map";
    public const string FeatureUridUnmap = NsUrid + "unmap";
    public const string FeatureOptions = NsOptions + "options";
    public const string FeatureBoundedBlock = NsBufSize + "boundedBlockLength";
    public const string FeatureWorkerSchedule = NsWorker + "schedule";
    public const string FeatureInstanceAccess = "http://lv2plug.in/ns/ext/instance-access";
    public const string FeatureDataAccess = "http://lv2plug.in/ns/ext/data-access";

    // --- UI (extensions/ui#) ---
    public const string UiEntrySymbol = "lv2ui_descriptor";
    public const string PredUiUi = NsUi + "ui";
    public const string PredUiBinary = NsUi + "binary";
    public const string ClassX11Ui = NsUi + "X11UI";
    public const string FeatureUiParent = NsUi + "parent";
    public const string FeatureUiResize = NsUi + "resize";
    public const string UiIdleInterface = NsUi + "idleInterface"; // both a feature and an extension_data uri
    public const string UiShowInterface = NsUi + "showInterface";

    // buf-size / parameter option keys.
    public const string OptMinBlockLength = NsBufSize + "minBlockLength";
    public const string OptMaxBlockLength = NsBufSize + "maxBlockLength";
    public const string OptNominalBlockLength = NsBufSize + "nominalBlockLength";
    public const string OptSampleRate = NsParams + "sampleRate";

    // --- ABI structs ---

    /// <summary>Opaque plugin instance handle (<c>LV2_Handle</c>).</summary>
    // (represented as void* throughout)

    [StructLayout(LayoutKind.Sequential)]
    public struct LV2_Feature
    {
        public byte* Uri;
        public void* Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LV2_Descriptor
    {
        public byte* Uri;
        public delegate* unmanaged[Cdecl]<LV2_Descriptor*, double, byte*, LV2_Feature**, void*> Instantiate;
        public delegate* unmanaged[Cdecl]<void*, uint, void*, void> ConnectPort;
        public delegate* unmanaged[Cdecl]<void*, void> Activate;
        public delegate* unmanaged[Cdecl]<void*, uint, void> Run;
        public delegate* unmanaged[Cdecl]<void*, void> Deactivate;
        public delegate* unmanaged[Cdecl]<void*, void> Cleanup;
        public delegate* unmanaged[Cdecl]<byte*, void*> ExtensionData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LV2_URID_Map
    {
        public void* Handle;
        public delegate* unmanaged[Cdecl]<void*, byte*, uint> Map; // (handle, uri) -> urid
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LV2_URID_Unmap
    {
        public void* Handle;
        public delegate* unmanaged[Cdecl]<void*, uint, byte*> Unmap; // (handle, urid) -> uri
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LV2_Atom
    {
        public uint Size; // bytes following this header
        public uint Type; // URID
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LV2_Atom_Event
    {
        public long Frames;  // time stamp (frames); the C union's first member
        public LV2_Atom Body;
        // body data follows, padded to 8 bytes
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LV2_Atom_Sequence_Body
    {
        public uint Unit; // URID of the time unit (0 = audio frames)
        public uint Pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LV2_Atom_Sequence
    {
        public LV2_Atom Atom;
        public LV2_Atom_Sequence_Body Body;
        // a series of LV2_Atom_Event follow
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LV2_Options_Option
    {
        public uint Context; // LV2_Options_Context (0 = INSTANCE)
        public uint Subject;
        public uint Key;     // URID
        public uint Size;
        public uint Type;    // URID
        public void* Value;
    }

    public const uint OptionsContextInstance = 0;

    // --- Worker (work#schedule / work#interface) ---

    public const string WorkerInterface = NsWorker + "interface";
    public const int WorkerSuccess = 0;

    /// <summary>Host feature given to the plugin: lets it schedule non-realtime work from <c>run()</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LV2_Worker_Schedule
    {
        public void* Handle;
        public delegate* unmanaged[Cdecl]<void*, uint, void*, int> ScheduleWork; // (handle, size, data) -> status
    }

    /// <summary>Plugin-provided interface (via extension_data) the host drives off the audio thread.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LV2_Worker_Interface
    {
        // (instance, respond_fn, respond_handle, size, data) -> status
        public delegate* unmanaged[Cdecl]<void*, delegate* unmanaged[Cdecl]<void*, uint, void*, int>, void*, uint, void*, int> Work;
        public delegate* unmanaged[Cdecl]<void*, uint, void*, int> WorkResponse; // (instance, size, body) -> status
        public delegate* unmanaged[Cdecl]<void*, int> EndRun;                    // (instance) -> status
    }

    // --- UI ABI (lv2ui_descriptor) ---

    [StructLayout(LayoutKind.Sequential)]
    public struct LV2UI_Descriptor
    {
        public byte* Uri;
        // (descriptor, plugin_uri, bundle_path, write_function, controller, widget*, features) -> handle
        public delegate* unmanaged[Cdecl]<LV2UI_Descriptor*, byte*, byte*,
            delegate* unmanaged[Cdecl]<void*, uint, uint, uint, void*, void>, void*, void**, LV2_Feature**, void*> Instantiate;
        public delegate* unmanaged[Cdecl]<void*, void> Cleanup;
        public delegate* unmanaged[Cdecl]<void*, uint, uint, uint, void*, void> PortEvent; // (ui, port, size, format, buffer)
        public delegate* unmanaged[Cdecl]<byte*, void*> ExtensionData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LV2UI_Idle_Interface
    {
        public delegate* unmanaged[Cdecl]<void*, int> Idle; // (ui) -> non-zero to request close
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LV2UI_Show_Interface
    {
        public delegate* unmanaged[Cdecl]<void*, int> Show;
        public delegate* unmanaged[Cdecl]<void*, int> Hide;
    }

    /// <summary>Host feature <c>ui:resize</c>: lets the UI ask the host to resize its container.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LV2UI_Resize
    {
        public void* Handle;
        public delegate* unmanaged[Cdecl]<void*, int, int, int> UiResize; // (handle, width, height) -> status
    }

    /// <summary>Host feature <c>data-access</c>: exposes the plugin's <c>extension_data</c> to its UI.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LV2_Extension_Data_Feature
    {
        public delegate* unmanaged[Cdecl]<byte*, void*> DataAccess; // (uri) -> extension data
    }

    // --- URID map (process-wide) ---

    private static readonly object _uridLock = new();
    private static readonly Dictionary<string, uint> _uridByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<uint, nint> _nameByUrid = new();
    private static uint _nextUrid = 1;

    private static LV2_URID_Map* _mapStruct;
    private static LV2_URID_Unmap* _unmapStruct;

    /// <summary>Maps a URI string to a stable (per-run) URID, allocating one on first use.</summary>
    public static uint MapUrid(string uri)
    {
        lock (_uridLock)
        {
            if (_uridByName.TryGetValue(uri, out var id)) return id;
            id = _nextUrid++;
            _uridByName[uri] = id;
            _nameByUrid[id] = (nint)Marshal.StringToCoTaskMemUTF8(uri);
            return id;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint UridMapCb(void* handle, byte* uri)
    {
        var s = ReadUtf8(uri);
        return s == null ? 0u : MapUrid(s);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte* UridUnmapCb(void* handle, uint urid)
    {
        lock (_uridLock) return _nameByUrid.TryGetValue(urid, out var p) ? (byte*)p : null;
    }

    /// <summary>The shared <c>urid:map</c> feature data (allocated once).</summary>
    public static LV2_URID_Map* UridMapData()
    {
        lock (_uridLock)
        {
            if (_mapStruct == null)
            {
                _mapStruct = (LV2_URID_Map*)Marshal.AllocHGlobal(sizeof(LV2_URID_Map));
                _mapStruct->Handle = null;
                _mapStruct->Map = &UridMapCb;
            }

            return _mapStruct;
        }
    }

    /// <summary>The shared <c>urid:unmap</c> feature data (allocated once).</summary>
    public static LV2_URID_Unmap* UridUnmapData()
    {
        lock (_uridLock)
        {
            if (_unmapStruct == null)
            {
                _unmapStruct = (LV2_URID_Unmap*)Marshal.AllocHGlobal(sizeof(LV2_URID_Unmap));
                _unmapStruct->Handle = null;
                _unmapStruct->Unmap = &UridUnmapCb;
            }

            return _unmapStruct;
        }
    }

    // Cached URIDs for the types the audio bridge forges every block.
    private static uint _uridSequence, _uridChunk, _uridMidiEvent;

    public static uint SequenceUrid => _uridSequence != 0 ? _uridSequence : _uridSequence = MapUrid(AtomSequence);
    public static uint ChunkUrid => _uridChunk != 0 ? _uridChunk : _uridChunk = MapUrid(AtomChunk);
    public static uint MidiEventUrid => _uridMidiEvent != 0 ? _uridMidiEvent : _uridMidiEvent = MapUrid(MidiEvent);

    // --- String helpers ---

    public static byte* Utf8(string s) => (byte*)Marshal.StringToCoTaskMemUTF8(s);
    public static void FreeUtf8(byte* p) { if (p != null) Marshal.FreeCoTaskMem((nint)p); }
    public static string? ReadUtf8(byte* p) => p == null ? null : Marshal.PtrToStringUTF8((nint)p);
}
