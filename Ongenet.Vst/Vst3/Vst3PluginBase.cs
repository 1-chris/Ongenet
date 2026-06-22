using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Platform;
using Ongenet.Vst.Vst3.Interop;

namespace Ongenet.Vst.Vst3;

/// <summary>
/// Shared host for a VST3 plugin instance: it instantiates the component, queries the audio processor
/// and edit controller (connecting them and copying component state across), wires up the audio + event
/// + parameter-change buses, runs <c>process()</c>, bridges parameters, and embeds the plugin view
/// (<c>IPlugView</c>). Subclasses specialise the audio I/O — <see cref="Vst3Instrument"/> (notes in,
/// audio out, additive) and <see cref="Vst3Effect"/> (audio in → audio out, in place). All native
/// failures are caught; a broken plugin produces silence / passes audio through.
/// </summary>
public abstract unsafe class Vst3PluginBase : IPluginEditor, IDisposable
{
    private const int MaxBlock = 8192;
    private const int MaxBusChannels = 8;
    private const int EventCapacity = 512;
    private const int MaxParamsShown = 512;
    private const int EmbedDefaultW = 900;
    private const int EmbedDefaultH = 600;

    /// <summary>Optional diagnostic sink (set once at startup); surfaces plugin + GUI logs to the app log.</summary>
    public static Action<string>? Log;

    protected readonly string ModulePath;
    protected readonly string Uid;

    private Vst3Module? _module;
    private void* _component;
    private void* _processor;
    private void* _controller;
    private bool _controllerSeparate;
    private void* _cpComponent; // component IConnectionPoint (for disconnect)
    private void* _cpController; // controller IConnectionPoint
    private void* _connToController; // host proxy: component -> controller
    private void* _connToComponent; // host proxy: controller -> component

    private GCHandle _selfHandle;
    private GCHandle _streamHandle;
    private void* _hostApp;
    private void* _handler;
    private void* _frame;
    private void* _runLoop;
    private void* _eventList;
    private void* _paramChanges;
    private void* _streamObj;
    private nint[] _queueObjs = Array.Empty<nint>();

    // Linux IRunLoop: file descriptors + timers the plugin GUI registers, serviced on the UI thread in
    // PumpEditor. Handlers are plugin-side IEventHandler/ITimerHandler pointers (method at vtable slot 3).
    private readonly object _loopLock = new();
    private readonly List<FdReg> _fds = new();
    private readonly List<TimerReg> _timers = new();

    // Audio buses (built once on load; channel buffers reused each block).
    private Vst3Api.AudioBusBuffers* _inBuses;
    private Vst3Api.AudioBusBuffers* _outBuses;
    private int _numInBuses;
    private int _numOutBuses;
    private readonly List<nint> _busAllocs = new(); // every Marshal.AllocHGlobal made for buses

    // Event input (audio thread reads these via the host IEventList).
    internal Vst3Api.Event[] InEvents = new Vst3Api.Event[EventCapacity];
    internal int InEventCount;
    private readonly object _noteLock = new();
    private readonly List<NoteMsg> _pendingNotes = new();
    private readonly HashSet<int> _held = new();

    // Parameter input (audio thread reads these via the host IParameterChanges).
    internal uint[] ParamChangeIds = Array.Empty<uint>();
    internal double[] ParamChangeValues = Array.Empty<double>();
    internal int ParamChangeCount;
    private readonly object _paramLock = new();
    private readonly Dictionary<uint, double> _pendingParams = new();

    private AudioFormat _format = AudioFormat.Default;
    private bool _loadAttempted;
    private bool _loaded;
    private bool _active;
    private double _setupRate;
    private bool _disposed;

    private bool _hasEditor;
    private void* _view;
    private bool _editorOpen;
    private int _editorW;
    private int _editorH;
    private nint _x11Display;
    private nint _embedWindow;

    private IReadOnlyList<Parameter>? _parameters;

    protected Vst3PluginBase(string modulePath, string uid, string displayName)
    {
        ModulePath = modulePath;
        Uid = uid;
        Name = displayName;
    }

    /// <summary>The composite registry id for a VST3 plugin: <c>vst3:&lt;bundle&gt;|&lt;classId&gt;</c>.</summary>
    public static string MakeId(string modulePath, string uid) => $"vst3:{modulePath}|{uid}";

    public string Name { get; }

