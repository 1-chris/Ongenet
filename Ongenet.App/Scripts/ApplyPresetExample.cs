// Apply a built-in instrument preset via script (see also Scripting → Export preset).
var trackId = api.AddInstrumentTrack("Preset Demo");
api.SetInstrument(trackId, 0, "kicka");
api.LoadInstrumentPreset(trackId, 0, "DnB Kick");
api.Log("Loaded DnB Kick preset on new track.");
