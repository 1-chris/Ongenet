using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Desktop.ViewModels.Effects
{
    /// <summary>
    /// A <see cref="SidechainEffect"/> in the chain. Adds a source picker on top of the generic parameters:
    /// "Tempo (synced)" for the built-in tempo pump, or any track/group whose output triggers the duck.
    /// </summary>
    public sealed class SidechainEffectViewModel : EffectViewModel
    {
        private readonly SidechainEffect _fx;
        private bool _suppress;

        public SidechainEffectViewModel(SidechainEffect effect, Action<EffectViewModel> remove,
            Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
            : base(effect, remove, moveUp, moveDown)
        {
            _fx = effect;
            BuildSources();
        }

        /// <summary>The duck source: "Tempo (synced)" plus every track/group that could trigger it.</summary>
        public ObservableCollection<SidechainSourceOption> Sources { get; } = new();

        private SidechainSourceOption? _selectedSource;
        public SidechainSourceOption? SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (!SetField(ref _selectedSource, value) || _suppress) return;
                _fx.SourceTrackId = value?.Id; // null id = tempo-synced pump mode
            }
        }

        private void BuildSources()
        {
            _suppress = true;
            Sources.Clear();
            Sources.Add(new SidechainSourceOption(null, "Tempo (synced)"));

            var project = App.ServiceProvider?.GetService<IProjectService>();
            if (project is not null)
                foreach (var t in project.Current.Tracks)
                    if (t.Kind != TrackKind.Master) // the master sums everything — not a useful trigger
                        Sources.Add(new SidechainSourceOption(t.Id, t.Name));

            _selectedSource = Sources.FirstOrDefault(s => s.Id == _fx.SourceTrackId) ?? Sources[0];
            OnPropertyChanged(nameof(SelectedSource));
            _suppress = false;
        }
    }

    /// <summary>One option in the sidechain source picker (null <see cref="Id"/> = tempo-synced mode).</summary>
    public sealed record SidechainSourceOption(Guid? Id, string Name);
}
