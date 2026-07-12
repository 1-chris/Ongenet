// Sets a 4-bar loop region starting at beat 0.
api.SetLoopRegion(0, 16);
api.Log($"Loop region: {api.GetLoopRegion().Start} – {api.GetLoopRegion().End} beats");
