using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Ongenet.Scripting.Editor.Controls;

public partial class ScriptEditorControl : UserControl
{
    private ScriptEditorSession? _session;
    private bool _syncingText;

    public ScriptEditorControl() => InitializeComponent();

    public ScriptEditorSession? Session
    {
        get => _session;
        set
        {
            if (_session is not null)
            {
                _session.AnalysisUpdated -= OnAnalysisUpdated;
                _session.TextChanged -= OnSessionTextChanged;
            }

            _session = value;
            if (_session is null) return;

            _session.AnalysisUpdated += OnAnalysisUpdated;
            _session.TextChanged += OnSessionTextChanged;
            SyncEditorText(_session.Text);
            RefreshHighlight();
            RefreshLineNumbers();
        }
    }

    private void OnSessionTextChanged(string text)
    {
        if (_syncingText) return;
        if (string.Equals(EditorBox.Text, text, StringComparison.Ordinal)) return;
        SyncEditorText(text, resetCaret: true);
    }

    private void SyncEditorText(string text, bool resetCaret = false)
    {
        _syncingText = true;
        try
        {
            EditorBox.Text = text;
            if (resetCaret)
                EditorBox.CaretIndex = 0;
            else
                EditorBox.CaretIndex = Math.Clamp(EditorBox.CaretIndex, 0, text.Length);
            RefreshLineNumbers();
            RefreshHighlight(forcePlainText: resetCaret);
        }
        finally
        {
            _syncingText = false;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EditorBox.TextChanged += EditorBox_TextChanged;
        EditorBox.PropertyChanged += EditorBox_PropertyChanged;
        EditorScroll.ScrollChanged += EditorScroll_ScrollChanged;
        CompletionList.SelectionChanged += CompletionList_SelectionChanged;
        CompletionList.DoubleTapped += (_, _) => ApplySelectedCompletion();
        ScriptEditorTheme.ThemeChanged += OnThemeChanged;
        ApplyTheme();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ScriptEditorTheme.ThemeChanged -= OnThemeChanged;
        EditorBox.TextChanged -= EditorBox_TextChanged;
        EditorBox.PropertyChanged -= EditorBox_PropertyChanged;
        EditorScroll.ScrollChanged -= EditorScroll_ScrollChanged;
        CompletionList.SelectionChanged -= CompletionList_SelectionChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnThemeChanged() => Dispatcher.UIThread.Post(ApplyTheme);

    private void ApplyTheme()
    {
        EditorRoot.Background = ScriptEditorTheme.EditorBackground;
        LineNumbers.Foreground = ScriptEditorTheme.Gutter;
        EditorBox.CaretBrush = ScriptEditorTheme.Caret;
        EditorBox.SelectionBrush = ScriptEditorTheme.SelectionFill;
        EditorBox.SelectionForegroundBrush = Brushes.Transparent;
        SignatureText.Foreground = ScriptEditorTheme.PopupForeground;
        CompletionBorder.Background = ScriptEditorTheme.PopupBackground;
        CompletionBorder.BorderBrush = ScriptEditorTheme.PopupBorder;
        SignatureBorder.Background = ScriptEditorTheme.PopupBackground;
        SignatureBorder.BorderBrush = ScriptEditorTheme.PopupBorder;
        RefreshHighlight();
    }

    private void EditorBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_session is null || _syncingText) return;
        var text = EditorBox.Text ?? string.Empty;
        if (string.Equals(_session.Text, text, StringComparison.Ordinal)) return;
        _session.NotifyTextEdited(text);
        _session.CaretOffset = EditorBox.CaretIndex;
        RefreshLineNumbers();
        RefreshHighlight();
    }

