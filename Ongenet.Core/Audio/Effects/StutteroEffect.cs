using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.Modulation;
using Ongenet.Core.Audio.Modules;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// "Stuttero" — a real-time stutter / beat-repeat performance effect in the spirit of Stutter Edit.
/// Incoming audio is captured into a buffer and chopped into tempo-synced slices (1/4 down to 1/512),
/// shaped by a drawable per-slice gate curve, and routed through a reorderable multi-FX rack
/// (tape-stop, lo-fi, comb, phaser, chorus, low-pass). Each "gesture" bundles those settings plus
/// time-variant curves (stutter-rate sweep, filter cutoff, per-module depth) and is triggered either by
/// the transport (Auto mode) or by mapped MIDI keys (MIDI mode), with a hold-to-freeze buffer.
///
/// Reuses the shared toolkit throughout: <see cref="CaptureBuffer"/>, <see cref="FxModuleRack"/>,
/// <see cref="ModulationCurve"/>, <see cref="TempoSync"/>-style beat maths, and the
/// <see cref="IMidiAwareEffect"/> seam for note triggering.
/// </summary>
public sealed class StutteroEffect : IAudioEffect, IContextualEffect, IMidiAwareEffect, IProjectStatefulComponent
{
    public const string TypeId = "stuttero";

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Stuttero";
    public bool Enabled { get; set; } = true;

    // --- Generic parameters (shown in the default panel, persisted, automatable) ---
    public int ModeIndex { get; set; }          // 0 = Auto, 1 = MIDI
    public double Mix { get; set; } = 1.0;
    public double OutputDb { get; set; }
    public bool Freeze { get; set; }

    // --- Performance model (edited via the custom UI, persisted as custom state) ---
    public List<StutterGesture> Gestures { get; } = new();
    public int[] KeyMap { get; } = new int[128];     // MIDI note → gesture index, -1 = none
    public int AutoGestureIndex { get; set; }        // gesture used by Auto mode / Freeze
    public FxModuleRack Rack { get; private set; } = FxModuleCatalog.DefaultRack();

    private static readonly string[] ModeNames = { "Auto", "MIDI" };

    private enum State { Idle, Active, Releasing }

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private long _clock;                          // absolute samples written since Prepare
    private CaptureBuffer[] _ring = Array.Empty<CaptureBuffer>();
    private float[][] _frozen = Array.Empty<float[]>();
    private float[] _wet = Array.Empty<float>();
    private int _frozenCap;

    private State _state = State.Idle;
    private int _activeGesture;
    private long _gestureStartClock;
    private int _sliceLen = 1;
    private int _slicePos;
    private long _segStartAbs;                     // slide mode: absolute start of the grabbed slice
    private int _segOffset;                        // frozen modes: offset within the snapshot
    private int _frozenLen;
    private int _heldNote = -1;
    private bool _needCapture;
    private float _releaseRamp = 1f;
    private float _releaseStep;
    private bool _wasFrozen;

    private readonly MidiEventFifo _fifo = new();
    private readonly List<MidiMessage> _drain = new();
    private FastRandom _rng = new(0x5EED1234); // mutable struct — must not be readonly
    private EffectContext? _ctx;
    private IReadOnlyList<Parameter>? _parameters;

