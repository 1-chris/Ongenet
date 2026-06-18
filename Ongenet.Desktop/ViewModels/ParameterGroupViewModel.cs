using System.Collections.Generic;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// A titled set of parameters, rendered by the instrument inspector as one fieldset
    /// (a faint bordered box with the group name sitting on its top edge). An empty
    /// <see cref="Name"/> means the parameters are ungrouped and the title chrome is hidden.
    /// </summary>
    public sealed class ParameterGroupViewModel : ViewModelBase
    {
        public ParameterGroupViewModel(string name, IReadOnlyList<ParameterViewModel> parameters)
        {
            Name = name;
            Parameters = parameters;
        }

        public string Name { get; }

        /// <summary>The parameters in this group (same VM instances as the flat list).</summary>
        public IReadOnlyList<ParameterViewModel> Parameters { get; }

        /// <summary>Whether to draw the titled fieldset chrome.</summary>
        public bool HasTitle => !string.IsNullOrEmpty(Name);
    }
}
