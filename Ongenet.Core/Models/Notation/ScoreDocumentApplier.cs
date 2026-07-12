using System.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Models.Notation;

/// <summary>Writes edited score notes back into project MIDI clips.</summary>
public static class ScoreDocumentApplier
{
    public static void ApplyToProject(Project project, ScoreDocument score)
    {
        foreach (var staff in score.Staves)
        {
            var track = project.Tracks.FirstOrDefault(t => t.Id == staff.TrackId);
            if (track is null) continue;

            foreach (var clip in track.Clips.Where(c => !c.IsAudio).ToList())
                track.Clips.Remove(clip);

            if (staff.Notes.Count == 0) continue;

            var groups = staff.Notes
                .GroupBy(n => System.Math.Floor(n.StartBeat / 4.0) * 4.0)
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                var start = group.Key;
                var clip = new Clip
                {
                    Name = "Notation",
                    StartBeat = start,
                    LengthBeats = System.Math.Max(1, group.Max(n => n.StartBeat + n.LengthBeats) - start),
                    IsAudio = false
                };

                foreach (var n in group)
                {
                    clip.Notes.Add(new MidiNote
                    {
                        Note = n.Pitch,
                        StartBeat = n.StartBeat - start,
                        LengthBeats = n.LengthBeats,
                        Velocity = (float)(n.Velocity / 127.0)
                    });
                }

                track.Clips.Add(clip);
            }
        }
    }

    public static void Transpose(ScoreDocument score, int semitones)
    {
        foreach (var staff in score.Staves)
        {
            foreach (var note in staff.Notes)
                note.Pitch = System.Math.Clamp(note.Pitch + semitones, 0, 127);
        }
    }
}
