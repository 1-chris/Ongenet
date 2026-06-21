using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Input;
using Avalonia.Threading;

namespace Ongenet.Desktop.ViewModels.Library;

/// <summary>
/// One node in a library tab's tree. A <b>folder</b> (<see cref="IsFolder"/>) is a titled, collapsible
/// group with <see cref="Children"/>; a <b>leaf</b> carries a drag payload and an optional double-click
/// action. The same node type backs every list tab (flat 2-level groups, the nested sample folder tree,
/// and the aggregated Everything tab).
/// </summary>
public sealed class LibraryNode : ViewModelBase
{
    private bool _isExpanded = true;

    public required string Title { get; init; }
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>Optional leading glyph (e.g. 📁 for folders, 🎹 for instruments). Empty hides it.</summary>
    public string Icon { get; init; } = string.Empty;

    public bool IsFolder { get; init; }

    // Leaf-only: how this row drags and what double-clicking it does. Null on folders.
    public DataFormat<string>? DragFormat { get; init; }
    public string? DragPayload { get; init; }
    public Action? Activate { get; init; }

    public ObservableCollection<LibraryNode> Children { get; } = new();

    public bool HasIcon => Icon.Length > 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }
}

/// <summary>
/// Base view model for the browseable library tabs (Everything, Samples, Soundfonts, Instruments, Effects,
/// Instrument/Effect/Chain presets). Holds a tree of <see cref="LibraryNode"/>s plus an instant
/// <see cref="SearchText"/> filter; subclasses fill the tree from a registry or scan service via
/// <see cref="SetRoots"/> and rebuild when their source changes. The shared <c>LibraryListView</c> renders
/// any of them.
///
/// <para>Performance: large trees (thousands of samples) would freeze the UI if the TreeView had to realise
/// every row at once, so folders are collapsed by default (only top-level rows realise on open), search is
/// debounced and runs off a single timer, and the number of rows search reveals is capped. When the search
/// box is empty the master tree is shown directly (no cloning), so clearing a search is instant.</para>
/// </summary>
public abstract class LibraryListViewModel : ViewModelBase
{
    /// <summary>Folders with more children than this start collapsed (keeps open/realisation cheap).</summary>
    private const int ExpandThreshold = 60;

    private List<LibraryNode> _allRoots = new();
    private string _searchText = string.Empty;
    private DispatcherTimer? _debounce;

    /// <summary>The currently displayed (filtered) tree.</summary>
    public ObservableCollection<LibraryNode> Roots { get; } = new();

    /// <summary>Hint shown when there are no entries (e.g. "Add scan folders in Settings").</summary>
    public string EmptyHint { get; protected set; } = string.Empty;

    public bool IsEmpty => Roots.Count == 0;

    /// <summary>Instant filter text; setting it re-applies the filter (debounced) to <see cref="Roots"/>.</summary>
    public string SearchText
    {
        get => _searchText;
        set { if (SetField(ref _searchText, value)) ScheduleFilter(); }
    }

    /// <summary>
    /// When &gt; 0 and no search is active, only this many leaves are shown per top-level root (the rest are
    /// summarised by a "+N more…" row). Used by the Everything tab to show a capped sample of each type.
    /// </summary>
    protected virtual int LeafCap => 0;

    /// <summary>Max leaves a search reveals at once (across the whole tab), so a broad query can't realise
    /// thousands of rows and freeze the UI. Excess matches are summarised by a trailing "+N more…" row.</summary>
    protected virtual int SearchCap => 300;

    /// <summary>Folders are auto-expanded only when small enough to realise cheaply.</summary>
    protected static bool ShouldAutoExpand(int childCount) => childCount <= ExpandThreshold;

    /// <summary>Stores the full (unfiltered) tree and applies the current filter immediately.</summary>
    protected void SetRoots(IEnumerable<LibraryNode> roots)
    {
        _allRoots = roots.ToList();
        _debounce?.Stop();
        ApplyFilter();
    }

