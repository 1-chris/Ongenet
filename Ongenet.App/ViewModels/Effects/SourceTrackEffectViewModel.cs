using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.Effects
{
    /// <summary>
    /// Base view model for any effect that taps another track's output (via <see cref="ISourceTrackEffect"/>
    /// and the engine's sidechain bus) — e.g. the sidechain trigger or the vocoder carrier. Exposes a
    /// source picker: a "none" entry (label supplied by the subclass) plus every track/group bar the
    /// master. Shared so each such effect doesn't re-implement the list.
    /// </summary>
    public abstract class SourceTrackEffectViewModel : EffectViewModel
    {
        private readonly ISourceTrackEffect _source;
        private bool _suppress;

        protected SourceTrackEffectViewModel(IAudioEffect effect, ISourceTrackEffect source, string noneLabel,
            Action<EffectViewModel> remove, Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
            : base(effect, remove, moveUp, moveDown)
        {
            _source = source;
            BuildSources(noneLabel);
        }

        /// <summary>The source options: a "none" entry plus every track/group that could be tapped.</summary>
        public ObservableCollection<SourceTrackOption> Sources { get; } = new();

        private SourceTrackOption? _selectedSource;
        public SourceTrackOption? SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (!SetField(ref _selectedSource, value) || _suppress) return;
                _source.SourceTrackId = value?.Id; // null id = "none"
            }
        }

        private void BuildSources(string noneLabel)
        {
            _suppress = true;
            Sources.Clear();
            Sources.Add(new SourceTrackOption(null, noneLabel));

            var project = App.ServiceProvider?.GetService<IProjectService>();
            if (project is not null)
                foreach (var t in project.Current.Tracks)
                    if (t.Kind != TrackKind.Master) // the master sums everything — not a useful source
                        Sources.Add(new SourceTrackOption(t.Id, t.Name));

            _selectedSource = Sources.FirstOrDefault(s => s.Id == _source.SourceTrackId) ?? Sources[0];
            OnPropertyChanged(nameof(SelectedSource));
            _suppress = false;
        }
    }

    /// <summary>One option in a source-track picker (null <see cref="Id"/> = the "none" entry).</summary>
    public sealed record SourceTrackOption(Guid? Id, string Name);
}
