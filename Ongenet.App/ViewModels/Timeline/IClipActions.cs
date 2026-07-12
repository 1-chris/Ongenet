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
        void OpenAraEditor(ClipViewModel clip);
        void OpenPitchEditor(ClipViewModel clip);
        void OpenInAudioEditor(ClipViewModel clip);
        void SetClipFades(ClipViewModel clip, double fadeInBeats, double fadeOutBeats);
        void MoveWarpMarker(ClipViewModel clip, int markerIndex, double beatPosition);
        void SendClipToSessionSlot(ClipViewModel clip);
        System.Threading.Tasks.Task ConvertAudioClipToMidiAsync(ClipViewModel clip);
        System.Threading.Tasks.Task ConvertAudioClipToPolyMidiAsync(ClipViewModel clip);
        System.Threading.Tasks.Task BounceClipInPlaceAsync(ClipViewModel clip);
        void OpenLogicalMidiEdit(ClipViewModel clip);
        void OpenAudioToMidiWizard(ClipViewModel clip);
        System.Threading.Tasks.Task SeparateStemsAsync(ClipViewModel clip);
        System.Threading.Tasks.Task SeparateStemsAsync(ClipViewModel clip,
            Ongenet.Core.Services.StemSeparationQuality quality, System.IProgress<double>? progress = null);
        void CreateLinkedCopy(ClipViewModel clip);
        void UnlinkClip(ClipViewModel clip);
        int GetLinkedInstanceCount(ClipViewModel clip);
        void CaptureRetrospectiveMidi();
        bool IsRenderingClip { get; }
    }
}
