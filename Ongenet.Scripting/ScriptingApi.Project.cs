using System;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Music;
using Ongenet.Core.Services;

namespace Ongenet.Scripting;

public sealed partial class ScriptingApi
{
    public void ClearProject()
    {
        _history.Capture("Clear project");
        var project = new Project
        {
            Name = "Untitled",
            Tempo = new Tempo(120),
            TimeSignature = TimeSignature.FourFour,
            BarCount = 16
        };
        _project.SetCurrentProject(project);
        _transport.Tempo = project.Tempo;
        _transport.LoopStart = 0;
        _transport.LoopEnd = 16;
        _events.Publish(new TracksChangedEvent());
    }

    public void SetProjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_project.Current.Name == name) return;
        _history.Capture("Rename project");
        _project.Current.Name = name;
    }

    public void SetKeySignature(int rootPitchClass, ScriptScaleType scale)
    {
        rootPitchClass = ((rootPitchClass % 12) + 12) % 12;
        var modelScale = ScriptingApiSupport.ToModelScale(scale);
        if (_project.Current.KeyRootPitchClass == rootPitchClass && _project.Current.KeyScale == modelScale) return;
        _history.Capture("Change key signature");
        _project.Current.KeyRootPitchClass = rootPitchClass;
        _project.Current.KeyScale = modelScale;
    }

    public (int RootPitchClass, ScriptScaleType Scale) GetKeySignature() =>
        (_project.Current.KeyRootPitchClass, ScriptingApiSupport.ToScriptScale(_project.Current.KeyScale));

    public ScriptPlaybackMode GetPlaybackMode() =>
        ScriptingApiSupport.ToScriptPlayback(_project.Current.PlaybackMode);

    public void SetPlaybackMode(ScriptPlaybackMode mode)
    {
        var next = ScriptingApiSupport.ToModelPlayback(mode);
        if (_project.Current.PlaybackMode == next) return;
        _history.Capture("Change playback mode");
        _project.Current.PlaybackMode = next;
    }

    public double GetLaunchQuantizeBeats() => _project.Current.LaunchQuantizeBeats;

    public void SetLaunchQuantizeBeats(double beats)
    {
        if (beats < 0) beats = 0;
        if (Math.Abs(_project.Current.LaunchQuantizeBeats - beats) < 1e-9) return;
        _history.Capture("Change launch quantize");
        _project.Current.LaunchQuantizeBeats = beats;
    }

    public ScriptMpeSettings GetMpeSettings()
    {
        var m = _project.Current.Mpe;
        return new ScriptMpeSettings(m.Enabled, m.MasterChannel, m.MemberChannelStart, m.MemberChannelCount);
    }

    public void SetMpeSettings(ScriptMpeSettings settings)
    {
        _history.Capture("Change MPE settings");
        _project.Current.Mpe.Enabled = settings.Enabled;
        _project.Current.Mpe.MasterChannel = settings.MasterChannel;
        _project.Current.Mpe.MemberChannelStart = settings.MemberChannelStart;
        _project.Current.Mpe.MemberChannelCount = settings.MemberChannelCount;
    }

    public ScriptGrooveTemplate? GetActiveGroove()
    {
        var g = _project.Current.ActiveGroove;
        return g is null ? null : new ScriptGrooveTemplate(g.Id, g.Name, g.SwingAmount, g.Division, g.StepOffsets?.ToArray());
    }

    public void SetActiveGroove(ScriptGrooveTemplate? groove)
    {
        _history.Capture("Change active groove");
        if (groove is null)
        {
            _project.Current.ActiveGroove = null;
            return;
        }

        _project.Current.ActiveGroove = new GrooveTemplate
        {
            Id = groove.Id,
            Name = groove.Name,
            SwingAmount = groove.SwingAmount,
            Division = groove.Division
        };
        if (groove.StepOffsets is not null)
            _project.Current.ActiveGroove.StepOffsets.AddRange(groove.StepOffsets);
    }

    public void SetLoopActive(bool active)
    {
        if (active)
        {
            if (_transport.LoopEnd <= _transport.LoopStart)
                _transport.LoopEnd = _transport.LoopStart + 4;
        }
        else
        {
            _history.Capture("Disable loop");
            _transport.LoopEnd = _transport.LoopStart;
        }
    }
}
