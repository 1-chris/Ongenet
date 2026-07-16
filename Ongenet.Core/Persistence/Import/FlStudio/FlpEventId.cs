namespace Ongenet.Core.Persistence.Import.FlStudio;

/// <summary>FLP event ids (PyFLP / public format notes as reference; constants authored in-repo).</summary>
internal static class FlpEventId
{
    // Byte (0–63)
    public const byte MainVol = 12;
    public const byte ChanType = 21;
    public const byte MixSliceNum = 22; // RoutedTo
    public const byte PlayTruncatedNotes = 30;

    // Word (64–127)
    public const byte NewChan = 64;
    public const byte NewPat = 65;
    public const byte Tempo = 66; // coarse BPM (legacy); FineTempo preferred when both present
    public const byte CurrentPatNum = 67;
    public const byte FadeStereo = 70;
    /// <summary>Sampler / channel filter cutoff (0..1024 typical).</summary>
    public const byte Cutoff = 71; // WORD+7
    public const byte PreAmp = 74;
    public const byte MainPitch = 80;
    /// <summary>Sampler / channel filter resonance.</summary>
    public const byte Resonance = 83; // WORD+19
    public const byte TempoFine = 93; // fractional BPM * 1000 (legacy with Tempo coarse)
    /// <summary>Layer child channel index (WORD). One event per child; PyFLP <c>ChannelID.Children</c>.</summary>
    public const byte Children = 94;
    public const byte InsertIcon = 95;
    public const byte CurrentSlotNum = 98;
    public const byte NewArrangement = 99;

    // DWord (128–191)
    public const byte Color = 128;
    public const byte FineTempo = 156;
    public const byte PatternColor = 150;
    public const byte PatternLength = 164; // DWORD+36 when DWORD=128 → 164
    public const byte InsertColor = 149;

    // Text (192–207) + some later text ids used as varlen
    public const byte ChanName = 192;
    public const byte PatName = 193;
    public const byte Title = 194;
    public const byte Comment = 195;
    public const byte SampleFileName = 196;
    public const byte Version = 199;
    public const byte GeneratorName = 201;
    public const byte PluginName = 203;
    public const byte InsertName = 204;
    public const byte PlaylistTrackName = 239; // TEXT+47
    public const byte ArrangementName = 241; // TEXT+49

    // Data — PyFLP uses DATA base 208; FLParser used 210. We accept both via explicit ids.
    public const byte DelayLine = 209;       // 208+1
    public const byte NewPlugin = 210;       // 208+2
    public const byte PluginParams = 211;    // 208+3
    public const byte ChanParams = 215;      // 208+7 (Parameters) / legacy
    public const byte EnvLfo = 218;          // 208+10
    public const byte Levels = 219;          // 208+11 modern vol/pan
    public const byte BasicChanParams = 219; // same id historically
    public const byte PatternNotes = 224;    // 208+16
    public const byte InsertParams = 225;    // 208+17
    public const byte Controllers = 223;     // 208+15
    public const byte PlaylistItems = 233;   // 208+25
    public const byte InsertRoutes = 235;
    public const byte InsertFlags = 236;
}

/// <summary>FL channel rack types (<c>ChannelID.Type</c> / PyFLP <c>ChannelType</c>).</summary>
internal static class FlpChanType
{
    public const byte Sampler = 0;
    /// <summary>Stock FL generators (3x Osc, FL Keys, …) and some audio-clip channels.</summary>
    public const byte Native = 2;
    public const byte Layer = 3;
    public const byte Instrument = 4;
    public const byte Automation = 5;

    // Legacy aliases kept so older call sites / docs remain readable.
    public const byte AudioClip = Native;
    public const byte Generator = Instrument;
}
