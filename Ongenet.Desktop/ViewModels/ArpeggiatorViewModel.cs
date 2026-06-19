using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Music;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>One selectable arp step rate (display name + length in beats).</summary>
    public sealed class ArpRate
    {
        public ArpRate(string name, double beats)
        {
            Name = name;
            Beats = beats;
        }

        public string Name { get; }
        public double Beats { get; }
        public override string ToString() => Name;
    }

    /// <summary>
    /// Backs the "Convert to arpeggio" window: turns the piano-roll's selected notes into an
    /// arpeggiated sequence (<see cref="Arpeggiator"/>) over the selection's beat span.
    /// </summary>
    public sealed class ArpeggiatorViewModel : ViewModelBase
    {
        private readonly PianoRollViewModel _pianoRoll;

        private ArpMode _selectedMode = ArpMode.Up;
        private ArpRate _selectedRate;
        private int _octaves = 1;
        private int _gatePercent = 90;
        private string _status = "Select notes in the piano roll, then Apply.";

        public ArpeggiatorViewModel(PianoRollViewModel pianoRoll)
        {
            _pianoRoll = pianoRoll;
            _selectedRate = Rates[2]; // 1/16 by default
        }

        /// <summary>All arp directions.</summary>
        public IReadOnlyList<ArpMode> Modes { get; } = (ArpMode[])Enum.GetValues(typeof(ArpMode));

        /// <summary>Selectable step rates (beats assume a quarter note = 1 beat).</summary>
        public IReadOnlyList<ArpRate> Rates { get; } = new[]
        {
            new ArpRate("1/4", 1.0),
            new ArpRate("1/8", 0.5),
            new ArpRate("1/16", 0.25),
            new ArpRate("1/32", 0.125),
            new ArpRate("1/8 triplet", 1.0 / 3.0),
            new ArpRate("1/16 triplet", 1.0 / 6.0)
        };

        public ArpMode SelectedMode { get => _selectedMode; set => SetField(ref _selectedMode, value); }
        public ArpRate SelectedRate { get => _selectedRate; set => SetField(ref _selectedRate, value); }
        public int Octaves { get => _octaves; set => SetField(ref _octaves, value); }
        public int GatePercent { get => _gatePercent; set => SetField(ref _gatePercent, value); }

        /// <summary>Status / feedback line.</summary>
        public string Status { get => _status; set => SetField(ref _status, value); }

        /// <summary>Arpeggiates the current selection and replaces it with the result.</summary>
        public void Apply()
        {
            if (!_pianoRoll.HasClip)
            {
                Status = "Select a MIDI clip first.";
                return;
            }

            var selected = _pianoRoll.SelectedNotes;
            if (selected.Count == 0)
            {
                Status = "No notes selected.";
                return;
            }

            var pitches = selected.Select(n => n.Model.Note).ToList();
            var spanStart = selected.Min(n => n.Model.StartBeat);
            var spanEnd = selected.Max(n => n.Model.EndBeat);
            var spanLength = spanEnd - spanStart;

            var notes = Arpeggiator.Arpeggiate(pitches, spanStart, spanLength, new ArpOptions
            {
                Mode = SelectedMode,
                Octaves = Octaves,
                StepBeats = SelectedRate.Beats,
                Gate = Math.Clamp(GatePercent / 100.0, 0.05, 1.0)
            });

            if (notes.Count == 0)
            {
                Status = "Nothing to arpeggiate (span too short for this rate).";
                return;
            }

            _pianoRoll.ReplaceSelectionWith(notes, "Arpeggiate");
            Status = $"Arpeggiated {selected.Count} note(s) into {notes.Count} steps.";
        }
    }
}
