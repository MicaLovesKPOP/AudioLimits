# Verification ledger — 1.0.0-rc.2

## Confirmed on the target Windows machine

- Preserved AudioLimits.Core tests: 50/50 passed in the latest supplied pre.28 build log.
- WinUI build completed with zero normal compile warnings/errors.
- Limiter transitions, persistence, Equalizer APO managed configuration, startup reconciliation and rollback behavior were exercised during the pre-release sequence.
- Devices/Settings WinUI surface, title bar and hardware-volume fallback were visually validated.
- Notification-area lifecycle and custom WinUI tray menu were exercised, including pointer dismissal, Escape/Alt+F4 and immediate keyboard navigation.
- Setup fresh/install/update/repair behavior was exercised.
- Setup prerequisite detection correctly left already-present .NET/VC++ runtimes alone and installed the missing Windows App Runtime on the target PC.
- The self-contained root launcher opens the framework-dependent app normally on the healthy target PC.
- The complete application folder was manually relocated/copied and launched successfully from the new location.

## rc.2 distribution invariants

- Public binary assets are exactly `AudioLimits-Setup.exe` and `AudioLimits-1.0.0-rc.2-x64.zip`.
- Both contain/use the same canonical application layout: root `AudioLimits.exe`, internal payload under `app\`.
- Root `AudioLimits.exe` is the only public entry point.
- Start-with-Windows resolves to the root launcher in the canonical layout.
- The no-install ZIP is not described as portable; settings remain in `%LOCALAPPDATA%\AudioLimits`.
- Setup remains the recommended distribution and remains registered in Windows Installed Apps.
- Uninstallation must not remove shared Microsoft runtimes.

## Deferred final-release verification

A clean Windows x64 VM is still required to prove the launcher's missing-prerequisite path end-to-end:

- launch with .NET/VC++/Windows App Runtime missing;
- decline path;
- Microsoft download/signature validation path;
- UAC/cancel/failure handling;
- successful acquisition and re-detection;
- restart-required handling;
- silent second launch after prerequisites exist.

Until that happens, `1.0.0-rc.2` should be published as a GitHub pre-release rather than final `1.0.0`.

See `PRE_RELEASE_TEST.md` for the exact deferred test.
