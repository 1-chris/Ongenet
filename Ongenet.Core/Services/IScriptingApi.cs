using System;
using System.Collections.Generic;

namespace Ongenet.Core.Services;

/// <summary>Project manipulation surface exposed to user scripts.</summary>
public interface IScriptingApi
{
    string GetProjectName();
    void SetTempo(double bpm);
    void RenameTrack(Guid trackId, string name);
    IReadOnlyDictionary<Guid, string> GetTrackNames();
    void QuantizeAllMidiClips(double gridBeats);
}
