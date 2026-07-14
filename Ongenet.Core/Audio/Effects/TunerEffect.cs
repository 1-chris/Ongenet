using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Music;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Pass-through tuner: detects pitch with <see cref="PitchDetector"/> and exposes
/// <see cref="DetectedNote"/> for display. Audio is unchanged.
/// </summary>
public sealed class TunerEffect : IAudioEffect
{
    public const string TypeId = "tuner";

    string IAudioEffect.TypeId => TypeId;

    private const int DetectHop = 256;

    public bool Enabled { get; set; } = true;

    public double ReferenceHz { get; set; } = 440.0;

    /// <summary>Last detected note name (e.g. "A4"), or empty when unvoiced.</summary>
    public string DetectedNote { get; private set; } = "";

    /// <summary>Last detected fundamental in Hz (0 when unvoiced).</summary>
    public double DetectedHz { get; private set; }

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private readonly PitchDetector _detector = new();
    private int _sinceDetect;

    public string Name => "Tuner";

    public IReadOnlyList<Parameter> Parameters { get; } = Array.Empty<Parameter>();

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _detector.Configure(_sampleRate, 70.0, 1000.0);
        _sinceDetect = 0;
        DetectedNote = "";
        DetectedHz = 0;
    }

    public IAudioEffect Clone() => new TunerEffect
    {
        Enabled = Enabled, ReferenceHz = ReferenceHz
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / channels;

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            float mono = 0;
            for (var c = 0; c < channels; c++) mono += buffer[i + c];
            mono /= channels;
            _detector.Push(mono);
        }

        _sinceDetect += frames;
        if (_sinceDetect < DetectHop) return;
        _sinceDetect = 0;

        var hz = _detector.Detect();
        DetectedHz = hz;
        if (hz <= 0)
        {
            DetectedNote = "";
            return;
        }

        var midi = 69.0 + 12.0 * Math.Log2(hz / Math.Max(1.0, ReferenceHz));
        var note = (int)Math.Round(midi);
        var pc = ((note % 12) + 12) % 12;
        var octave = note / 12 - 1;
        DetectedNote = MusicTheory.NoteNames[pc] + octave;
    }
}
