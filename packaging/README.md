# Ongenet packaging

Native installers and portable ZIPs share the same `dotnet publish` output from [publish-desktop.sh](../publish-desktop.sh).

## Directory layout

| Path | Purpose |
|------|---------|
| `icons/` | App icons (PNG, ICO, ICNS) |
| `linux/` | `.desktop` file for AppImage / Flatpak |
| `flatpak/` | Flathub metainfo template |
| `windows/` | Inno Setup script (`ongenet.iss`) |
| `macos/` | `Info.plist` template, pkg `preinstall` / `postinstall` |

## Build commands

```bash
# Portable ZIPs (all platforms)
./publish-desktop.sh

# Linux AppImage + install helper
./scripts/build-appimage.sh linux-x64
./scripts/install-appimage.sh dist/Ongenet-*-linux-x64.AppImage

# Linux Flatpak bundle
./scripts/build-flatpak.sh linux-x64

# Windows installer (requires Inno Setup `iscc` on PATH)
./scripts/build-windows-installer.sh            # win-x64
./scripts/build-windows-installer.sh win-arm64  # win-arm64

# macOS DMG + pkg (macOS only)
./scripts/build-dmg.sh osx-arm64
./scripts/build-macos-pkg.sh osx-arm64
```

## Release tiers

| Tier | Formats |
|------|---------|
| **Installer** | `.flatpak`, `.AppImage` (+ `install-appimage.sh`), `*-setup.exe`, `.pkg`, `.dmg` |
| **Portable** | `*-portable.zip` (self-contained, extract anywhere) |

User settings live outside install folders (`~/.config/Ongenet`, `%AppData%\Ongenet`, `~/Library/Application Support/Ongenet`). Upgrading an installer preserves them.

## Flathub

See [flatpak/FLATHUB.md](flatpak/FLATHUB.md) for submitting to Flathub after validating a local flatpak build.
