using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Instruments.Sfz;

/// <summary>
/// "Sampler": a multi-sample SFZ instrument. Parses an <c>.sfz</c> patch, maps incoming notes/velocities
/// to one or more regions (layers + round-robin), and plays each through an <see cref="SfzVoice"/>.
/// Per-region opcodes live as data on the regions; only a few global macros are exposed as automatable
/// <see cref="Parameter"/>s. Sample loading is delegated to <see cref="ISfzLoadService"/> so the
/// instrument stays free of file I/O.
/// </summary>
public sealed class SfzInstrument : IInstrument, IProjectStatefulComponent, IRuntimeCloneable
{
    public const string TypeId = "sfz";

    private const int Polyphony = 64;
    private const int StateVersion = 1;

    /// <summary>
    /// App-wide loader, set at startup, used to rebuild the sampler from persisted state on project load
    /// (persistence runs without DI, and rebuilding needs the audio-file decoders).
    /// </summary>
    public static ISfzLoadService? Loader { get; set; }

    private readonly object _lock = new();
    private readonly SfzVoice[] _voices = new SfzVoice[Polyphony];
    private readonly uint[] _startOrder = new uint[Polyphony];
    private readonly RoundRobinCounter _roundRobin = new();
    private uint _counter;

    // Live MIDI control + articulation state.
    private readonly SfzModState _modState = new();
    private readonly HashSet<int> _heldNotes = new();
    private readonly Dictionary<int, int> _heldVelocity = new();
    private readonly HashSet<int> _sustained = new();   // notes released under a held sustain pedal
    private int _currentKeyswitch = -1;
    private int _swLoKey = -1, _swHiKey = -1;
    private bool _sustainDown;
    private bool _streamsRegistered;

    private volatile SfzRegionRuntime[] _regions = Array.Empty<SfzRegionRuntime>();
    private AudioFormat _format = AudioFormat.Default;

    // Persistence + display state.
    private string _sfzPath = string.Empty;
    private string _sfzText = string.Empty;
    private string _displayName = string.Empty;

    // Global macros (decision: per-region opcodes are data; only these are automatable parameters).
    public double MasterGain { get; set; } = 1.0;       // linear
    public double TransposeSemis { get; set; }          // -24..24
    public double TuneCents { get; set; }               // -100..100

    public SfzInstrument()
    {
        for (var i = 0; i < _voices.Length; i++) _voices[i] = new SfzVoice();
    }

    public string Name => _displayName.Length > 0 ? _displayName : "Sampler";
    string IInstrument.TypeId => TypeId;

    /// <summary>The loaded regions (for the zone-map UI). Empty until an SFZ is loaded.</summary>
    public IReadOnlyList<SfzRegionRuntime> Regions => _regions;

