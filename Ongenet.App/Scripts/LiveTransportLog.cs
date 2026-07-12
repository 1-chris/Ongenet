// Live script example — use Start Live in the Scripts window.
api.OnTransportStateChanged(state =>
{
    if (state == ScriptTransportState.Playing)
        api.Log($"Transport playing at {api.GetPlayheadBeats():F1} beats");
    else
        api.Log("Transport stopped.");
});

api.OnBeat(beat => api.Log($"Beat {beat:F1}"), gridBeats: 4.0);
