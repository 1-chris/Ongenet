namespace Ongenet.Desktop.ViewModels.Timeline
{
    /// <summary>Clip-level operations a clip's context menu invokes, implemented by the timeline.</summary>
    public interface IClipActions
    {
        void DuplicateClip(ClipViewModel clip);
        void DeleteClip(ClipViewModel clip);
        void ReverseClip(ClipViewModel clip);
    }
}
