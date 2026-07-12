using Ongenet.Core.Models.Audio;

namespace Ongenet.App.ViewModels.Timeline
{
    /// <summary>Comp take-lane operations invoked from the timeline context menu.</summary>
    public interface ITakeLaneActions
    {
        void PromoteTake(TakeLaneViewModel lane);
        void FlattenComp(TakeLaneViewModel lane);
        void SplitCompAtPlayhead(TakeLaneViewModel lane);
        void AddTakeLane(Track track);
    }
}