    private void EditorBox_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_session is null) return;
        if (e.Property == TextBox.CaretIndexProperty)
        {
            _session.CaretOffset = EditorBox.CaretIndex;
            UpdateSignaturePopup();
        }
    }

    private void EditorScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        GutterScroll.Offset = new Vector(0, EditorScroll.Offset.Y);
    }

    private void OnAnalysisUpdated()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyAnalysisResults();
            return;
        }

        Dispatcher.UIThread.Post(ApplyAnalysisResults, DispatcherPriority.Input);
    }

    private string? _lastRenderedText;
    private int _lastRenderedSpanVersion;

    private void ApplyAnalysisResults()
    {
        if (_session is not null && _session.HighlightSpanVersion != _lastRenderedSpanVersion)
            RefreshHighlight();
        UpdateCompletionPopup();
        UpdateSignaturePopup();
    }

    private void RefreshHighlight(bool forcePlainText = false)
    {
        if (_session is null) return;
        var text = EditorBox.Text ?? string.Empty;
        if (text.Length == 0)
        {
            HighlightLayer.Text = string.Empty;
            HighlightLayer.Inlines = null;
            _lastRenderedText = string.Empty;
            _lastRenderedSpanVersion = _session.HighlightSpanVersion;
            return;
        }

        if (forcePlainText)
        {
            HighlightLayer.Inlines = null;
            HighlightLayer.Text = text;
            HighlightLayer.Foreground = ScriptEditorTheme.Default;
            _lastRenderedText = text;
            _lastRenderedSpanVersion = -1;
            return;
        }

        if (text == _lastRenderedText && _session.HighlightSpanVersion == _lastRenderedSpanVersion)
            return;

        ApplyHighlightInlines(text, _session.HighlightSpans);
        _lastRenderedText = text;
        _lastRenderedSpanVersion = _session.HighlightSpanVersion;
    }

    private void ApplyHighlightInlines(string text, IReadOnlyList<ScriptHighlightSpan> spans)
    {
        var ordered = spans.OrderBy(s => s.Start).ToList();
        var inlines = new InlineCollection();
        var index = 0;
        foreach (var span in ordered)
        {
            if (span.Start >= text.Length) break;
            var start = span.Start;
            var length = Math.Min(span.Length, text.Length - start);
            if (length <= 0) continue;
            if (start < index) continue;

            if (start > index)
                inlines.Add(new Run(text[index..start]) { Foreground = ScriptEditorTheme.Default });

            inlines.Add(new Run(text.Substring(start, length)) { Foreground = ScriptEditorTheme.BrushFor(span.Kind) });
            index = start + length;
        }

        if (index < text.Length)
            inlines.Add(new Run(text[index..]) { Foreground = ScriptEditorTheme.Default });

        HighlightLayer.Text = string.Empty;
        HighlightLayer.Inlines = inlines;
    }

    private void RefreshLineNumbers()
    {
        var text = EditorBox.Text ?? string.Empty;
        var lines = text.Length == 0 ? 1 : text.Split('\n').Length;
        var sb = new StringBuilder();
        for (var i = 1; i <= lines; i++)
        {
            if (i > 1) sb.AppendLine();
            sb.Append(i);
        }

        LineNumbers.Text = sb.ToString();
    }

    private void UpdateCompletionPopup()
    {
        if (_session is null || _session.Completions.Count == 0)
        {
            CompletionPopup.IsOpen = false;
            return;
        }

        CompletionList.ItemsSource = _session.Completions;
        CompletionList.SelectedIndex = 0;
        CompletionPopup.IsOpen = true;
    }

    private void UpdateSignaturePopup()
    {
        if (_session?.SignatureHelp is not { } sig)
        {
            SignaturePopup.IsOpen = false;
            return;
        }

        SignatureText.Text = sig.Text;
        SignaturePopup.IsOpen = true;
    }

    private void CompletionList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Description shown via list item content when available.
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_session is null) return;

        if (CompletionPopup.IsOpen)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                ApplySelectedCompletion();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                CompletionPopup.IsOpen = false;
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Space && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            _session.ShowCompletion();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.OemPeriod || e.Key == Key.OemComma)
        {
            Dispatcher.UIThread.Post(() => _session.ShowCompletion(), DispatcherPriority.Input);
        }

        if (e.Key == Key.OemOpenBrackets)
            Dispatcher.UIThread.Post(UpdateSignaturePopup, DispatcherPriority.Input);
    }

    private void ApplySelectedCompletion()
    {
        if (_session is null) return;
        if (CompletionList.SelectedItem is not ScriptCompletionItem item) return;
        if (!_session.TryApplyCompletion(item)) return;
        SyncEditorText(_session.Text);
        EditorBox.CaretIndex = _session.CaretOffset;
        CompletionPopup.IsOpen = false;
    }
}
