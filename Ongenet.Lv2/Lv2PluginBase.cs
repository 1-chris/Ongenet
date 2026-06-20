using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Lv2.Interop;

namespace Ongenet.Lv2;

/// <summary>
/// Shared host for one LV2 plugin instance: binary loading, the <c>instantiate</c>/<c>connect_port</c>/
/// <c>activate</c>/<c>run</c> lifecycle, per-port scratch buffers, parameter bridging from input
/// control ports, and MIDI delivery via a forged <c>LV2_Atom_Sequence</c>. Subclasses specialise the
/// audio I/O — <see cref="Lv2Instrument"/> (MIDI in, audio out, additive) and <see cref="Lv2Effect"/>
/// (audio in → audio out, in place). All native failures are caught; a broken plugin simply produces
/// silence / passes audio through.
///
/// Control-port values live in a managed array (<see cref="_controlValues"/>) that is the single source
/// of truth: parameters read/write it, and it is pushed into the native control cells each block. This
/// keeps editable values independent of the native instance, so they survive the re-instantiation LV2
/// requires on a sample-rate change (and lets parameters exist before the plugin is even loaded).
///
/// GUI: v1 has no native plugin window (LV2 UIs are separate binaries with their own ABI). Plugins are
/// edited through Ongenet's generic parameter inspector; <see cref="HasEditor"/> is false. The
/// <see cref="IPluginEditor"/> members are the seam for native UI hosting in a later phase.
/// </summary>
public abstract unsafe partial class Lv2PluginBase : IPluginEditor, IDisposable
{
    protected const int MaxBlock = 8192;
    private const int AtomBufferBytes = 1 << 16; // 64 KiB per atom port

