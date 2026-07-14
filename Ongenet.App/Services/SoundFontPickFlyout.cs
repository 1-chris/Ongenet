using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Localization;
using Ongenet.App.ViewModels.Library;

namespace Ongenet.App.Services;

/// <summary>
/// Nested menu of scanned library soundfonts (Factory + user folders), plus "Choose from disk…".
/// Shared by Sampler Load / Add (slot) and the Sampler editor.
/// </summary>
public static class SoundFontPickFlyout
{
    public static void Show(Control anchor, Action<string> onPicked, string diskPickerTitle = "Choose sound font")
    {
        var flyout = new MenuFlyout();
        var scan = App.ServiceProvider?.GetService<ILibraryScanService>();
        var groups = scan?.SoundFonts ?? Array.Empty<LibraryGroup>();

        if (groups.Count == 0)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = Loc.Get("SoundFontPick_No_library_soundfonts"),
                IsEnabled = false
            });
        }
        else
        {
            foreach (var group in groups)
            {
                var tree = SoundFontLibraryViewModel.BuildSoundFontTree(group);
                flyout.Items.Add(ToMenuItem(tree, onPicked));
            }
        }

        flyout.Items.Add(new Separator());

        var disk = new MenuItem { Header = Loc.Get("SoundFontPick_Choose_from_disk") };
        disk.Click += async (_, _) =>
        {
            var path = await PickFromDiskAsync(anchor, diskPickerTitle);
            if (!string.IsNullOrEmpty(path)) onPicked(path!);
        };
        flyout.Items.Add(disk);

        flyout.ShowAt(anchor, true);
    }

    private static MenuItem ToMenuItem(LibraryNode node, Action<string> onPicked)
    {
        if (!node.IsFolder)
        {
            var path = node.DragPayload ?? string.Empty;
            var leaf = new MenuItem { Header = node.Title, IsEnabled = path.Length > 0 };
            if (path.Length > 0)
                leaf.Click += (_, _) => onPicked(path);
            return leaf;
        }

        var folder = new MenuItem { Header = node.Title };
        foreach (var child in node.Children)
            folder.Items.Add(ToMenuItem(child, onPicked));
        if (folder.Items.Count == 0)
            folder.IsEnabled = false;
        return folder;
    }

    public static async Task<string?> PickFromDiskAsync(Control anchor, string title)
    {
        var top = TopLevel.GetTopLevel(anchor);
        if (top is null) return null;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Sound fonts") { Patterns = new[] { "*.sfz", "*.sf2" } },
                new("SFZ instrument") { Patterns = new[] { "*.sfz" } },
                new("SF2 SoundFont") { Patterns = new[] { "*.sf2" } }
            }
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
