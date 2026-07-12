namespace Ongenet.Core.Services;

/// <summary>Minimal undo-history capture seam for scripting and batch edits.</summary>
public interface IHistoryCapture
{
    void Capture(string label);
}
