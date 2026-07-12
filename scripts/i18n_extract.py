#!/usr/bin/env python3
"""Extract UI strings from Ongenet.App AXAML files into Strings.en.axaml and apply DynamicResource bindings."""

from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "Ongenet.App"

ATTRS = [
    "Text",
    "Content",
    "Title",
    "Header",
    "PlaceholderText",
    "ToolTip.Tip",
    "AutomationProperties.Name",
    "AutomationProperties.HelpText",
]

SKIP_PREFIXES = ("{", "$", "@")
SKIP_EXACT = {"", " "}

PREFIX_MAP = {
    "Views/Windows/MainWindow.axaml": "Main",
    "Views/Windows/SettingsWindow.axaml": "Settings",
    "Views/Windows/ExportDialog.axaml": "Export",
    "Views/Windows/GuideWindow.axaml": "Guide",
    "Views/Windows/LogWindow.axaml": "Log",
    "Views/Windows/HistoryWindow.axaml": "History",
    "Views/Windows/PluginWindow.axaml": "Plugin",
    "Views/Windows/FieldWindow.axaml": "Field",
    "Views/Windows/AudioEditorWindow.axaml": "AudioEditor",
    "Views/Windows/AudioToMidiWindow.axaml": "AudioToMidi",
    "Views/Windows/ChordTrackWindow.axaml": "ChordTrack",
    "Views/Windows/DrumMapEditorWindow.axaml": "DrumMap",
    "Views/Windows/ExpressionMapWindow.axaml": "ExpressionMap",
    "Views/Windows/LogicalMidiEditWindow.axaml": "LogicalMidi",
    "Views/Windows/MonophonicPitchEditorWindow.axaml": "MonoPitch",
    "Views/Windows/RoutingMatrixWindow.axaml": "Routing",
    "Views/Windows/SamplerZoneEditorWindow.axaml": "SamplerZone",
    "Views/Windows/SectionPlaylistWindow.axaml": "SectionPlaylist",
    "Views/Windows/TempoMapWindow.axaml": "TempoMap",
    "Views/Windows/ArpeggioWindow.axaml": "Arpeggio",
    "Views/Windows/MidiGeneratorWindow.axaml": "MidiGen",
    "Views/Windows/Engine3DVisualWindow.axaml": "Engine3D",
    "Views/Panels/TransportView.axaml": "Transport",
    "Views/Panels/TimelineView.axaml": "Timeline",
    "Views/Panels/PianoRollView.axaml": "PianoRoll",
    "Views/Panels/MixerView.axaml": "Mixer",
    "Views/Panels/TrackInspectorView.axaml": "TrackInspector",
    "Views/Panels/SampleInspectorView.axaml": "SampleInspector",
    "Views/Panels/InstrumentInspectorView.axaml": "InstrumentInspector",
    "Views/Panels/ClipInspectorView.axaml": "ClipInspector",
    "Views/Panels/EffectChainView.axaml": "EffectChain",
    "Views/Panels/InstrumentSlotView.axaml": "InstrumentSlot",
    "Views/Panels/SessionView.axaml": "Session",
    "Views/Panels/NotationView.axaml": "Notation",
    "Views/Panels/MidiFxView.axaml": "MidiFx",
    "Views/Panels/GrooveSettingsView.axaml": "Groove",
    "Views/Panels/PatternTrackInspectorView.axaml": "PatternInspector",
    "Views/Panels/VideoTrackView.axaml": "VideoTrack",
    "Views/Panels/ChannelRackView.axaml": "ChannelRack",
    "Views/Panels/PatternEditorView.axaml": "PatternEditor",
    "Views/Panels/StepSequencerView.axaml": "StepSeq",
    "Views/Panels/InstrumentRackView.axaml": "InstrumentRack",
    "Views/Panels/EffectHeaderView.axaml": "EffectHeader",
    "Views/Panels/ProjectClipsView.axaml": "ProjectClips",
    "Views/Panels/LibraryListView.axaml": "LibraryList",
    "Views/Panels/LibraryOptionsView.axaml": "LibraryOptions",
    "Views/Panels/PreviewPanelView.axaml": "Preview",
    "Views/Panels/FileBrowserView.axaml": "FileBrowser",
    "Views/Panels/ParametersView.axaml": "Parameters",
    "Views/Panels/GroupedParametersView.axaml": "GroupedParams",
    "Views/Panels/EffectsView.axaml": "Effects",
    "Views/MainView.axaml": "MainView",
    "Views/Field/FieldEditorView.axaml": "FieldEditor",
    "Views/Settings/AudioSettingsView.axaml": "SettingsAudio",
    "Views/Settings/MidiSettingsView.axaml": "SettingsMidi",
    "Views/Settings/ThemeEditorView.axaml": "SettingsTheme",
    "Views/Settings/LibrarySettingsView.axaml": "SettingsLibrary",
    "Views/Settings/ControlSurfaceSettingsView.axaml": "SettingsControlSurface",
    "Views/Settings/ControlRoomSettingsView.axaml": "SettingsControlRoom",
}


