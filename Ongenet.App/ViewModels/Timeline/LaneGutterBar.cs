namespace Ongenet.App.ViewModels.Timeline
{
    /// <summary>
    /// One vertical colour rail in a lane header's left gutter. A row renders one bar per nesting level —
    /// the ancestor groups' colours followed by the row's own colour — so a group's colour forms a
    /// continuous rail down through all of its descendants.
    /// </summary>
    public sealed class LaneGutterBar
    {
        public LaneGutterBar(string colorKey) => ColorKey = colorKey;

        /// <summary>Palette key or hex string; resolved to a brush by the view.</summary>
        public string ColorKey { get; }
    }
}
