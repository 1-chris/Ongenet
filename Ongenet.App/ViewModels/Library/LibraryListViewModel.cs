using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Input;
using Avalonia.Threading;
using Ongenet.App.Localization;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels.Library;

/// <summary>
/// One node in a library tab's tree. A <b>folder</b> (<see cref="IsFolder"/>) is a titled, collapsible
/// group with <see cref="Children"/>; a <b>leaf</b> carries a drag payload and an optional double-click
/// action. The same node type backs every list tab (flat 2-level groups, the nested sample folder tree,
/// and the aggregated Everything tab).
/// </summary>
public sealed class LibraryNode : ViewModelBase
{
    private bool _isExpanded = true;
    private bool _isFavourite;

    public required string Title { get; init; }
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>Optional leading glyph (e.g. 📁 for folders, 🎹 for instruments). Empty hides it.</summary>
    public string Icon { get; init; } = string.Empty;

    public bool IsFolder { get; init; }

    /// <summary>Stable key for favourites/categories (<see cref="LibraryItemKeys"/>). Empty = not organisable.</summary>
    public string ItemKey { get; init; } = string.Empty;

    // Leaf-only: how this row drags and what double-clicking it does. Null on folders.
    public DataFormat<string>? DragFormat { get; init; }
    public string? DragPayload { get; init; }
    public Action? Activate { get; init; }

    /// <summary>Toggles favourite state; wired by the owning list when an ItemKey is set.</summary>
    public Action? ToggleFavouriteAction { get; set; }

    public ObservableCollection<LibraryNode> Children { get; } = new();

    public bool HasIcon => Icon.Length > 0;
    public bool CanFavourite => ItemKey.Length > 0;