def slug(s: str, max_len: int = 48) -> str:
    s = s.strip()
    s = re.sub(r"[^\w\s\-/()]", "", s)
    s = re.sub(r"[\s/\-()]+", "_", s)
    s = re.sub(r"_+", "_", s).strip("_")
    if not s:
        s = "Text"
    if s[0].isdigit():
        s = "N" + s
    return s[:max_len]


def prefix_for(rel: str) -> str:
    rel = rel.replace("\\", "/")
    if rel in PREFIX_MAP:
        return PREFIX_MAP[rel]
    parts = Path(rel).parts
    if len(parts) >= 2:
        return slug(parts[-1].replace(".axaml", ""), 24)
    return "App"


def should_skip(value: str) -> bool:
    if not value or value in SKIP_EXACT:
        return True
    if value.startswith(SKIP_PREFIXES):
        return True
    if re.fullmatch(r"[\u2190-\u21FF\u25B6\u25C0\u25B2\u25BC↶↷▸◂▴▾\s]+", value):
        return True
    if value in ("New ▾", "Export ▾", "Layout ▾", "View ▾"):
        return True
    return False


def attr_suffix(attr: str) -> str:
    if attr == "ToolTip.Tip":
        return "Tip"
    if attr == "AutomationProperties.Name":
        return "A11yName"
    if attr == "AutomationProperties.HelpText":
        return "A11yHelp"
    if attr == "PlaceholderText":
        return "Placeholder"
    if attr == "Title":
        return "Title"
    return ""


def make_key(prefix: str, attr: str, value: str, global_used: dict[str, str]) -> str:
    suf = attr_suffix(attr)
    base = slug(value)
    key = f"{prefix}_{base}"
    if suf and not key.endswith(suf):
        key = f"{key}_{suf}"
    original = key
    n = 2
    while key in global_used and global_used[key] != value:
        key = f"{original}_{n}"
        n += 1
    global_used[key] = value
    return key


def escape_xml(s: str) -> str:
    return (
        s.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )


