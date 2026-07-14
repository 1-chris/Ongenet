using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels.FileSystem
{
    /// <summary>
    /// A node in the file browser tree. Directories enumerate their children lazily — only
    /// when first expanded — so opening the browser never walks the whole filesystem.
    /// </summary>
    public class FileNodeViewModel : ViewModelBase
    {
        private static readonly FileNodeViewModel Placeholder = new PlaceholderNode();

        private readonly Func<string, bool>? _fileFilter;
        private readonly ILibraryOrganizationService? _org;

        private bool _isExpanded;
        private bool _childrenLoaded;
        private bool _isFavourite;

        private FileNodeViewModel(string name, string fullPath, bool isDirectory, string itemKey,
            Func<string, bool>? fileFilter, ILibraryOrganizationService? org, bool isSynthetic, string icon)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
            ItemKey = itemKey;
            _fileFilter = fileFilter;
            _org = org;
            IsSynthetic = isSynthetic;
            Icon = icon;
            if (!isSynthetic && CanFavourite && org is not null)
                _isFavourite = org.IsFavourite(itemKey);
            if (isDirectory && !isSynthetic)
                Children.Add(Placeholder);
        }

        private sealed class PlaceholderNode : FileNodeViewModel
        {
            public PlaceholderNode() : base("", "", false, "", null, null, false, "") { }
        }

        public FileNodeViewModel(string fullPath, bool isDirectory, Func<string, bool>? fileFilter = null,
            ILibraryOrganizationService? org = null)
            : this(
                name: Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                      is { Length: > 0 } n ? n : fullPath,
                fullPath: fullPath,
                isDirectory: isDirectory,
                itemKey: isDirectory ? LibraryItemKeys.FolderKey(fullPath) : LibraryItemKeys.FileKey(fullPath),
                fileFilter: fileFilter,
                org: org,
                isSynthetic: false,
                icon: "")
        {
        }

        /// <summary>Pin folder (Favourites / category) that is not a real filesystem path.</summary>
        public static FileNodeViewModel SyntheticFolder(string title, IEnumerable<FileNodeViewModel> children,
            string icon = "")
        {
            var node = new FileNodeViewModel(title, "", isDirectory: true, itemKey: "",
                fileFilter: null, org: null, isSynthetic: true, icon: icon)
            {
                _childrenLoaded = true,
                _isExpanded = true
            };
            foreach (var c in children) node.Children.Add(c);
            return node;
        }

        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public string ItemKey { get; }
        public string Icon { get; }
        public bool IsSynthetic { get; }
        public bool HasIcon => Icon.Length > 0;
        public bool CanFavourite => ItemKey.Length > 0 && !IsSynthetic;

        public ObservableCollection<FileNodeViewModel> Children { get; } = new();

        public bool IsFavourite
        {
            get => _isFavourite;
            private set => SetField(ref _isFavourite, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (!SetField(ref _isExpanded, value)) return;
                if (value && IsDirectory && !IsSynthetic) LoadChildren();
            }
        }

        public void SyncFavourite(ILibraryOrganizationService? org)
        {
            if (org is null || !CanFavourite) { IsFavourite = false; return; }
            IsFavourite = org.IsFavourite(ItemKey);
            foreach (var c in Children)
            {
                if (ReferenceEquals(c, Placeholder)) continue;
                c.SyncFavourite(org);
            }
        }

        public void ToggleFavourite()
        {
            if (_org is null || !CanFavourite) return;
            _org.ToggleFavourite(ItemKey);
            IsFavourite = _org.IsFavourite(ItemKey);
        }

        private void LoadChildren()
        {
            if (_childrenLoaded) return;
            _childrenLoaded = true;

            Children.Clear();
            try
            {
                var dirs = Directory.EnumerateDirectories(FullPath)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
                foreach (var dir in dirs)
                    Children.Add(new FileNodeViewModel(dir, isDirectory: true, _fileFilter, _org));

                var files = Directory.EnumerateFiles(FullPath)
                    .Where(f => _fileFilter is null || _fileFilter(f))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                    Children.Add(new FileNodeViewModel(file, isDirectory: false, _fileFilter, _org));
            }
            catch (Exception)
            {
                // Inaccessible directory: leave empty.
            }
        }
    }
}
