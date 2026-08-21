# Audio Limits 1.0.0-rc.2 release-candidate gate

This file records the final practical validation state before the repository is published. rc.2 intentionally avoids new limiter/UI/tray features.

## Passed on the target Windows machine

- Preserved Core test suite passes (50 tests in the latest supplied build log).
- WinUI application builds with zero ordinary compile warnings/errors.
- Per-device limiting, change/remove limit flow, persistence, startup reconciliation and Equalizer APO integration were exercised through the preceding pre-release sequence.
- Modern Devices/Settings UI, title bar, light/dark behavior and hardware-volume fallback were visually validated.
- Notification-area lifecycle and custom Fluent tray menu were validated, including pointer dismissal, Escape/Alt+F4 dismissal and immediate keyboard navigation.
- Setup fresh/install/update/repair flow was validated, including Just for me / all-users scope and custom install location.
- Setup correctly avoided reinstalling already-present .NET/VC++ prerequisites and acquired the missing Windows App Runtime on the target PC.
- Installed Audio Limits launches through the root bootstrap launcher.
- Copying/relocating the complete application folder and running the root launcher works on the healthy target PC.

## rc.2 packaging checks

Run `Build-PreRelease.cmd` and verify:

- all Core tests pass;
- WinUI app and launcher build;
- `release\AudioLimits-Setup.exe` exists;
- `release\AudioLimits-1.0.0-rc.2-x64.zip` exists;
- `release\` contains no other project-generated user-facing artifact;
- the ZIP contains `Audio Limits\AudioLimits.exe` and `Audio Limits\app\AudioLimits.App.exe`;
- `publish\AudioLimits.exe` opens Audio Limits normally;
- Start with Windows points to the root launcher, not the internal app host.

## Deferred clean-VM test

Do **not** damage the working development PC merely to perform this test. When the project is revisited, use a clean Windows x64 VM/sandbox and copy/extract the no-install folder without running Setup first.

Validate all of the following:

- root `AudioLimits.exe` runs even when .NET/VC++/Windows App Runtime prerequisites are absent;
- missing components are listed before changes are made;
- declining exits cleanly;
- accepting downloads only the missing Microsoft installers;
- each downloaded installer must pass the launcher's Microsoft Authenticode check before execution;
- UAC/cancel/error paths are understandable;
- successful installs are re-detected and Audio Limits starts;
- restart-required exit codes are handled by asking for a restart;
- a second launch after prerequisites are available is visually silent.

## Release status

Because the missing-prerequisite launcher path has not had that clean-VM test, publish `1.0.0-rc.2` as a GitHub **pre-release**, not as final `1.0.0`. Everything that can reasonably be completed on the current development machine is otherwise frozen for this candidate.