    /// <summary>Builds a collapsible folder node with the given children (auto-collapsed if large).</summary>
    protected static LibraryNode Folder(string title, IEnumerable<LibraryNode> children, string icon = "")
    {
        var f = new LibraryNode { Title = title, Icon = icon, IsFolder = true };
        foreach (var c in children) f.Children.Add(c);
        f.IsExpanded = ShouldAutoExpand(f.Children.Count);
        return f;
    }

    /// <summary>Builds a draggable leaf node.</summary>
    protected static LibraryNode Leaf(string title, DataFormat<string> format, string payload,
        Action? activate = null, string subtitle = "", string icon = "")
        => new()
        {
            Title = title,
            Subtitle = subtitle,
            Icon = icon,
            DragFormat = format,
            DragPayload = payload,
            Activate = activate
        };

    // Restart the debounce timer so a burst of keystrokes triggers a single filter pass.
    private void ScheduleFilter()
    {
        _debounce ??= MakeDebounce();
        _debounce.Stop();
        _debounce.Start();
    }

    private DispatcherTimer MakeDebounce()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        t.Tick += (_, _) => { t.Stop(); ApplyFilter(); };
        return t;
    }

    private void ApplyFilter()
    {
        var query = _searchText.Trim();
        Roots.Clear();

        if (query.Length == 0)
        {
            // No search: show the master tree directly (or a capped clone for the Everything tab).
            foreach (var root in _allRoots)
                Roots.Add(LeafCap > 0 ? CapClone(root, LeafCap) : root);
        }
        else
        {
            // Searching: clone only the matching branches, capped so a broad query stays responsive.
            var budget = new int[2]; // [0] = leaves still allowed, [1] = leaves hidden by the cap
            budget[0] = SearchCap;
            foreach (var root in _allRoots)
            {
                var f = SearchClone(root, query, budget);
                if (f is not null) Roots.Add(f);
            }
            if (budget[1] > 0 && Roots.Count > 0) Roots.Add(MoreNode(budget[1]));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    // At-rest cap (Everything tab): keep at most `cap` leaves of a top-level folder, summarise the rest.
    private static LibraryNode CapClone(LibraryNode node, int cap)
    {
        var kept = new List<LibraryNode>();
        int leaves = 0, hidden = 0;
        foreach (var child in node.Children)
        {
            if (child.IsFolder) { kept.Add(child); continue; }
            if (leaves >= cap) { hidden++; continue; }
            kept.Add(CloneLeaf(child));
            leaves++;
        }
        if (hidden > 0) kept.Add(MoreNode(hidden));
        return CloneFolder(node, kept, ShouldAutoExpand(kept.Count));
    }

    // Returns a pruned clone of a node, or null if nothing in it matches. Leaves match when their title
    // contains the query; folders survive only if they have a surviving descendant. The shared budget caps
    // total revealed leaves and counts those hidden once the cap is hit.
    private static LibraryNode? SearchClone(LibraryNode node, string query, int[] budget)
    {
        if (!node.IsFolder)
        {
            if (!node.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) return null;
            if (budget[0] <= 0) { budget[1]++; return null; }
            budget[0]--;
            return CloneLeaf(node);
        }

        var kept = new List<LibraryNode>();
        foreach (var child in node.Children)
        {
            var f = SearchClone(child, query, budget);
            if (f is not null) kept.Add(f);
        }
        return kept.Count == 0 ? null : CloneFolder(node, kept, expanded: true);
    }

    private static LibraryNode CloneLeaf(LibraryNode l) => new()
    {
        Title = l.Title,
        Subtitle = l.Subtitle,
        Icon = l.Icon,
        IsFolder = false,
        DragFormat = l.DragFormat,
        DragPayload = l.DragPayload,
        Activate = l.Activate
    };

    private static LibraryNode CloneFolder(LibraryNode src, List<LibraryNode> children, bool expanded)
    {
        var f = new LibraryNode { Title = src.Title, Subtitle = src.Subtitle, Icon = src.Icon, IsFolder = true };
        foreach (var c in children) f.Children.Add(c);
        f.IsExpanded = expanded;
        return f;
    }

    private static LibraryNode MoreNode(int hidden) => new() { Title = $"+{hidden} more…" };
}
