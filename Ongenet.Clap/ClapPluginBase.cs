using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ongenet.Clap.Interop;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Clap;

/// <summary>
/// Shared host for a CLAP plugin instance: module loading, the host struct + callbacks, parameter
/// bridging, the GUI (<see cref="IPluginEditor"/>) with its timer/fd event loop, and the
/// <c>process()</c> audio bridge. Subclasses specialise the audio I/O — <see cref="ClapInstrument"/>
/// (notes in, audio out, additive) and <see cref="ClapEffect"/> (audio in → audio out, in place).
/// All native failures are caught; a broken plugin simply produces silence / passes audio through.
/// </summary>
public abstract unsafe class ClapPluginBase : IPluginEditor, IDisposable
{
    protected const int MaxBlock = 8192;
    private const int EventCapacity = 1024;
    private const int EventStride = 64; // >= sizeof(ClapEventParamValue)
    private const int MaxParamsShown = 64;

    /// <summary>Optional diagnostic sink (set once at startup); surfaces plugin + GUI logs to the app log.</summary>
    public static Action<string>? Log;

    protected readonly string ModulePath;
    protected readonly string PluginId;

    private readonly object _evLock = new();
    private readonly List<Pending> _pending = new();
    private readonly HashSet<int> _held = new();

    private ClapModule? _module;
    private ClapApi.ClapHost* _host;
    private ClapApi.ClapPlugin* _plugin;
    private ClapApi.ClapPluginParams* _params;
    private ClapApi.ClapPluginGui* _gui;
    private ClapApi.ClapPluginTimerSupport* _timerExt;
    private ClapApi.ClapPluginPosixFdSupport* _fdExt;
    private GCHandle _selfHandle;

    // GUI event-loop integration (touched on the UI thread).
    private readonly object _loopLock = new();
    private readonly List<TimerReg> _timers = new();
    private readonly List<FdReg> _fds = new();
    private uint _nextTimerId = 1;
    private volatile bool _callbackPending;

    // Native scratch (allocated on load).
    private ClapApi.ClapAudioBuffer* _outBuf;
    private float** _outData;
    private int _outChannels = 2;
    private ClapApi.ClapAudioBuffer* _inBuf;
    private float** _inData;
    private int _inChannels;
    private ClapApi.ClapInputEvents* _inEvents;
    private ClapApi.InputEventsCtx* _inCtx;
    private ClapApi.ClapOutputEvents* _outEvents;
    private byte* _eventBuffer;

    private AudioFormat _format = AudioFormat.Default;
    private bool _loadAttempted;
    private bool _loaded;
    private bool _activated;
    private double _activatedRate;
    private volatile bool _processing;
    private bool _disposed;

    private string? _guiApi;
    private bool _guiFloating;
    private bool _guiEmbedded;
    private bool _guiCreated;
    private bool _editorOpen;
    private int _editorW;
    private int _editorH;

    private IReadOnlyList<Parameter>? _parameters;

    protected ClapPluginBase(string modulePath, string pluginId, string displayName)
    {
        ModulePath = modulePath;
        PluginId = pluginId;
        Name = displayName;
    }

    /// <summary>The composite registry id for a CLAP plugin: <c>clap:&lt;module&gt;|&lt;pluginId&gt;</c>.</summary>
    public static string MakeId(string modulePath, string pluginId) => $"clap:{modulePath}|{pluginId}";

    public string Name { get; }

    public IReadOnlyList<Parameter> Parameters
    {
        get { EnsureLoaded(); return _parameters ?? Array.Empty<Parameter>(); }
    }

    // --- Loading / activation ---

    protected bool EnsureLoaded()
    {
        if (_loaded) return true;
        if (_loadAttempted) return false;
        _loadAttempted = true;

        try
        {
            _module = new ClapModule(ModulePath);
            _selfHandle = GCHandle.Alloc(this, GCHandleType.Weak);
            _host = ClapApi.AllocHost((void*)GCHandle.ToIntPtr(_selfHandle));

            _plugin = _module.CreatePlugin(PluginId, _host);
            if (_plugin == null) throw new InvalidOperationException("create_plugin returned null.");
            if (_plugin->Init != null && _plugin->Init(_plugin) == 0)
                throw new InvalidOperationException("plugin.init failed.");

            FetchExtensions();
            AllocateScratch();
            BuildParameters();
            ResolveGuiApi();

            _loaded = true;
            return true;
        }
        catch
        {
            TeardownNative();
            return false;
        }
    }

