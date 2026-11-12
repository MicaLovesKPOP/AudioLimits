# Audio Limits one-click APO mode fallback — sandbox acceptance

**Date:** 2026-11-12
**Branch:** `fix/window-lifecycle-apo-refresh-20261111` (experimental, unreleased)

## Tested environment

- The tests ran **only** in disposable Windows Sandbox, not against the host
  Corsair HS80 MAX, LG TV, volume controls, or Windows Audio service.
- An officially signed VB-Audio virtual cable created two active playback
  endpoints, one selected target (`CABLE Input`) and one untouched
  control (`CABLE In 16ch`).
- Official Equalizer APO 1.4.2 installation archive was checked against its
  pinned SHA-256, and the sandbox-installed helper was a fresh build from this
  development branch.
- No arbitrary endpoint-GUID value was accepted: the helper re-enumerated
  the current active render device and verified its protected backup.

## Actual installation / fallback / restart sequence

| Test | Observed result |
|---|---|
| Register target as SFX/EFX | success; four expected FX values, control untouched |
| Reboot, run selected post-mix stage verification | exit **8**; `StageNotInitialized` (fail closed) |
| Run `--switch-mode` against failed endpoint | exit **13**; protected fallback journal and `ModeSwitchRestartRequired` |
| Inspect registry | post-mix moved from EFX slot 7 to MFX slot 6; original pre-mix slot retained |
| Run `--switch-mode` again | exit **14**; duplicate attempt refused, registry state unchanged |
| Run `--verify` before reboot | exit **9**; restart still required |
| Reboot, run `--verify` | exit **7**; `StageInitialized`, no diagnostic registry key left |
| Simulate crash by marking the saved mode-switch journal `ModeSwitchPrepared` | test only inside sandbox; trusted original journal retained |
| Execute `--restore` | exit **6**; slot 7 restored, slot 6 removed, `StageNotInitialized` |
| Inspect second playback endpoint | zero APO FX entries throughout testing |

The fallback changes only the four intended registry values and does not
automatically restart Windows Audio, apply attenuation, or raise endpoint
volume. The helper requires an actual failed stage-init test and refuses any
manufacturer effect or unknown layout, altered chain, altered original
registration, or second attempt. Recovery checks each current value against
the original/intended journal values before restoring it.

## Measured attenuation in fallback mode

With SFX/MFX and the same 997 Hz two-channel test signal routed through
`CABLE Input`, captured at `CABLE Output`:

| Equalizer APO preamp | Captured RMS | Captured peak |
|---|---:|---:|
| -6 dB | 0.0334216719 | 0.0510864258 |
| -26 dB | 0.00335231237 | 0.00510787964 |
| -6 dB restored | 0.0335351164 | 0.0510864258 |

The RMS ratio is approximately 0.1003, corresponding to **-19.97 dB**,
consistent with the requested -20 dB additional attenuation. Windows
endpoint volume remained approximately 0.999999 throughout. The restored
configuration returned to the baseline level.

This is stronger evidence than registry registration or observing an APO
DLL in audiodg: post-mix initialized, the captured signal was attenuated,
and the effect reversed when requested.

## Regression checks

- 150 automated Core tests pass, including additional mode-selection,
  crash-recovery, journal-structure, OEM-preservation, and duplicate-attempt
  cases.
- Full solution Release build: zero errors and zero warnings.
- The original installed Audio Limits 1.0.0 remains unchanged.

## Remaining release gates

- Elevated helper code signing, installed helper trust/path checks, GPL
  obligations, and installer/ZIP packaging validation.
- User-session UAC and UI acceptance testing, beyond system-session sandbox.
- USB replug and cross-port testing on actual consumer hardware, without
  changing existing working audio setups.
- Additional OEM driver combinations, Bluetooth modes, unusual registry
  values, service restarts, rollback failure and concurrent installer behavior.
- The stage initialization test is necessary but not an absolute hearing
  protection guarantee; future versions should consider per-device signal-level
  validation wherever possible.

**Status:** end-to-end sandbox compatibility fallback verified, **not yet
release-qualified**. Do not ship or replace the installed application.
