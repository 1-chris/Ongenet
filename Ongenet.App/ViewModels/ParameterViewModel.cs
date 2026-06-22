using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Parameters;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels
{
    /// <summary>Base view model for one editable instrument/effect parameter.</summary>
    public abstract class ParameterViewModel : ViewModelBase
    {
        protected ParameterViewModel(string name) => Name = name;

        /// <summary>Undo history, resolved on demand (these VMs are created by a factory, not DI).</summary>
        private protected static IHistoryService? History => App.ServiceProvider?.GetService<IHistoryService>();

        public string Name { get; }

        /// <summary>
        /// Re-reads the underlying parameter and raises change notifications, so a value written
        /// directly to the model (e.g. by automation during playback) is reflected in the bound control.
        /// </summary>
        public virtual void Refresh() { }

        /// <summary>Wraps a core <see cref="Parameter"/> in the matching view model.</summary>
        public static ParameterViewModel Create(Parameter parameter) => parameter switch
        {
            FloatParameter f => new FloatParameterViewModel(f),
            ChoiceParameter c => new ChoiceParameterViewModel(c),
            BoolParameter b => new BoolParameterViewModel(b),
            _ => new FloatParameterViewModel(new FloatParameter(parameter.Name, 0, 1, () => 0, _ => { }))
        };
    }

    /// <summary>A numeric parameter (rotary knob, with optional unit + skewed curve).</summary>
    public sealed class FloatParameterViewModel : ParameterViewModel
    {
        private readonly FloatParameter _parameter;

        public FloatParameterViewModel(FloatParameter parameter) : base(parameter.Name) => _parameter = parameter;

        /// <summary>The underlying core parameter (used to build an automation target).</summary>
        public FloatParameter Parameter => _parameter;

        public double Min => _parameter.Min;
        public double Max => _parameter.Max;
        public double Skew => _parameter.Skew;

        public double Value
        {
            get => _parameter.Value;
            set
            {
                if (_parameter.Value == value) return;
                _parameter.Value = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ValueText));
            }
        }

        public string ValueText
        {
            get
            {
                var text = _parameter.Value.ToString(_parameter.Format);
                return string.IsNullOrEmpty(_parameter.Unit) ? text : $"{text} {_parameter.Unit}";
            }
        }

        public override void Refresh()
        {
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(ValueText));
        }
    }

    /// <summary>A two-state parameter (checkbox).</summary>
    public sealed class BoolParameterViewModel : ParameterViewModel
    {
        private readonly BoolParameter _parameter;

        public BoolParameterViewModel(BoolParameter parameter) : base(parameter.Name) => _parameter = parameter;

        /// <summary>The underlying core parameter (used to build an automation target).</summary>
        public BoolParameter Parameter => _parameter;

        public bool Value
        {
            get => _parameter.Value;
            set
            {
                if (_parameter.Value == value) return;
                History?.Capture("Toggle parameter");
                _parameter.Value = value;
                OnPropertyChanged();
            }
        }

        public override void Refresh() => OnPropertyChanged(nameof(Value));
    }

    /// <summary>A discrete-choice parameter (combo box).</summary>
    public sealed class ChoiceParameterViewModel : ParameterViewModel
    {
        private readonly ChoiceParameter _parameter;

        public ChoiceParameterViewModel(ChoiceParameter parameter) : base(parameter.Name) => _parameter = parameter;

        public IReadOnlyList<string> Options => _parameter.Options;

        public int SelectedIndex
        {
            get => _parameter.SelectedIndex;
            set
            {
                if (_parameter.SelectedIndex == value) return;
                History?.Capture("Change parameter");
                _parameter.SelectedIndex = value;
                OnPropertyChanged();
            }
        }
    }
}
