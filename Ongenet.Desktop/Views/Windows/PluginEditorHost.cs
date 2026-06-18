using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Desktop.Views.Windows
{
    /// <summary>
    /// Shared toggling of a plugin's GUI for instrument inspectors and effect cards. Embedded GUIs
    /// get a persistent, resizable <see cref="PluginWindow"/> per editor (hidden on close, reused on
    /// reopen so the GUI is never destroyed/recreated); floating plugins own their window.
    /// <see cref="EditorStateChanged"/> lets the relevant view refresh its open/close button.
    /// </summary>
    internal static class PluginEditorHost
    {
        private static readonly Dictionary<IPluginEditor, PluginWindow> Windows = new();

        /// <summary>Raised (UI thread) after an editor opens/closes (including the user hiding its window).</summary>
        public static event Action<IPluginEditor>? EditorStateChanged;

        public static void Toggle(IPluginEditor editor, string title, Window? owner)
        {
            try { ToggleCore(editor, title, owner); }
            finally { EditorStateChanged?.Invoke(editor); }
        }

        private static void ToggleCore(IPluginEditor editor, string title, Window? owner)
        {
            if (!editor.HasEditor) return;

            // Floating plugins own their window — toggle via the editor, transient to the main window.
            if (editor.PrefersFloating)
            {
                if (editor.IsEditorOpen) editor.CloseEditor();
                else
                {
                    var mh = owner?.TryGetPlatformHandle();
                    if (mh is not null) editor.OpenEditor(mh.Handle, MapApi(mh.HandleDescriptor), floating: true);
                }

                return;
            }

            // Embedded: reuse one persistent host window per editor.
            if (Windows.TryGetValue(editor, out var win))
            {
                if (win.IsVisible) { editor.CloseEditor(); win.Hide(); }
                else
                {
                    win.Show();
                    var h = win.TryGetPlatformHandle();
                    if (h is not null) editor.OpenEditor(h.Handle, MapApi(h.HandleDescriptor), floating: false);
                }

                return;
            }

            var window = new PluginWindow { Title = title };
            if (owner is not null) window.Show(owner); else window.Show();

            var handle = window.TryGetPlatformHandle();
            if (handle is null) { window.Close(); return; }

            editor.OpenEditor(handle.Handle, MapApi(handle.HandleDescriptor), floating: false);
            if (editor.EditorWidth > 0 && editor.EditorHeight > 0)
            {
                window.Width = editor.EditorWidth;
                window.Height = editor.EditorHeight;
            }

            window.Bind(editor);
            window.HiddenByUser += () => EditorStateChanged?.Invoke(editor);
            window.Closed += (_, _) => Windows.Remove(editor);
            Windows[editor] = window;
        }

        /// <summary>Maps an Avalonia platform-handle descriptor to a CLAP window API string.</summary>
        public static string MapApi(string? descriptor)
        {
            if (descriptor is null) return "x11";
            if (descriptor.Contains("HWND", StringComparison.OrdinalIgnoreCase)) return "win32";
            if (descriptor.Contains("NS", StringComparison.OrdinalIgnoreCase)) return "cocoa";
            return "x11";
        }
    }
}