    private void FetchExtensions()
    {
        _params = (ClapApi.ClapPluginParams*)GetExtension(ClapApi.ExtParams);
        _gui = (ClapApi.ClapPluginGui*)GetExtension(ClapApi.ExtGui);
        _timerExt = (ClapApi.ClapPluginTimerSupport*)GetExtension(ClapApi.ExtTimerSupport);
        _fdExt = (ClapApi.ClapPluginPosixFdSupport*)GetExtension(ClapApi.ExtPosixFdSupport);

        _outChannels = 2;
        _inChannels = 0;
        var audioPorts = (ClapApi.ClapPluginAudioPorts*)GetExtension(ClapApi.ExtAudioPorts);
        if (audioPorts != null && audioPorts->Count != null && audioPorts->Get != null)
        {
            _outChannels = PortChannels(audioPorts, isInput: 0, fallback: 2);
            _inChannels = PortChannels(audioPorts, isInput: 1, fallback: 0);
        }
    }

    private int PortChannels(ClapApi.ClapPluginAudioPorts* ports, byte isInput, int fallback)
    {
        if (ports->Count(_plugin, isInput) == 0) return fallback;
        ClapApi.ClapAudioPortInfo info = default;
        if (ports->Get(_plugin, 0, isInput, &info) != 0 && info.ChannelCount is > 0 and <= 8)
            return (int)info.ChannelCount;
        return fallback == 0 ? 0 : fallback;
    }

    private void* GetExtension(string id)
    {
        if (_plugin == null || _plugin->GetExtension == null) return null;
        var idUtf8 = ClapApi.Utf8(id);
        try { return _plugin->GetExtension(_plugin, idUtf8); }
        finally { ClapApi.FreeUtf8(idUtf8); }
    }

    private void AllocateScratch()
    {
        _outData = AllocChannels(_outChannels);
        _outBuf = AllocAudioBuffer(_outData, _outChannels);

        if (_inChannels > 0)
        {
            _inData = AllocChannels(_inChannels);
            _inBuf = AllocAudioBuffer(_inData, _inChannels);
        }

        _eventBuffer = (byte*)Marshal.AllocHGlobal(EventCapacity * EventStride);
        _inCtx = (ClapApi.InputEventsCtx*)Marshal.AllocHGlobal(sizeof(ClapApi.InputEventsCtx));
        _inCtx->Count = 0;
        _inCtx->Stride = EventStride;
        _inCtx->Buffer = _eventBuffer;

        _inEvents = (ClapApi.ClapInputEvents*)Marshal.AllocHGlobal(sizeof(ClapApi.ClapInputEvents));
        ClapApi.InitInputEvents(_inEvents, _inCtx);
        _outEvents = (ClapApi.ClapOutputEvents*)Marshal.AllocHGlobal(sizeof(ClapApi.ClapOutputEvents));
        ClapApi.InitOutputEvents(_outEvents);
    }

    private static float** AllocChannels(int channels)
    {
        var data = (float**)Marshal.AllocHGlobal(channels * sizeof(void*));
        for (var c = 0; c < channels; c++) data[c] = (float*)Marshal.AllocHGlobal(MaxBlock * sizeof(float));
        return data;
    }

    private static ClapApi.ClapAudioBuffer* AllocAudioBuffer(float** data, int channels)
    {
        var buf = (ClapApi.ClapAudioBuffer*)Marshal.AllocHGlobal(sizeof(ClapApi.ClapAudioBuffer));
        *buf = default;
        buf->Data32 = data;
        buf->ChannelCount = (uint)channels;
        return buf;
    }

