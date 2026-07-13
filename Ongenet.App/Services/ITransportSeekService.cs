namespace Ongenet.App.Services;

/// <summary>Moves transport playhead and start marker together (full seek).</summary>
public interface ITransportSeekService
{
    void SeekToBeat(double beat, bool snapToBar = false);
}
