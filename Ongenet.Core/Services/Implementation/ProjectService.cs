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

    /// <summary>A fresh project: one empty instrument track, ready to play.</summary>
    private Project CreateBlankProject()
    {
        var project = new Project
        {
            Name = "Untitled",
            Tempo = new Tempo(120.0),
            TimeSignature = TimeSignature.FourFour
        };

        project.Tracks.Add(new Track
        {
            Name = "Instrument 1",
            Kind = TrackKind.Instrument,
            ColorKey = "CatppuccinMauve",
            Instrument = _instruments.Create(InstrumentRegistry.DefaultInstrumentId)
        });

        return project;
    }
}