    public IReadOnlyList<Parameter> Parameters
    {
        get { EnsureLoaded(); return _parameters ?? Array.Empty<Parameter>(); }
    }

    // --- Loading ---

    private bool EnsureLoaded()
    {
        if (_loaded) return true;
        if (_loadAttempted) return false;
        _loadAttempted = true;

        try
        {
            _module = new Vst3Module(ModulePath);
            _selfHandle = GCHandle.Alloc(this, GCHandleType.Weak);
            var gc = GCHandle.ToIntPtr(_selfHandle);

            _hostApp = Vst3Host.BuildHostApplication(gc);
            _handler = Vst3Host.BuildComponentHandler(gc);
            _frame = Vst3Host.BuildPlugFrame(gc);
            _runLoop = Vst3Host.BuildRunLoop(gc);
            _eventList = Vst3Host.BuildEventList(gc);
            _paramChanges = Vst3Host.BuildParamChanges(gc);

            Log?.Invoke($"VST3 '{Name}': createComponent...");
            _component = _module.CreateComponent(Uid);
            if (_component == null) throw new InvalidOperationException("createInstance(component) failed.");

            Log?.Invoke($"VST3 '{Name}': component.initialize...");
            var cv = Comp;
            if (cv->Initialize == null || cv->Initialize(_component, _hostApp) != Vst3Api.ResultOk)
                throw new InvalidOperationException("component.initialize failed.");

            Log?.Invoke($"VST3 '{Name}': query processor...");
            _processor = Vst3Api.QueryInterface(_component, Vst3Api.IidAudioProcessor);
            if (_processor == null) throw new InvalidOperationException("no IAudioProcessor.");

            Log?.Invoke($"VST3 '{Name}': resolve controller...");
            ResolveController();
            // NOTE: we intentionally do NOT connect the component<->controller IConnectionPoints. The
            // connection is optional (state is synced via getState/setComponentState in TransferState), and
            // connecting it makes some plugins (e.g. Philharmonik 2) try to create an IMessage via
            // IHostApplication::createInstance — which we don't vend yet — and crash. Proper connection
            // support needs host IMessage/IAttributeList objects; until then, staying unconnected is safe.
            Log?.Invoke($"VST3 '{Name}': connect components (skipped, separate={_controllerSeparate}).");
            Log?.Invoke($"VST3 '{Name}': transfer state...");
            TransferState();
            Log?.Invoke($"VST3 '{Name}': setComponentHandler...");
            if (_controller != null && Ctrl->SetComponentHandler != null)
                Ctrl->SetComponentHandler(_controller, _handler);

            Log?.Invoke($"VST3 '{Name}': setup buses...");
            SetupBuses();
            Log?.Invoke($"VST3 '{Name}': build parameters...");
            BuildParameters();

            _hasEditor = _controller != null && Ctrl->CreateView != null;
            _loaded = true;
            Log?.Invoke($"VST3 '{Name}': loaded.");
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"VST3 '{Name}': load failed: {ex.Message}");
            TeardownNative();
            return false;
        }
    }

    private void ResolveController()
    {
        // Single-component plugins expose IEditController on the component itself.
        _controller = Vst3Api.QueryInterface(_component, Vst3Api.IidEditController);
        if (_controller != null) { _controllerSeparate = false; return; }

        // Otherwise the component names a separate controller class to instantiate.
        if (Comp->GetControllerClassId == null) return;
        var cid = stackalloc byte[16];
        if (Comp->GetControllerClassId(_component, cid) != Vst3Api.ResultOk) return;

        _controller = _module!.CreateInstance(cid, Vst3Api.IidEditController);
        if (_controller == null) return;
        _controllerSeparate = true;
        if (Ctrl->Initialize != null) Ctrl->Initialize(_controller, _hostApp);
    }

    private void ConnectComponents()
    {
        if (_controller == null || !_controllerSeparate) return;
        _cpComponent = Vst3Api.QueryInterface(_component, Vst3Api.IidConnectionPoint);
        _cpController = Vst3Api.QueryInterface(_controller, Vst3Api.IidConnectionPoint);
        if (_cpComponent == null || _cpController == null) return;

        // Connect each side to a host proxy (not directly to the other side). The proxy forwards notify()
        // to the real peer but drops a re-entrant notify, so a plugin that replies to a notify with
        // another notify can't recurse the two sides into a stack overflow (Philharmonik 2 does this).
        var gc = GCHandle.ToIntPtr(_selfHandle);
        _connToController = Vst3Host.BuildConnectionProxy(gc, (nint)_cpController);
        _connToComponent = Vst3Host.BuildConnectionProxy(gc, (nint)_cpComponent);

        var a = *(Vst3Api.ConnectionPointVtbl**)_cpComponent;
        var b = *(Vst3Api.ConnectionPointVtbl**)_cpController;
        if (a->Connect != null) a->Connect(_cpComponent, _connToController);
        if (b->Connect != null) b->Connect(_cpController, _connToComponent);
    }

    // Re-entrancy guard for IConnectionPoint notify forwarding (per thread). BeginNotify returns false if
    // a notify is already being delivered on this thread, so the host proxy drops the nested one.
    [ThreadStatic] private static bool _inNotify;
    internal static bool BeginNotify() { if (_inNotify) return false; _inNotify = true; return true; }
    internal static void EndNotify() { _inNotify = false; }

    private void TransferState()
    {
        if (_controller == null || Comp->GetState == null || Ctrl->SetComponentState == null) return;

        var stream = new Vst3MemoryStream();
        _streamHandle = GCHandle.Alloc(stream);
        _streamObj = Vst3Host.BuildStream(GCHandle.ToIntPtr(_streamHandle));

        if (Comp->GetState(_component, _streamObj) != Vst3Api.ResultOk) return;
        stream.Seek(0, System.IO.SeekOrigin.Begin);
        Ctrl->SetComponentState(_controller, _streamObj);
    }

    private void SetupBuses()
    {
        _numInBuses = Math.Max(0, Comp->GetBusCount(_component, Vst3Api.MediaAudio, Vst3Api.DirInput));
        _numOutBuses = Math.Max(0, Comp->GetBusCount(_component, Vst3Api.MediaAudio, Vst3Api.DirOutput));
        var numEventIn = Math.Max(0, Comp->GetBusCount(_component, Vst3Api.MediaEvent, Vst3Api.DirInput));

        // Activate all audio + event-input buses.
        ActivateBuses(Vst3Api.MediaAudio, Vst3Api.DirInput, _numInBuses);
        ActivateBuses(Vst3Api.MediaAudio, Vst3Api.DirOutput, _numOutBuses);
        ActivateBuses(Vst3Api.MediaEvent, Vst3Api.DirInput, numEventIn);

        _inBuses = BuildBusBuffers(Vst3Api.DirInput, _numInBuses);
        _outBuses = BuildBusBuffers(Vst3Api.DirOutput, _numOutBuses);

        // Negotiate stereo arrangements (best-effort; plugins keep their defaults if this is rejected).
        if (Proc->SetBusArrangements != null && (_numInBuses > 0 || _numOutBuses > 0))
        {
            var ins = (ulong*)Alloc(Math.Max(1, _numInBuses) * sizeof(ulong));
            var outs = (ulong*)Alloc(Math.Max(1, _numOutBuses) * sizeof(ulong));
            for (var i = 0; i < _numInBuses; i++) ins[i] = Vst3Api.SpeakerStereo;
            for (var i = 0; i < _numOutBuses; i++) outs[i] = Vst3Api.SpeakerStereo;
            Proc->SetBusArrangements(_processor, ins, _numInBuses, outs, _numOutBuses);
        }
    }

    private void ActivateBuses(int mediaType, int dir, int count)
    {
        if (Comp->ActivateBus == null) return;
        for (var i = 0; i < count; i++) Comp->ActivateBus(_component, mediaType, dir, i, 1);
    }

    private Vst3Api.AudioBusBuffers* BuildBusBuffers(int dir, int count)
    {
        if (count <= 0) return null;
        var buses = (Vst3Api.AudioBusBuffers*)Alloc(count * sizeof(Vst3Api.AudioBusBuffers));
        for (var b = 0; b < count; b++)
        {
            var channels = BusChannelCount(dir, b);
            var ptrs = (float**)Alloc(channels * sizeof(void*));
            for (var c = 0; c < channels; c++) ptrs[c] = (float*)Alloc(MaxBlock * sizeof(float));
            buses[b].NumChannels = channels;
            buses[b].SilenceFlags = 0;
            buses[b].ChannelBuffers = ptrs;
        }

        return buses;
    }

    private int BusChannelCount(int dir, int index)
    {
        Vst3Api.BusInfo info;
        if (Comp->GetBusInfo != null && Comp->GetBusInfo(_component, Vst3Api.MediaAudio, dir, index, &info) == Vst3Api.ResultOk)
            return Math.Clamp(info.ChannelCount, 1, MaxBusChannels);
        return 2;
    }

    private void BuildParameters()
    {
        if (_controller == null || Ctrl->GetParameterCount == null || Ctrl->GetParameterInfo == null)
        {
            _parameters = Array.Empty<Parameter>();
            return;
        }

        var count = Math.Min(Ctrl->GetParameterCount(_controller), MaxParamsShown);
        var list = new List<Parameter>(Math.Max(0, count));
        for (var i = 0; i < count; i++)
        {
            Vst3Api.ParameterInfo info;
            if (Ctrl->GetParameterInfo(_controller, i, &info) != Vst3Api.ResultOk) continue;

            var id = info.Id;
            var name = Vst3Api.ReadUtf16(info.Title, Vst3Api.Str128Bytes);
            if (string.IsNullOrWhiteSpace(name)) name = $"Param {i}";

            list.Add(new FloatParameter(name, 0, 1, () => GetParam(id), v => EnqueueParam(id, v)));
        }

        _parameters = list;

        // One reusable IParamValueQueue per possible simultaneous change (one per parameter slot).
        var slots = Math.Max(1, list.Count);
        ParamChangeIds = new uint[slots];
        ParamChangeValues = new double[slots];
        _queueObjs = new nint[slots];
        var gc = GCHandle.ToIntPtr(_selfHandle);
        for (var i = 0; i < slots; i++) _queueObjs[i] = (nint)Vst3Host.BuildParamQueue(gc, i);
    }

    private double GetParam(uint id)
        => _controller != null && Ctrl->GetParamNormalized != null ? Ctrl->GetParamNormalized(_controller, id) : 0;

    private void EnqueueParam(uint id, double value)
    {
        // Keep the controller (GUI) in sync immediately; queue the change for the processor's next block.
        if (_controller != null && Ctrl->SetParamNormalized != null) Ctrl->SetParamNormalized(_controller, id, value);
        lock (_paramLock) _pendingParams[id] = value;
    }

    internal void OnControllerEdit(uint id, double value)
    {
        // The plugin GUI moved a control: push it to the processor too.
        lock (_paramLock) _pendingParams[id] = value;
    }

    // --- Activation ---

    public void Prepare(AudioFormat format)
    {
        _format = format;
        if (!EnsureLoaded()) return;

        var rate = format.SampleRate > 0 ? format.SampleRate : 44100;
        if (_active && Math.Abs(_setupRate - rate) < 0.5) return;
        if (_active) Suspend();

        var setup = new Vst3Api.ProcessSetup
        {
            ProcessMode = Vst3Api.ProcessModeRealtime,
            SymbolicSampleSize = Vst3Api.Sample32,
            MaxSamplesPerBlock = MaxBlock,
            SampleRate = rate,
        };
        Log?.Invoke($"VST3 '{Name}': setupProcessing({rate}Hz)...");
        if (Proc->SetupProcessing != null) Proc->SetupProcessing(_processor, &setup);

        Log?.Invoke($"VST3 '{Name}': setActive...");
        if (Comp->SetActive != null) Comp->SetActive(_component, 1);
        Log?.Invoke($"VST3 '{Name}': setProcessing...");
        if (Proc->SetProcessing != null) Proc->SetProcessing(_processor, 1);
        _active = true;
        _setupRate = rate;
        Log?.Invoke($"VST3 '{Name}': activated.");
    }

    private void Suspend()
    {
        if (_processor != null && Proc->SetProcessing != null) Proc->SetProcessing(_processor, 0);
        if (_component != null && Comp->SetActive != null) Comp->SetActive(_component, 0);
        _active = false;
    }

    // --- Audio thread ---

    protected void RenderAudio(Span<float> buffer, bool feedInput, bool replace)
    {
        if (!_active || _processor == null || Proc->Process == null || _numOutBuses <= 0) return;

        var channels = _format.Channels < 1 ? 1 : _format.Channels;
        var frames = buffer.Length / channels;
        if (frames <= 0) return;
        if (frames > MaxBlock) frames = MaxBlock;

        DrainParams();
        DrainNotes();

        // Input bus 0 from the engine (effects); silence elsewhere.
        for (var b = 0; b < _numInBuses; b++)
        {
            var bus = &_inBuses[b];
            var bufs = (float**)bus->ChannelBuffers;
            for (var c = 0; c < bus->NumChannels; c++)
            {
                var dst = bufs[c];
                if (feedInput && b == 0)
                    for (var f = 0; f < frames; f++) dst[f] = c < channels ? buffer[f * channels + c] : 0f;
                else
                    new Span<float>(dst, frames).Clear();
            }
        }

        for (var b = 0; b < _numOutBuses; b++)
        {
            var bus = &_outBuses[b];
            var bufs = (float**)bus->ChannelBuffers;
            for (var c = 0; c < bus->NumChannels; c++) new Span<float>(bufs[c], frames).Clear();
        }

        var data = new Vst3Api.ProcessData
        {
            ProcessMode = Vst3Api.ProcessModeRealtime,
            SymbolicSampleSize = Vst3Api.Sample32,
            NumSamples = frames,
            NumInputs = _numInBuses,
            NumOutputs = _numOutBuses,
            Inputs = _inBuses,
            Outputs = _outBuses,
            InputParameterChanges = _paramChanges,
            OutputParameterChanges = null,
            InputEvents = _eventList,
            OutputEvents = null,
            ProcessContext = null,
        };

        if (Proc->Process(_processor, &data) != Vst3Api.ResultOk) return;
        InEventCount = 0;

        var outBus = &_outBuses[0];
        var outBufs = (float**)outBus->ChannelBuffers;
        var outCh = outBus->NumChannels;
        if (outCh <= 0) return;

        for (var f = 0; f < frames; f++)
        {
            var i = f * channels;
            for (var c = 0; c < channels; c++)
            {
                var pc = c < outCh ? c : outCh - 1; // mono out → spread to all channels
                var v = outBufs[pc][f];
                if (replace) buffer[i + c] = v;
                else buffer[i + c] += v;
            }
        }
    }

    private void DrainParams()
    {
        lock (_paramLock)
        {
            var n = 0;
            foreach (var kv in _pendingParams)
            {
                if (n >= ParamChangeIds.Length) break;
                ParamChangeIds[n] = kv.Key;
                ParamChangeValues[n] = kv.Value;
                n++;
            }

            ParamChangeCount = n;
            _pendingParams.Clear();
        }
    }

    private void DrainNotes()
    {
        lock (_noteLock)
        {
            var n = 0;
            foreach (var m in _pendingNotes)
            {
                if (n >= EventCapacity) break;
                var e = new Vst3Api.Event { BusIndex = 0, SampleOffset = 0, Type = m.On ? Vst3Api.NoteOnEvent : Vst3Api.NoteOffEvent };
                if (m.On)
                {
                    e.NoteOnChannel = 0;
                    e.NoteOnPitch = (short)m.Pitch;
                    e.NoteOnVelocity = m.Velocity;
                    e.NoteOnNoteId = -1;
                }
                else
                {
                    e.NoteOffChannel = 0;
                    e.NoteOffPitch = (short)m.Pitch;
                    e.NoteOffVelocity = m.Velocity;
                    e.NoteOffNoteId = -1;
                }

                InEvents[n++] = e;
            }

            InEventCount = n;
            _pendingNotes.Clear();
        }
    }

    // --- Event input (UI + scheduler threads) ---

    protected void EnqueueNoteOn(int midiNote, float velocity)
    {
        lock (_noteLock)
        {
            _pendingNotes.Add(new NoteMsg(true, midiNote, Math.Clamp(velocity, 0f, 1f)));
            _held.Add(midiNote);
        }
    }

    protected void EnqueueNoteOff(int midiNote)
    {
        lock (_noteLock)
        {
            _pendingNotes.Add(new NoteMsg(false, midiNote, 0f));
            _held.Remove(midiNote);
        }
    }

    protected void EnqueueAllNotesOff()
    {
        lock (_noteLock)
        {
            foreach (var note in _held) _pendingNotes.Add(new NoteMsg(false, note, 0f));
            _held.Clear();
        }
    }

    // --- IPluginEditor (IPlugView) ---

    public bool HasEditor { get { EnsureLoaded(); return _hasEditor; } }
    public bool IsEditorOpen => _editorOpen;
    public bool PrefersFloating => false; // VST3 views are embedded into a parent window
    public int EditorWidth => _editorW;
    public int EditorHeight => _editorH;

    public void OpenEditor(nint windowHandle, string apiType, bool floating)
    {
        if (_editorOpen) return;
        if (!EnsureLoaded() || _controller == null || Ctrl->CreateView == null) { Log?.Invoke($"VST3 '{Name}': no editor."); return; }

        var platform = apiType switch
        {
            "win32" => Vst3Api.PlatformHwnd,
            "cocoa" => Vst3Api.PlatformNsView,
            _ => Vst3Api.PlatformX11,
        };

        try
        {
            if (_view == null)
            {
                Log?.Invoke($"VST3 '{Name}': createView...");
                var viewType = stackalloc byte[8];
                "editor"u8.CopyTo(new Span<byte>(viewType, 8));
                _view = Ctrl->CreateView(_controller, viewType);
            }

            if (_view == null) { Log?.Invoke($"VST3 '{Name}': createView returned null."); return; }
            Log?.Invoke($"VST3 '{Name}': view created.");
            var vv = View;

            var typeUtf8 = Marshal.StringToCoTaskMemUTF8(platform);
            try
            {
                if (vv->IsPlatformTypeSupported != null && vv->IsPlatformTypeSupported(_view, (byte*)typeUtf8) != Vst3Api.ResultOk)
                    Log?.Invoke($"VST3 '{Name}': view reports {platform} unsupported; attaching anyway.");

                // Give the view our frame, then ask its preferred size *before* attaching, so the parent
                // window is already the right size and the plugin needn't immediately call resizeView.
                if (vv->SetFrame != null) vv->SetFrame(_view, _frame);
                Log?.Invoke($"VST3 '{Name}': setFrame done.");

                _editorW = EmbedDefaultW;
                _editorH = EmbedDefaultH;
                Vst3Api.ViewRect rect;
                if (vv->GetSize != null && vv->GetSize(_view, &rect) == Vst3Api.ResultOk)
                {
                    var w = rect.Right - rect.Left;
                    var h = rect.Bottom - rect.Top;
                    // Trust the plugin's size only if it's plausible; a 0 or absurd value (some plugins
                    // only know their size after attach) would make a degenerate embed window and can send
                    // the plugin's own layout into a recursive resize.
                    if (w is >= 32 and <= 8000 && h is >= 32 and <= 8000) { _editorW = w; _editorH = h; }
                    else Log?.Invoke($"VST3 '{Name}': getSize returned implausible {w}x{h}; using {_editorW}x{_editorH}.");
                }
                Log?.Invoke($"VST3 '{Name}': getSize -> {_editorW}x{_editorH}.");

                var parent = windowHandle;
                if (OperatingSystem.IsLinux() && _embedWindow == 0)
                {
                    if (X11Embed.Create(windowHandle, _editorW, _editorH, out _x11Display, out _embedWindow))
                        parent = _embedWindow;
                    else
                        Log?.Invoke($"VST3 '{Name}': X11 embed window failed; using host window directly.");
                }
                else if (_embedWindow != 0)
                {
                    parent = _embedWindow;
                    X11Embed.Resize(_x11Display, _embedWindow, _editorW, _editorH);
                }

                Log?.Invoke($"VST3 '{Name}': calling attached(parent=0x{parent:x}, type={platform})...");
                if (vv->Attached == null || vv->Attached(_view, (void*)parent, (byte*)typeUtf8) != Vst3Api.ResultOk)
                {
                    Log?.Invoke($"VST3 '{Name}': view attach failed.");
                    return;
                }

                _editorOpen = true;
                Log?.Invoke($"VST3 '{Name}': editor opened {_editorW}x{_editorH}.");
            }
            finally { Marshal.FreeCoTaskMem(typeUtf8); }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"VST3 '{Name}': editor open failed: {ex.Message}");
            CloseEditor();
        }
    }

    // Host-initiated (the user resized our window): record the size; PumpEditor tells the plugin.
    public void SetEditorSize(int width, int height) => QueueViewSize(width, height);

    // Called from IPlugFrame::resizeView — the plugin asked the host to resize its container.
    internal void OnViewResize(int width, int height, void* view) => QueueViewSize(width, height);

    // Record a requested view size and resize the embed window now. We do NOT call IPlugView::onSize
    // synchronously here: onSize is a call back INTO the plugin, and several plugins respond to it by
    // calling resizeView again — which over the yabridge socket becomes an unbounded resizeView<->onSize
    // recursion that overflows the plugin's Wine stack. Instead onSize is applied later from PumpEditor,
    // on the UI thread and outside any plugin call stack, so the bounce can never recurse.
    private readonly object _sizeLock = new();
    private bool _pendingSize;
    private int _pendingW, _pendingH;
    private void QueueViewSize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        _editorW = width;
        _editorH = height;
        if (_embedWindow != 0) X11Embed.Resize(_x11Display, _embedWindow, width, height);
        lock (_sizeLock) { _pendingSize = true; _pendingW = width; _pendingH = height; }
    }

    private void ApplyPendingSize()
    {
        int w, h;
        lock (_sizeLock)
        {
            if (!_pendingSize) return;
            _pendingSize = false;
            w = _pendingW;
            h = _pendingH;
        }

        if (_view == null || View->OnSize == null) return;
        var r = new Vst3Api.ViewRect { Left = 0, Top = 0, Right = w, Bottom = h };
        try { View->OnSize(_view, &r); } catch { /* ignore */ }
    }

    public void CloseEditor()
    {
        if (_view != null && _editorOpen && View->Removed != null) View->Removed(_view);
        _editorOpen = false;
        // Drop any FDs/timers the view registered, so the pump can't call into a removed view.
        lock (_loopLock) { _fds.Clear(); _timers.Clear(); }
        lock (_sizeLock) _pendingSize = false;
        X11Embed.Destroy(ref _x11Display, ref _embedWindow);
    }

    public void PumpEditor()
    {
        if (!_editorOpen) return;
        // Apply any deferred resize first (all platforms). Then, on X11, service the file descriptors and
        // timers the plugin registered through our IRunLoop (Windows/macOS views drive their own loop).
        ApplyPendingSize();
        if (!OperatingSystem.IsLinux()) return;
        ServiceTimers();
        ServiceFds();
    }

    // --- IRunLoop registries (called by the plugin, possibly off the UI thread) ---

    internal nint RunLoopPtr => (nint)_runLoop;

    internal void RegisterFd(nint handler, int fd)
    {
        Log?.Invoke($"VST3 '{Name}': IRunLoop registerEventHandler(fd={fd}).");
        lock (_loopLock) { _fds.RemoveAll(f => f.Handler == handler && f.Fd == fd); _fds.Add(new FdReg { Handler = handler, Fd = fd }); }
    }

    internal void UnregisterFd(nint handler) { lock (_loopLock) _fds.RemoveAll(f => f.Handler == handler); }

    internal void RegisterTimer(nint handler, ulong milliseconds)
    {
        Log?.Invoke($"VST3 '{Name}': IRunLoop registerTimer({milliseconds}ms).");
        var period = Math.Max(16, (long)milliseconds);
        lock (_loopLock) _timers.Add(new TimerReg { Handler = handler, Period = period, NextDue = Environment.TickCount64 + period });
    }

    internal void UnregisterTimer(nint handler) { lock (_loopLock) _timers.RemoveAll(t => t.Handler == handler); }

    private void ServiceTimers()
    {
        var now = Environment.TickCount64;
        TimerReg[] due;
        lock (_loopLock)
        {
            due = _timers.FindAll(t => now >= t.NextDue).ToArray();
            foreach (var t in due) t.NextDue = now + t.Period;
        }

        foreach (var t in due)
        {
            try
            {
                var h = (void*)t.Handler;
                var fn = (delegate* unmanaged[Cdecl]<void*, void>)(*(void***)h)[3]; // ITimerHandler::onTimer
                fn(h);
            }
            catch { /* a faulty handler must not kill the pump */ }
        }
    }

    private void ServiceFds()
    {
        FdReg[] regs;
        lock (_loopLock)
        {
            if (_fds.Count == 0) return;
            regs = _fds.ToArray();
        }

        var poll = new PollFd[regs.Length];
        for (var i = 0; i < regs.Length; i++) poll[i] = new PollFd { Fd = regs[i].Fd, Events = PollIn };

        int ready;
        try { ready = PollFds(poll, (uint)poll.Length, 0); } catch { return; }
        if (ready <= 0) return;

        for (var i = 0; i < poll.Length; i++)
        {
            if (poll[i].Revents == 0) continue;
            try
            {
                var h = (void*)regs[i].Handler;
                var fn = (delegate* unmanaged[Cdecl]<void*, int, void>)(*(void***)h)[3]; // IEventHandler::onFDIsSet
                fn(h, regs[i].Fd);
            }
            catch { /* ignore */ }
        }
    }

    private const short PollIn = 0x001;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd { public int Fd; public short Events; public short Revents; }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int PollFds(PollFd[] fds, uint nfds, int timeout);

    private sealed class FdReg { public nint Handler; public int Fd; }
    private sealed class TimerReg { public nint Handler; public long Period; public long NextDue; }

    // --- vtable accessors ---

    private Vst3Api.ComponentVtbl* Comp => *(Vst3Api.ComponentVtbl**)_component;
    private Vst3Api.AudioProcessorVtbl* Proc => *(Vst3Api.AudioProcessorVtbl**)_processor;
    private Vst3Api.EditControllerVtbl* Ctrl => *(Vst3Api.EditControllerVtbl**)_controller;
    private Vst3Api.PlugViewVtbl* View => *(Vst3Api.PlugViewVtbl**)_view;

    // --- host-object accessors (read by Vst3Host thunks) ---

    internal nint QueueObjAt(int index) => _queueObjs[index];
    internal uint ParamChangeIdAt(int index) => (uint)(index >= 0 && index < ParamChangeIds.Length ? ParamChangeIds[index] : 0);
    internal double ParamChangeValueAt(int index) => index >= 0 && index < ParamChangeValues.Length ? ParamChangeValues[index] : 0;

    // --- Teardown ---

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TeardownNative();
    }

    private void TeardownNative()
    {
        try
        {
            CloseEditor();
            if (_view != null) { Vst3Api.Release(_view); _view = null; }
            if (_active) Suspend();

            if (_cpComponent != null && _connToController != null)
            {
                var a = *(Vst3Api.ConnectionPointVtbl**)_cpComponent;
                if (a->Disconnect != null) a->Disconnect(_cpComponent, _connToController);
            }

            if (_cpController != null && _connToComponent != null)
            {
                var b = *(Vst3Api.ConnectionPointVtbl**)_cpController;
                if (b->Disconnect != null) b->Disconnect(_cpController, _connToComponent);
            }

            if (_controller != null && Ctrl->SetComponentHandler != null) Ctrl->SetComponentHandler(_controller, null);

            if (_controllerSeparate && _controller != null && Ctrl->Terminate != null) Ctrl->Terminate(_controller);
            if (_component != null && Comp->Terminate != null) Comp->Terminate(_component);
        }
        catch { /* ignore */ }

        if (_cpComponent != null) { Vst3Api.Release(_cpComponent); _cpComponent = null; }
        if (_cpController != null) { Vst3Api.Release(_cpController); _cpController = null; }
        if (_controllerSeparate && _controller != null) Vst3Api.Release(_controller);
        _controller = null;
        if (_processor != null) { Vst3Api.Release(_processor); _processor = null; }
        if (_component != null) { Vst3Api.Release(_component); _component = null; }

        foreach (var p in _busAllocs) Marshal.FreeHGlobal(p);
        _busAllocs.Clear();
        _inBuses = null;
        _outBuses = null;

        foreach (var q in _queueObjs) Vst3Host.Free((void*)q);
        _queueObjs = Array.Empty<nint>();
        lock (_loopLock) { _fds.Clear(); _timers.Clear(); }
        Vst3Host.Free(_hostApp); _hostApp = null;
        Vst3Host.Free(_handler); _handler = null;
        Vst3Host.Free(_frame); _frame = null;
        Vst3Host.Free(_runLoop); _runLoop = null;
        Vst3Host.Free(_eventList); _eventList = null;
        Vst3Host.Free(_paramChanges); _paramChanges = null;
        Vst3Host.Free(_streamObj); _streamObj = null;
        Vst3Host.Free(_connToController); _connToController = null;
        Vst3Host.Free(_connToComponent); _connToComponent = null;

        if (_streamHandle.IsAllocated) _streamHandle.Free();
        if (_selfHandle.IsAllocated) _selfHandle.Free();

        _module?.Dispose();
        _module = null;
        _loaded = false;
        _active = false;
    }

    private void* Alloc(int bytes)
    {
        var p = Marshal.AllocHGlobal(bytes);
        _busAllocs.Add(p);
        return (void*)p;
    }

    private readonly record struct NoteMsg(bool On, int Pitch, float Velocity);
}
