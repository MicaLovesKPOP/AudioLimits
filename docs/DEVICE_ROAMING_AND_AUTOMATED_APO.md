# Device roaming and Equalizer APO provisioning (post-1.0.0)

Status 2026-11-11: hardware recognition and conservative remembered-limit restoration implemented
on branch `fix/window-lifecycle-apo-refresh-20261111`. **Not yet released or deployed.**
Native automatic APO endpoint provisioning is still **unimplemented**.

## Problem

USB receivers can get a new Windows MMDevice endpoint GUID after replugging, moving
ports, or a driver re-enumeration. Settings and Equalizer APO attach to that GUID;
the receiver itself can still be exactly the same hardware. On the owner's MicaPC,
historical settings held three Corsair HS80 MAX endpoint GUIDs with different caps.

## What is implemented on the development branch

- `PlaybackHardwareIdentity` walks the Windows Configuration Manager/PnP
  parent chain of an active render endpoint. It distinguishes genuine parent USB
  device serials from port-generated strings containing `&`, and normalizes a
  model/interface + playback-role key. The raw USB topology suffix isn't persisted.
- A read-only real-device probe on MicaPC returned a serialized identity for the
  Corsair HS80 MAX wireless receiver. This validates PnP lookup on that device;
  it is **not** a completed live cross-port reconnection test.
- New/changed caps persist that key in an optional field of the existing schema 3.
  On first startup, any already-configured and connected endpoint can be tagged
  with its known identity without touching its audio gain. Settings without a
  connected endpoint cannot be magically attributed to the old receiver.
- A resolver considers one active matching playback role and consistent saved
  cap percentages. A serial-based match is eligible for automatic restoration;
  model-only matches and legacy friendly-name matches require manual confirmation.
  Conflicting past cap values or concurrent ambiguous devices are never guessed.
- A 5-second tray-resident scan can restore a serialized device cap to a newly
  enumerated endpoint **only after the exact endpoint is already registered with
  Equalizer APO** and state recovery is healthy. It invokes the existing
  `LimitService.SetLimitAsync` transaction, which recalibrates the current
  endpoint and uses existing mute/rollback protections. Failed endpoints are not
  retried repeatedly in the same app session.
- A device card explains that a remembered cap is NOT active and offers a
  `Restore N% limit` action. If Device Selector was needed and its endpoint
  registration succeeds, the requested limit operation resumes without a
  second click. Every 3 seconds the visible page detects endpoint, APO
  registration, and saved-cap changes without restarting Audio Limits.
- The taskbar/tray fix remains: minimize goes to taskbar, X hides to tray, explicit
  tray Exit quits. The reconnect monitor is cancelled on Exit and a transaction
  already underway prevents a premature exit.

## What is NOT implemented

**Automatic registration of Equalizer APO to a newly created Windows endpoint.**
The official 1.4.2 Device Selector can install the APO while preserving existing
driver effects and troubleshooting modes, but does not expose a verified
single-endpoint unattended install interface. Directly writing FxProperties
would risk breaking manufacturer APOs or falsely claiming a limit is active.

We therefore do not modify system APO registrations or restart the Windows Audio
service automatically. If Windows creates a new endpoint with no APO installed,
the cap stays inactive and the user must finish selection in the existing
Device Selector. The refreshed UI notices that change without restarting.
A future one-click onboarding helper must preserve original effects, use bounded
UAC, support rollback, and test the actual audio-engine activation before claiming
success. Source-compatible forks' CLI options must not be assumed to work with
the official distribution. Avoid a permanent elevated service.

## Validation status

- All 63 Core tests pass (50 pre-existing + 13 new), including serial/port
  fixtures, conflicting old and new presets, ambiguous identical devices,
  and persistence.
- WinUI Release build passes with zero warnings/errors.
- Real MicaPC read-only detection recognizes the HS80 MAX receiver by a physical
  USB serial, not by its Windows MMDevice GUID.
- **Still required before public release:** true unplug/replug and port-switch
  acceptance tests; hot-reconnect in tray and visible UI; new port lacking APO;
  hardware-volume safety and audio-engine verification; relaunch/exit behavior;
  collision test with two identical receivers, and UAC/driver failure testing.
- Installed AudioLimits 1.0.0 is deliberately left unchanged until approved
  acceptance tests and a properly versioned update.

This remains a convenience aid, not an absolute hearing-protection guarantee.
Windows or apps may bypass the Equalizer APO audio-processing path.