    private void BuildParameters()
    {
        if (_params == null || _params->Count == null || _params->GetInfo == null)
        {
            _parameters = Array.Empty<Parameter>();
            return;
        }

        var list = new List<Parameter>();
        var count = Math.Min(_params->Count(_plugin), (uint)MaxParamsShown);
        for (var i = 0u; i < count; i++)
        {
            ClapApi.ClapParamInfo info = default;
            if (_params->GetInfo(_plugin, i, &info) == 0) continue;

            var id = info.Id;
            var name = ClapApi.ReadFixedUtf8(info.Name, ClapApi.NameSize);
            if (string.IsNullOrEmpty(name)) name = $"Param {i}";
            var min = info.MinValue;
            var max = info.MaxValue;
            if (max <= min) max = min + 1;

            list.Add(new FloatParameter(name, min, max, () => GetParamValue(id), v => EnqueueParam(id, v)));
        }

        _parameters = list;
    }

    private double GetParamValue(uint paramId)
    {
        if (_params == null || _params->GetValue == null || _plugin == null) return 0;
        double v = 0;
        _params->GetValue(_plugin, paramId, &v);
        return v;
    }

    private void ResolveGuiApi()
    {
        _guiApi = null;
        _guiFloating = false;
        _guiEmbedded = false;
        if (_gui == null || _gui->IsApiSupported == null) return;

        var platform = PlatformApi();
        if (platform == null) return;

        var apiUtf8 = ClapApi.Utf8(platform);
        try
        {
            _guiFloating = _gui->IsApiSupported(_plugin, apiUtf8, 1) != 0;
            _guiEmbedded = _gui->IsApiSupported(_plugin, apiUtf8, 0) != 0;
            if (_guiFloating || _guiEmbedded) _guiApi = platform;
        }
        finally { ClapApi.FreeUtf8(apiUtf8); }
    }

    private static string? PlatformApi()
    {
        if (OperatingSystem.IsWindows()) return ClapApi.WindowApiWin32;
        if (OperatingSystem.IsMacOS()) return ClapApi.WindowApiCocoa;
        return ClapApi.WindowApiX11;
    }

    public void Prepare(AudioFormat format)
    {
        _format = format;
        if (!EnsureLoaded() || _plugin == null) return;

        if (_activated && Math.Abs(_activatedRate - format.SampleRate) < 0.5) return;
        if (_activated) Deactivate();

        if (_plugin->Activate != null && _plugin->Activate(_plugin, format.SampleRate, 1, MaxBlock) != 0)
        {
            _activated = true;
            _activatedRate = format.SampleRate;
        }
    }

    private void Deactivate()
    {
        if (_plugin == null) return;
        if (_processing && _plugin->StopProcessing != null) _plugin->StopProcessing(_plugin);
        _processing = false;
        if (_plugin->Deactivate != null) _plugin->Deactivate(_plugin);
        _activated = false;
    }

    // --- Audio thread ---

