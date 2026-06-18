using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Ongenet.Desktop.ViewModels.FileSystem
{
    /// <summary>
    /// A node in the file browser tree. Directories enumerate their children lazily — only
    /// when first expanded — so opening the browser never walks the whole filesystem.
    /// </summary>
    public class FileNodeViewModel : ViewModelBase
    {
        // Sentinel child shown under an unexpanded directory so the TreeView draws an
        // expander arrow before we've actually enumerated its contents.
        private static readonly FileNodeViewModel Placeholder = new();

        private bool _isExpanded;
        private bool _childrenLoaded;

        private FileNodeViewModel()
        {
            // Placeholder node only.
            Name = string.Empty;
            FullPath = string.Empty;
            IsDirectory = false;
        }

        public FileNodeViewModel(string fullPath, bool isDirectory)
        {
            FullPath = fullPath;
            IsDirectory = isDirectory;
            Name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(Name)) Name = fullPath; // drive/root

            if (isDirectory)
            {
                // Defer real enumeration; show the placeholder so the expander appears.
                Children.Add(Placeholder);
            }
        }

        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }

        public ObservableCollection<FileNodeViewModel> Children { get; } = new();

        /// <summary>Bound to the TreeView item; loads children on first expand.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (!SetField(ref _isExpanded, value)) return;
                if (value && IsDirectory) LoadChildren();
            }
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
                {
                    Children.Add(new FileNodeViewModel(dir, isDirectory: true));
                }

                var files = Directory.EnumerateFiles(FullPath)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    Children.Add(new FileNodeViewModel(file, isDirectory: false));
                }
            }
            catch (Exception)
            {
                // Inaccessible directory (permissions, removed media): leave it empty rather
                // than throwing into the UI.
            }
        }
    }
}
