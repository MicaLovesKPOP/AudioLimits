# GitHub publish checklist — 1.0.0-rc.2

Repository: `MicaLovesKPOP/AudioLimits`

## Repository

1. Put the contents of this source folder at the repository root.
2. Commit and push.
3. Let the included Windows GitHub Actions build run once; a green run is useful additional build reproducibility evidence, but it does not replace the deferred clean-VM runtime test.

No license has been selected by the project owner in the development history, so this source tree intentionally does **not** invent one. Choose/add a license separately if public reuse should be permitted.

## Release

Create tag/release:

- Tag: `v1.0.0-rc.2`
- Title: `Audio Limits 1.0.0-rc.2`
- Mark as **pre-release**.

After running `Build-PreRelease.cmd`, attach only:

- `release\AudioLimits-Setup.exe`
- `release\AudioLimits-1.0.0-rc.2-x64.zip`

Do not manually attach a source ZIP/tarball; GitHub generates those from the tag.

## Suggested release notes

Audio Limits 1.0.0-rc.2 is the first GitHub-ready release candidate of the WinUI version.

**Recommended:** `AudioLimits-Setup.exe` performs a normal Windows installation and automatically prepares missing Microsoft runtime prerequisites. Routine installed launches go directly to the app after Setup has prepared those prerequisites.

**No installation:** `AudioLimits-1.0.0-rc.2-x64.zip` can be extracted anywhere; run the root `AudioLimits.exe`. The implementation files are kept under `app\`. This is an extract-and-run distribution, not portable-state software.

The limiter, Equalizer APO integration, WinUI interface, tray behavior, Setup update/repair path, healthy launcher path, and full-folder relocation have been exercised on the development Windows PC. The remaining deferred test before final 1.0.0 is the launcher's automatic prerequisite recovery on a genuinely clean Windows VM.
