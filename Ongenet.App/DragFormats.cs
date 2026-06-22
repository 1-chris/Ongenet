using Avalonia.Input;

namespace Ongenet.App
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

        /// <summary>Payload: the effect type id dragged from the Effects library tab.</summary>
        public static readonly DataFormat<string> Effect = DataFormat.CreateStringApplicationFormat("net.ongenet.effect");

        /// <summary>Payload: the full path of an <c>.ongenpreset</c> dragged from a Presets tab.</summary>
        public static readonly DataFormat<string> Preset = DataFormat.CreateStringApplicationFormat("net.ongenet.preset");

        /// <summary>Payload: the full path of an FX-chain <c>.ongenpreset</c> dragged from the FX Chains tab.</summary>
        public static readonly DataFormat<string> EffectChain = DataFormat.CreateStringApplicationFormat("net.ongenet.fxchain");

        /// <summary>Payload: the full path of a sound font (.sf2/.sfz) dragged from the Soundfonts tab.</summary>
        public static readonly DataFormat<string> SoundFont = DataFormat.CreateStringApplicationFormat("net.ongenet.soundfont");
    }
}
