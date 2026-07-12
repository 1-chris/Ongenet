#!/usr/bin/env python3
"""Generate Strings.ja.axaml from Strings.en.axaml with Japanese DAW-oriented translations."""

from __future__ import annotations

import re
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EN = ROOT / "Ongenet.App" / "Resources" / "Strings.en.axaml"
JA = ROOT / "Ongenet.App" / "Resources" / "Strings.ja.axaml"

NS = {"x": "http://schemas.microsoft.com/winfx/2006/xaml"}

# Exact English → Japanese (longest phrases first when applying ordered replacements below)
PHRASES: list[tuple[str, str]] = [
    ("System default", "システムの既定"),
    ("User interface language", "ユーザーインターフェースの言語"),
    ("Some open windows may need to be reopened to refresh all text.", "一部のウィンドウは、すべてのテキストを更新するために再度開く必要がある場合があります。"),
    ("Language and general preferences", "言語と一般設定"),
    ("General settings", "一般設定"),
    ("Getting started", "はじめに"),
    ("Timeline & clips", "タイムラインとクリップ"),
    ("Session & patterns", "セッションとパターン"),
    ("Field modular", "Field モジュラー"),
    ("Mixer & export", "ミキサーとエクスポート"),
    ("Keyboard shortcuts", "キーボードショートカット"),
    ("Play / Pause", "再生 / 一時停止"),
    ("Export timeline XML (post handoff)", "タイムライン XML をエクスポート（ポスト連携）"),
    ("Custom XML timeline summary for video/post workflows — not binary AAF/OMF.", "動画/ポスト向けのカスタム XML タイムライン概要 — バイナリ AAF/OMF ではありません。"),
    ("Custom Ongenet timeline XML for video/post reference — not binary AAF/OMF", "動画/ポスト参照用の Ongenet タイムライン XML — バイナリ AAF/OMF ではありません"),
    ("Remove MIDI mapping (CC {0})", "MIDI マッピングを削除 (CC {0})"),
    ("Enter \"{0}\"", "「{0}」に入る"),
    ("No SFZ loaded", "SFZ が読み込まれていません"),
    ("⤢ Open in window", "⤢ ウィンドウで開く"),
    ("Create automation track", "オートメーショントラックを作成"),
    ("Reset to default", "既定値にリセット"),
    ("Delete band", "バンドを削除"),
    ("Delete node", "ノードを削除"),
    ("Disconnect all", "すべて切断"),
    ("MIDI learn", "MIDI ラーン"),
    ("Native (not stretched)", "ネイティブ（ストレッチなし）"),
    ("No selection", "選択なし"),
    ("Playlist off", "プレイリスト OFF"),
    ("{0} bar", "{0} 小節"),
    ("{0} bars", "{0} 小節"),
    ("{0} beats", "{0} 拍"),
    ("Piano roll", "ピアノロール"),
    ("Control Surface", "コントロールサーフェス"),
    ("Control Room", "コントロールルーム"),
    ("Soundfonts", "サウンドフォント"),
    ("Audio devices", "オーディオデバイス"),
    ("Audio to MIDI", "オーディオ to MIDI"),
    ("New project", "新規プロジェクト"),
    ("Open project", "プロジェクトを開く"),
    ("Save project", "プロジェクトを保存"),
    ("Save project as…", "名前を付けてプロジェクトを保存…"),
    ("Save As", "名前を付けて保存"),
    ("Export and collaboration", "エクスポートとコラボレーション"),
    ("Save or restore window layout", "ウィンドウレイアウトを保存/復元"),
    ("In-app guide", "アプリ内ガイド"),
    ("Settings (audio, MIDI, theme)", "設定（オーディオ、MIDI、テーマ）"),
    ("Collapse / expand panel", "パネルを折りたたむ / 展開"),
    ("Play (Space)", "再生 (Space)"),
    ("Stop (Space)", "停止 (Space)"),
    ("Record (one-bar count-in into armed tracks)", "録音（アームしたトラックへ 1 小節カウントイン）"),
    ("Start playback from the playhead", "再生ヘッドから再生を開始"),
    ("Stop playback and return to start", "再生を停止して先頭に戻る"),
    ("Record into armed tracks with count-in", "カウントイン付きでアームしたトラックに録音"),
    ("WASAPI exclusive mode (lower latency)", "WASAPI 排他モード（低レイテンシ）"),
    ("Send MIDI clock (24 PPQN)", "MIDI クロックを送信 (24 PPQN)"),
    ("Choose a folder to scan", "スキャンするフォルダを選択"),
    ("Detecting…", "検出中…"),
    ("Projects", "プロジェクト"),
    ("Everything", "すべて"),
    ("Samples", "サンプル"),
    ("Instruments", "インストゥルメント"),
    ("Effects", "エフェクト"),
    ("Transport", "トランスポート"),
    ("Timeline", "タイムライン"),
    ("Library", "ライブラリ"),
    ("Mixer", "ミキサー"),
    ("Settings", "設定"),
    ("General", "一般"),
    ("Language", "言語"),
    ("Theme", "テーマ"),
    ("Audio", "オーディオ"),
    ("Output", "出力"),
    ("Input", "入力"),
    ("Record", "録音"),
    ("Play", "再生"),
    ("Stop", "停止"),
    ("Pause", "一時停止"),
    ("Undo", "元に戻す"),
    ("Redo", "やり直し"),
    ("Export", "エクスポート"),
    ("Import", "インポート"),
    ("Cancel", "キャンセル"),
    ("Close", "閉じる"),
    ("Apply", "適用"),
    ("Delete", "削除"),
    ("Remove", "削除"),
    ("Add", "追加"),
    ("New", "新規"),
    ("Open", "開く"),
    ("Save", "保存"),
    ("Copy", "コピー"),
    ("Paste", "貼り付け"),
    ("Duplicate", "複製"),
    ("Rename", "名前を変更"),
    ("Mute", "ミュート"),
    ("Solo", "ソロ"),
    ("Arm", "アーム"),
    ("Volume", "音量"),
    ("Pan", "パン"),
    ("Tempo", "テンポ"),
    ("Track", "トラック"),
    ("Tracks", "トラック"),
    ("Clip", "クリップ"),
    ("Clips", "クリップ"),
    ("Pattern", "パターン"),
    ("Patterns", "パターン"),
    ("Session", "セッション"),
    ("Scene", "シーン"),
    ("Scenes", "シーン"),
    ("Marker", "マーカー"),
    ("Markers", "マーカー"),
    ("Loop", "ループ"),
    ("Guide", "ガイド"),
    ("History", "履歴"),
    ("Logs", "ログ"),
    ("Layout", "レイアウト"),
    ("View", "表示"),
    ("Help", "ヘルプ"),
    ("Yes", "はい"),
    ("No", "いいえ"),
    ("OK", "OK"),
    ("Dismiss", "閉じる"),
    ("Recover", "復元"),
    ("Confirm", "確認"),
    ("English", "English"),
    ("日本語", "日本語"),
    ("Ongenet", "Ongenet"),
    ("—", "—"),
]

