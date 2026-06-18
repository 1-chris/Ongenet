using Avalonia.Input;

namespace Ongenet.Desktop
{
    /// <summary>Custom drag-and-drop data formats used within the app (Avalonia 12 DataTransfer API).</summary>
    internal static class DragFormats
    {
        // Application-format identifiers may contain only ASCII letters/digits, '.' and '-'
        // (Avalonia 12 validates this) — so reverse-DNS style, no '/'.

        /// <summary>Payload: the full path of an audio file dragged from the browser.</summary>
        public static readonly DataFormat<string> AudioFile = DataFormat.CreateStringApplicationFormat("net.ongenet.audiofile");

        /// <summary>Payload: the instrument type id dragged from the Instruments tab.</summary>
        public static readonly DataFormat<string> Instrument = DataFormat.CreateStringApplicationFormat("net.ongenet.instrument");
    }
}