MANUAL_ENTRIES = [
    ("App_Name", "Ongenet"),
    ("Dialog_OK", "OK"),
    ("Dialog_Cancel", "Cancel"),
    ("Dialog_Yes", "Yes"),
    ("Dialog_No", "No"),
    ("Dialog_Close", "Close"),
    ("Dialog_Dismiss", "Dismiss"),
    ("Dialog_Recover", "Recover"),
    ("Dialog_Confirm", "Confirm"),
    ("Menu_DeleteBand", "Delete band"),
    ("Menu_DeleteNode", "Delete node"),
    ("Menu_DisconnectAll", "Disconnect all"),
    ("Menu_ResetDefault", "Reset to default"),
    ("Menu_CreateAutomation", "Create automation track"),
    ("Menu_MidiLearn", "MIDI learn"),
    ("Menu_RemoveMidiMapping", "Remove MIDI mapping (CC {0})"),
    ("Menu_EnterGroup", "Enter \"{0}\""),
    ("Control_OpenInWindow", "⤢ Open in window"),
    ("Control_NoSfzLoaded", "No SFZ loaded"),
    ("A11y_Transport", "Transport"),
    ("A11y_Timeline", "Timeline"),
    ("A11y_Library", "Library"),
    ("A11y_Mixer", "Mixer"),
    ("A11y_PianoRoll", "Piano roll"),
    ("Settings_LocaleSystem", "System default"),
    ("Settings_LocaleEnglish", "English"),
    ("Settings_LocaleJapanese", "日本語"),
    ("Settings_TabGeneral", "General"),
    ("Settings_Language", "Language"),
    ("Settings_LanguageTip", "User interface language"),
    ("Settings_RestartNote", "Some open windows may need to be reopened to refresh all text."),
    ("Guide_GettingStarted_Title", "Getting started"),
    ("Guide_Timeline_Title", "Timeline & clips"),
    ("Guide_Session_Title", "Session & patterns"),
    ("Guide_Field_Title", "Field modular"),
    ("Guide_Mixer_Title", "Mixer & export"),
    ("Guide_Shortcuts_Title", "Keyboard shortcuts"),
    ("TransportAction_PlayPause", "Play / Pause"),
    ("TransportAction_Stop", "Stop"),
    ("TransportAction_Record", "Record"),
    ("Status_Detecting", "Detecting…"),
    ("Status_NativeNotStretched", "Native (not stretched)"),
    ("Status_NoSelection", "No selection"),
    ("Status_PlaylistOff", "Playlist off"),
    ("Status_EmDash", "—"),
    ("ProjectClips_BarSingular", "{0} bar"),
    ("ProjectClips_BarPlural", "{0} bars"),
    ("ProjectClips_Beats", "{0} beats"),
]


def extract_occurrences(path: Path) -> list[tuple[str, str, int]]:
    """Return (attr, value, position) for each localizable attribute."""
    text = path.read_text(encoding="utf-8")
    results: list[tuple[str, str, int]] = []
    for attr in ATTRS:
        for m in re.finditer(rf'({re.escape(attr)})="([^"]*)"', text):
            value = m.group(2)
            if not should_skip(value):
                results.append((attr, value, m.start()))
    return results


def main() -> None:
    axaml_files = sorted(APP.rglob("*.axaml"))
    axaml_files = [
        p for p in axaml_files
        if "Strings." not in p.name
        and p.name not in ("Icons.axaml",)
        and "ControlThemes" not in str(p)
        and "AppStyles" not in str(p)
    ]

    global_used: dict[str, str] = {}
    entries: list[tuple[str, str]] = []
    file_replacements: dict[str, list[tuple[str, str, str]]] = {}

    for k, v in MANUAL_ENTRIES:
        global_used[k] = v
        entries.append((k, v))

    for path in axaml_files:
        rel = str(path.relative_to(APP)).replace("\\", "/")
        prefix = prefix_for(rel)
        replacements: list[tuple[str, str, str]] = []
        for attr, value, _ in extract_occurrences(path):
            key = make_key(prefix, attr, value, global_used)
            replacements.append((attr, value, key))
            if (key, value) not in entries and key in dict(entries):
                pass
            elif not any(e[0] == key for e in entries):
                entries.append((key, value))
        file_replacements[rel] = replacements

    entries.sort(key=lambda x: x[0])

    lines = [
        '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
        '                    xmlns:system="using:System">',
        "    <!-- UI strings (English). Keys shared across locales. -->",
    ]
    for key, value in entries:
        lines.append(f'    <system:String x:Key="{key}">{escape_xml(value)}</system:String>')
    lines.append("</ResourceDictionary>")
    out = APP / "Resources" / "Strings.en.axaml"
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Wrote {len(entries)} keys to {out}")

    for path in axaml_files:
        rel = str(path.relative_to(APP)).replace("\\", "/")
        text = path.read_text(encoding="utf-8")
        original = text
        for attr, value, key in file_replacements.get(rel, []):
            old = f'{attr}="{value}"'
            new = attr + '="{DynamicResource ' + key + '}"'
            if old in text:
                text = text.replace(old, new, 1)
        if text != original:
            path.write_text(text, encoding="utf-8")

    print(f"Updated {len(axaml_files)} axaml files")


if __name__ == "__main__":
    main()
