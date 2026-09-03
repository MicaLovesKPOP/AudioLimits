# Audio Limits continuation notes

## Current gate

`1.0.0` is the stable release. Avoid feature work unless a concrete regression or a deliberately scoped future release requires it.

## Frozen behavior

- Preserve the limiter/Equalizer APO/Core behavior and its existing tests.
- Preserve the WinUI Devices/Settings design and hardware-volume fallback wording.
- Preserve title-bar layout, tray lifecycle/menu behavior, single-instance activation and Start-with-Windows semantics.
- Preserve Setup fresh/update/repair behavior and prerequisite policy.

## Canonical app layout

```text
Audio Limits\
├─ AudioLimits.exe
└─ app\
   └─ AudioLimits.App.exe + supporting files
```

The root launcher is the obvious manual entry point for a freshly extracted/copied folder and for prerequisite recovery. Once prerequisites are prepared, routine Windows-created launch paths bypass the bootstrapper: Setup-created shortcuts/post-install launch and Start with Windows target `app\AudioLimits.App.exe` directly. `StartupService` also migrates the previous root-launcher startup registration when it encounters it.

## Public release artifacts

Only:

- `AudioLimits-Setup.exe`
- `AudioLimits-1.0.0-x64.zip`

Do not resurrect the old Portable/Standalone artifact. The ZIP is no-install, not portable-state software.

## 1.0.0 validation state

The final prerequisite bootstrap has been exercised on a clean Windows Sandbox through automatic Equalizer APO installation and app startup. Equalizer APO Device Selector can be opened explicitly afterward. Windows Sandbox remote audio does not expose a normal EAPO-selectable playback endpoint, so endpoint selection itself remains a real-PC path.