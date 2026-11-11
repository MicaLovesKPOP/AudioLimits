# Device roaming and native Equalizer APO setup — design, not implemented

Owner report (2026-11-11): unplugging/reconnecting Corsair HS80 MAX can require selecting the headset in Equalizer APO again; Audio Limits doesn't refresh after external Device Selector changes until the app restarts. Observed saved settings contain three distinct render endpoint GUIDs under the same Corsair HS80 MAX display name, with historically different limits.

## Current verified behavior

- Settings and managed APO configuration target Windows endpoint GUIDs, which are not guaranteed to remain stable after USB port changes or driver re-enumeration.
- A running Devices page now re-reads active endpoint identities and APO registration every three seconds. This is **display refresh**, not an assurance that the APO is already active inside the Windows audio engine.
- The standard Equalizer APO Device Selector owns endpoint installation and troubleshooting. Audio Limits currently launches it instead of installing endpoint effects itself.
- Audio Limits 1.0.0 must continue to fail closed rather than raise volume when processing cannot be verified.

## Implementation goals for a follow-up version

1. Persist a stable *physical-device identity* independently of Windows render endpoint GUID. Collect defensible, privacy-minimized Windows PnP identifiers (USB VID/PID plus serial/container/parent where reliable) and verify that they survive unplug/replug and cross-port movement. Preserve per-endpoint GUID as the actual APO target.
2. Reconcile a new endpoint with prior device preferences only after a high-confidence identity match. Multiple simultaneously connected identical units, ambiguous generic USB devices, and name-only matches must ask the owner to confirm; never silently attach another person's saved limit.
3. Keep the most recently approved device-specific limit preference, but do not assume that a historic persisted limit is active for a new endpoint until that endpoint has independently verified APO support.
4. Replace the third-party Device Selector learning burden with an Audio Limits **Enable for this device** action and explanatory, bounded UAC elevation. Favor a documented upstream interface when available; do not assume that third-party forks' command-line switches are in official Equalizer APO. If an elevated integration is required, port/audit upstream installation logic, preserving and backing up third-party/driver APO registration, audio processing mode, and rollback state. No direct blind FX registry writes.
5. If enabling the effect requires Windows Audio restart/reconnect, ask at a safe moment instead of interrupting a call, changing hardware volume unexpectedly, or promising an already-active limit. A verified test or explicit failure result must drive the UI.
6. On failure, retain the existing device volume and managed configuration, explain the unresolved issue in Audio Limits itself, and expose Device Selector as an **advanced recovery** option rather than the default flow.
7. Avoid a constantly running elevated service solely to dodge UAC. Establish actual need and attack surface before considering privileged automation or triggered tasks.

## Required real-machine acceptance checks

- Same headset unplugged/replugged into the same USB port and another port; after reboot; after driver update.
- Unplugged while the app is open, minimized to taskbar, or hidden to tray.
- External Device Selector completes while Audio Limits is open: UI updates without restarting; any necessary audio-engine restart is communicated correctly.
- Two identical headsets attached at once: cannot mix their limits.
- APO registration missing/disabled, third-party effects present, limited-permission user, UAC denial, rollback.
- Hardware-linked endpoint volume stays unchanged while APO availability is uncertain. No louder transient or duplicate attenuation.
- Close X hides to tray; minimize remains on taskbar; tray Exit quits; relaunch activates existing instance.

Keep this design separate from the current UI detection fix. Do not claim these acceptance checks have passed until observed and logged on actual devices.
