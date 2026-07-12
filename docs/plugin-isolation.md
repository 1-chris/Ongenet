# Plugin crash isolation

Optional **out-of-process plugin hosting** protects the main DAW from VST3 effect crashes.

## Enabling isolation

1. Open **Settings → General → Plugins**
2. Enable **Isolate plugins in separate process**
3. Restart Ongenet (new effect instances use the isolated host)

When enabled and `Ongenet.PluginHost` is present beside the main executable, VST3 **effects** load
in a child process. Instruments and CLAP/LV2/AU plugins remain in-process.

## Architecture

```
Ongenet (main)  ←named pipe→  Ongenet.PluginHost (child)
     │                              │
 RemotePluginProxy              Vst3Effect
 (IAudioEffect)                 (native plugin)
```

- **PluginHostIpc** — binary protocol: `Ping`, `LoadPlugin`, `ProcessAudio`, `SetParameter`,
  `GetLatency`, `Shutdown`
- **OutOfProcessPluginHost** — launches child, probes with ping/pong, sets
  `IPluginProcessHost.IsIsolationEnabled`
- **RemotePluginProxy** — forwards audio blocks; outputs silence if the child exits unexpectedly

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| Isolation toggle has no effect | Confirm `Ongenet.PluginHost` exists next to `Ongenet` in the install folder |
| Plugin silent after crash | Disable/re-enable the effect or restart — child process exited |
| Higher CPU usage | Isolation adds IPC overhead — disable for trusted plugins |

## Publishing

`Ongenet.Desktop.csproj` copies `Ongenet.PluginHost` to the output directory on build. Release
packaging includes both executables.

## Source map

| Piece | Path |
| --- | --- |
| IPC protocol | [`PluginHostIpc.cs`](../Ongenet.Core/Services/PluginHostIpc.cs) |
| Child process | [`Ongenet.PluginHost/`](../Ongenet.PluginHost/) |
| Host launcher | [`OutOfProcessPluginHost.cs`](../Ongenet.Desktop/Services/OutOfProcessPluginHost.cs) |
| Effect proxy | [`RemotePluginProxy.cs`](../Ongenet.Desktop/Services/RemotePluginProxy.cs) |
| Seam | [`IPluginProcessHost.cs`](../Ongenet.Core/Services/IPluginProcessHost.cs) |

## Related

- [Web demo](web-demo.md) — isolation is impossible in WASM (desktop only)
- [Creating new effects](creating-effects.md) — in-process effect model the proxy implements
