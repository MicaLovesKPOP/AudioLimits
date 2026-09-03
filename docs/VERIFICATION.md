# Verification ledger — 1.0.0

## Confirmed

- Preserved AudioLimits.Core tests: 50/50 pass.
- WinUI app and prerequisite launcher build successfully in Release configuration.
- Limiter transitions, persistence, Equalizer APO managed configuration, startup reconciliation and rollback behavior were exercised through the pre-release sequence.
- Devices/Settings WinUI surface, title bar and hardware-volume fallback were visually validated.
- Notification-area lifecycle and custom WinUI tray menu were exercised.
- Setup fresh/install/update/repair behavior was exercised.
- Healthy root-launcher and relocated complete-folder launch paths were validated.
- Clean Windows Sandbox bootstrap was exercised with the relevant prerequisites missing.
- Equalizer APO 1.4.2 was automatically downloaded, verified and installed by the bootstrapper; its installer-opened selector was suppressed during bootstrap.
- The app reached the explicit `Audio setup required` state afterward, and the user-invoked Device Selector opened correctly.
- The prerequisite preparation UI now exposes real cancellation rather than a misleading OK button.
- The final download pipeline performs one payload download at a time while overlapping download/verification with sequential prerequisite installation.

## 1.0.0 distribution invariants

- Public binary assets are exactly `AudioLimits-Setup.exe` and `AudioLimits-1.0.0-x64.zip`.
- Both contain/use the same canonical application layout: root `AudioLimits.exe`, internal payload under `app\`.
- Root `AudioLimits.exe` remains the obvious manual first-run/recovery entry point for extracted/copied folders.
- Setup-created Start-menu/desktop shortcuts and Setup's post-install launch target `app\AudioLimits.App.exe` directly.
- Start with Windows targets `app\AudioLimits.App.exe --background` directly.
- The no-install ZIP is not described as portable; settings remain in `%LOCALAPPDATA%\AudioLimits`.
- Setup remains the recommended distribution and remains registered in Windows Installed Apps.
- Uninstallation must not remove shared prerequisites.

## Sandbox limitation

Windows Sandbox Remote Audio does not expose a normal playback endpoint to Equalizer APO, so selecting/configuring a real playback endpoint is not testable there. The clean-machine bootstrap and Device Selector handoff are nevertheless validated.