WORD_PHRASES: list[tuple[str, str]] = [
    (" settings", " 設定"),
    (" device", " デバイス"),
    (" devices", " デバイス"),
    (" track", " トラック"),
    (" tracks", " トラック"),
    (" clip", " クリップ"),
    (" clips", " クリップ"),
    (" pattern", " パターン"),
    (" mixer", " ミキサー"),
    (" export", " エクスポート"),
    (" import", " インポート"),
    (" recording", " 録音"),
    (" playback", " 再生"),
    (" project", " プロジェクト"),
    (" instrument", " インストゥルメント"),
    (" effect", " エフェクト"),
    (" effects", " エフェクト"),
    (" preset", " プリセット"),
    (" presets", " プリセット"),
    (" sample", " サンプル"),
    (" samples", " サンプル"),
    (" folder", " フォルダ"),
    (" file", " ファイル"),
    (" window", " ウィンドウ"),
    (" panel", " パネル"),
    (" enabled", " 有効"),
    (" disabled", " 無効"),
    (" selected", " 選択"),
    (" selection", " 選択"),
    (" default", " 既定"),
    (" velocity", " ベロシティ"),
    (" octave", " オクターブ"),
    (" octaves", " オクターブ"),
    (" direction", " 方向"),
    (" rate", " レート"),
    (" gate", " ゲート"),
    (" analyze", " 解析"),
    (" create", " 作成"),
    (" edit", " 編集"),
    (" editor", " エディター"),
    (" routing", " ルーティング"),
    (" matrix", " マトリクス"),
    (" groove", " グルーヴ"),
    (" quantize", " クオンタイズ"),
    (" swing", " スウィング"),
    (" shuffle", " シャッフル"),
    (" stretch", " ストレッチ"),
    (" warp", " ワープ"),
    (" fade", " フェード"),
    (" gain", " ゲイン"),
    (" pitch", " ピッチ"),
    (" note", " ノート"),
    (" notes", " ノート"),
    (" chord", " コード"),
    (" chords", " コード"),
    (" drum", " ドラム"),
    (" map", " マップ"),
    (" zone", " ゾーン"),
    (" zones", " ゾーン"),
    (" sampler", " サンプラー"),
    (" channel", " チャンネル"),
    (" channels", " チャンネル"),
    (" bus", " バス"),
    (" send", " センド"),
    (" monitor", " モニター"),
    (" cue", " キュー"),
    (" surround", " サラウンド"),
    (" stereo", " ステレオ"),
    (" mono", " モノ"),
    (" backend", " バックエンド"),
    (" latency", " レイテンシ"),
    (" shortcut", " ショートカット"),
    (" shortcuts", " ショートカット"),
    (" profile", " プロファイル"),
    (" profiles", " プロファイル"),
    (" mapping", " マッピング"),
    (" mappings", " マッピング"),
    (" automation", " オートメーション"),
    (" parameter", " パラメーター"),
    (" parameters", " パラメーター"),
    (" preview", " プレビュー"),
    (" render", " レンダー"),
    (" freeze", " フリーズ"),
    (" separate", " 分離"),
    (" stems", " ステム"),
    (" notation", " 記譜"),
    (" video", " ビデオ"),
    (" audio", " オーディオ"),
    (" MIDI", " MIDI"),
    (" BPM", " BPM"),
]