    /// <summary>Features this host advertises; plugins requiring anything else are not registered.</summary>
    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Lv2Api.FeatureUridMap,
        Lv2Api.FeatureUridUnmap,
        Lv2Api.FeatureOptions,
        Lv2Api.FeatureBoundedBlock,
        Lv2Api.FeatureWorkerSchedule,
    };

    /// <summary>Optional diagnostic sink (set once at startup); surfaces plugin + host logs to the app log.</summary>
    public static Action<string>? Log;

    protected readonly Lv2PluginDescriptor Descriptor;

    private readonly object _evLock = new();
    private readonly List<MidiEvent> _pending = new();
    private readonly HashSet<int> _held = new();

    private Lv2Module? _module;
    private Lv2Api.LV2_Descriptor* _desc;
    private void* _handle;
    private Lv2HostFeatures? _features;

    // LV2 Worker (work#schedule): the plugin schedules non-realtime work from run(); we run it on a
    // background thread and deliver responses back at the top of the next block.
    private GCHandle _selfHandle;
    private Lv2Api.LV2_Worker_Interface* _workerIface;
    private readonly ConcurrentQueue<byte[]> _workRequests = new();
    private readonly ConcurrentQueue<byte[]> _workResponses = new();
    private SemaphoreSlim? _workSignal;
    private Thread? _workerThread;
    private volatile bool _workerStop;

    // Per-port native scratch (allocated on load, freed on teardown). Pointers are stored as nint
    // (pointer types can't be tuple/generic type arguments) and cast at use.
    private readonly List<nint> _allocs = new();
    private readonly List<nint> _audioIn = new();
    private readonly List<nint> _audioOut = new();
    private readonly List<(nint Buf, bool Midi)> _atomIn = new();
    private readonly List<nint> _atomOut = new();

    // Managed control model (built once from the descriptor; outlives native re-instantiation).
    private PortDescriptor[]? _controlPorts; // input control ports, in descriptor order
    private float[]? _controlValues;         // current value per control port (parallel to _controlPorts)
    private readonly nint[] _controlCells;   // native cell per control port (parallel; 0 until loaded)
    private readonly Dictionary<int, int> _controlIndexByPort = new(); // LV2 port index -> control array index

    private AudioFormat _format = AudioFormat.Default;
    private bool _loadAttempted;
    private bool _loaded;
    private bool _activated;
    private double _instSampleRate;
    private bool _disposed;

    private IReadOnlyList<Parameter>? _parameters;

    protected Lv2PluginBase(Lv2PluginDescriptor descriptor)
    {
        Descriptor = descriptor;
        Name = descriptor.Name;
        _controlCells = new nint[descriptor.Ports.Count(p => p.Kind == PortKind.Control && p.Direction == PortDirection.Input)];
    }

    /// <summary>The registry id for an LV2 plugin: <c>lv2:&lt;pluginUri&gt;</c> (the URI is globally unique).</summary>
    public static string MakeId(string pluginUri) => $"lv2:{pluginUri}";

    /// <summary>True when every required feature is one this host provides.</summary>
    public static bool SupportsRequiredFeatures(IEnumerable<string> required) => required.All(Supported.Contains);

    /// <summary>The required features this host does NOT provide (for diagnostics).</summary>
    public static IReadOnlyList<string> UnsupportedFeatures(IEnumerable<string> required)
        => required.Where(f => !Supported.Contains(f)).ToList();

    public string Name { get; }

    public IReadOnlyList<Parameter> Parameters
    {
        get { EnsureControlModel(); return _parameters!; }
    }

    // --- Control model (independent of the native instance) ---

    private void EnsureControlModel()
    {
        if (_controlPorts != null) return;

        _controlPorts = Descriptor.Ports
            .Where(p => p.Kind == PortKind.Control && p.Direction == PortDirection.Input)
            .ToArray();
        _controlValues = new float[_controlPorts.Length];

        var sampleRate = _format.SampleRate <= 0 ? 44100 : _format.SampleRate;
        for (var i = 0; i < _controlPorts.Length; i++)
        {
            var port = _controlPorts[i];
            _controlValues[i] = port.Default * (port.SampleRate ? sampleRate : 1f);
            _controlIndexByPort[port.Index] = i;
        }

        _parameters = BuildParameters(sampleRate);
    }

    private IReadOnlyList<Parameter> BuildParameters(int sampleRate)
    {
        var values = _controlValues!;
        var list = new List<Parameter>(_controlPorts!.Length);

        for (var i = 0; i < _controlPorts.Length; i++)
        {
            var port = _controlPorts[i];
            var idx = i; // capture per iteration
            var scale = port.SampleRate ? sampleRate : 1f;
            var min = port.Min * scale;
            var max = port.Max * scale;
            if (max <= min) max = min + 1;

            if (port.Toggled)
            {
                var on = port.HasRange ? max : 1f;
                var off = port.HasRange ? min : 0f;
                var mid = (on + off) * 0.5f;
                list.Add(new BoolParameter(port.Name,
                    () => values[idx] > mid,
                    v => values[idx] = v ? on : off));
            }
            else if (port.Enumeration && port.ScalePoints.Count > 0)
            {
                var points = port.ScalePoints;
                list.Add(new ChoiceParameter(port.Name, points.Select(sp => sp.Label).ToList(),
                    () => NearestScalePoint(points, values[idx]),
                    sel => values[idx] = (float)points[sel].Value));
            }
            else
            {
                list.Add(new FloatParameter(port.Name, min, max,
                    () => values[idx],
                    v => values[idx] = port.Integer ? (float)Math.Round(v) : (float)v,
                    skew: port.Logarithmic ? 3.0 : 1.0));
            }
        }

        return list;
    }

    // Index of the scale point nearest a value (for displaying an enumerated control's current choice).
    private static int NearestScalePoint(IReadOnlyList<ScalePoint> points, float value)
    {
        var best = 0;
        var bestDist = double.MaxValue;
        for (var i = 0; i < points.Count; i++)
        {
            var d = Math.Abs(points[i].Value - value);
            if (d < bestDist) { bestDist = d; best = i; }
        }

        return best;
    }

    // --- Loading / activation ---

    protected bool EnsureLoaded()
    {
        if (_loaded) return true;
        if (_loadAttempted) return false;
        _loadAttempted = true;
        EnsureControlModel();

        try
        {
            if (!SupportsRequiredFeatures(Descriptor.RequiredFeatures))
                throw new InvalidOperationException("requires unsupported features: " + string.Join(", ", Descriptor.RequiredFeatures));

            _module = new Lv2Module(Descriptor.BinaryPath);
            _desc = _module.FindDescriptor(Descriptor.Uri);
            if (_desc == null) throw new InvalidOperationException("lv2_descriptor has no matching URI.");
            if (_desc->Instantiate == null || _desc->ConnectPort == null || _desc->Run == null)
                throw new InvalidOperationException("descriptor is missing instantiate/connect_port/run.");

            var rate = _format.SampleRate <= 0 ? 44100 : _format.SampleRate;

            // A work#schedule feature whose handle points back at this instance (so the static
            // schedule callback can recover us). Provided to every plugin; harmless if unused.
            _selfHandle = GCHandle.Alloc(this);
            var schedule = (Lv2Api.LV2_Worker_Schedule*)Alloc(sizeof(Lv2Api.LV2_Worker_Schedule));
            schedule->Handle = (void*)GCHandle.ToIntPtr(_selfHandle);
            schedule->ScheduleWork = &WorkerScheduleCb;

            _features = new Lv2HostFeatures(rate, 1, MaxBlock, (nint)schedule);

            var bundlePath = Lv2Api.Utf8(EnsureTrailingSep(Descriptor.BundlePath));
            try { _handle = _desc->Instantiate(_desc, rate, bundlePath, _features.Array); }
            finally { Lv2Api.FreeUtf8(bundlePath); }
            if (_handle == null) throw new InvalidOperationException("instantiate() returned null.");

            AllocateAndConnectPorts();
            SetupWorker();

            if (_desc->Activate != null) _desc->Activate(_handle);
            _activated = true;
            _instSampleRate = rate;
            _loaded = true;
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"LV2 '{Name}': load failed: {ex.Message}");
            TeardownNative();
            return false;
        }
    }

    private void AllocateAndConnectPorts()
    {
        var ci = 0; // input-control-port counter (parallel to _controlPorts / _controlValues)

        foreach (var port in Descriptor.Ports)
        {
            void* buf;
            switch (port.Kind)
            {
                case PortKind.Audio:
                case PortKind.Cv:
                    var ab = (float*)Alloc(MaxBlock * sizeof(float));
                    new Span<float>(ab, MaxBlock).Clear();
                    (port.Direction == PortDirection.Output ? _audioOut : _audioIn).Add((nint)ab);
                    buf = ab;
                    break;

                case PortKind.Control:
                    var cell = (float*)Alloc(sizeof(float));
                    if (port.Direction == PortDirection.Input)
                    {
                        *cell = _controlValues![ci];
                        _controlCells[ci] = (nint)cell;
                        ci++;
                    }
                    else
                    {
                        *cell = port.Default; // output control: connected but unused
                    }

                    buf = cell;
                    break;

                case PortKind.Atom:
                case PortKind.Event:
                    var atom = (byte*)Alloc(AtomBufferBytes);
                    if (port.Direction == PortDirection.Output) _atomOut.Add((nint)atom);
                    else _atomIn.Add(((nint)atom, port.SupportsMidi));
                    buf = atom;
                    break;

                default:
                    continue; // unreachable: unknown kinds are filtered during discovery
            }

            _desc->ConnectPort(_handle, (uint)port.Index, buf);
        }
    }

    // --- Worker (work#schedule) ---

    private void SetupWorker()
    {
        if (_desc->ExtensionData == null) return;

        var uri = Lv2Api.Utf8(Lv2Api.WorkerInterface);
        try { _workerIface = (Lv2Api.LV2_Worker_Interface*)_desc->ExtensionData(uri); }
        finally { Lv2Api.FreeUtf8(uri); }

        if (_workerIface == null || _workerIface->Work == null) { _workerIface = null; return; }

        _workerStop = false;
        _workSignal = new SemaphoreSlim(0);
        _workerThread = new Thread(WorkerLoop) { IsBackground = true, Name = $"LV2 worker: {Name}" };
        _workerThread.Start();
        if (!_workRequests.IsEmpty) _workSignal.Release();
    }

    // Background thread: runs the plugin's work() for each scheduled request and lets it post responses.
    private void WorkerLoop()
    {
        while (!_workerStop)
        {
            try { _workSignal!.Wait(); } catch { break; }
            if (_workerStop) break;

            while (_workRequests.TryDequeue(out var data))
            {
                var p = Marshal.AllocHGlobal(Math.Max(1, data.Length));
                try
                {
                    if (data.Length > 0) Marshal.Copy(data, 0, p, data.Length);
                    _workerIface->Work(_handle, &WorkerRespondCb, (void*)GCHandle.ToIntPtr(_selfHandle), (uint)data.Length, (void*)p);
                }
                catch { /* a faulty worker must not take down the thread */ }
                finally { Marshal.FreeHGlobal(p); }
            }
        }
    }

    // Applies any worker responses on the audio thread before run() (then end_run() is called after).
    private void DrainWorkerResponses()
    {
        if (_workerIface == null || _workerIface->WorkResponse == null) return;
        while (_workResponses.TryDequeue(out var data))
        {
            var p = Marshal.AllocHGlobal(Math.Max(1, data.Length));
            try
            {
                if (data.Length > 0) Marshal.Copy(data, 0, p, data.Length);
                _workerIface->WorkResponse(_handle, (uint)data.Length, (void*)p);
            }
            finally { Marshal.FreeHGlobal(p); }
        }
    }

    private static Lv2PluginBase? Recover(void* handle)
    {
        if (handle == null) return null;
        try { return GCHandle.FromIntPtr((nint)handle).Target as Lv2PluginBase; }
        catch { return null; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int WorkerScheduleCb(void* handle, uint size, void* data)
    {
        var inst = Recover(handle);
        if (inst == null) return 1; // LV2_WORKER_ERR_UNKNOWN
        var bytes = new byte[size];
        if (size > 0) Marshal.Copy((nint)data, bytes, 0, (int)size);
        inst._workRequests.Enqueue(bytes);
        inst._workSignal?.Release();
        return Lv2Api.WorkerSuccess;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int WorkerRespondCb(void* handle, uint size, void* data)
    {
        var inst = Recover(handle);
        if (inst == null) return 1;
        var bytes = new byte[size];
        if (size > 0) Marshal.Copy((nint)data, bytes, 0, (int)size);
        inst._workResponses.Enqueue(bytes);
        return Lv2Api.WorkerSuccess;
    }

    private void StopWorker()
    {
        _workerStop = true;
        _workSignal?.Release();
        try { _workerThread?.Join(1000); } catch { /* ignore */ }
        _workerThread = null;
        _workSignal?.Dispose();
        _workSignal = null;
        _workerIface = null;
        _workRequests.Clear();
        _workResponses.Clear();
    }

    public void Prepare(AudioFormat format)
    {
        var rateChanged = _loaded && Math.Abs(_instSampleRate - format.SampleRate) > 0.5;
        _format = format;

        // LV2 sample rate is fixed at instantiate(), so a rate change means a full re-instantiate. The
        // managed control values survive teardown and are restored into the new cells.
        if (rateChanged)
        {
            TeardownNative();
            _loadAttempted = false;
        }

        EnsureLoaded();
    }

    // --- Audio thread ---

    /// <summary>
    /// Runs one <c>run()</c> block. <paramref name="feedInput"/> de-interleaves the engine buffer into
    /// the plugin's audio inputs (effects); <paramref name="replace"/> overwrites the engine buffer
    /// with the plugin output (effects) vs. adding to it (instruments).
    /// </summary>
    protected void RenderAudio(Span<float> buffer, bool feedInput, bool replace)
    {
        if (!_loaded || !_activated || _handle == null || _desc->Run == null) return;

        var channels = _format.Channels < 1 ? 1 : _format.Channels;
        var frames = buffer.Length / channels;
        if (frames <= 0) return;
        if (frames > MaxBlock) frames = MaxBlock;

        PushControlValues();

        // Audio inputs: de-interleave for effects, silence for instruments.
        for (var i = 0; i < _audioIn.Count; i++)
        {
            var dst = (float*)_audioIn[i];
            if (feedInput)
                for (var f = 0; f < frames; f++) dst[f] = i < channels ? buffer[f * channels + i] : 0f;
            else
                new Span<float>(dst, frames).Clear();
        }

        for (var i = 0; i < _audioOut.Count; i++) new Span<float>((float*)_audioOut[i], frames).Clear();

        PrepareAtomPorts();
        DrainWorkerResponses();

        _desc->Run(_handle, (uint)frames);
        if (_workerIface != null && _workerIface->EndRun != null) _workerIface->EndRun(_handle);

        var outCount = _audioOut.Count;
        if (outCount == 0) return;

        for (var f = 0; f < frames; f++)
        {
            var i = f * channels;
            for (var c = 0; c < channels; c++)
            {
                var pc = c < outCount ? c : outCount - 1; // mono out → spread to all channels
                var v = ((float*)_audioOut[pc])[f];
                if (replace) buffer[i + c] = v;
                else buffer[i + c] += v;
            }
        }
    }

    // Copies the managed control values into the plugin's native control cells (read at run()).
    private void PushControlValues()
    {
        var values = _controlValues;
        if (values == null) return;
        for (var i = 0; i < _controlCells.Length; i++)
            if (_controlCells[i] != 0)
                *(float*)_controlCells[i] = values[i];
    }

    // Forges pending MIDI into the (first) MIDI atom-in port and resets the other atom buffers.
    private void PrepareAtomPorts()
    {
        var midiWritten = false;
        foreach (var (buf, midi) in _atomIn)
        {
            if (midi && !midiWritten) { ForgeMidiSequence((byte*)buf); midiWritten = true; }
            else WriteEmptySequence((byte*)buf);
        }

        if (!midiWritten)
        {
            // No MIDI port consumed the queue (e.g. an effect): drop anything queued so it can't pile up.
            lock (_evLock) _pending.Clear();
        }

        foreach (var buf in _atomOut) WriteChunk((byte*)buf);
    }

    private void ForgeMidiSequence(byte* buf)
    {
        var seq = (Lv2Api.LV2_Atom_Sequence*)buf;
        seq->Atom.Type = Lv2Api.SequenceUrid;
        seq->Body.Unit = 0;
        seq->Body.Pad = 0;

        var write = buf + sizeof(Lv2Api.LV2_Atom_Sequence);
        var limit = buf + AtomBufferBytes;

        lock (_evLock)
        {
            foreach (var ev in _pending)
            {
                var total = sizeof(Lv2Api.LV2_Atom_Event) + ev.Length;
                var padded = (total + 7) & ~7;
                if (write + padded > limit) break;

                var e = (Lv2Api.LV2_Atom_Event*)write;
                e->Frames = 0;
                e->Body.Size = (uint)ev.Length;
                e->Body.Type = Lv2Api.MidiEventUrid;
                var data = write + sizeof(Lv2Api.LV2_Atom_Event);
                data[0] = ev.B0;
                if (ev.Length > 1) data[1] = ev.B1;
                if (ev.Length > 2) data[2] = ev.B2;
                write += padded;
            }

            _pending.Clear();
        }

        seq->Atom.Size = (uint)(write - (buf + sizeof(Lv2Api.LV2_Atom)));
    }

    private static void WriteEmptySequence(byte* buf)
    {
        var seq = (Lv2Api.LV2_Atom_Sequence*)buf;
        seq->Atom.Size = (uint)sizeof(Lv2Api.LV2_Atom_Sequence_Body);
        seq->Atom.Type = Lv2Api.SequenceUrid;
        seq->Body.Unit = 0;
        seq->Body.Pad = 0;
    }

    // An output atom port: the plugin reads atom.size as the capacity available for forging.
    private static void WriteChunk(byte* buf)
    {
        var atom = (Lv2Api.LV2_Atom*)buf;
        atom->Size = (uint)(AtomBufferBytes - sizeof(Lv2Api.LV2_Atom));
        atom->Type = Lv2Api.ChunkUrid;
    }

    // --- MIDI input (UI + scheduler threads) ---

    protected void EnqueueNoteOn(int midiNote, float velocity)
    {
        var vel = (int)Math.Round(velocity * 127f);
        vel = vel < 1 ? 1 : vel > 127 ? 127 : vel;
        lock (_evLock)
        {
            _pending.Add(MidiEvent.Three(0x90, (byte)(midiNote & 0x7F), (byte)vel));
            _held.Add(midiNote);
        }
    }

    protected void EnqueueNoteOff(int midiNote)
    {
        lock (_evLock)
        {
            _pending.Add(MidiEvent.Three(0x80, (byte)(midiNote & 0x7F), 0));
            _held.Remove(midiNote);
        }
    }

    protected void EnqueueAllNotesOff()
    {
        lock (_evLock)
        {
            foreach (var note in _held) _pending.Add(MidiEvent.Three(0x80, (byte)(note & 0x7F), 0));
            _held.Clear();
        }
    }

    protected void EnqueueControlChange(int controller, int value)
    {
        lock (_evLock) _pending.Add(MidiEvent.Three(0xB0, (byte)(controller & 0x7F), (byte)(value & 0x7F)));
    }

    protected void EnqueuePitchBend(int value14)
    {
        lock (_evLock) _pending.Add(MidiEvent.Three(0xE0, (byte)(value14 & 0x7F), (byte)((value14 >> 7) & 0x7F)));
    }

    protected void EnqueueAftertouch(int value)
    {
        lock (_evLock) _pending.Add(MidiEvent.Two(0xD0, (byte)(value & 0x7F)));
    }

    // IPluginEditor (native X11 UI hosting) lives in Lv2PluginBase.Editor.cs.

    // --- Teardown ---

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TeardownNative();
    }

    private void TeardownNative()
    {
        // Tear down the UI first — it holds instance-access to the plugin handle.
        CloseEditorInternal();

        // Stop the worker before cleanup() so work() can't run against a torn-down instance.
        StopWorker();

        try
        {
            if (_handle != null && _desc != null)
            {
                if (_activated && _desc->Deactivate != null) _desc->Deactivate(_handle);
                if (_desc->Cleanup != null) _desc->Cleanup(_handle);
            }
        }
        catch { /* ignore */ }

        _handle = null;
        _desc = null;
        _activated = false;

        foreach (var p in _allocs) Marshal.FreeHGlobal(p);
        _allocs.Clear();
        if (_selfHandle.IsAllocated) _selfHandle.Free();
        Array.Clear(_controlCells);
        _audioIn.Clear();
        _audioOut.Clear();
        _atomIn.Clear();
        _atomOut.Clear();
        // _controlPorts / _controlValues / _parameters are intentionally kept (managed source of truth).

        _features?.Dispose();
        _features = null;
        _module?.Dispose();
        _module = null;
        _loaded = false;
    }

    private void* Alloc(int bytes)
    {
        var p = Marshal.AllocHGlobal(bytes);
        _allocs.Add(p);
        return (void*)p;
    }

    private readonly record struct MidiEvent(byte B0, byte B1, byte B2, int Length)
    {
        public static MidiEvent Two(byte b0, byte b1) => new(b0, b1, 0, 2);
        public static MidiEvent Three(byte b0, byte b1, byte b2) => new(b0, b1, b2, 3);
    }

    private static string EnsureTrailingSep(string path)
        => path.EndsWith(System.IO.Path.DirectorySeparatorChar) ? path : path + System.IO.Path.DirectorySeparatorChar;
}
