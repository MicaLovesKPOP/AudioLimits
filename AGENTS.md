# Audio Limits continuation notes

## Current gate

`1.0.0-rc.2` is the GitHub-ready release candidate. Avoid feature work unless a concrete regression is found.

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

The root launcher is the only public entry point. `AudioLimits.App.exe` is internal. `StartupService` must continue to resolve the launcher one directory above the canonical `app` folder.

## Public release artifacts

Only:

- `AudioLimits-Setup.exe`
- `AudioLimits-1.0.0-rc.2-x64.zip`

Do not resurrect the old Portable/Standalone artifact. The ZIP is no-install, not portable-state software.

## Remaining release blocker for final 1.0.0

Clean-VM validation of launcher prerequisite recovery. Healthy-machine and relocated-folder launch paths have already passed. See `PRE_RELEASE_TEST.md` before changing deployment architecture.