    public bool IsFavourite
    {
        get => _isFavourite;
        set => SetField(ref _isFavourite, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public void ToggleFavourite() => ToggleFavouriteAction?.Invoke();
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

    private List<LibraryNode> _contentRoots = new();
    private List<LibraryNode> _allRoots = new();
    private string _searchText = string.Empty;
    private DispatcherTimer? _debounce;
    private ILibraryOrganizationService? _org;
    private HashSet<string>? _orgKinds;

    /// <summary>The currently displayed (filtered) tree.</summary>
    public ObservableCollection<LibraryNode> Roots { get; } = new();

    /// <summary>Hint shown when there are no entries (e.g. "Add scan folders in Settings").</summary>
    public string EmptyHint { get; protected set; } = string.Empty;

    public bool IsEmpty => Roots.Count == 0;

    /// <summary>Organization service (favourites / categories), when attached.</summary>
    public ILibraryOrganizationService? Organization => _org;

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

    /// <summary>
    /// Wire favourites/categories. <paramref name="kinds"/> filters which item-key kinds appear in this
    /// tab's Favourites/Categories folders (empty = all kinds).
    /// </summary>
    protected void AttachOrganization(ILibraryOrganizationService org, params string[] kinds)
    {
        if (_org is not null) _org.Changed -= OnOrganizationChanged;
        _org = org;
        _orgKinds = kinds.Length == 0 ? null : new HashSet<string>(kinds, StringComparer.Ordinal);
        _org.Changed += OnOrganizationChanged;
    }

    private void OnOrganizationChanged() => Dispatcher.UIThread.Post(RebuildDisplayRoots);

    /// <summary>Stores the full (unfiltered) content tree and applies org layers + the current filter.</summary>
    protected void SetRoots(IEnumerable<LibraryNode> roots)
    {
        _contentRoots = roots.ToList();
        WireOrganization(_contentRoots);
        RebuildDisplayRoots();
    }

    private void RebuildDisplayRoots()
    {
        SyncFavouriteFlags(_contentRoots);
        _allRoots = BuildDisplayRoots(_contentRoots);
        _debounce?.Stop();
        ApplyFilter();
    }

    /// <summary>Builds a collapsible folder node with the given children (auto-collapsed if large).</summary>
    protected static LibraryNode Folder(string title, IEnumerable<LibraryNode> children, string icon = "",
        string itemKey = "")
    {
        var f = new LibraryNode { Title = title, Icon = icon, IsFolder = true, ItemKey = itemKey };
        foreach (var c in children) f.Children.Add(c);
        f.IsExpanded = ShouldAutoExpand(f.Children.Count);
        return f;
    }

    /// <summary>Finds or creates a child folder under <paramref name="parent"/>.</summary>
    protected static LibraryNode GetOrAddChildFolder(LibraryNode parent, string name, string fullPath)
    {
        foreach (var child in parent.Children)
            if (child.IsFolder && string.Equals(child.Title, name, StringComparison.OrdinalIgnoreCase))
                return child;

        var folder = new LibraryNode
        {
            Title = name,
            Icon = "📁",
            IsFolder = true,
            ItemKey = LibraryItemKeys.FolderKey(fullPath)
        };
        parent.Children.Add(folder);
        return folder;
    }

    /// <summary>Sorts folders first, then by title (recursive).</summary>
    protected static void SortPathTree(LibraryNode folder)
    {
        var ordered = folder.Children
            .OrderBy(c => c.IsFolder ? 0 : 1)
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        folder.Children.Clear();
        foreach (var c in ordered) folder.Children.Add(c);
        folder.IsExpanded = false;
        foreach (var c in ordered)
            if (c.IsFolder) SortPathTree(c);
    }

    /// <summary>Builds a draggable leaf node.</summary>
    protected static LibraryNode Leaf(string title, DataFormat<string> format, string payload,
        Action? activate = null, string subtitle = "", string icon = "", string itemKey = "")
        => new()
        {
            Title = title,
            Subtitle = subtitle,
            Icon = icon,
            DragFormat = format,
            DragPayload = payload,
            Activate = activate,
            ItemKey = itemKey
        };

    private void WireOrganization(IEnumerable<LibraryNode> roots)
    {
        if (_org is null) return;
        foreach (var n in Enumerate(roots))
        {
            if (!n.CanFavourite) continue;
            var key = n.ItemKey;
            n.ToggleFavouriteAction = () => _org.ToggleFavourite(key);
            n.IsFavourite = _org.IsFavourite(key);
        }
    }

    private void SyncFavouriteFlags(IEnumerable<LibraryNode> roots)
    {
        if (_org is null) return;
        foreach (var n in Enumerate(roots))
        {
            if (!n.CanFavourite) continue;
            n.IsFavourite = _org.IsFavourite(n.ItemKey);
            n.ToggleFavouriteAction ??= () => _org.ToggleFavourite(n.ItemKey);
        }
    }

    private List<LibraryNode> BuildDisplayRoots(List<LibraryNode> content)
    {
        if (_org is null) return content.ToList();

        var byKey = new Dictionary<string, LibraryNode>(StringComparer.Ordinal);
        IndexByKey(content, byKey);

        var display = new List<LibraryNode>();

        var favChildren = ResolveKeys(_org.Favourites, byKey);
        if (favChildren.Count > 0)
        {
            display.Add(PinFolder(Loc.Get("LibraryOrg_Favourites"), "★",
                favChildren.Select(DeepClone), expanded: true));
        }

        foreach (var cat in _org.Categories)
        {
            var kids = ResolveKeys(cat.ItemKeys, byKey);
            if (kids.Count == 0) continue;
            display.Add(PinFolder(cat.Name, "🏷", kids.Select(DeepClone),
                expanded: ShouldAutoExpand(kids.Count)));
        }

        display.AddRange(content);
        WireOrganization(display);
        return display;
    }

    private List<LibraryNode> ResolveKeys(IEnumerable<string> keys, Dictionary<string, LibraryNode> byKey)
    {
        var list = new List<LibraryNode>();
        foreach (var key in keys)
        {
            if (!MatchesKindFilter(key)) continue;
            if (!byKey.TryGetValue(key, out var node)) continue;
            list.Add(node);
        }
        return list;
    }

    private bool MatchesKindFilter(string key)
    {
        if (_orgKinds is null) return true;
        var kind = LibraryItemKeys.KindOf(key);
        return kind is not null && _orgKinds.Contains(kind);
    }

    private static void IndexByKey(IEnumerable<LibraryNode> roots, Dictionary<string, LibraryNode> byKey)
    {
        foreach (var n in Enumerate(roots))
        {
            if (n.ItemKey.Length == 0) continue;
            byKey.TryAdd(n.ItemKey, n);
        }
    }

    private static IEnumerable<LibraryNode> Enumerate(IEnumerable<LibraryNode> roots)
    {
        foreach (var r in roots)
        {
            yield return r;
            if (r.Children.Count == 0) continue;
            foreach (var c in Enumerate(r.Children)) yield return c;
        }
    }

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
            foreach (var root in _allRoots)
                Roots.Add(LeafCap > 0 && !IsOrgPinFolder(root) ? CapClone(root, LeafCap) : root);
        }
        else
        {
            var budget = new int[2];
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

    /// <summary>Favourites / category pin folders are never LeafCap-trimmed.</summary>
    private static bool IsOrgPinFolder(LibraryNode n)
        => n.IsFolder && n.ItemKey.Length == 0 && (n.Icon is "★" or "🏷");

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

    private static LibraryNode DeepClone(LibraryNode src)
    {
        if (!src.IsFolder) return CloneLeaf(src);
        var kids = src.Children.Select(DeepClone).ToList();
        return CloneFolder(src, kids, src.IsExpanded);
    }

    private static LibraryNode CloneLeaf(LibraryNode l) => new()
    {
        Title = l.Title,
        Subtitle = l.Subtitle,
        Icon = l.Icon,
        IsFolder = false,
        ItemKey = l.ItemKey,
        DragFormat = l.DragFormat,
        DragPayload = l.DragPayload,
        Activate = l.Activate,
        ToggleFavouriteAction = l.ToggleFavouriteAction,
        IsFavourite = l.IsFavourite
    };

    private static LibraryNode CloneFolder(LibraryNode src, List<LibraryNode> children, bool expanded)
    {
        var f = new LibraryNode
        {
            Title = src.Title,
            Subtitle = src.Subtitle,
            Icon = src.Icon,
            IsFolder = true,
            ItemKey = src.ItemKey,
            ToggleFavouriteAction = src.ToggleFavouriteAction,
            IsFavourite = src.IsFavourite
        };
        foreach (var c in children) f.Children.Add(c);
        f.IsExpanded = expanded;
        return f;
    }

    private static LibraryNode MoreNode(int hidden) => new() { Title = $"+{hidden} more…" };

    private static LibraryNode PinFolder(string title, string icon, IEnumerable<LibraryNode> children, bool expanded)
    {
        var f = new LibraryNode { Title = title, Icon = icon, IsFolder = true, IsExpanded = expanded };
        foreach (var c in children) f.Children.Add(c);
        return f;
    }
}
