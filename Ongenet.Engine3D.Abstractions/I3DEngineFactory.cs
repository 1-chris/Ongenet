namespace Ongenet.Engine3D.Abstractions;

/// <summary>
/// Creates rendering sessions for the 3D control. Registered in DI by the desktop head only (the native
/// GPU backend); the shared control resolves it at runtime and degrades gracefully to a placeholder when
/// it is absent (Browser/Android) or reports <see cref="IsAvailable"/> = false (no usable GPU).
/// </summary>
public interface I3DEngineFactory
{
    /// <summary>True when a GPU backend initialised successfully and sessions can be created.</summary>
    bool IsAvailable { get; }

    /// <summary>A short human-readable backend description (e.g. "Vulkan (MoltenVK)") for diagnostics/UI.</summary>
    string BackendName { get; }

    /// <summary>
    /// Creates a session sized to <paramref name="width"/> x <paramref name="height"/> pixels. Returns null
    /// when the backend is unavailable or session creation failed.
    /// </summary>
    I3DRenderSession? CreateSession(int width, int height);
}
