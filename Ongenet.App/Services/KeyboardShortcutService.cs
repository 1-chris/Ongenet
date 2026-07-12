using System;
using System.Collections.Generic;
using Avalonia.Input;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.Services;

/// <summary>App-wide keyboard shortcut actions that can be rebound in settings.</summary>
public enum AppShortcutAction
{
    RippleInsert,
    RippleDelete,
    OpenTempoMap,
    OpenSectionPlaylist
}

/// <summary>
/// Resolves keyboard shortcuts from persisted overrides with built-in defaults.
/// </summary>
public sealed class KeyboardShortcutService
{
    private readonly IAppSettingsService _settings;

    public KeyboardShortcutService(IAppSettingsService settings)
    {
        _settings = settings;
    }

    public event Action? BindingsChanged;

    public bool Matches(KeyEventArgs e, AppShortcutAction action)
    {
        var binding = GetBinding(action);
        return e.Key == binding.Key && e.KeyModifiers == binding.Modifiers;
    }

    public KeyboardShortcutBinding GetBinding(AppShortcutAction action)
    {
        var key = action.ToString();
        var custom = _settings.Current.KeyboardShortcuts.Find(b => b.Action == key);
        if (custom is not null && Enum.TryParse<Key>(custom.Key, out var customKey))
            return new KeyboardShortcutBinding(customKey, ParseModifiers(custom.Modifiers));

        return DefaultBinding(action);
    }

    public void SetBinding(AppShortcutAction action, Key key, KeyModifiers modifiers)
    {
        var keyName = action.ToString();
        _settings.Current.KeyboardShortcuts.RemoveAll(b => b.Action == keyName);
        _settings.Current.KeyboardShortcuts.Add(new KeyboardShortcutDto
        {
            Action = keyName,
            Key = key.ToString(),
            Modifiers = FormatModifiers(modifiers)
        });
        _settings.CaptureAndSave();
        BindingsChanged?.Invoke();
    }

    public void ResetBinding(AppShortcutAction action)
    {
        var keyName = action.ToString();
        _settings.Current.KeyboardShortcuts.RemoveAll(b => b.Action == keyName);
        _settings.CaptureAndSave();
        BindingsChanged?.Invoke();
    }

    public IReadOnlyList<KeyboardShortcutRow> AllRows()
    {
        var rows = new List<KeyboardShortcutRow>();
        foreach (AppShortcutAction action in Enum.GetValues<AppShortcutAction>())
        {
            var binding = GetBinding(action);
            rows.Add(new KeyboardShortcutRow(action, LabelFor(action), binding));
        }
        return rows;
    }

    private static KeyboardShortcutBinding DefaultBinding(AppShortcutAction action) => action switch
    {
        AppShortcutAction.RippleInsert => new(Key.I, KeyModifiers.Control | KeyModifiers.Shift),
        AppShortcutAction.RippleDelete => new(Key.Back, KeyModifiers.Control | KeyModifiers.Shift),
        AppShortcutAction.OpenTempoMap => new(Key.T, KeyModifiers.Control | KeyModifiers.Alt),
        AppShortcutAction.OpenSectionPlaylist => new(Key.L, KeyModifiers.Control | KeyModifiers.Alt),
        _ => new(Key.None, KeyModifiers.None)
    };

    private static string LabelFor(AppShortcutAction action) => action switch
    {
        AppShortcutAction.RippleInsert => "Ripple insert time",
        AppShortcutAction.RippleDelete => "Ripple delete time",
        AppShortcutAction.OpenTempoMap => "Open tempo map",
        AppShortcutAction.OpenSectionPlaylist => "Open section playlist",
        _ => action.ToString()
    };

    private static KeyModifiers ParseModifiers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return KeyModifiers.None;
        var mods = KeyModifiers.None;
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<KeyModifiers>(part, out var m)) mods |= m;
        }
        return mods;
    }

    private static string FormatModifiers(KeyModifiers mods)
    {
        if (mods == KeyModifiers.None) return "";
        var parts = new List<string>();
        if (mods.HasFlag(KeyModifiers.Control)) parts.Add(nameof(KeyModifiers.Control));
        if (mods.HasFlag(KeyModifiers.Shift)) parts.Add(nameof(KeyModifiers.Shift));
        if (mods.HasFlag(KeyModifiers.Alt)) parts.Add(nameof(KeyModifiers.Alt));
        if (mods.HasFlag(KeyModifiers.Meta)) parts.Add(nameof(KeyModifiers.Meta));
        return string.Join(",", parts);
    }
}

public readonly record struct KeyboardShortcutBinding(Key Key, KeyModifiers Modifiers)
{
    public string Display => Modifiers == KeyModifiers.None ? Key.ToString() : $"{FormatMods(Modifiers)} + {Key}";

    private static string FormatMods(KeyModifiers mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(KeyModifiers.Meta)) parts.Add("Meta");
        return string.Join("+", parts);
    }
}

public sealed class KeyboardShortcutRow
{
    public KeyboardShortcutRow(AppShortcutAction action, string label, KeyboardShortcutBinding binding)
    {
        Action = action;
        Label = label;
        Binding = binding.Display;
    }

    public AppShortcutAction Action { get; }
    public string Label { get; }
    public string Binding { get; }
}
