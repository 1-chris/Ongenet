namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Optional voice-state query for instruments that can skip rendering when idle.
/// </summary>
public interface IInstrumentVoiceState
{
    /// <summary>True when the instrument has at least one sounding or releasing voice.</summary>
    bool HasActiveVoices { get; }
}
