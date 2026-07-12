// Documents portable project export — use Scripting → Export project to generate from the open project.
api.Log("Run Export project in the Scripting tab to generate a portable script.");
api.Log($"Current project: {api.GetProjectName()} @ {api.GetTempo()} BPM, {api.GetTracks().Count} tracks");
