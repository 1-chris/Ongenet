using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Draws a miniature of a MIDI clip's notes inside the arrange-view clip. Like
    /// <see cref="WaveformControl"/>, it renders directly; an incrementing <see cref="Revision"/>
    /// lets the view model force a repaint when notes change in the piano roll.
    /// </summary>
    public sealed class MidiNoteMiniControl : Control
    {
        /// <summary>The notes to draw (clip-relative positions).</summary>
        public static readonly StyledProperty<IReadOnlyList<MidiNote>?> NotesProperty =
            AvaloniaProperty.Register<MidiNoteMiniControl, IReadOnlyList<MidiNote>?>(nameof(Notes));

        /// <summary>The clip length in beats (the horizontal extent the notes map into).</summary>
        public static readonly StyledProperty<double> ClipLengthBeatsProperty =
            AvaloniaProperty.Register<MidiNoteMiniControl, double>(nameof(ClipLengthBeats));

        /// <summary>Bump to force a repaint when the notes list mutates in place.</summary>
        public static readonly StyledProperty<int> RevisionProperty =
            AvaloniaProperty.Register<MidiNoteMiniControl, int>(nameof(Revision));

        /// <summary>Brush for the note blocks.</summary>
        public static readonly StyledProperty<IBrush?> FillProperty =
            AvaloniaProperty.Register<MidiNoteMiniControl, IBrush?>(nameof(Fill));

        // Default pitch window shown when notes don't span much (keeps tiny clips readable).
        private const int DefaultLowNote = 48;
        private const int DefaultHighNote = 84;

        static MidiNoteMiniControl()
        {
            AffectsRender<MidiNoteMiniControl>(NotesProperty, ClipLengthBeatsProperty, RevisionProperty, FillProperty);
        }

        public IReadOnlyList<MidiNote>? Notes
        {
            get => GetValue(NotesProperty);
            set => SetValue(NotesProperty, value);
        }

        public double ClipLengthBeats
        {
            get => GetValue(ClipLengthBeatsProperty);
            set => SetValue(ClipLengthBeatsProperty, value);
        }

        public int Revision
        {
            get => GetValue(RevisionProperty);
            set => SetValue(RevisionProperty, value);
        }

        public IBrush? Fill
        {
            get => GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            var notes = Notes;
            if (notes is null || notes.Count == 0) return;

            var width = Bounds.Width;
            var height = Bounds.Height;
            var length = ClipLengthBeats;
            if (width < 1 || height < 1 || length <= 0) return;

            // Pitch window: at least the default range, expanded to fit any out-of-range notes.
            var low = DefaultLowNote;
            var high = DefaultHighNote;
            foreach (var note in notes)
            {
                if (note.Note < low) low = note.Note;
                if (note.Note > high) high = note.Note;
            }

            var span = high - low + 1;
            var rowHeight = height / span;
            var brush = Fill ?? Brushes.Black;

            foreach (var note in notes)
            {
                var x = note.StartBeat / length * width;
                var w = note.LengthBeats / length * width;
                if (w < 1) w = 1;

                // Row 0 = highest note at the top.
                var row = high - note.Note;
                var y = row * rowHeight;
                var h = rowHeight < 1.5 ? 1.5 : rowHeight - 0.5;

                context.FillRectangle(brush, new Rect(x, y, w, h));
            }
        }
    }
}
