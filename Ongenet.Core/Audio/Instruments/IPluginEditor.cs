using System;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Implemented by instruments that have their own native GUI (e.g. CLAP plugins), so the host can
/// show the plugin's UI. Uses primitives only (no Avalonia dependency): the host creates the
/// container window and passes its native handle. The host embeds the GUI into a dedicated window
/// when the plugin supports it, and only falls back to a plugin-owned floating window otherwise.
/// </summary>
public interface IPluginEditor
{
    /// <summary>True when the plugin exposes an openable GUI.</summary>
    bool HasEditor { get; }

    /// <summary>True while the plugin GUI is currently open.</summary>
    bool IsEditorOpen { get; }

    /// <summary>
    /// True if the plugin only supports a floating (plugin-owned) window — the host should pass its
    /// main window handle as the transient owner rather than creating a container window.
    /// </summary>
    bool PrefersFloating { get; }

    /// <summary>Preferred GUI size in pixels (valid after <see cref="OpenEditor"/>); 0 if unknown.</summary>
    int EditorWidth { get; }
    int EditorHeight { get; }

    /// <summary>
    /// Opens the plugin GUI. <paramref name="windowHandle"/>/<paramref name="apiType"/> are the
    /// container window's native handle and kind ("win32"/"cocoa"/"x11"). When
    /// <paramref name="floating"/> is false the GUI is embedded into that window; when true the
    /// plugin creates its own window transient to it.
    /// </summary>
    void OpenEditor(nint windowHandle, string apiType, bool floating);

    /// <summary>Tells an embedded GUI its container was resized (no-op if the plugin can't resize).</summary>
    void SetEditorSize(int width, int height);

    /// <summary>Closes the plugin GUI if open.</summary>
    void CloseEditor();

    /// <summary>Services the plugin's main-thread work while its GUI is open (called on a UI timer).</summary>
    void PumpEditor();
}
