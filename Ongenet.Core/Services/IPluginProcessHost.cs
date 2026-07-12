using System.Threading;
using System.Threading.Tasks;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Services;

/// <summary>
/// Out-of-process plugin host bridge — isolates plugin crashes from the main DAW process.
/// Desktop head provides a real implementation; other heads use the in-process fallback.
/// </summary>
public interface IPluginProcessHost
{
    bool IsIsolationEnabled { get; }
    Task<bool> TryLaunchPluginHostAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates an isolated effect proxy when isolation is enabled; otherwise null.</summary>
    IAudioEffect? TryCreateIsolatedEffect(string modulePath, string uid, string displayName);
}

/// <summary>In-process fallback — plugins run in the main process (legacy behaviour).</summary>
public sealed class InProcessPluginHost : IPluginProcessHost
{
    public bool IsIsolationEnabled => false;
    public Task<bool> TryLaunchPluginHostAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public IAudioEffect? TryCreateIsolatedEffect(string modulePath, string uid, string displayName) => null;
}
