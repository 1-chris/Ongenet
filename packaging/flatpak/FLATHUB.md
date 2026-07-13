# Submitting Ongenet to Flathub

This is a manual step after `scripts/build-flatpak.sh` produces a valid bundle locally and in CI.

## Prerequisites

- Local build succeeds: `./scripts/build-flatpak.sh linux-x64`
- App ID: `net.onge.Ongenet`
- License: MIT (see root `LICENSE`)

## Steps

1. Fork [github.com/flathub/flathub](https://github.com/flathub/flathub) (use their new-app issue template if required).
2. Create a repository `github.com/flathub/net.onge.Ongenet` with:
   - `net.onge.Ongenet.yml` — copy from the generated manifest in `packaging/flatpak/build/net.onge.Ongenet.yml` after a local build, or adapt `scripts/build-flatpak.sh` output.
   - `net.onge.Ongenet.metainfo.xml` — from `packaging/flatpak/` (version/date updated per release).
   - Icon and desktop files from `packaging/`.
3. Source tarball: Flathub builders run `flatpak-builder` from tagged GitHub releases. Point the manifest `sources` at the release tag and publish output, or vendor the self-contained publish folder via `extra-data` / git submodule as per Flathub review feedback.
4. Open a PR against Flathub; review typically takes 1–4 weeks.

## Until Flathub accepts the app

Distribute the CI-built `.flatpak` bundle on GitHub Releases:

```bash
flatpak install --user --bundle Ongenet-VERSION-linux-x64.flatpak
```

Updates: re-run the installer bundle, or `flatpak update net.onge.Ongenet` after Flathub publication.
