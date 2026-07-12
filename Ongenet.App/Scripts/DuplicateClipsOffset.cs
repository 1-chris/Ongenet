// Duplicates every clip on the timeline, placing the copy one clip-length later.
foreach (var clip in api.GetClips())
{
    var copyId = api.DuplicateClip(clip.Id);
    api.Log($"Duplicated '{clip.Name}' → {copyId}");
}
