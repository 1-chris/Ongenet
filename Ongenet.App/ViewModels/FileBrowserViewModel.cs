using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using Ongenet.App.Localization;
using Ongenet.App.Services;
using Ongenet.App.ViewModels.FileSystem;
using Ongenet.Core.Audio.Files;

namespace Ongenet.App.ViewModels
{
    /// <summary>
    /// Right-hand file browser. Presents a handful of useful root folders; each expands its
    /// contents lazily through <see cref="FileNodeViewModel"/>. Favourites and user categories
    /// from <see cref="ILibraryOrganizationService"/> appear as synthetic roots at the top.
    /// </summary>
    public class FileBrowserViewModel : ViewModelBase
    {
        private readonly IAudioFileService _audioFiles;
        private readonly ILibraryOrganizationService _org;
        private readonly ObservableCollection<FileNodeViewModel> _fsRoots = new();

        public FileBrowserViewModel(IAudioFileService audioFiles, ILibraryOrganizationService org)
        {
            _audioFiles = audioFiles;
            _org = org;
            Organization = org;
            AddRootIfExists(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            AddRootIfExists(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            AddRootIfExists(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            _org.Changed += () => Dispatcher.UIThread.Post(RebuildRoots);
            RebuildRoots();
        }

        public ILibraryOrganizationService Organization { get; }

        /// <summary>Whether a path is an audio file that can be dragged onto the timeline.</summary>
        public bool IsAudioFile(string path) => _audioFiles.IsAudioFile(path);

        /// <summary>Top-level folders shown in the tree (favourites / categories / filesystem roots).</summary>
        public ObservableCollection<FileNodeViewModel> Roots { get; } = new();

        private void RebuildRoots()
        {
            Roots.Clear();

            var favKids = ResolveOrgKeys(_org.Favourites);
            if (favKids.Count > 0)
            {
                Roots.Add(FileNodeViewModel.SyntheticFolder(Loc.Get("LibraryOrg_Favourites", "Favourites"), favKids, "★"));
            }

            foreach (var cat in _org.Categories)
            {
                var kids = ResolveOrgKeys(cat.ItemKeys);
                if (kids.Count == 0) continue;
                Roots.Add(FileNodeViewModel.SyntheticFolder(cat.Name, kids, "🏷"));
            }

            foreach (var r in _fsRoots)
            {
                r.SyncFavourite(_org);
                Roots.Add(r);
            }
        }

        private System.Collections.Generic.List<FileNodeViewModel> ResolveOrgKeys(
            System.Collections.Generic.IEnumerable<string> keys)
        {
            var list = new System.Collections.Generic.List<FileNodeViewModel>();
            foreach (var key in keys)
            {
                if (!LibraryItemKeys.TryParse(key, out var kind, out var payload)) continue;
                if (kind is not (LibraryItemKeys.File or LibraryItemKeys.Folder)) continue;
                try
                {
                    if (kind == LibraryItemKeys.Folder && Directory.Exists(payload))
                        list.Add(new FileNodeViewModel(payload, isDirectory: true, _audioFiles.IsAudioFile, _org));
                    else if (kind == LibraryItemKeys.File && File.Exists(payload) && _audioFiles.IsAudioFile(payload))
                        list.Add(new FileNodeViewModel(payload, isDirectory: false, _audioFiles.IsAudioFile, _org));
                }
                catch { /* ignore bad paths */ }
            }
            return list;
        }

        private void AddRootIfExists(string? path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            foreach (var root in _fsRoots)
            {
                if (string.Equals(root.FullPath, path, StringComparison.Ordinal)) return;
            }

            _fsRoots.Add(new FileNodeViewModel(path, isDirectory: true, _audioFiles.IsAudioFile, _org));
        }
    }
}
