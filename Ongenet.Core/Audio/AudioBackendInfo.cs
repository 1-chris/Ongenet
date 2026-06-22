namespace Ongenet.Core.Audio;

/// <summary>
/// A backend entry for the audio-system picker: its id, display name, whether it runs on this OS, and
/// whether it is the currently active backend.
/// </summary>
public sealed record AudioBackendInfo(string Id, string DisplayName, bool IsSupported, bool IsActive);
