# GitHub publish checklist — 1.0.0

Repository: `MicaLovesKPOP/AudioLimits`

## Repository

1. Commit the final 1.0.0 source.
2. Push `main`.
3. Let the included Windows GitHub Actions build run once and confirm it is green.
4. Create the stable tag from that exact commit.

No license has been selected by the project owner, so this source tree intentionally does **not** invent one.

## Release

Create tag/release:

- Tag: `v1.0.0`
- Title: `Audio Limits 1.0.0`
- **Do not** mark it as a pre-release.

After running `Build-PreRelease.cmd`, attach only:

- `release\AudioLimits-Setup.exe`
- `release\AudioLimits-1.0.0-x64.zip`

Do not manually attach a source ZIP/tarball; GitHub generates those from the tag.

## Suggested release notes

Audio Limits 1.0.0 is the first stable WinUI release.

**Recommended:** `AudioLimits-Setup.exe` performs a normal Windows installation and prepares missing prerequisites. Routine installed launches go directly to the app after setup.

**No installation:** `AudioLimits-1.0.0-x64.zip` can be extracted anywhere; run the root `AudioLimits.exe`. The implementation files are kept under `app\`. This is an extract-and-run distribution, not portable-state software.

The release includes per-playback-device limits, persistent Equalizer APO attenuation, a WinUI 3 interface, tray/startup integration, state-aware Setup, and a prerequisite/recovery launcher that can acquire missing Microsoft runtimes and Equalizer APO while leaving playback-device selection explicit.