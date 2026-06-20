using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Desktop.Services;

/// <summary>
/// Default <see cref="IMidiMappingService"/>. Mappings live on the current project; this service arms
/// "MIDI learn", binds the next CC, and applies incoming CC values to the bound parameters.
///
/// Threading: the project's mapping list and the resolved-target snapshot are only ever mutated on the
/// UI thread (learn completion from the MIDI thread is marshalled there). The MIDI thread only reads
/// the volatile snapshot to apply values — the same atomic-swap pattern the mixer uses. Applying a CC
/// writes the parameter directly from the MIDI thread, consistent with how automation already writes
/// parameters from the audio thread (last writer per block wins).
/// </summary>
public sealed class MidiMappingService : IMidiMappingService
{
    private sealed record LearnRequest(Track Owner, IAutomationTarget Target);

    private readonly IProjectService _project;
    private readonly IAutomationService _automation;
    private readonly IUiThreadDispatcher? _ui;

    private volatile MidiMapping[] _snapshot = Array.Empty<MidiMapping>();
    private volatile LearnRequest? _pending;

    public MidiMappingService(IProjectService project, IAutomationService automation, IUiThreadDispatcher? ui = null)
    {
        _project = project;
        _automation = automation;
        _ui = ui;
        _project.ProjectChanged += RebuildSnapshot;
        RebuildSnapshot();
    }

    public bool IsLearning => _pending is not null;

    public IAutomationTarget? LearnTarget => _pending?.Target;

    public IReadOnlyList<MidiMapping> Mappings => _project.Current.MidiMappings;

    public event Action? MappingsChanged;
    public event Action? LearnStateChanged;

    public void BeginLearn(Track owner, IAutomationTarget target)
    {
        _pending = new LearnRequest(owner, target);
        LearnStateChanged?.Invoke();
    }

    public void CancelLearn()
    {
        if (_pending is null) return;
        _pending = null;
        LearnStateChanged?.Invoke();
    }

    public bool HandleControlChange(MidiMessage message)
    {
        var pending = _pending;
        if (pending is not null)
        {
            // Consume this CC as the learn binding; finish on the UI thread (it mutates the model).
            _pending = null;
            var controller = message.Controller;
            Post(() =>
            {
                CompleteLearn(pending, controller);
                LearnStateChanged?.Invoke();
            });
            return true;
        }

        var snapshot = _snapshot;
        var consumed = false;
        foreach (var mapping in snapshot)
        {
            if (mapping.Controller != message.Controller) continue;
            if (mapping.Channel >= 0 && mapping.Channel != message.Channel) continue;
            var target = mapping.Target;
            if (target is not null)
            {
                target.Write(Scale(message.Value, target.Minimum, target.Maximum));
                consumed = true;
            }
        }

        return consumed;
    }

    public MidiMapping? FindMapping(Track owner, AutomationBinding binding)
        => _project.Current.MidiMappings.FirstOrDefault(m => ReferenceEquals(m.Owner, owner) && m.Binding == binding);

    public void Remove(MidiMapping mapping)
    {
        if (!_project.Current.MidiMappings.Remove(mapping)) return;
        RebuildSnapshot();
        MappingsChanged?.Invoke();
    }

    // UI thread: bind the armed target to the learned controller (replacing any existing mapping for it).
    private void CompleteLearn(LearnRequest request, int controller)
    {
        var binding = _automation.DeriveBinding(request.Owner, request.Target);
        if (binding is null) return;

        var mappings = _project.Current.MidiMappings;
        mappings.RemoveAll(m => ReferenceEquals(m.Owner, request.Owner) && m.Binding == binding);
        mappings.Add(new MidiMapping
        {
            Owner = request.Owner,
            Channel = -1, // any channel (v1)
            Controller = controller,
            Binding = binding,
            Target = request.Target,
        });

        RebuildSnapshot();
        MappingsChanged?.Invoke();
    }

    // UI thread: resolve any unresolved targets and publish a fresh snapshot for the MIDI thread to read.
    private void RebuildSnapshot()
    {
        var mappings = _project.Current.MidiMappings;
        var live = new List<MidiMapping>(mappings.Count);
        foreach (var m in mappings)
        {
            m.Target ??= ProjectFile.BuildTarget(m.Owner, (int)m.Binding.Kind, m.Binding.EffectIndex, m.Binding.ParamIndex);
            if (m.Target is not null) live.Add(m);
        }

        _snapshot = live.ToArray();
    }

    private static double Scale(int cc, double min, double max)
    {
        var t = Math.Clamp(cc / 127.0, 0.0, 1.0);
        return min + t * (max - min);
    }

    private void Post(Action action)
    {
        if (_ui is null) action();
        else _ui.Post(action);
    }
}
