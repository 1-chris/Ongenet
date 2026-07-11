namespace Ongenet.App.ViewModels.Timeline
{
    /// <summary>Clip-level operations a clip's context menu invokes, implemented by the timeline.</summary>
    public interface IClipActions
    {
        void DuplicateClip(ClipViewModel clip);
        void DeleteClip(ClipViewModel clip);
        void ReverseClip(ClipViewModel clip);
        void RenameClip(ClipViewModel clip);
        void MakeClipUnique(ClipViewModel clip);
        int GetSharedInstanceCount(ClipViewModel clip);
        System.Threading.Tasks.Task RenderClipToNewTrackAsync(ClipViewModel clip);
        bool IsRenderingClip { get; }
    }
}
