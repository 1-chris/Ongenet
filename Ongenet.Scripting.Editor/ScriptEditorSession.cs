using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ongenet.Scripting.Editor;

public sealed class ScriptEditorSession : IScriptEditorSession
{
    private readonly ScriptEditorAnalysisService _analysis = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _debounce;
    private IReadOnlyList<ScriptCompletionItem> _completions = Array.Empty<ScriptCompletionItem>();
    private ScriptSignatureInfo? _signature;

    public event Action<string>? TextChanged;
    public event Action? AnalysisUpdated;

    /// <summary>Called by the editor surface on each keystroke (does not re-sync the TextBox).</summary>
    public void NotifyTextEdited(string text)
    {
        if (text == _analysis.Text) return;
        _analysis.UpdateText(text);
        TextChanged?.Invoke(text);
        ScheduleAnalysis();
    }

    public string Text
    {
        get => _analysis.Text;
        set
        {
            if (value == _analysis.Text) return;
            _analysis.UpdateText(value);
            TextChanged?.Invoke(value);
            ScheduleAnalysis();
        }
    }

    public int CaretOffset { get; set; }

    public int ErrorCount { get; private set; }
    public int WarningCount { get; private set; }
    public IReadOnlyList<ScriptCompletionItem> Completions => _completions;
    public ScriptSignatureInfo? SignatureHelp => _signature;

    public IReadOnlyList<ScriptHighlightSpan> HighlightSpans { get; private set; } = Array.Empty<ScriptHighlightSpan>();
    public IReadOnlyList<ScriptDiagnosticInfo> Diagnostics { get; private set; } = Array.Empty<ScriptDiagnosticInfo>();
    public int HighlightSpanVersion { get; private set; }

    public void LoadText(string text)
    {
        HighlightSpans = Array.Empty<ScriptHighlightSpan>();
        HighlightSpanVersion++;

        if (text == _analysis.Text)
        {
            TextChanged?.Invoke(text);
            RefreshAnalysis();
            return;
        }

        _analysis.UpdateText(text);
        TextChanged?.Invoke(text);
        RefreshAnalysis();
    }

    public async void ShowCompletion()
    {
        _completions = await _analysis.GetCompletionsAsync(CaretOffset).ConfigureAwait(false);
        AnalysisUpdated?.Invoke();
    }

    public bool TryApplyCompletion(ScriptCompletionItem item)
    {
        var result = _analysis.ApplyCompletionAsync(CaretOffset, item).GetAwaiter().GetResult();
        if (result is null) return false;
        Text = result.Value.text;
        CaretOffset = result.Value.newCaret;
        return true;
    }

    public void RefreshAnalysis()
    {
        HighlightSpans = _analysis.GetHighlightSpans();
        HighlightSpanVersion++;
        AnalysisUpdated?.Invoke();

        Diagnostics = _analysis.GetDiagnosticsAsync().GetAwaiter().GetResult();
        ErrorCount = Diagnostics.Count(d => d.IsError);
        WarningCount = Diagnostics.Count(d => !d.IsError);
        _signature = _analysis.GetSignatureHelpAsync(CaretOffset).GetAwaiter().GetResult();
        AnalysisUpdated?.Invoke();
    }

    private void RefreshHighlighting()
    {
        HighlightSpans = _analysis.GetHighlightSpans();
        HighlightSpanVersion++;
        AnalysisUpdated?.Invoke();
    }

    private void ScheduleAnalysis()
    {
        lock (_gate)
        {
            _debounce?.Cancel();
            _debounce = new CancellationTokenSource();
            var token = _debounce.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(75, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) return;
                    RefreshHighlighting();

                    await Task.Delay(125, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) return;

                    Diagnostics = _analysis.GetDiagnosticsAsync(token).GetAwaiter().GetResult();
                    ErrorCount = Diagnostics.Count(d => d.IsError);
                    WarningCount = Diagnostics.Count(d => !d.IsError);
                    _signature = _analysis.GetSignatureHelpAsync(CaretOffset, token).GetAwaiter().GetResult();
                    AnalysisUpdated?.Invoke();
                }
                catch (OperationCanceledException) { }
            }, token);
        }
    }

    public void Dispose() => _analysis.Dispose();
}
