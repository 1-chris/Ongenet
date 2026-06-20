using System;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>
/// Default <see cref="IProjectService"/>. Holds the current project in memory and creates new
/// blank projects. A new project starts with a single empty instrument track.
/// </summary>
public class ProjectService : IProjectService
{
    private readonly IInstrumentRegistry _instruments;

    public ProjectService(IInstrumentRegistry instruments)
    {
        _instruments = instruments;
        Current = CreateBlankProject();
    }

    public Project Current { get; private set; }

    public event Action? ProjectChanged;

    public void NewProject()
    {
        Current = CreateBlankProject();
        ProjectChanged?.Invoke();
    }

    public void SetCurrentProject(Project project)
    {
        Current = project;
        ProjectChanged?.Invoke();
    }

    /// <summary>A fresh project: the master bus plus one empty instrument track, ready to play.</summary>
    private Project CreateBlankProject()
    {
        var project = new Project
        {
            Name = "Untitled",
            Tempo = new Tempo(120.0),
            TimeSignature = TimeSignature.FourFour
        };

        // The master bus is always present and pinned at the top; every other track routes through it.
        project.Tracks.Add(new Track
        {
            Name = "Master",
            Kind = TrackKind.Master,
            ColorKey = "CatppuccinSubtext0",
            Volume = 1.0 // unity so the master is transparent by default (bus pan law is unity at centre)
        });

        // A fresh instrument track starts with an empty rack — the user drags instruments in from the
        // library (the Instrument panel shows a drop zone until then).
        project.Tracks.Add(new Track
        {
            Name = "Instrument 1",
            Kind = TrackKind.Instrument,
            ColorKey = "CatppuccinMauve"
        });

        return project;
    }
}
