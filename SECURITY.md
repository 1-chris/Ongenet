# Security Policy

## Supported versions

Security fixes are published for the **latest release** on
[GitHub Releases](https://github.com/1-chris/Ongenet/releases).

The `main` branch may receive fixes before the next tagged release. Older
releases are not supported unless noted in a security advisory.

| Version | Supported |
| ------- | --------- |
| Latest GitHub Release | Yes |
| Older releases | No |
| `main` (development) | Best-effort; prefer upgrading to the latest release |

## Reporting a vulnerability

**Please do not open a public GitHub Issue for security vulnerabilities.**

Report issues privately using GitHub's **Private vulnerability reporting**:

1. Open [github.com/1-chris/Ongenet/security](https://github.com/1-chris/Ongenet/security)
2. Click **Report a vulnerability**
3. Describe the issue, affected component (desktop / Android), version or commit, reproduction steps, and impact

If private reporting is unavailable, contact the maintainer through GitHub (e.g. a minimal private communication channel) and ask for a security advisory — do not post exploit details publicly.

### What to include

- Ongenet version (title bar / About) or commit SHA
- Platform (Linux / Windows / macOS / Android) and architecture
- Steps to reproduce, proof-of-concept if available, and realistic impact (e.g. RCE, sandbox escape, privilege escalation, data exposure)
- Whether the issue requires loading a third-party plugin or running a user script

### Response expectations

- **Acknowledgement:** within 7 days of a valid report
- **Updates:** periodic status while investigating
- **Disclosure:** coordinated disclosure after a fix is available; we aim to publish a GitHub Security Advisory and credit reporters who wish to be named

We appreciate responsible disclosure and will not pursue legal action against researchers who follow this policy in good faith.

## In scope

Security issues in **Ongenet itself**, primarily the **desktop** application, including but not limited to:

- Remote or local code execution in the Ongenet host process beyond documented, user-consented behaviour
- Sandbox or isolation bypass in [`Ongenet.PluginHost`](docs/plugin-isolation.md) IPC (when plugin isolation is enabled)
- Unauthorised filesystem, network, or process access via the [scripting API](docs/scripting.md) beyond documented limits
- Supply-chain or integrity issues in official release artifacts (installers, portable zips, Flatpak/AppImage builds published by this project)
- Memory-safety or privilege issues in Ongenet's own native code (audio/MIDI backends, plugin host bridges, GPU engine)

The **browser demo** at [onge.net/app/](https://onge.net/app/) is a statically hosted WASM showcase with no plugins, scripting, or native host access; it runs under the browser's normal sandbox. Reports affecting only the demo (e.g. generic browser/WASM behaviour) are lower priority and may be redirected upstream unless they involve compromise of Ongenet's deployed static assets or site integrity.

## Out of scope

The following are generally **not** treated as Ongenet security vulnerabilities:

- Crashes, hangs, or audio glitches in **third-party** CLAP, LV2, VST, VST3, or AU plugins (report to the plugin vendor)
- Harm from **user-authored C# scripts** the user chose to run in-process (see [scripting security limits](docs/scripting.md#security--limits))
- Bugs with no realistic security impact (UI glitches, feature requests, general stability)
- Vulnerabilities in **upstream dependencies** (e.g. .NET runtime, Avalonia, ffmpeg, MoltenVK/Vulkan drivers) — please report upstream; we will still track and ship updates when fixes are available
- Denial-of-service from extremely large projects or pathological inputs unless they demonstrate exploitable memory corruption or RCE
- Issues already fixed on `main` or in a newer release
- Social engineering or physical access attacks

## Safe use guidance

Ongenet is a powerful local application. To reduce risk:

- Install from [official GitHub Releases](https://github.com/1-chris/Ongenet/releases) or [onge.net](https://onge.net/)
- Only load plugins and scripts from sources you trust
- Consider enabling **Settings → General → Plugins → Isolate plugins in separate process** for untrusted VST3 effects (see [plugin isolation](docs/plugin-isolation.md)); this limits crash impact but is not a full security sandbox
- Keep Ongenet updated to the latest release

## Privacy

Ongenet does not collect telemetry or personal data. See the [Privacy Policy](https://onge.net/legal/privacy.html).

## License

Ongenet is distributed under the [MIT License](LICENSE).
