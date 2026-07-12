using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Ongenet.App.ViewModels.Panels;

namespace Ongenet.App.Views.Panels;

/// <summary>Picks a DataTemplate per MIDI FX slot view model type.</summary>
public sealed class MidiFxTemplateSelector : IDataTemplate
{
    public IDataTemplate? ScaleTemplate { get; set; }
    public IDataTemplate? ChordTemplate { get; set; }
    public IDataTemplate? ArpTemplate { get; set; }
    public IDataTemplate? EchoTemplate { get; set; }
    public IDataTemplate? RandomTemplate { get; set; }
    public IDataTemplate? DefaultTemplate { get; set; }

    public Control? Build(object? param)
    {
        var template = param switch
        {
            ScaleMidiEffectSlotViewModel => ScaleTemplate,
            ChordMidiEffectSlotViewModel => ChordTemplate,
            ArpMidiEffectSlotViewModel => ArpTemplate,
            NoteEchoMidiEffectSlotViewModel => EchoTemplate,
            RandomMidiEffectSlotViewModel => RandomTemplate,
            _ => DefaultTemplate
        };
        return template?.Build(param);
    }

    public bool Match(object? data) => data is MidiEffectSlotViewModel;
}
