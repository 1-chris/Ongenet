using System;
using Microsoft.Extensions.Logging;
using Ongenet.Engine3D.Abstractions;
using Ongenet.Engine3D.Vulkan;

namespace Ongenet.Engine3D;

/// <summary>
/// The desktop GPU engine entry point: a Vulkan-backed <see cref="I3DEngineFactory"/>. It tries to bring up
/// the Vulkan device once at construction; if no usable GPU/driver is present (or MoltenVK/libshaderc fail
/// to load) it stays unavailable instead of throwing, so the host registers it unconditionally and the 3D
/// control simply shows its placeholder. Registered in DI by the desktop head only.
/// </summary>
public sealed class VulkanEngineFactory : I3DEngineFactory, IDisposable
{
    private readonly VulkanBackend? _backend;

    public VulkanEngineFactory(ILogger? logger = null)
    {
        try
        {
            var backend = new VulkanBackend(logger);
            if (backend.TryInitialize())
            {
                _backend = backend;
                logger?.LogInformation("3D engine: {Backend} ready.", backend.Name);
            }
            else
            {
                backend.Dispose();
                logger?.LogInformation("3D engine: no usable Vulkan device; 3D controls will show a placeholder.");
            }
        }
        catch (Exception ex)
        {
            _backend = null;
            logger?.LogWarning(ex, "3D engine: Vulkan initialisation failed; 3D controls will show a placeholder.");
        }
    }

    public bool IsAvailable => _backend is { IsInitialized: true };

    public string BackendName => _backend?.Name ?? "Unavailable";

    public I3DRenderSession? CreateSession(int width, int height)
    {
        if (_backend is null) return null;
        var session = new VulkanRenderSession(_backend, width, height);
        if (session.IsValid) return session;
        session.Dispose();
        return null;
    }

    public void Dispose() => _backend?.Dispose();
}
