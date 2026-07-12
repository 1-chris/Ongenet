namespace Ongenet.App.ViewModels.Timeline;

/// <summary>Pattern-clip operations invoked from the timeline context menu.</summary>
public interface IPatternClipActions
{
    void DeletePatternClip(PatternClipViewModel clip);
    void DuplicatePatternClip(PatternClipViewModel clip);
}
