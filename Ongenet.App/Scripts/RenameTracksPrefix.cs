// Factory script: prefix non-master track names.
foreach (var entry in api.GetTrackNames())
{
    if (entry.Value == "Master") continue;
    api.RenameTrack(entry.Key, "Mix - " + entry.Value);
}
