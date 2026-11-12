# AudioLimits APO activation: Windows Sandbox acceptance evidence

Date: 2026-11-11. Experimental development branch:
`fix/window-lifecycle-apo-refresh-20261111`.

## Scope and safety

The host MicaPC had only two playback endpoints (Corsair HS80 MAX and LG TV),
both already running Equalizer APO. Neither host endpoint, volume, registry, or
Windows Audio service was modified. Tests were executed in a disposable Windows
Sandbox (Windows build 26100), with networking and host audio input disabled.
A signed VB-Audio virtual cable driver supplied independent render/capture
endpoints. Equalizer APO 1.4.2 was verified against SHA-256:
`7403be7427bbe1936a40dded082829b6e217fc4f5990fee5cba501f0ae055afa`.

The system-session sandbox test used the experimental elevated helper.
It registered the intended **CABLE Input** render endpoint and created an
admin-protected backup. A second **CABLE In 16ch** playback endpoint remained
without effects. Restart-pending status cleared only after a guest reboot.
Shared-mode WASAPI playback opened. The Windows audio-engine process loaded
EqualizerAPO.dll.

## Driver mode experiment (sandbox only)

The normal installation-mode advisor selected **SFX/EFX**, registering pre-mix
in slot 5 and post-mix in slot 7. The original registry configuration was saved.
The active audio engine's documented test pipe yielded a **PreMix Initialize**
message for the exact endpoint; it did not yield PostMix Initialize in the
valid single-listener run. Separately, a multi-listener harness generated
access-denied errors and was discarded as invalid evidence.

With SFX/EFX, a 997 Hz sine supplied to CABLE Input was captured via the paired
CABLE Output. Changing global Equalizer APO preamp from -6 dB to -26 dB did
not materially change the captured amplitude, including after restarting
Windows Audio. A positive registration and DLL load therefore did **not**
establish effective limiting on that driver in the chosen installation mode.

An alternative **SFX/MFX** mode was then staged **manually in the sandbox only**
after backing up the SFX/EFX registry values. The endpoint's pre-mix stayed in
slot 5; post-mix moved to slot 6, with its default processing-mode entry.
After a guest reboot, a valid named-pipe diagnostic received both **PreMix
Initialize** and **PostMix Initialize** messages for the target. The control
endpoint still had no effect registrations.

## Measured attenuation

All three measurements used the same 48 kHz, 2-channel, 997 Hz signal, the same
virtual render/capture pair, and unchanged Windows endpoint volume
(scalar approximately 0.999999). These figures are recorded from the sandbox
reports under ignored local `artifacts/sandbox-device-test/guest/` paths.

| APO mode | Config preamp | Captured RMS | Captured peak |
|---|---:|---:|---:|
| SFX/MFX | -6 dB | 0.0337180178 | 0.0510864258 |
| SFX/MFX | -26 dB | 0.00335231237 | 0.00510787964 |
| SFX/MFX | restored -6 dB | 0.0335917885 | 0.0510864258 |

RMS ratio 0.00335231237 / 0.0337180178 = approximately 0.0994,
corresponding to approximately **-20.05 dB**. That agrees with the requested
20 dB additional attenuation. Returning to -6 dB restored the baseline.
This is direct evidence of effective processing on this disposable virtual
endpoint in SFX/MFX mode.

The SFX/EFX captured peak stayed approximately 0.100002 at both settings.
Together with the absent post-mix diagnostic event, this strongly suggests
that the driver did not initialize the post-mix EFX stage in SFX/EFX mode.
It does **not** prove that all drivers have the same behavior.

## Product implications

**The current experimental one-click installer is NOT release-ready.** It
chooses a default mode and verifies registry registration, but it does not yet
test initialization of its chosen stage or discover an alternative mode.
After reboot, a registry-based "ready" state could be misleading, as this
experiment demonstrates. A cap should not be represented as demonstrably
effective merely because its APO CLSID is in FxProperties or its DLL was
loaded by audiodg.

The engineering next step is a guarded installation/verification lifecycle
that checks actual APO initialization of the chosen stage, selects a different
supported mode when necessary, preserves original manufacturer effects, and
rolls back unsuccessful experiments. Any Windows Audio restart must be explicit
and user-controlled (or be deferred to a permitted reboot), not triggered in
the background. Signal-level testing on a suitable endpoint is still needed
to validate actual limiting beyond initialization messages.

The local user version 1.0.0, release tag, and both real host playback devices
remain unchanged. Other outstanding gates: ordinary-UAC user-session tests,
USB reconnection/port switching, differing OEM audio stacks, signed elevated
helper, license review, full installer/ZIP packaging, and final release QA.

## Upstream reference

- Equalizer APO 1.4.2 Device Selector's mode testing / fallback:
  https://github.com/mirror/equalizerapo/blob/master/DeviceSelector/DeviceTestThread.cpp
- APO init pipe protocol:
  https://github.com/mirror/equalizerapo/blob/master/EqualizerAPO/EqualizerAPO.cpp
