using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Input;

namespace Ongenet.Desktop.ViewModels.Library;

/// <summary>One draggable row in a library tab: a label, optional sublabel, the drag payload it carries,
/// and an optional double-click action.</summary>
public sealed class LibraryEntry
{
    public required string Title { get; init; }
    public string Subtitle { get; init; } = string.Empty;
    public required DataFormat<string> DragFormat { get; init; }
    public required string DragPayload { get; init; }
    public Action? Activate { get; init; }
}

/// <summary>A titled group of library entries (e.g. a category or scan folder).</summary>
public sealed record LibrarySection(string Name, IReadOnlyList<LibraryEntry> Entries);

/// <summary>
/// Base view model for the browseable library tabs (Effects, Samples, Soundfonts, Instrument/Effect
/// Presets). Holds grouped, draggable entries; subclasses fill <see cref="Sections"/> from a registry or
/// scan service and rebuild when their source changes. The shared <c>LibraryListView</c> renders any of them.
/// </summary>
public abstract class LibraryListViewModel : ViewModelBase
{
    public ObservableCollection<LibrarySection> Sections { get; } = new();

    /// <summary>Hint shown when there are no entries (e.g. "Add scan folders in Settings").</summary>
    public string EmptyHint { get; protected set; } = string.Empty;

    public bool IsEmpty => Sections.Count == 0;

    protected void Replace(IEnumerable<LibrarySection> sections)
    {
        Sections.Clear();
        foreach (var s in sections) Sections.Add(s);
        OnPropertyChanged(nameof(IsEmpty));
    }
}
