# Audio Limits 1.0.0 release gate

This file records the practical validation state used to promote the release candidate to stable 1.0.0.

## Passed

- Preserved Core test suite passes: 50/50.
- WinUI application and prerequisite launcher build successfully in Release configuration.
- Per-device limiting, change/remove limit flow, persistence, startup reconciliation and Equalizer APO integration were exercised through the pre-release sequence.
- Modern Devices/Settings UI, title bar, light/dark behavior and hardware-volume fallback were visually validated.
- Notification-area lifecycle and custom Fluent tray menu were validated.
- Setup fresh/install/update/repair flow was validated, including per-user/all-users scope and custom install location.
- Healthy root-launcher and relocated complete-folder launch paths were validated.
- Clean Windows Sandbox prerequisite bootstrap was exercised through automatic prerequisite acquisition, Equalizer APO installation, suppression of the installer-opened selector, app startup, and later explicit Device Selector launch.
- The final preparation UI uses real Cancel semantics. Download cancellation can stop immediately; a prerequisite installer already running is allowed to finish before setup exits.
- Public release output remains exactly two binary assets.

## Packaging checks

Run `Build-PreRelease.cmd` and verify:

- all 50 Core tests pass;
- WinUI app and launcher build;
- `release\AudioLimits-Setup.exe` exists;
- `release\AudioLimits-1.0.0-x64.zip` exists;
- `release\` contains no other project-generated user-facing artifact;
- the ZIP contains `Audio Limits\AudioLimits.exe` and `Audio Limits\app\AudioLimits.App.exe`.

## Sandbox limitation

Windows Sandbox's Remote Audio environment does not expose a normal Equalizer APO-selectable playback endpoint. Therefore the clean-machine test validates prerequisite installation and the explicit Device Selector handoff, while actual endpoint selection remains covered by normal real-PC use rather than by Sandbox.

## Release status

The intended 1.0.0 release gate is satisfied. Freeze behavior and publish the stable release if the final `1.0.0` build and 50-test suite pass.