def translate_value(en: str) -> str:
    if en in ("Ongenet", "OK", "M", "S", "—", "English", "日本語"):
        return en
    if en in ("PR", "ST"):
        return en

    result = en
    for src, dst in sorted(PHRASES, key=lambda x: -len(x[0])):
        if src in result:
            result = result.replace(src, dst)

    # Title case / sentence word replacements (only if still mostly ASCII)
    if sum(1 for c in result if ord(c) < 128) > len(result) * 0.6:
        for src, dst in sorted(WORD_PHRASES, key=lambda x: -len(x[0])):
            result = re.sub(re.escape(src.strip()), dst.strip(), result, flags=re.IGNORECASE)

    # Common prefixes
    result = re.sub(r"^Add ", "追加: ", result)
    result = re.sub(r"^Remove ", "削除: ", result)
    result = re.sub(r"^Delete ", "削除: ", result)
    result = re.sub(r"^Create ", "作成: ", result)
    result = re.sub(r"^Open ", "開く: ", result)
    result = re.sub(r"^Save ", "保存: ", result)
    result = re.sub(r"^Export ", "エクスポート: ", result)
    result = re.sub(r"^Import ", "インポート: ", result)

    return result


def escape_xml(s: str) -> str:
    return (
        s.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )


def parse_en(path: Path) -> list[tuple[str, str]]:
    text = path.read_text(encoding="utf-8")
    entries: list[tuple[str, str]] = []
    for m in re.finditer(r'x:Key="([^"]+)"[^>]*>([^<]*)</system:String>', text):
        entries.append((m.group(1), m.group(2)))
    return entries


def main() -> None:
    entries = parse_en(EN)
    lines = [
        '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
        '                    xmlns:system="using:System">',
        "    <!-- UI strings (Japanese). Keys must match Strings.en.axaml. -->",
    ]
    for key, value in entries:
        ja = translate_value(value)
        lines.append(f'    <system:String x:Key="{key}">{escape_xml(ja)}</system:String>')
    lines.append("</ResourceDictionary>")
    JA.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Wrote {len(entries)} keys to {JA}")


if __name__ == "__main__":
    main()
