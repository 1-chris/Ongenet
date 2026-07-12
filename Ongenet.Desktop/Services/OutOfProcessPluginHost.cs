using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Ongenet.App.Services;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Services;
using Ongenet.Vst.Vst3;

namespace Ongenet.Desktop.Services;

/// <summary>
/// Desktop plugin isolation bridge — launches <c>Ongenet.PluginHost</c> beside the main binary when enabled.
/// </summary>
public sealed class OutOfProcessPluginHost : IPluginProcessHost
{
    private readonly IAppSettingsService _settings;

    public OutOfProcessPluginHost(IAppSettingsService settings)
    {
        _settings = settings;
        RefreshIsolationState();
    }

    public bool IsIsolationEnabled { get; private set; }

    public Task<bool> TryLaunchPluginHostAsync(CancellationToken cancellationToken = default)
    {
        RefreshIsolationState();
        if (!IsIsolationEnabled)
            return Task.FromResult(false);

        return Task.Run(async () =>
        {
            Process? process = null;
            PluginHostIpc.Client? client = null;
            try
            {
                var hostPath = ResolveHostExecutablePath();
                if (hostPath is null)
                    return false;

                var pipeName = PluginHostIpc.CreatePipeName(PluginHostIpc.NewInstanceId());
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = hostPath,
                    Arguments = $"--pipe {pipeName}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (process is null)
                    return false;

                client = await PluginHostIpc.Client.ConnectAsync(pipeName, TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);
                await client.PingAsync(cancellationToken).ConfigureAwait(false);
                await client.SendShutdownAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch
            {
                IsIsolationEnabled = false;
                return false;
            }
            finally
            {
                client?.Dispose();
                if (process is { HasExited: false })
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch { /* ignore */ }
                }

                process?.Dispose();
            }
        }, cancellationToken);
    }

    public IAudioEffect? TryCreateIsolatedEffect(string modulePath, string uid, string displayName)
    {
        if (!IsIsolationEnabled)
            return null;

        var typeId = Vst3PluginBase.MakeId(modulePath, uid);
        return new RemotePluginProxy(modulePath, uid, displayName, typeId);
    }

    internal static string? ResolveHostExecutablePath()
    {
        var baseDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(baseDir))
            return null;

        var fileName = OperatingSystem.IsWindows() ? "Ongenet.PluginHost.exe" : "Ongenet.PluginHost";
        var path = Path.Combine(baseDir, fileName);
        return File.Exists(path) ? path : null;
    }

    private void RefreshIsolationState()
    {
        IsIsolationEnabled = _settings.Current.PluginIsolationEnabled && ResolveHostExecutablePath() is not null;
    }
}
