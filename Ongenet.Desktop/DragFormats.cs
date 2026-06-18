namespace Ongenet.Desktop
{
    /// <summary>Custom drag-and-drop data formats used within the app.</summary>
    internal static class DragFormats
    {
        /// <summary>Payload: the full path of an audio file dragged from the browser.</summary>
        public const string AudioFile = "ongenet/audiofile";

        /// <summary>Payload: the instrument type id dragged from the Instruments tab.</summary>
        public const string Instrument = "ongenet/instrument";
    }
}
