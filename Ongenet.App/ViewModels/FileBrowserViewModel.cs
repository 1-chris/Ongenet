using System;
using System.Collections.ObjectModel;
using System.IO;
using Ongenet.Core.Audio.Files;
using Ongenet.App.ViewModels.FileSystem;

namespace Ongenet.App.ViewModels
{
    /// <summary>
    /// Right-hand file browser. Presents a handful of useful root folders; each expands its
    /// contents lazily through <see cref="FileNodeViewModel"/>.
    /// </summary>
    public class FileBrowserViewModel : ViewModelBase
    {
        private readonly IAudioFileService _audioFiles;

        public FileBrowserViewModel(IAudioFileService audioFiles)
        {
            _audioFiles = audioFiles;
            AddRootIfExists(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            AddRootIfExists(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            AddRootIfExists(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        }

        /// <summary>Whether a path is an audio file that can be dragged onto the timeline.</summary>
        public bool IsAudioFile(string path) => _audioFiles.IsAudioFile(path);

        /// <summary>Top-level folders shown in the tree.</summary>
        public ObservableCollection<FileNodeViewModel> Roots { get; } = new();

        private void AddRootIfExists(string? path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            // Skip duplicates (on some platforms several special folders resolve to the same path).
            foreach (var root in Roots)
            {
                if (string.Equals(root.FullPath, path, StringComparison.Ordinal)) return;
            }

            // Only show folders and audio files we can ingest (WAV natively, others via ffmpeg).
            Roots.Add(new FileNodeViewModel(path, isDirectory: true, _audioFiles.IsAudioFile));
        }
    }
}