    /// <summary>Absolute path of the loaded <c>.sfz</c>, or empty.</summary>
    public string SfzPath => _sfzPath;

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Gain", 0.0, 1.0, () => MasterGain, v => MasterGain = v) { Group = "Output" },
        new FloatParameter("Transpose", -24, 24, () => TransposeSemis, v => TransposeSemis = v, "0", "st") { Group = "Pitch" },
        new FloatParameter("Tune", -100, 100, () => TuneCents, v => TuneCents = v, "0", "ct") { Group = "Pitch" }
    };

    /// <summary>Applies a freshly loaded SFZ patch to this instrument.</summary>
    public void ApplyLoad(SfzLoadResult result)
    {
        var runtimes = new List<SfzRegionRuntime>(result.Document.Regions.Count);
        foreach (var region in result.Document.Regions)
        {
            var rt = SfzRegionRuntime.Build(region, result.Library.Get(region.Sample));
            if (rt is not null) runtimes.Add(rt);
        }

        lock (_lock)
        {
            foreach (var v in _voices) v.Stop(); // hard-stop + release any open streams before swapping
            _roundRobin.Reset();
            _regions = runtimes.ToArray();
            _sfzPath = result.SfzPath;
            _sfzText = result.SfzText;
            _displayName = result.DisplayName;
            ConfigureArticulation();
            _heldNotes.Clear();
            _heldVelocity.Clear();
            _sustained.Clear();
            _sustainDown = false;
            _modState.Reset();

            // Register this (live) instrument's voice streams with the streaming engine once a patch
            // actually needs disk streaming. Snapshot clones never call ApplyLoad, so they never register.
            if (result.Library.HasStreamed && !_streamsRegistered)
            {
                foreach (var v in _voices) SfzStreamingEngine.Instance.Register(v.Stream);
                _streamsRegistered = true;
            }
        }
    }

    // Derives the key-switch zone and initial key-switch from the loaded regions.
    private void ConfigureArticulation()
    {
        _swLoKey = -1;
        _swHiKey = -1;
        _currentKeyswitch = -1;
        foreach (var rt in _regions)
        {
            if (rt.SwLoKey >= 0 && (_swLoKey < 0 || rt.SwLoKey < _swLoKey)) _swLoKey = rt.SwLoKey;
            if (rt.SwHiKey > _swHiKey) _swHiKey = rt.SwHiKey;
            if (rt.SwDefault >= 0 && _currentKeyswitch < 0) _currentKeyswitch = rt.SwDefault;
        }
    }

    public void Prepare(AudioFormat format) => _format = format;

    public void NoteOn(int midiNote, float velocity)
    {
        if (_regions.Length == 0) return;

        var vel = (int)(velocity * 127f + 0.5f);
        if (vel < 0) vel = 0; else if (vel > 127) vel = 127;

        lock (_lock)
        {
            // Notes inside the key-switch zone select an articulation instead of sounding.
            if (_swLoKey >= 0 && _swHiKey >= _swLoKey && midiNote >= _swLoKey && midiNote <= _swHiKey)
            {
                _currentKeyswitch = midiNote;
                return;
            }

            var heldBefore = _heldNotes.Count;
            _heldNotes.Add(midiNote);
            _heldVelocity[midiNote] = vel;
            TriggerMatching(midiNote, vel, attackPhase: true, heldBefore);
        }
    }

    public void NoteOff(int midiNote)
    {
        lock (_lock)
        {
            _heldNotes.Remove(midiNote);
            if (_sustainDown) { _sustained.Add(midiNote); return; } // pedal holds the note
            ReleaseNote(midiNote);
        }
    }

    // Releases a note's sustaining voices and fires any release-triggered regions. Caller holds the lock.
    private void ReleaseNote(int midiNote)
    {
        if (!_heldVelocity.TryGetValue(midiNote, out var vel)) vel = 100;
        _heldVelocity.Remove(midiNote);

        foreach (var v in _voices)
            if (v.IsActive && v.TriggerNote == midiNote && v.Trigger != SfzTrigger.Release) v.Release();

        TriggerMatching(midiNote, vel, attackPhase: false, heldBefore: 0);
    }

    // Starts every eligible region for a note event (round-robin filtered, exclusive-group cutting).
    private void TriggerMatching(int midiNote, int vel, bool attackPhase, int heldBefore)
    {
        var regions = _regions;
        var extraSemis = TransposeSemis + TuneCents / 100.0;

        // Resolve round-robin positions once per sequence group among the eligible candidates.
        Dictionary<int, int>? rr = null;
        foreach (var rt in regions)
        {
            if (rt.SeqLength <= 1 || !Eligible(rt, midiNote, vel, attackPhase, heldBefore)) continue;
            rr ??= new Dictionary<int, int>();
            if (!rr.ContainsKey(rt.RoundRobinKey))
                rr[rt.RoundRobinKey] = _roundRobin.NextPosition(rt.RoundRobinKey, rt.SeqLength);
        }

        List<SfzRegionRuntime>? toPlay = null;
        foreach (var rt in regions)
        {
            if (!Eligible(rt, midiNote, vel, attackPhase, heldBefore)) continue;
            if (rt.SeqLength > 1 && rr is not null
                && rr.TryGetValue(rt.RoundRobinKey, out var pos) && rt.SeqPosition != pos) continue;
            (toPlay ??= new List<SfzRegionRuntime>()).Add(rt);
        }

        if (toPlay is null) return;

        // Exclusive groups: cut active voices that are "off_by" any group we're about to trigger.
        HashSet<int>? newGroups = null;
        foreach (var rt in toPlay)
            if (rt.Group > 0) (newGroups ??= new HashSet<int>()).Add(rt.Group);
        if (newGroups is not null)
            foreach (var v in _voices)
                if (v.IsActive && v.OffBy > 0 && newGroups.Contains(v.OffBy)) v.FastRelease();

        foreach (var rt in toPlay)
        {
            var index = FindFreeVoice();
            if (index < 0) index = FindOldestVoice();
            _voices[index].Start(rt, midiNote, vel, extraSemis, _modState, _format);
            _startOrder[index] = _counter++;
        }
    }

    private bool Eligible(SfzRegionRuntime rt, int note, int vel, bool attackPhase, int heldBefore)
    {
        if (!rt.Matches(note, vel)) return false;
        if (rt.SwLast >= 0 && _currentKeyswitch != rt.SwLast) return false;
        return attackPhase
            ? rt.Trigger switch
            {
                SfzTrigger.Attack => true,
                SfzTrigger.First => heldBefore == 0,
                SfzTrigger.Legato => heldBefore > 0,
                _ => false
            }
            : rt.Trigger == SfzTrigger.Release;
    }

    public void ControlChange(int controller, int value)
    {
        if (controller < 0 || controller > 127) return;
        if (value < 0) value = 0; else if (value > 127) value = 127;

        lock (_lock)
        {
            _modState.Cc[controller] = value;
            if (controller == 64) // sustain pedal
            {
                var down = value >= 64;
                if (_sustainDown && !down)
                {
                    foreach (var n in _sustained.ToArray()) ReleaseNote(n);
                    _sustained.Clear();
                }
                _sustainDown = down;
            }
        }
    }

    public void PitchBend(int value14)
    {
        var bend = (value14 - 8192) / 8192.0;
        _modState.Bend = bend < -1 ? -1 : bend > 1 ? 1 : bend;
    }

    public void ChannelAftertouch(int value)
        => _modState.ChannelAftertouch = value < 0 ? 0 : value > 127 ? 127 : value;

    public void AllNotesOff()
    {
        lock (_lock)
        {
            foreach (var v in _voices) if (v.IsActive) v.Release();
            _heldNotes.Clear();
            _heldVelocity.Clear();
            _sustained.Clear();
            _sustainDown = false;
        }
    }

    public void Render(Span<float> buffer)
    {
        foreach (var v in _voices)
        {
            if (v.IsActive) v.Render(buffer);
        }

        var gain = (float)MasterGain;
        if (gain != 1f)
        {
            for (var i = 0; i < buffer.Length; i++) buffer[i] *= gain;
        }
    }

    public IInstrument Clone()
    {
        var copy = new SfzInstrument
        {
            MasterGain = MasterGain,
            TransposeSemis = TransposeSemis,
            TuneCents = TuneCents,
            _regions = _regions, // immutable runtimes + shared sample buffers
            _sfzPath = _sfzPath,
            _sfzText = _sfzText,
            _displayName = _displayName
        };
        copy.ConfigureArticulation();
        return copy;
    }

    // Snapshot clone (undo/redo): share the decoded regions by reference — never re-decode from disk.
    public void CopyRuntimeStateFrom(object source)
    {
        if (source is not SfzInstrument s) return;
        lock (_lock)
        {
            _regions = s._regions; // immutable runtimes + shared sample buffers
            _sfzPath = s._sfzPath;
            _sfzText = s._sfzText;
            _displayName = s._displayName;
            ConfigureArticulation();
        }
    }

    private int FindFreeVoice()
    {
        for (var i = 0; i < _voices.Length; i++) if (!_voices[i].IsActive) return i;
        return -1;
    }

    private int FindOldestVoice()
    {
        var oldest = 0;
        for (var i = 1; i < _voices.Length; i++)
            if (_startOrder[i] < _startOrder[oldest]) oldest = i;
        return oldest;
    }

    // --- Persistence: store the SFZ path + source text; rebuild via the loader on read. ---

    public void WriteProjectState(OngenWriter writer)
    {
        writer.WriteInt(StateVersion);
        writer.WriteString(_sfzPath);
        writer.WriteString(_sfzText);
        writer.WriteDouble(MasterGain);
        writer.WriteDouble(TransposeSemis);
        writer.WriteDouble(TuneCents);
    }

    public void ReadProjectState(OngenReader reader)
    {
        _ = reader.ReadInt(); // version
        var path = reader.ReadString();
        var text = reader.ReadString();
        MasterGain = reader.ReadDouble();
        TransposeSemis = reader.ReadDouble();
        TuneCents = reader.ReadDouble();

        _sfzPath = path;
        _sfzText = text;

        // Rebuild regions + samples. Prefer the on-disk file; fall back to the persisted text.
        var loader = Loader;
        if (loader is null || text.Length == 0) return;

        var result = loader.Load(path) ?? loader.LoadFromText(text, path);
        if (result is not null) ApplyLoad(result);
    }
}
