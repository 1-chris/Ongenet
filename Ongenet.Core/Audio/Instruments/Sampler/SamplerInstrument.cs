using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>
/// "Sampler": a multi-sample, multi-format, multi-layer sound-font instrument. Loads one or more
/// <c>.sfz</c>/<c>.sf2</c> patches into stacked <see cref="SamplerLayer"/>s, maps notes to matching
/// regions across enabled layers, and plays through <see cref="SamplerVoice"/>s.
/// </summary>
public sealed class SamplerInstrument : IInstrument, IInstrumentVoiceState, IProjectStatefulComponent,
    IRuntimeCloneable, IPresetProvider
{
    public const string TypeId = "sfz";

    private const int Polyphony = 64;
    private const int StateVersion = 4;

    public static ISamplerLoadService? Loader { get; set; }

    /// <summary>Optional resolver for factory-relative preset paths (set by the app).</summary>
    public static Func<string, string?>? FactoryPathResolver { get; set; }

    private readonly object _lock = new();
    private readonly SamplerVoice[] _voices = new SamplerVoice[Polyphony];
    private readonly uint[] _startOrder = new uint[Polyphony];
    private readonly RoundRobinCounter _roundRobin = new();
    private uint _counter;

    private readonly SamplerModState _modState = new();
    private readonly HashSet<int> _heldNotes = new();
    private readonly Dictionary<int, int> _heldVelocity = new();
    private readonly HashSet<int> _sustained = new();
    private readonly HashSet<int> _heldKeyswitches = new();
    private readonly Random _rng = new();
    private int _currentKeyswitch = -1;
    private int _previousKeyswitch = -1;
    private int _lastKeyswitchVelocity = 100;
    private int _swLoKey = -1, _swHiKey = -1;
    private bool _sustainDown;
    private bool _streamsRegistered;
    private int _noteOffset;
    private int _octaveOffset;
    private readonly List<string> _lastWarnings = new();

    private volatile SamplerRegion[] _regions = Array.Empty<SamplerRegion>();
    private readonly List<SamplerLayer> _layers = new();
    private AudioFormat _format = AudioFormat.Default;

    // Legacy single-source mirrors of the first / primary layer (for UI compat).
    private string _displayName = string.Empty;

    public double MasterGain { get; set; } = 1.0;
    public double TransposeSemis { get; set; }
    public double TuneCents { get; set; }

    /// <summary>Warnings from the most recent load/add-layer operation.</summary>
    public IReadOnlyList<string> LastLoadWarnings
    {
        get { lock (_lock) return _lastWarnings.ToArray(); }
    }

    public SamplerInstrument()
    {
        for (var i = 0; i < _voices.Length; i++) _voices[i] = new SamplerVoice();
    }

    public string Name => _displayName.Length > 0 ? _displayName : "Sampler";

    public bool HasActiveVoices
    {
        get
        {
            foreach (var v in _voices)
                if (v.IsActive) return true;
            return false;
        }
    }
    string IInstrument.TypeId => TypeId;

    public IReadOnlyList<SamplerRegion> Regions => _regions;
    public IReadOnlyList<SamplerLayer> Layers
    {
        get { lock (_lock) return _layers.ToArray(); }
    }

    public int LayerCount
    {
        get { lock (_lock) return _layers.Count; }
    }

    public void ReplaceRegions(IReadOnlyList<SamplerRegion> regions)
    {
        var copy = regions as SamplerRegion[] ?? regions.ToArray();
        lock (_lock)
        {
            foreach (var v in _voices) v.Stop();
            _roundRobin.Reset();
            _regions = copy;
            ConfigureArticulation();
        }
    }

    /// <summary>Appends exported slice zones without replacing existing regions.</summary>
    public void AppendRegions(IReadOnlyList<SamplerRegion> regions)
    {
        if (regions.Count == 0) return;
        lock (_lock)
        {
            var merged = new List<SamplerRegion>(_regions);
            merged.AddRange(regions);
            foreach (var v in _voices) v.Stop();
            _roundRobin.Reset();
            _regions = merged.ToArray();
            ConfigureArticulation();
        }
    }

    /// <summary>Path of the first layer (legacy). Empty when unloaded.</summary>
    public string SourcePath
    {
        get { lock (_lock) return _layers.Count > 0 ? _layers[0].SourcePath : string.Empty; }
    }

    public SamplerFormat SourceFormat
    {
        get { lock (_lock) return _layers.Count > 0 ? _layers[0].Format : SamplerFormat.Sfz; }
    }

    public int PresetIndex
    {
        get { lock (_lock) return _layers.Count > 0 ? _layers[0].PresetIndex : -1; }
    }

    public IReadOnlyList<SamplerPresetInfo> Presets
    {
        get { lock (_lock) return _layers.Count > 0 ? _layers[0].Presets : Array.Empty<SamplerPresetInfo>(); }
    }

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Gain", 0.0, 1.0, () => MasterGain, v => MasterGain = v) { Group = "Output" },
        new FloatParameter("Transpose", -24, 24, () => TransposeSemis, v => TransposeSemis = v, "0", "st") { Group = "Pitch" },
        new FloatParameter("Tune", -100, 100, () => TuneCents, v => TuneCents = v, "0", "ct") { Group = "Pitch" }
    };

    /// <summary>Replaces all layers with a single loaded patch.</summary>
    public void ApplyLoad(SamplerLoadResult result)
    {
        var layer = SamplerLayer.FromLoad(result);
        lock (_lock)
        {
            foreach (var v in _voices) v.Stop();
            _roundRobin.Reset();
            _layers.Clear();
            _layers.Add(layer);
            ApplyControlFromLoad(result);
            RebuildRegionsUnlocked();
            RegisterStreamsIfNeeded(result.Library);
            ResetArticulationStateUnlocked();
            _modState.SeedCc(result.InitialCcValues);
            _modState.Curves = result.Curves;
            CaptureWarnings(result);
        }
    }

    /// <summary>Appends another loaded patch as a stacked layer.</summary>
    public void AddLayer(SamplerLoadResult result)
    {
        var layer = SamplerLayer.FromLoad(result);
        lock (_lock)
        {
            foreach (var v in _voices) v.Stop();
            _roundRobin.Reset();
            _layers.Add(layer);
            // Keep first layer's note offsets; merge CC seeds and curves.
            if (_layers.Count == 1) ApplyControlFromLoad(result);
            _modState.SeedCc(result.InitialCcValues);
            if (result.Curves != SamplerCurveBank.Empty) _modState.Curves = result.Curves;
            RebuildRegionsUnlocked();
            RegisterStreamsIfNeeded(result.Library);
            ResetArticulationStateUnlocked();
            _modState.SeedCc(result.InitialCcValues);
            CaptureWarnings(result);
        }
    }

    private void ApplyControlFromLoad(SamplerLoadResult result)
    {
        _noteOffset = result.NoteOffset;
        _octaveOffset = result.OctaveOffset;
    }

    private void CaptureWarnings(SamplerLoadResult result)
    {
        _lastWarnings.Clear();
        if (result.Warnings.Count > 0) _lastWarnings.AddRange(result.Warnings);
        if (result.MissingSamples.Count > 0)
            _lastWarnings.Add($"Missing samples: {string.Join(", ", result.MissingSamples)}");
    }

    public bool RemoveLayer(Guid layerId)
    {
        lock (_lock)
        {
            var idx = _layers.FindIndex(l => l.Id == layerId);
            if (idx < 0) return false;
            foreach (var v in _voices) v.Stop();
            _roundRobin.Reset();
            _layers.RemoveAt(idx);
            RebuildRegionsUnlocked();
            ResetArticulationStateUnlocked();
            return true;
        }
    }

    public bool SetLayerEnabled(Guid layerId, bool enabled)
    {
        lock (_lock)
        {
            var layer = _layers.FirstOrDefault(l => l.Id == layerId);
            if (layer is null) return false;
            layer.Enabled = enabled;
            foreach (var v in _voices) v.Stop();
            _roundRobin.Reset();
            RebuildRegionsUnlocked();
            ConfigureArticulation();
            return true;
        }
    }

    public bool SetLayerKeyMask(Guid layerId, int lo, int hi)
    {
        lock (_lock)
        {
            var layer = _layers.FirstOrDefault(l => l.Id == layerId);
            if (layer is null) return false;
            layer.KeyMaskLo = lo;
            layer.KeyMaskHi = hi;
            foreach (var v in _voices) v.Stop();
            _roundRobin.Reset();
            RebuildRegionsUnlocked();
            ConfigureArticulation();
            return true;
        }
    }

    public bool MoveLayer(Guid layerId, int newIndex)
    {
        lock (_lock)
        {
            var idx = _layers.FindIndex(l => l.Id == layerId);
            if (idx < 0 || newIndex < 0 || newIndex >= _layers.Count) return false;
            var layer = _layers[idx];
            _layers.RemoveAt(idx);
            _layers.Insert(newIndex, layer);
            RebuildRegionsUnlocked();
            return true;
        }
    }

    public bool SetLayerColor(Guid layerId, uint colorArgb)
    {
        lock (_lock)
        {
            var layer = _layers.FirstOrDefault(l => l.Id == layerId);
            if (layer is null) return false;
            layer.ColorArgb = colorArgb;
            RebuildRegionsUnlocked();
            return true;
        }
    }

    /// <summary>Reloads one SF2 layer with a different preset.</summary>
    public SamplerLoadResult? LoadLayerPreset(Guid layerId, int presetIndex, IProgress<double>? progress = null)
    {
        var loader = Loader;
        if (loader is null) return null;
        string path;
        Guid id;
        uint color;
        bool enabled;
        int maskLo, maskHi;
        string name;
        lock (_lock)
        {
            var layer = _layers.FirstOrDefault(l => l.Id == layerId);
            if (layer is null || layer.Format != SamplerFormat.Sf2) return null;
            path = layer.SourcePath;
            id = layer.Id;
            color = layer.ColorArgb;
            enabled = layer.Enabled;
            maskLo = layer.KeyMaskLo;
            maskHi = layer.KeyMaskHi;
            name = layer.Name;
        }
        var result = loader.Load(path, presetIndex, progress);
        if (result is null) return null;
        lock (_lock)
        {
            var idx = _layers.FindIndex(l => l.Id == layerId);
            if (idx < 0) return null;
            foreach (var v in _voices) v.Stop();
            _roundRobin.Reset();
            var neu = SamplerLayer.FromLoad(result, id, color);
            neu.Enabled = enabled;
            neu.KeyMaskLo = maskLo;
            neu.KeyMaskHi = maskHi;
            if (name.Length > 0) neu.Name = name;
            _layers[idx] = neu;
            RebuildRegionsUnlocked();
            RegisterStreamsIfNeeded(result.Library);
            ResetArticulationStateUnlocked();
        }
        return result;
    }

    private void RebuildRegionsUnlocked()
    {
        var list = new List<SamplerRegion>();
        foreach (var layer in _layers)
            list.AddRange(layer.FlattenedRegions());
        _regions = list.ToArray();
        UpdateDisplayNameUnlocked();
        ConfigureArticulation();
    }

    private void UpdateDisplayNameUnlocked()
    {
        if (_layers.Count == 0) { _displayName = string.Empty; return; }
        if (_layers.Count == 1) { _displayName = _layers[0].Name; return; }
        _displayName = $"{_layers.Count} layers";
    }

    private void RegisterStreamsIfNeeded(SamplerSampleLibrary? library)
    {
        if (library is null || !library.HasStreamed || _streamsRegistered) return;
        foreach (var v in _voices) SamplerStreamingEngine.Instance.Register(v.Stream);
        _streamsRegistered = true;
    }

    private void ResetArticulationStateUnlocked()
    {
        _heldNotes.Clear();
        _heldVelocity.Clear();
        _sustained.Clear();
        _heldKeyswitches.Clear();
        _sustainDown = false;
        var bpm = _modState.HostBpm;
        var curves = _modState.Curves;
        _modState.Reset();
        _modState.HostBpm = bpm;
        _modState.Curves = curves;
    }

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

    public void SetHostTempo(double bpm)
    {
        if (bpm > 0) _modState.HostBpm = bpm;
    }

    public void NoteOn(int midiNote, float velocity)
    {
        if (_regions.Length == 0) return;

        var vel = (int)(velocity * 127f + 0.5f);
        if (vel < 0) vel = 0; else if (vel > 127) vel = 127;

        lock (_lock)
        {
            var note = midiNote + _noteOffset + _octaveOffset * 12;
            if (note < 0) note = 0; else if (note > 127) note = 127;

            if (_swLoKey >= 0 && _swHiKey >= _swLoKey && note >= _swLoKey && note <= _swHiKey)
            {
                _previousKeyswitch = _currentKeyswitch;
                _currentKeyswitch = note;
                _lastKeyswitchVelocity = vel;
                _heldKeyswitches.Add(note);
                return;
            }

            var heldBefore = _heldNotes.Count;
            _heldNotes.Add(note);
            _heldVelocity[note] = vel;
            EnforcePolyphony(note);
            TriggerMatching(note, vel, attackPhase: true, heldBefore, ccTriggerOnly: false);
        }
    }

    public void NoteOff(int midiNote)
    {
        lock (_lock)
        {
            var note = midiNote + _noteOffset + _octaveOffset * 12;
            if (note < 0) note = 0; else if (note > 127) note = 127;

            if (_heldKeyswitches.Remove(note))
            {
                // sw_up regions fire on keyswitch release
                TriggerMatching(note, _lastKeyswitchVelocity, attackPhase: true, heldBefore: 0, ccTriggerOnly: false, swUpNote: note);
                return;
            }

            _heldNotes.Remove(note);
            if (_sustainDown) { _sustained.Add(note); return; }
            ReleaseNote(note);
        }
    }

    private void ReleaseNote(int midiNote)
    {
        if (!_heldVelocity.TryGetValue(midiNote, out var vel)) vel = 100;
        _heldVelocity.Remove(midiNote);

        foreach (var v in _voices)
            if (v.IsActive && v.TriggerNote == midiNote && v.Trigger != SamplerTrigger.Release) v.Release();

        TriggerMatching(midiNote, vel, attackPhase: false, heldBefore: 0, ccTriggerOnly: false);
    }

    private void TriggerMatching(int midiNote, int vel, bool attackPhase, int heldBefore,
        bool ccTriggerOnly, int swUpNote = -1)
    {
        var regions = _regions;
        var extraSemis = TransposeSemis + TuneCents / 100.0;
        var rand = _rng.NextDouble();

        Dictionary<int, int>? rr = null;
        foreach (var rt in regions)
        {
            if (rt.SeqLength <= 1 || !Eligible(rt, midiNote, vel, attackPhase, heldBefore, rand, ccTriggerOnly, swUpNote)) continue;
            rr ??= new Dictionary<int, int>();
            if (!rr.ContainsKey(rt.RoundRobinKey))
                rr[rt.RoundRobinKey] = _roundRobin.NextPosition(rt.RoundRobinKey, rt.SeqLength);
        }

        List<SamplerRegion>? toPlay = null;
        foreach (var rt in regions)
        {
            if (!Eligible(rt, midiNote, vel, attackPhase, heldBefore, rand, ccTriggerOnly, swUpNote)) continue;
            if (rt.SeqLength > 1 && rr is not null
                && rr.TryGetValue(rt.RoundRobinKey, out var pos) && rt.SeqPosition != pos) continue;
            (toPlay ??= new List<SamplerRegion>()).Add(rt);
        }

        if (toPlay is null) return;

        HashSet<int>? newGroups = null;
        foreach (var rt in toPlay)
            if (rt.Group > 0) (newGroups ??= new HashSet<int>()).Add(rt.Group);
        if (newGroups is not null)
        {
            foreach (var v in _voices)
            {
                if (!v.IsActive || v.OffBy <= 0 || !newGroups.Contains(v.OffBy)) continue;
                if (v.OffMode == SamplerOffMode.Normal) v.Release();
                else v.FastRelease();
            }
        }

        foreach (var rt in toPlay)
        {
            var index = FindFreeVoice();
            if (index < 0) index = FindOldestVoice();
            _voices[index].Start(rt, midiNote, vel, extraSemis, _modState, _format);
            _startOrder[index] = _counter++;
        }
    }

    private bool Eligible(SamplerRegion rt, int note, int vel, bool attackPhase, int heldBefore,
        double rand, bool ccTriggerOnly, int swUpNote)
    {
        if (ccTriggerOnly)
        {
            if (rt.OnCcTriggers.Count == 0) return false;
        }
        else if (rt.OnCcTriggers.Count > 0 && attackPhase && rt.Trigger == SamplerTrigger.Attack)
        {
            // on_locc regions are CC-triggered, not note-triggered (unless also matching note for layered use)
            // SFZ: regions with on_locc fire on CC and ignore note-ons for that region.
            return false;
        }

        if (!rt.Matches(note, vel) && !ccTriggerOnly && swUpNote < 0) return false;
        if (ccTriggerOnly && !rt.Matches(note, vel) && rt.LoKey == 0 && rt.HiKey == 127) { /* ok */ }
        else if (ccTriggerOnly && !rt.Matches(note, vel)) return false;

        if (_modState.Channel < rt.LoChan || _modState.Channel > rt.HiChan) return false;
        if (_modState.BendRaw < rt.LoBend || _modState.BendRaw > rt.HiBend) return false;
        if (_modState.ChannelAftertouch < rt.LoChanAft || _modState.ChannelAftertouch > rt.HiChanAft) return false;
        var polyAft = _modState.PolyAftertouch(note);
        if (polyAft < rt.LoPolyAft || polyAft > rt.HiPolyAft) return false;
        if (_modState.HostBpm < rt.LoBpm || _modState.HostBpm > rt.HiBpm) return false;
        if (_modState.Program < rt.LoProg || _modState.Program > rt.HiProg) return false;

        if (rand < rt.LoRand || rand >= rt.HiRand) return false;

        foreach (var g in rt.CcGates)
        {
            var cc = _modState.Cc[g.Cc];
            if (cc < g.Lo || cc > g.Hi) return false;
        }

        if (rt.SwLast >= 0 && _currentKeyswitch != rt.SwLast) return false;
        if (rt.SwDown >= 0 && !_heldKeyswitches.Contains(rt.SwDown)) return false;
        if (rt.SwPrevious >= 0 && _previousKeyswitch != rt.SwPrevious) return false;
        if (rt.SwVel >= 0 && _lastKeyswitchVelocity < rt.SwVel) return false;
        if (rt.SwUp >= 0)
        {
            if (swUpNote < 0 || swUpNote != rt.SwUp) return false;
        }
        else if (swUpNote >= 0)
        {
            return false; // sw_up event only triggers sw_up regions
        }

        if (ccTriggerOnly)
        {
            foreach (var t in rt.OnCcTriggers)
            {
                var cc = _modState.Cc[t.Cc];
                if (cc >= t.Lo && cc <= t.Hi) return true;
            }
            return false;
        }

        return attackPhase
            ? rt.Trigger switch
            {
                SamplerTrigger.Attack => true,
                SamplerTrigger.First => heldBefore == 0,
                SamplerTrigger.Legato => heldBefore > 0,
                _ => false
            }
            : rt.Trigger == SamplerTrigger.Release;
    }

    private void EnforcePolyphony(int note)
    {
        foreach (var rt in _regions)
        {
            if (rt.NotePolyphony <= 0 && rt.Polyphony <= 0) continue;
            if (rt.NotePolyphony > 0)
            {
                var count = 0;
                foreach (var v in _voices)
                    if (v.IsActive && v.TriggerNote == note) count++;
                if (count >= rt.NotePolyphony)
                {
                    foreach (var v in _voices)
                        if (v.IsActive && v.TriggerNote == note) { v.FastRelease(); break; }
                }
            }
        }
    }

    public void ControlChange(int controller, int value)
    {
        if (controller < 0 || controller > 127) return;
        if (value < 0) value = 0; else if (value > 127) value = 127;

        lock (_lock)
        {
            var prev = _modState.Cc[controller];
            _modState.Cc[controller] = value;

            // stop_loccN
            foreach (var v in _voices)
            {
                if (!v.IsActive) continue;
                var rt = v.Region;
                if (rt is null) continue;
                foreach (var t in rt.StopCcTriggers)
                {
                    if (t.Cc == controller && value >= t.Lo && value <= t.Hi)
                        v.FastRelease();
                }
            }

            // on_locc / start_locc — rising into range
            if (value != prev)
            {
                foreach (var rt in _regions)
                {
                    foreach (var t in rt.OnCcTriggers)
                    {
                        if (t.Cc != controller) continue;
                        var entered = value >= t.Lo && value <= t.Hi && (prev < t.Lo || prev > t.Hi);
                        if (entered)
                            TriggerMatching(60, value, attackPhase: true, heldBefore: 0, ccTriggerOnly: true);
                    }
                }
            }

            if (controller == 64)
            {
                var down = value >= 64;
                if (_sustainDown && !down)
                {
                    foreach (var n in _sustained.ToArray())
                    {
                        // honour sustain_sw on any active voice for that note
                        var allow = true;
                        foreach (var v in _voices)
                            if (v.IsActive && v.TriggerNote == n && !v.Region!.SustainSw) { allow = false; break; }
                        if (allow) ReleaseNote(n);
                    }
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
        _modState.BendRaw = value14 - 8192;
    }

    public void ChannelAftertouch(int value)
        => _modState.ChannelAftertouch = value < 0 ? 0 : value > 127 ? 127 : value;

    public void NotePressure(int midiNote, int value)
    {
        if (midiNote is < 0 or > 127) return;
        _modState.PolyAft[midiNote] = value < 0 ? 0 : value > 127 ? 127 : value;
    }

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
        var copy = new SamplerInstrument
        {
            MasterGain = MasterGain,
            TransposeSemis = TransposeSemis,
            TuneCents = TuneCents,
        };
        lock (_lock)
        {
            copy._layers.AddRange(_layers);
            copy._regions = _regions;
            copy._displayName = _displayName;
            copy.ConfigureArticulation();
        }
        return copy;
    }

    public void CopyRuntimeStateFrom(object source)
    {
        if (source is not SamplerInstrument s) return;
        lock (_lock)
        {
            _layers.Clear();
            lock (s._lock)
            {
                _layers.AddRange(s._layers);
                _regions = s._regions;
                _displayName = s._displayName;
            }
            MasterGain = s.MasterGain;
            TransposeSemis = s.TransposeSemis;
            TuneCents = s.TuneCents;
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

    public void WriteProjectState(OngenWriter writer)
    {
        writer.WriteInt(StateVersion);
        writer.WriteDouble(MasterGain);
        writer.WriteDouble(TransposeSemis);
        writer.WriteDouble(TuneCents);
        lock (_lock)
        {
            writer.WriteInt(_layers.Count);
            foreach (var layer in _layers)
            {
                writer.WriteString(layer.Id.ToString());
                writer.WriteString(layer.Name);
                writer.WriteString(layer.SourcePath);
                writer.WriteString(layer.SourceText);
                writer.WriteInt((int)layer.Format);
                writer.WriteInt(layer.PresetIndex);
                writer.WriteBool(layer.Enabled);
                writer.WriteInt(layer.KeyMaskLo);
                writer.WriteInt(layer.KeyMaskHi);
                writer.WriteInt(unchecked((int)layer.ColorArgb));
            }
        }
    }

    public void ReadProjectState(OngenReader reader)
    {
        var version = reader.ReadInt();
        if (version < 3)
        {
            ReadLegacyState(reader, version);
            return;
        }

        MasterGain = reader.ReadDouble();
        TransposeSemis = reader.ReadDouble();
        TuneCents = reader.ReadDouble();
        var count = reader.ReadInt();
        var loader = Loader;
        lock (_lock)
        {
            foreach (var v in _voices) v.Stop();
            _layers.Clear();
            for (var i = 0; i < count; i++)
            {
                var idStr = reader.ReadString();
                var name = reader.ReadString();
                var path = reader.ReadString();
                var text = reader.ReadString();
                var format = (SamplerFormat)reader.ReadInt();
                var preset = reader.ReadInt();
                var enabled = reader.ReadBool();
                var maskLo = reader.ReadInt();
                var maskHi = reader.ReadInt();
                uint color = 0;
                if (version >= 4)
                    color = unchecked((uint)reader.ReadInt());

                SamplerLoadResult? result = null;
                if (loader is not null)
                {
                    result = loader.Load(path, preset);
                    if (result is null && text.Length > 0) result = loader.LoadFromText(text, path);
                }
                if (result is null) continue;
                Guid? reuseId = Guid.TryParse(idStr, out var parsed) ? parsed : null;
                var layer = SamplerLayer.FromLoad(result, reuseId, color != 0 ? color : null);
                if (name.Length > 0) layer.Name = name;
                layer.Enabled = enabled;
                layer.KeyMaskLo = maskLo;
                layer.KeyMaskHi = maskHi;
                _layers.Add(layer);
                RegisterStreamsIfNeeded(layer.Library);
                _ = format;
            }
            RebuildRegionsUnlocked();
            ResetArticulationStateUnlocked();
        }
    }

    private void ReadLegacyState(OngenReader reader, int version)
    {
        var path = reader.ReadString();
        var text = reader.ReadString();
        var format = SamplerFormat.Sfz;
        var presetIndex = -1;
        if (version >= 2)
        {
            format = (SamplerFormat)reader.ReadInt();
            presetIndex = reader.ReadInt();
        }
        MasterGain = reader.ReadDouble();
        TransposeSemis = reader.ReadDouble();
        TuneCents = reader.ReadDouble();

        var loader = Loader;
        if (loader is null) return;
        var result = loader.Load(path, presetIndex);
        if (result is null && text.Length > 0) result = loader.LoadFromText(text, path);
        if (result is not null) ApplyLoad(result);
        _ = format;
    }

    /// <summary>Reloads the first SF2 layer with a different bank program (not an <see cref="IPresetProvider"/> stack).</summary>
    public SamplerLoadResult? LoadFirstLayerSf2Program(int presetIndex, IProgress<double>? progress = null)
    {
        Guid id;
        lock (_lock)
        {
            if (_layers.Count == 0) return null;
            id = _layers[0].Id;
        }
        return LoadLayerPreset(id, presetIndex, progress);
    }

    // --- IPresetProvider: named stacks of factory / empty patches ---

    private static readonly string[] PresetNamesList =
    {
        "Empty",
        "GM GeneralUser",
        "GM JNS",
        "VSCO Strings",
        "VCSL Kit + Piano",
    };

    public IReadOnlyList<string> PresetNames => PresetNamesList;

    public void LoadPreset(int index)
    {
        switch (index)
        {
            case 1: LoadFactoryStack("Sf2/GM/GeneralUser/GeneralUser.sf2"); break;
            case 2: LoadFactoryStack("Sf2/GM/Jnsgm2/Jnsgm2.sf2"); break;
            case 3:
                LoadFactoryStack(
                    "VSCO2CE/ViolinEnsSusVib.sfz",
                    "VSCO2CE/CelloEnsSusVib.sfz");
                break;
            case 4:
                LoadFactoryStack(
                    "VCSL/VcslAcousticKit.sfz",
                    "VCSL/Piano.sfz");
                break;
            default:
                lock (_lock)
                {
                    foreach (var v in _voices) v.Stop();
                    _layers.Clear();
                    RebuildRegionsUnlocked();
                }
                break;
        }
    }

    private void LoadFactoryStack(params string[] relativePaths)
    {
        var loader = Loader;
        if (loader is null) return;
        var results = new List<SamplerLoadResult>();
        foreach (var rel in relativePaths)
        {
            var abs = ResolveFactoryPath(rel);
            if (abs is null) continue;
            var result = loader.Load(abs);
            if (result is not null) results.Add(result);
        }
        if (results.Count == 0) return;
        ApplyLoad(results[0]);
        for (var i = 1; i < results.Count; i++) AddLayer(results[i]);
    }

    private static string? ResolveFactoryPath(string relative)
    {
        if (FactoryPathResolver is not null)
        {
            var resolved = FactoryPathResolver(relative);
            if (resolved is not null && File.Exists(resolved)) return resolved;
        }
        // Fallback: treat as absolute if it exists.
        return File.Exists(relative) ? relative : null;
    }
}