    public StutteroEffect()
    {
        for (var i = 0; i < KeyMap.Length; i++) KeyMap[i] = -1;
        BuildDefaultGestures();
    }

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Mode", ModeNames, () => ModeIndex, v => ModeIndex = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
        new BoolParameter("Freeze", () => Freeze, v => Freeze = v),
        new FloatParameter("Output", -24.0, 24.0, () => OutputDb, v => OutputDb = v, "0.0", "dB")
    };

    public void SetContext(EffectContext context) => _ctx = context;

    public void HandleMidi(in MidiMessage message) => _fifo.Push(message);

    public void AllNotesOff()
    {
        _fifo.Clear();
        _heldNote = -1;
        _state = State.Idle;
    }

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;

        _frozenCap = (int)(_sampleRate * 8.0); // up to ~8 s of captured material (covers a bar at slow tempi)
        _ring = new CaptureBuffer[_channels];
        _frozen = new float[_channels][];
        for (var c = 0; c < _channels; c++)
        {
            _ring[c] = new CaptureBuffer();
            _ring[c].Resize(_frozenCap + 8);
            _frozen[c] = new float[_frozenCap + 8];
        }

        _clock = 0;
        _state = State.Idle;
        _heldNote = -1;
        Rack.Prepare(format);
    }

    public void Process(Span<float> buffer)
    {
        if (_ring.Length == 0) return;
        var channels = _channels;
        var frames = buffer.Length / channels;
        if (_wet.Length < buffer.Length) _wet = new float[buffer.Length];

        var spb = SamplesPerBeat();

        HandleTriggers(spb);

        var gesture = (_state != State.Idle && _activeGesture >= 0 && _activeGesture < Gestures.Count)
            ? Gestures[_activeGesture]
            : null;

        // Drive the rack's per-module modulation once per block from the gesture phase at block start.
        if (gesture is not null) ApplyModuleModulation(gesture, GesturePhase(gesture, spb));

        var active = gesture is not null;
        var renderWet = active; // we'll blend wet this block even if a release completes partway through
        var tailSamples = active ? Math.Max(1, (int)(gesture!.TailMs / 1000.0 * _sampleRate)) : 1;

        for (var f = 0; f < frames; f++)
        {
            var bi = f * channels;

            // Always feed the live ring so Slide/Lock have history available.
            for (var c = 0; c < channels; c++) _ring[c].Write(buffer[bi + c]);

            if (active)
            {
                if (_slicePos >= _sliceLen) Retrigger(gesture!, spb);

                var p = _sliceLen > 0 ? _slicePos / (double)_sliceLen : 0.0;
                var amp = (float)gesture!.Gate.Evaluate(p) * FadeWindow(_slicePos, _sliceLen, tailSamples) * _releaseRamp;

                for (var c = 0; c < channels; c++)
                    _wet[bi + c] = ReadSlice(gesture!, c) * amp;

                _slicePos++;

                if (_state == State.Releasing)
                {
                    _releaseRamp -= _releaseStep;
                    if (_releaseRamp <= 0f) { _releaseRamp = 0f; active = false; _state = State.Idle; gesture = null; }
                }
            }

            if (!active)
                for (var c = 0; c < channels; c++) _wet[bi + c] = 0f;

            _clock++;
        }

        if (!renderWet) return; // fully dry this block — leave the buffer untouched

        // Run the wet path through the rack, then blend with the dry buffer.
        var wetSpan = _wet.AsSpan(0, buffer.Length);
        Rack.Process(wetSpan);

        var mix = (float)Math.Clamp(Mix, 0, 1);
        var gain = (float)AudioMath.Db2Lin(OutputDb);
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = buffer[i] * (1f - mix) + _wet[i] * mix * gain;
    }

    // --- Triggering -----------------------------------------------------------------------------

    private void HandleTriggers(double spb)
    {
        _fifo.Drain(_drain);

        if (ModeIndex == 1) // MIDI mode
        {
            foreach (var m in _drain)
            {
                if (m.Kind == MidiMessageKind.NoteOn && m.Data2 > 0)
                {
                    var g = KeyMap[m.Note & 127];
                    if (g >= 0) StartGesture(g, spb);
                }
                else if (m.Kind == MidiMessageKind.NoteOff || (m.Kind == MidiMessageKind.NoteOn && m.Data2 == 0))
                {
                    if (m.Note == _heldNote) BeginRelease();
                }
            }
        }

        // Freeze (either mode): hold the Auto gesture for as long as it's engaged.
        if (Freeze)
        {
            if (_state == State.Idle) StartGesture(AutoGestureIndex, spb);
            return;
        }

        if (_wasFrozen && _state != State.Idle) BeginRelease(); // Freeze just turned off
        _wasFrozen = Freeze;

        if (ModeIndex == 0) // Auto mode: continuously engaged while the effect is enabled
        {
            if (_state == State.Idle || (_state == State.Active && _activeGesture != AutoGestureIndex))
            {
                StartGesture(AutoGestureIndex, spb);
            }
            else if (_state == State.Active && _activeGesture >= 0 && _activeGesture < Gestures.Count)
            {
                // Re-grab the buffer each gesture cycle so the stutter keeps tracking the incoming audio
                // instead of latching onto whatever it first captured.
                var g = Gestures[_activeGesture];
                var cycle = Math.Max(1.0, g.LengthBeats * spb);
                if (_clock - _gestureStartClock >= cycle)
                {
                    _gestureStartClock = _clock;
                    if (g.Buffer != BufferMode.Slide) _needCapture = true;
                }
            }
        }
    }

    private void StartGesture(int index, double spb)
    {
        if (index < 0 || index >= Gestures.Count) return;
        var g = Gestures[index];
        _activeGesture = index;
        _gestureStartClock = _clock;
        _slicePos = _sliceLen; // force a retrigger on the first processed sample
        _releaseRamp = 1f;
        _state = State.Active;
        _heldNote = ModeIndex == 1 ? FindKeyFor(index) : -1;
        // Frozen modes capture lazily, at the first slice that actually has audio (see Retrigger), so a
        // gesture started before any sound exists doesn't lock onto silence.
        _needCapture = g.Buffer != BufferMode.Slide;
        Rack.Reset();
    }

    private void BeginRelease()
    {
        if (_state != State.Active) return;
        _state = State.Releasing;
        var relSamples = Math.Max(1, (int)(0.012 * _sampleRate)); // ~12 ms wet fade-out, click-free
        _releaseStep = 1f / relSamples;
    }

    private void Retrigger(StutterGesture g, double spb)
    {
        var gp = GesturePhase(g, spb);
        var rateBeats = RateBeats(g, gp);
        _sliceLen = Math.Max(1, (int)Math.Round(rateBeats * spb));
        _slicePos = 0;

        switch (g.Buffer)
        {
            case BufferMode.Slide:
                // Replay the most-recent slice of live audio; re-grabbed every slice (tracks the source).
                _segStartAbs = Math.Max(0, _clock - _sliceLen);
                break;
            case BufferMode.Random:
                if (_needCapture && CaptureWindow(g, spb)) _needCapture = false;
                var room = Math.Max(0, _frozenLen - _sliceLen);
                _segOffset = room > 0 ? _rng.NextInt(room) : 0;
                break;
            default: // Lock — grab one slice of recent audio, then repeat it for the gesture.
                if (_needCapture && CaptureWindow(g, spb)) _needCapture = false;
                _segOffset = Math.Max(0, _frozenLen - _sliceLen);
                break;
        }
    }

    // Copies the most recent buffer window from the live ring into the frozen snapshot. Returns false (and
    // leaves _needCapture set, so the next slice retries) when that window is effectively silent — this is
    // what stops Lock/Random from latching onto silence if the gesture began before any sound arrived.
    private bool CaptureWindow(StutterGesture g, double spb)
    {
        var window = (int)Math.Round(Math.Max(g.BufferLengthBeats, 0.0) * spb);
        window = Math.Clamp(Math.Max(window, _sliceLen), 1, _frozenCap);
        _frozenLen = window;

        var peak = 0f;
        for (var c = 0; c < _channels; c++)
        {
            _ring[c].Snapshot(_frozen[c], window);
            for (var i = 0; i < window; i++)
            {
                var a = _frozen[c][i];
                if (a < 0) a = -a;
                if (a > peak) peak = a;
            }
        }

        return peak > 1e-4f;
    }

    private float ReadSlice(StutterGesture g, int channel)
    {
        if (g.Buffer == BufferMode.Slide)
            return _ring[channel].ReadAbs(_segStartAbs + _slicePos);

        var idx = _segOffset + _slicePos;
        if (idx >= _frozenLen) idx = _frozenLen - 1;
        if (idx < 0) idx = 0;
        return _frozen[channel][idx];
    }

    // --- Modulation -----------------------------------------------------------------------------

    private void ApplyModuleModulation(StutterGesture g, double gp)
    {
        foreach (var module in Rack.Active)
        {
            ModulationCurve? curve;
            if (module.Id == LowPassModule.ModuleId && g.Cutoff is not null) curve = g.Cutoff;
            else g.ModuleCurves.TryGetValue(module.Id, out curve);

            module.ModulationOverride = curve is null ? null : curve.Evaluate(gp);
        }
    }

    private double GesturePhase(StutterGesture g, double spb)
    {
        var len = Math.Max(1.0, g.LengthBeats * spb);
        var t = (_clock - _gestureStartClock) / len;
        return t - Math.Floor(t);
    }

    private double RateBeats(StutterGesture g, double gp)
    {
        if (g.Rate is { } rc)
        {
            var v = Math.Clamp(rc.Evaluate(gp), 0, 1);
            var idx = (int)Math.Round(g.RateMinIndex + v * (g.RateMaxIndex - g.RateMinIndex));
            return StutterRates.BeatsFor(idx);
        }

        return StutterRates.BeatsFor(g.RateIndex);
    }

    private double SamplesPerBeat()
    {
        var bpm = _ctx?.Bpm ?? 120.0;
        if (bpm <= 0) bpm = 120.0;
        return _sampleRate * 60.0 / bpm;
    }

    private int FindKeyFor(int gestureIndex)
    {
        for (var n = 0; n < KeyMap.Length; n++) if (KeyMap[n] == gestureIndex) return n;
        return -1;
    }

    // A short fade-in plus a tail-length fade-out, applied within each slice to suppress clicks.
    private static float FadeWindow(int pos, int len, int tail)
    {
        const int fadeIn = 24;
        var a = 1f;
        if (pos < fadeIn) a *= pos / (float)fadeIn;
        var fromEnd = len - 1 - pos;
        if (fromEnd < tail) a *= Math.Max(0, fromEnd) / (float)tail;
        return a;
    }

    private void BuildDefaultGestures()
    {
        // A handful of ready-to-play gestures mapped to C3..F3 (notes 60..63) for MIDI mode.
        StutterGesture Make(string name, int rate, BufferMode buf, int gateShape)
        {
            var g = new StutterGesture { Name = name, RateIndex = rate, Buffer = buf };
            g.Gate.Set(CurveShapes.All[gateShape].Build());
            return g;
        }

        // Default gates have audible shape (decay/gaps) so even Slide chops rather than passing through.
        Gestures.Add(Make("1/16 Lock", 2, BufferMode.Lock, 4));     // Exp Down — percussive repeats
        Gestures.Add(Make("1/32 Gate", 3, BufferMode.Lock, 10));    // Gate 1/4
        Gestures.Add(Make("Random 1/16", 2, BufferMode.Random, 5)); // Triangle
        Gestures.Add(Make("Slide 1/8", 1, BufferMode.Slide, 7));    // Pulse — gaps so Slide audibly chops

        for (var i = 0; i < 4; i++) KeyMap[60 + i] = i;
    }

    public IAudioEffect Clone()
    {
        var clone = new StutteroEffect
        {
            Enabled = Enabled,
            ModeIndex = ModeIndex,
            Mix = Mix,
            OutputDb = OutputDb,
            Freeze = Freeze,
            AutoGestureIndex = AutoGestureIndex
        };
        clone.Gestures.Clear();
        foreach (var g in Gestures) clone.Gestures.Add(g.Clone());
        Array.Copy(KeyMap, clone.KeyMap, KeyMap.Length);
        clone.Rack = Rack.Clone();
        return clone;
    }

    // --- Persistence (gestures, key map, rack — beyond the generic parameters) ------------------

    public void WriteProjectState(OngenWriter writer)
    {
        writer.WriteInt(AutoGestureIndex);

        writer.WriteInt(Gestures.Count);
        foreach (var g in Gestures) writer.WriteChunk(w => WriteGesture(w, g));

        // Key map: only the assigned entries.
        var assigned = new List<int>();
        for (var n = 0; n < KeyMap.Length; n++) if (KeyMap[n] >= 0) assigned.Add(n);
        writer.WriteInt(assigned.Count);
        foreach (var n in assigned) { writer.WriteInt(n); writer.WriteInt(KeyMap[n]); }

        // Rack order + per-module enable/params.
        writer.WriteInt(Rack.Modules.Count);
        foreach (var m in Rack.Modules)
            writer.WriteChunk(w =>
            {
                w.WriteString(m.Id);
                w.WriteBool(m.Enabled);
                WriteParams(w, m.Parameters);
            });
    }

    public void ReadProjectState(OngenReader reader)
    {
        AutoGestureIndex = reader.ReadInt();

        var gCount = reader.ReadInt();
        Gestures.Clear();
        for (var i = 0; i < gCount; i++)
        {
            StutterGesture g = new();
            reader.ReadChunk(r => g = ReadGesture(r));
            Gestures.Add(g);
        }

        for (var i = 0; i < KeyMap.Length; i++) KeyMap[i] = -1;
        var assigned = reader.ReadInt();
        for (var i = 0; i < assigned; i++)
        {
            var n = reader.ReadInt();
            var g = reader.ReadInt();
            if (n >= 0 && n < KeyMap.Length) KeyMap[n] = g;
        }

        var modCount = reader.ReadInt();
        var rebuilt = new FxModuleRack();
        for (var i = 0; i < modCount; i++)
        {
            reader.ReadChunk(r =>
            {
                var id = r.ReadString();
                var enabled = r.ReadBool();
                var module = FxModuleCatalog.Create(id);
                if (module is null) { ReadParams(r, null); return; }
                module.Enabled = enabled;
                ReadParams(r, module.Parameters);
                rebuilt.Modules.Add(module);
            });
        }

        if (rebuilt.Modules.Count > 0) { rebuilt.Commit(); Rack = rebuilt; }
    }

    private static void WriteGesture(OngenWriter w, StutterGesture g)
    {
        w.WriteString(g.Name);
        w.WriteDouble(g.LengthBeats);
        w.WriteInt(g.RateIndex);
        w.WriteInt(g.RateMinIndex);
        w.WriteInt(g.RateMaxIndex);
        w.WriteInt((int)g.Buffer);
        w.WriteDouble(g.BufferLengthBeats);
        w.WriteDouble(g.TailMs);
        WriteCurve(w, g.Gate);
        WriteOptionalCurve(w, g.Rate);
        WriteOptionalCurve(w, g.Cutoff);
        w.WriteInt(g.ModuleCurves.Count);
        foreach (var (id, curve) in g.ModuleCurves) { w.WriteString(id); WriteCurve(w, curve); }
    }

    private static StutterGesture ReadGesture(OngenReader r)
    {
        var g = new StutterGesture
        {
            Name = r.ReadString(),
            LengthBeats = r.ReadDouble(),
            RateIndex = r.ReadInt(),
            RateMinIndex = r.ReadInt(),
            RateMaxIndex = r.ReadInt(),
            Buffer = (BufferMode)r.ReadInt(),
            BufferLengthBeats = r.ReadDouble(),
            TailMs = r.ReadDouble()
        };
        g.Gate = ReadCurve(r);
        g.Rate = ReadOptionalCurve(r);
        g.Cutoff = ReadOptionalCurve(r);
        var mc = r.ReadInt();
        for (var i = 0; i < mc; i++)
        {
            var id = r.ReadString();
            g.ModuleCurves[id] = ReadCurve(r);
        }

        return g;
    }

    private static void WriteOptionalCurve(OngenWriter w, ModulationCurve? c)
    {
        w.WriteBool(c is not null);
        if (c is not null) WriteCurve(w, c);
    }

    private static ModulationCurve? ReadOptionalCurve(OngenReader r) => r.ReadBool() ? ReadCurve(r) : null;

    private static void WriteCurve(OngenWriter w, ModulationCurve c)
    {
        w.WriteBool(c.Palindrome);
        w.WriteInt(c.QuantizeSteps);
        w.WriteInt(c.Points.Count);
        foreach (var p in c.Points) { w.WriteDouble(p.Beat); w.WriteDouble(p.Value); w.WriteDouble(p.Curve); }
    }

    private static ModulationCurve ReadCurve(OngenReader r)
    {
        var c = new ModulationCurve { Palindrome = r.ReadBool(), QuantizeSteps = r.ReadInt() };
        var n = r.ReadInt();
        for (var i = 0; i < n; i++) c.Points.Add(new AutomationPoint(r.ReadDouble(), r.ReadDouble(), r.ReadDouble()));
        c.Sort();
        return c;
    }

    private static void WriteParams(OngenWriter w, IReadOnlyList<Parameter> parameters)
    {
        w.WriteInt(parameters.Count);
        foreach (var p in parameters)
        {
            switch (p)
            {
                case FloatParameter f: w.WriteInt(0); w.WriteDouble(f.Value); break;
                case BoolParameter b: w.WriteInt(1); w.WriteBool(b.Value); break;
                case ChoiceParameter ch: w.WriteInt(2); w.WriteInt(ch.SelectedIndex); break;
                default: w.WriteInt(-1); break;
            }
        }
    }

    private static void ReadParams(OngenReader r, IReadOnlyList<Parameter>? parameters)
    {
        var count = r.ReadInt();
        for (var i = 0; i < count; i++)
        {
            var kind = r.ReadInt();
            switch (kind)
            {
                case 0: { var v = r.ReadDouble(); if (parameters is not null && i < parameters.Count && parameters[i] is FloatParameter f) f.Value = v; break; }
                case 1: { var v = r.ReadBool(); if (parameters is not null && i < parameters.Count && parameters[i] is BoolParameter b) b.Value = v; break; }
                case 2: { var v = r.ReadInt(); if (parameters is not null && i < parameters.Count && parameters[i] is ChoiceParameter c) c.SelectedIndex = v; break; }
            }
        }
    }
}