    /// <summary>
    /// Runs one <c>process()</c> block. <paramref name="feedInput"/> de-interleaves the engine buffer
    /// into the plugin's audio input (for effects); <paramref name="replace"/> overwrites the engine
    /// buffer with the plugin output (effects) vs. adding to it (instruments).
    /// </summary>
    protected void RenderAudio(Span<float> buffer, bool feedInput, bool replace)
    {
        if (!_activated || _plugin == null || _plugin->Process == null) return;

        var channels = _format.Channels < 1 ? 1 : _format.Channels;
        var frames = buffer.Length / channels;
        if (frames <= 0) return;
        if (frames > MaxBlock) frames = MaxBlock;

        if (!_processing)
        {
            _processing = _plugin->StartProcessing == null || _plugin->StartProcessing(_plugin) != 0;
            if (!_processing) return;
        }

        DrainEvents();

        var hasInput = feedInput && _inData != null && _inChannels > 0;
        if (hasInput)
        {
            for (var ic = 0; ic < _inChannels; ic++)
            {
                var dst = _inData[ic];
                for (var f = 0; f < frames; f++) dst[f] = ic < channels ? buffer[f * channels + ic] : 0f;
            }

            _inBuf->ChannelCount = (uint)_inChannels;
        }

        for (var c = 0; c < _outChannels; c++) new Span<float>(_outData[c], frames).Clear();
        _outBuf->ChannelCount = (uint)_outChannels;

        ClapApi.ClapProcess proc = default;
        proc.SteadyTime = -1;
        proc.FramesCount = (uint)frames;
        proc.AudioInputs = hasInput ? _inBuf : null;
        proc.AudioInputsCount = hasInput ? 1u : 0u;
        proc.AudioOutputs = _outBuf;
        proc.AudioOutputsCount = 1;
        proc.InEvents = _inEvents;
        proc.OutEvents = _outEvents;

        var status = _plugin->Process(_plugin, &proc);
        _inCtx->Count = 0;
        if (status == 0) return; // CLAP_PROCESS_ERROR — leave the buffer untouched (passthrough)

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var pc = c < _outChannels ? c : _outChannels - 1;
                var v = _outData[pc][frame];
                if (replace) buffer[i + c] = v;
                else buffer[i + c] += v;
            }
        }
    }

    private void DrainEvents()
    {
        lock (_evLock)
        {
            var n = Math.Min(_pending.Count, EventCapacity);
            for (var i = 0; i < n; i++) WriteEvent(_eventBuffer + (long)i * EventStride, _pending[i]);
            _inCtx->Count = n;
            _pending.Clear();
        }
    }

    private static void WriteEvent(byte* slot, Pending p)
    {
        if (p.Kind == PendingKind.Param)
        {
            var ev = (ClapApi.ClapEventParamValue*)slot;
            *ev = default;
            ev->Header.Size = (uint)sizeof(ClapApi.ClapEventParamValue);
            ev->Header.SpaceId = ClapApi.CoreEventSpaceId;
            ev->Header.Type = ClapApi.EventParamValue;
            ev->ParamId = p.ParamId;
            ev->NoteId = -1;
            ev->PortIndex = -1;
            ev->Channel = -1;
            ev->Key = -1;
            ev->Value = p.Value;
        }
        else
        {
            var ev = (ClapApi.ClapEventNote*)slot;
            *ev = default;
            ev->Header.Size = (uint)sizeof(ClapApi.ClapEventNote);
            ev->Header.SpaceId = ClapApi.CoreEventSpaceId;
            ev->Header.Type = p.Kind == PendingKind.NoteOn ? ClapApi.EventNoteOn : ClapApi.EventNoteOff;
            ev->NoteId = -1;
            ev->PortIndex = 0;
            ev->Channel = 0;
            ev->Key = p.Key;
            ev->Velocity = p.Value;
        }
    }

    // --- Event input (UI + scheduler threads) ---

    protected void EnqueueNoteOn(int midiNote, float velocity)
    {
        lock (_evLock)
        {
            _pending.Add(new Pending(PendingKind.NoteOn, (short)midiNote, velocity, 0));
            _held.Add(midiNote);
        }
    }

    protected void EnqueueNoteOff(int midiNote)
    {
        lock (_evLock)
        {
            _pending.Add(new Pending(PendingKind.NoteOff, (short)midiNote, 0, 0));
            _held.Remove(midiNote);
        }
    }

    protected void EnqueueAllNotesOff()
    {
        lock (_evLock)
        {
            foreach (var note in _held) _pending.Add(new Pending(PendingKind.NoteOff, (short)note, 0, 0));
            _held.Clear();
        }
    }

    private void EnqueueParam(uint paramId, double value)
    {
        lock (_evLock) _pending.Add(new Pending(PendingKind.Param, 0, value, paramId));
    }

    // --- IPluginEditor (GUI) ---

    public bool HasEditor { get { EnsureLoaded(); return _guiApi != null; } }
    public bool IsEditorOpen => _editorOpen;
    public bool PrefersFloating { get { EnsureLoaded(); return _guiFloating && !_guiEmbedded; } }
    public int EditorWidth => _editorW;
    public int EditorHeight => _editorH;

    public void OpenEditor(nint windowHandle, string apiType, bool floating)
    {
        if (_editorOpen) return;
        if (!EnsureLoaded() || _gui == null || _guiApi == null)
        {
            Log?.Invoke($"CLAP '{Name}': no GUI (gui ext or api unavailable).");
            return;
        }

        var api = ClapApi.Utf8(_guiApi);
        try
        {
            var window = new ClapApi.ClapWindow { Api = api, Handle = (void*)windowHandle };

            // Create once; thereafter just show/hide (destroy/recreate leaves many plugins black,
            // and on X11 the embedded child dies with its parent window anyway).
            if (!_guiCreated)
            {
                Log?.Invoke($"CLAP '{Name}': creating GUI api={_guiApi} floating={floating}.");
                if (_gui->Create == null || _gui->Create(_plugin, api, floating ? (byte)1 : (byte)0) == 0)
                {
                    Log?.Invoke($"CLAP '{Name}': gui.create failed.");
                    return;
                }

                if (floating)
                {
                    if (_gui->SetTransient != null) _gui->SetTransient(_plugin, &window);
                    var title = ClapApi.Utf8(Name);
                    try { if (_gui->SuggestTitle != null) _gui->SuggestTitle(_plugin, title); }
                    finally { ClapApi.FreeUtf8(title); }
                }
                else if (_gui->SetParent != null)
                {
                    _gui->SetParent(_plugin, &window);
                }

                if (_gui->GetSize != null)
                {
                    uint w = 0, h = 0;
                    if (_gui->GetSize(_plugin, &w, &h) != 0) { _editorW = (int)w; _editorH = (int)h; }
                }

                _guiCreated = true;
            }

            var shown = _gui->Show == null || _gui->Show(_plugin) != 0;
            _editorOpen = true;
            Log?.Invoke($"CLAP '{Name}': GUI shown={shown} size={_editorW}x{_editorH} (timers={_timers.Count}, fds={_fds.Count}).");
        }
        finally { ClapApi.FreeUtf8(api); }
    }

    public void SetEditorSize(int width, int height)
    {
        if (!_editorOpen || _gui == null || _gui->SetSize == null || width <= 0 || height <= 0) return;
        var canResize = _gui->CanResize == null || _gui->CanResize(_plugin) != 0;
        if (canResize) _gui->SetSize(_plugin, (uint)width, (uint)height);
    }

    public void CloseEditor()
    {
        if (!_editorOpen || _gui == null) return;
        if (_gui->Hide != null) _gui->Hide(_plugin);
        _editorOpen = false;
    }

    private void DestroyEditor()
    {
        if (_gui != null && _guiCreated)
        {
            if (_editorOpen && _gui->Hide != null) _gui->Hide(_plugin);
            if (_gui->Destroy != null) _gui->Destroy(_plugin);
        }

        _guiCreated = false;
        _editorOpen = false;
        lock (_loopLock) { _timers.Clear(); _fds.Clear(); }
    }

    internal void RequestCallback() => _callbackPending = true;

    internal uint RegisterTimer(uint periodMs)
    {
        lock (_loopLock)
        {
            var id = _nextTimerId++;
            var period = Math.Max(16, (long)periodMs);
            _timers.Add(new TimerReg { Id = id, Period = period, NextDue = Environment.TickCount64 + period });
            return id;
        }
    }

    internal void UnregisterTimer(uint id) { lock (_loopLock) _timers.RemoveAll(t => t.Id == id); }
    internal void RegisterFd(int fd, uint flags) { lock (_loopLock) { _fds.RemoveAll(f => f.Fd == fd); _fds.Add(new FdReg { Fd = fd, Flags = flags }); } }
    internal void ModifyFd(int fd, uint flags) { lock (_loopLock) foreach (var f in _fds) if (f.Fd == fd) f.Flags = flags; }
    internal void UnregisterFd(int fd) { lock (_loopLock) _fds.RemoveAll(f => f.Fd == fd); }

    internal void OnGuiClosed(bool wasDestroyed)
    {
        Log?.Invoke($"CLAP '{Name}': plugin closed its GUI (destroyed={wasDestroyed}).");
        _editorOpen = false;
        if (wasDestroyed)
        {
            _guiCreated = false;
            lock (_loopLock) { _timers.Clear(); _fds.Clear(); }
        }
    }

    public void PumpEditor()
    {
        if (_plugin == null) return;

        if (_callbackPending)
        {
            _callbackPending = false;
            if (_plugin->OnMainThread != null) _plugin->OnMainThread(_plugin);
        }

        if (!_editorOpen) return;
        ServiceTimers();
        ServiceFds();
    }

    private void ServiceTimers()
    {
        if (_timerExt == null || _timerExt->OnTimer == null) return;
        var now = Environment.TickCount64;
        TimerReg[] due;
        lock (_loopLock)
        {
            due = _timers.FindAll(t => now >= t.NextDue).ToArray();
            foreach (var t in due) t.NextDue = now + t.Period;
        }

        foreach (var t in due)
        {
            try { _timerExt->OnTimer(_plugin, t.Id); } catch { /* ignore */ }
        }
    }

    private void ServiceFds()
    {
        if (_fdExt == null || _fdExt->OnFd == null || OperatingSystem.IsWindows()) return;

        PollFd[] poll;
        FdReg[] regs;
        lock (_loopLock)
        {
            if (_fds.Count == 0) return;
            regs = _fds.ToArray();
            poll = new PollFd[regs.Length];
            for (var i = 0; i < regs.Length; i++)
            {
                short ev = 0;
                if ((regs[i].Flags & ClapApi.FdRead) != 0) ev |= PollIn;
                if ((regs[i].Flags & ClapApi.FdWrite) != 0) ev |= PollOut;
                poll[i] = new PollFd { Fd = regs[i].Fd, Events = ev, Revents = 0 };
            }
        }

        int ready;
        try { ready = PollFds(poll, (uint)poll.Length, 0); } catch { return; }
        if (ready <= 0) return;

        for (var i = 0; i < poll.Length; i++)
        {
            if (poll[i].Revents == 0) continue;
            uint flags = 0;
            if ((poll[i].Revents & PollIn) != 0) flags |= ClapApi.FdRead;
            if ((poll[i].Revents & PollOut) != 0) flags |= ClapApi.FdWrite;
            if ((poll[i].Revents & (PollErr | PollHup)) != 0) flags |= ClapApi.FdError;
            try { _fdExt->OnFd(_plugin, poll[i].Fd, flags); } catch { /* ignore */ }
        }
    }

    private const short PollIn = 0x001, PollOut = 0x004, PollErr = 0x008, PollHup = 0x010;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd { public int Fd; public short Events; public short Revents; }

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int PollFds(PollFd[] fds, uint nfds, int timeout);

    private sealed class TimerReg { public uint Id; public long Period; public long NextDue; }
    private sealed class FdReg { public int Fd; public uint Flags; }

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
            if (_plugin != null)
            {
                DestroyEditor();
                Deactivate();
                if (_plugin->Destroy != null) _plugin->Destroy(_plugin);
            }
        }
        catch { /* ignore */ }
        _plugin = null;
        _params = null;
        _gui = null;

        FreeScratch();
        if (_host != null) { ClapApi.FreeHost(_host); _host = null; }
        if (_selfHandle.IsAllocated) _selfHandle.Free();
        _module?.Dispose();
        _module = null;
        _loaded = false;
        _activated = false;
    }

    private void FreeScratch()
    {
        FreeChannels(ref _outData, _outChannels);
        FreeChannels(ref _inData, _inChannels);
        if (_outBuf != null) { Marshal.FreeHGlobal((nint)_outBuf); _outBuf = null; }
        if (_inBuf != null) { Marshal.FreeHGlobal((nint)_inBuf); _inBuf = null; }
        if (_eventBuffer != null) { Marshal.FreeHGlobal((nint)_eventBuffer); _eventBuffer = null; }
        if (_inCtx != null) { Marshal.FreeHGlobal((nint)_inCtx); _inCtx = null; }
        if (_inEvents != null) { Marshal.FreeHGlobal((nint)_inEvents); _inEvents = null; }
        if (_outEvents != null) { Marshal.FreeHGlobal((nint)_outEvents); _outEvents = null; }
    }

    private static void FreeChannels(ref float** data, int channels)
    {
        if (data == null) return;
        for (var c = 0; c < channels; c++) if (data[c] != null) Marshal.FreeHGlobal((nint)data[c]);
        Marshal.FreeHGlobal((nint)data);
        data = null;
    }

    private enum PendingKind : byte { NoteOn, NoteOff, Param }

    private readonly record struct Pending(PendingKind Kind, short Key, double Value, uint ParamId);
}
