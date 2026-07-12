using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels.Panels
{
    /// <summary>Project-level groove pool and MPE settings.</summary>
    public class GrooveSettingsViewModel : ViewModelBase
    {
        private readonly IProjectService _project;
        private readonly IEventAggregator _events;
        private readonly IHistoryService _history;
        private readonly ISelectionService _selection;

        private static readonly GrooveTemplate[] FactoryGrooves =
        {
            new() { Id = Guid.Parse("11111111-1111-1111-1111-111111111101"), Name = "Straight 16", SwingAmount = 0.5, Division = 16 },
            new() { Id = Guid.Parse("11111111-1111-1111-1111-111111111102"), Name = "Light Swing", SwingAmount = 0.58, Division = 16 },
            new() { Id = Guid.Parse("11111111-1111-1111-1111-111111111103"), Name = "Heavy Swing", SwingAmount = 0.72, Division = 16 },
            new() { Id = Guid.Parse("11111111-1111-1111-1111-111111111104"), Name = "Shuffle 8", SwingAmount = 0.67, Division = 8 }
        };

        public GrooveSettingsViewModel(IProjectService project, IEventAggregator events, IHistoryService history,
            ISelectionService selection)
        {
            _project = project;
            _events = events;
            _history = history;
            _selection = selection;
            _project.ProjectChanged += Refresh;
            _selection.SelectionChanged += () => Dispatcher.UIThread.Post(Refresh);
            OpenDrumMapEditorCommand = new RelayCommand(OpenDrumMapEditor);
            ImportGrooveCommand = new RelayCommand(() => _ = ImportGrooveAsync());
            ExtractGrooveCommand = new RelayCommand(ExtractGroove, () => CanExtractGroove);
            Refresh();
        }

        public RelayCommand OpenDrumMapEditorCommand { get; }
        public RelayCommand ImportGrooveCommand { get; }
        public RelayCommand ExtractGrooveCommand { get; }

        public Func<System.Threading.Tasks.Task<string?>>? PickGroovePathAsync { get; set; }

        public IReadOnlyList<GrooveTemplate> GroovePool =>
            FactoryGrooves.Concat(_project.Current.UserGrooves).ToList();

        public bool CanExtractGroove =>
            _selection.SelectedClip is { IsMidi: true } && _selection.SelectedClip.Notes.Count > 0;

        public GrooveTemplate? SelectedGroove
        {
            get => _project.Current.ActiveGroove;
            set
            {
                if (ReferenceEquals(_project.Current.ActiveGroove, value)) return;
                _history.Capture("Change groove");
                _project.Current.ActiveGroove = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasActiveGroove));
                OnPropertyChanged(nameof(SwingAmount));
            }
        }

        public bool HasActiveGroove => SelectedGroove is not null;

        public double SwingAmount
        {
            get => SelectedGroove?.SwingAmount ?? 0.5;
            set
            {
                if (SelectedGroove is not { } g || Math.Abs(g.SwingAmount - value) < 1e-6) return;
                _history.Capture("Adjust swing");
                g.SwingAmount = value;
                OnPropertyChanged();
            }
        }

        public bool MpeEnabled
        {
            get => _project.Current.Mpe.Enabled;
            set
            {
                if (_project.Current.Mpe.Enabled == value) return;
                _history.Capture("Toggle MPE");
                _project.Current.Mpe.Enabled = value;
                OnPropertyChanged();
            }
        }

        public int MpeMasterChannel
        {
            get => _project.Current.Mpe.MasterChannel;
            set
            {
                if (_project.Current.Mpe.MasterChannel == value) return;
                _project.Current.Mpe.MasterChannel = value;
                OnPropertyChanged();
            }
        }

        public int MpeMemberChannelStart
        {
            get => _project.Current.Mpe.MemberChannelStart;
            set
            {
                if (_project.Current.Mpe.MemberChannelStart == value) return;
                _project.Current.Mpe.MemberChannelStart = value;
                OnPropertyChanged();
            }
        }

        public int MpeMemberChannelCount
        {
            get => _project.Current.Mpe.MemberChannelCount;
            set
            {
                if (_project.Current.Mpe.MemberChannelCount == value) return;
                _project.Current.Mpe.MemberChannelCount = value;
                OnPropertyChanged();
            }
        }

        public int DrumMapCount => _project.Current.DrumMaps.Count;

        private async System.Threading.Tasks.Task ImportGrooveAsync()
        {
            if (PickGroovePathAsync is null) return;
            var path = await PickGroovePathAsync();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            try
            {
                var file = GroovePoolService.Load(path);
                var template = GroovePoolService.ToTemplate(file);
                _history.Capture("Import groove");
                if (_project.Current.UserGrooves.Any(g => g.Name == template.Name))
                    template.Name += $" ({DateTime.Now:HHmmss})";
                _project.Current.UserGrooves.Add(template);
                _project.Current.ActiveGroove = template;
                OnPropertyChanged(nameof(GroovePool));
                Refresh();
            }
            catch
            {
                // Invalid groove file — ignore.
            }
        }

        private void ExtractGroove()
        {
            var clip = _selection.SelectedClip;
            if (clip is not { IsMidi: true } || clip.Notes.Count == 0) return;

            _history.Capture("Extract groove");
            var file = GroovePoolService.ExtractFromClip(clip, $"Extracted {clip.Name}");
            var template = GroovePoolService.ToTemplate(file);
            _project.Current.UserGrooves.Add(template);
            _project.Current.ActiveGroove = template;
            OnPropertyChanged(nameof(GroovePool));
            Refresh();
        }

        private void Refresh()
        {
            OnPropertyChanged(nameof(GroovePool));
            OnPropertyChanged(nameof(SelectedGroove));
            OnPropertyChanged(nameof(HasActiveGroove));
            OnPropertyChanged(nameof(SwingAmount));
            OnPropertyChanged(nameof(MpeEnabled));
            OnPropertyChanged(nameof(MpeMasterChannel));
            OnPropertyChanged(nameof(MpeMemberChannelStart));
            OnPropertyChanged(nameof(MpeMemberChannelCount));
            OnPropertyChanged(nameof(DrumMapCount));
            OnPropertyChanged(nameof(CanExtractGroove));
            ExtractGrooveCommand.RaiseCanExecuteChanged();
        }

        private void OpenDrumMapEditor()
        {
            var vm = App.ServiceProvider?.GetService(typeof(DrumMapEditorViewModel)) as DrumMapEditorViewModel;
            if (vm is null) return;
            vm.LoadProject(_project.Current);
            var win = new Views.Windows.DrumMapEditorWindow { DataContext = vm };
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is not null)
                win.Show(desktop.MainWindow);
            else
                win.Show();
        }
    }
}
