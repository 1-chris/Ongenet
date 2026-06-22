using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Music;

namespace Ongenet.App.ViewModels
{
    /// <summary>
    /// Backs the MIDI Generator window: builds randomized diatonic chord progressions
    /// (<see cref="ChordProgressionGenerator"/>) and inserts/replaces them into the piano-roll clip.
    /// Generation is stored so "Insert"/"Replace" apply exactly what "Generate" produced.
    /// </summary>
    public sealed class MidiGeneratorViewModel : ViewModelBase
    {
        private readonly PianoRollViewModel _pianoRoll;
        private IReadOnlyList<MidiNote> _generated = Array.Empty<MidiNote>();

        private int _rootIndex;
        private ScaleType _selectedScale = ScaleType.Major;
        private int _octave = 4;
        private int _chordCount = 4;
        private double _chordLengthBeats = 4.0;
        private bool _allowTriads = true;
        private bool _allowSevenths;
        private bool _randomInversions;
        private bool _addMelody;
        private ArpRate _melodyRate;
        private int _melodyOctave = 1;
        private int _melodyDensityPercent = 75;
        private string _status = "Choose a key and press Generate.";

        public MidiGeneratorViewModel(PianoRollViewModel pianoRoll)
        {
            _pianoRoll = pianoRoll;
            _melodyRate = MelodyRates[1]; // 1/8 by default
        }

        /// <summary>Pitch-class names for the root combo (0 = C).</summary>
        public IReadOnlyList<string> RootNotes { get; } = MusicTheory.NoteNames;

        /// <summary>All scales/modes for the scale combo.</summary>
        public IReadOnlyList<ScaleType> Scales { get; } = (ScaleType[])Enum.GetValues(typeof(ScaleType));

        public int RootIndex { get => _rootIndex; set => SetField(ref _rootIndex, value); }
        public ScaleType SelectedScale { get => _selectedScale; set => SetField(ref _selectedScale, value); }
        public int Octave { get => _octave; set => SetField(ref _octave, value); }
        public int ChordCount { get => _chordCount; set => SetField(ref _chordCount, value); }
        public double ChordLengthBeats { get => _chordLengthBeats; set => SetField(ref _chordLengthBeats, value); }
        public bool AllowTriads { get => _allowTriads; set => SetField(ref _allowTriads, value); }
        public bool AllowSevenths { get => _allowSevenths; set => SetField(ref _allowSevenths, value); }
        public bool RandomInversions { get => _randomInversions; set => SetField(ref _randomInversions, value); }

        /// <summary>Overlay a chord-aware melody line on top of the generated chords.</summary>
        public bool AddMelody { get => _addMelody; set => SetField(ref _addMelody, value); }

        /// <summary>Selectable melody step rates.</summary>
        public IReadOnlyList<ArpRate> MelodyRates { get; } = new[]
        {
            new ArpRate("1/4", 1.0),
            new ArpRate("1/8", 0.5),
            new ArpRate("1/16", 0.25),
            new ArpRate("1/8 triplet", 1.0 / 3.0)
        };

        public ArpRate MelodyRate { get => _melodyRate; set => SetField(ref _melodyRate, value); }

        /// <summary>Octaves above the chords the melody sits in.</summary>
        public int MelodyOctave { get => _melodyOctave; set => SetField(ref _melodyOctave, value); }

        /// <summary>How busy the melody is (probability a weak step sounds), 10..100%.</summary>
        public int MelodyDensityPercent { get => _melodyDensityPercent; set => SetField(ref _melodyDensityPercent, value); }

        /// <summary>Status / feedback line shown at the bottom of the window.</summary>
        public string Status { get => _status; set => SetField(ref _status, value); }

        private double TotalLengthBeats => Math.Max(0, ChordCount) * Math.Max(0, ChordLengthBeats);

        /// <summary>Builds a fresh progression (plus an optional melody) and stores it for insert/replace.</summary>
        public void Generate()
        {
            var options = new ChordGenOptions
            {
                RootPitchClass = RootIndex,
                Octave = Octave,
                Scale = SelectedScale,
                ChordCount = ChordCount,
                ChordLengthBeats = ChordLengthBeats,
                AllowTriads = AllowTriads,
                AllowSevenths = AllowSevenths,
                RandomInversions = RandomInversions
            };

            // Generate the chords once, then overlay a melody over those same chords (so the melody
            // matches the harmony rather than a separately-rolled progression).
            var chords = ChordProgressionGenerator.GenerateChords(options);
            var notes = new List<MidiNote>(ChordProgressionGenerator.Flatten(chords, options.Velocity));

            var melodyCount = 0;
            if (AddMelody && chords.Count > 0)
            {
                var keyRoot = MusicTheory.KeyRoot(RootIndex, Octave);
                var melody = MelodyGenerator.Generate(chords, keyRoot, SelectedScale, new MelodyOptions
                {
                    StepBeats = MelodyRate.Beats,
                    OctaveOffset = MelodyOctave,
                    Density = MelodyDensityPercent / 100.0
                });
                notes.AddRange(melody);
                melodyCount = melody.Count;
            }

            _generated = notes;

            if (_generated.Count == 0)
                Status = "Nothing generated — check the settings.";
            else if (melodyCount > 0)
                Status = $"Generated {_generated.Count - melodyCount} chord notes + {melodyCount} melody notes — Insert or Replace.";
            else
                Status = $"Generated {_generated.Count} notes — Insert or Replace to apply.";
        }

        /// <summary>Adds the generated notes to the clip without removing existing ones.</summary>
        public void Insert()
        {
            if (!Ensure()) return;
            _pianoRoll.InsertNotes(_generated, "Generate chords");
            Status = $"Inserted {_generated.Count} notes.";
        }

        /// <summary>Replaces all notes in the clip with the generated progression.</summary>
        public void Replace()
        {
            if (!Ensure()) return;
            _pianoRoll.ReplaceNotes(_generated, TotalLengthBeats, "Generate chords");
            Status = $"Replaced clip with {_generated.Count} notes.";
        }

        // Generates on demand if needed and verifies a clip is bound; returns false (with a status
        // message) if there's nothing to apply.
        private bool Ensure()
        {
            if (!_pianoRoll.HasClip)
            {
                Status = "Select a MIDI clip first.";
                return false;
            }

            if (_generated.Count == 0) Generate();
            return _generated.Count > 0;
        }
    }
}
