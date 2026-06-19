using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Desktop.Services;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// Backs the History window: mirrors the <see cref="IHistoryService"/> timeline as a selectable list.
    /// Selecting a row jumps the project to that point (a bulk undo/redo).
    /// </summary>
    public sealed class HistoryViewModel : ViewModelBase
    {
        private readonly IHistoryService _history;
        private bool _suppress; // true while syncing the selection from the service (so it doesn't re-jump)

        public HistoryViewModel(IHistoryService history)
        {
            _history = history;
            _history.Changed += Rebuild;
            Rebuild();
        }

        /// <summary>The history timeline, oldest first.</summary>
        public ObservableCollection<HistoryItemViewModel> Items { get; } = new();

        private HistoryItemViewModel? _selectedItem;
        public HistoryItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (!SetField(ref _selectedItem, value)) return;
                if (_suppress || value is null) return;
                _history.JumpTo(value.Index); // jump to the chosen point in time
            }
        }

        private void Rebuild()
        {
            _suppress = true;
            Items.Clear();
            foreach (var e in _history.Timeline)
                Items.Add(new HistoryItemViewModel(e.Index, e.Label, e.IsCurrent));
            _selectedItem = Items.FirstOrDefault(i => i.IsCurrent);
            OnPropertyChanged(nameof(SelectedItem));
            _suppress = false;
        }
    }

    /// <summary>One row in the history list.</summary>
    public sealed class HistoryItemViewModel : ViewModelBase
    {
        public HistoryItemViewModel(int index, string label, bool isCurrent)
        {
            Index = index;
            Label = label;
            IsCurrent = isCurrent;
        }

        /// <summary>Index into the history timeline (passed back to <c>JumpTo</c>).</summary>
        public int Index { get; }

        /// <summary>The action that produced this state ("Open" for the initial state).</summary>
        public string Label { get; }

        /// <summary>True for the currently-active state (highlighted in the list).</summary>
        public bool IsCurrent { get; }
    }
}
