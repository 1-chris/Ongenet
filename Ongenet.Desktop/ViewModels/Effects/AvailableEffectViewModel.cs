namespace Ongenet.Desktop.ViewModels.Effects
{
    /// <summary>An entry in the "Add effect" menu: a display name and a self-contained add command.</summary>
    public class AvailableEffectViewModel : ViewModelBase
    {
        public AvailableEffectViewModel(string name, RelayCommand add)
        {
            Name = name;
            AddCommand = add;
        }

        public string Name { get; }
        public RelayCommand AddCommand { get; }
    }
}
