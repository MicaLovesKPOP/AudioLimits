# USB endpoint roaming and Audio Limits-managed Equalizer APO setup

**Status (2026-11-11): experimental implementation on**
`fix/window-lifecycle-apo-refresh-20261111`. Not shipped, deployed, or
validated on a newly provisioned physical Windows playback endpoint.

## User goal

Audio Limits should recognize a headset after USB-port changes, restore
previously chosen caps conservatively, and allow the owner to enable volume
processing for a newly created Windows audio endpoint without navigating
Equalizer APO's Device Selector. The user's normal window preference is:
Minimize -> taskbar; X -> tray; explicit tray Exit -> quit.

## Implemented

- Stable hardware identity for serialized USB devices from Windows PnP ancestry,
  with a hashed serial and playback role. A new Windows endpoint GUID is never
  assumed to be the same device based on its name alone.
- Consistent remembered caps can be restored to recognized serialized hardware.
  Model-only, old name-only, conflicting, and duplicated device matches require
  confirmation or are rejected. Reconnection monitoring also runs in the tray.
- New endpoint restoration is non-boosting: it never automatically raises
  Windows endpoint volume. The existing limiter's mute/rollback transaction
  remains authoritative.
- Read-only Windows APO preflight examines the five ordinary APO slots, three
  multi-effect slots, enhancements status, and Bluetooth combined-endpoint
  marker; supplies device-specific guidance in the WinUI Devices page.
- Native endpoint installer plan: LFX/GFX for endpoints with no FxProperties key
  or legacy-only effects, SFX/MFX for combined Bluetooth devices with a modern
  effects key, otherwise SFX/EFX. It saves the original driver effect CLSIDs and
  chains the appropriate original pre/post effects in the Equalizer APO child
  registration, following the audited official upstream 1.4.2 behavior.
- New **AudioLimits.EndpointSetup.exe** is a compact framework-dependent x64
  helper that shares libraries with the main application, launched only after
  the user selects "Enable this device" and
  approves Windows UAC. It accepts only a playback endpoint GUID. The parent
  WinUI process never elevates itself.
- The helper rechecks the active playback endpoint, Equalizer APO DLL/COM
  registration (restricted to audited 1.4.2), driver setup status, and whether
  Device Selector/Configurator is currently running before making changes.
- It stores a durable per-endpoint backup in
  `%ProgramData%\AudioLimits.EndpointSetup.Backups` BEFORE writing the Windows
  registry. The helper creates this directory with a protected, admin-owned
  ACL, permits ordinary users read-only access for status, and refuses
  pre-existing user-writable directories or directory junctions. It preserves
  existing effects, refuses unsupported/malformed registrations, does not
  take ownership of protected keys, and restores the original affected
  values if a transaction fails.
- A separate UAC-gated recovery mode can restore interrupted/failed writes
  only when current relevant values still match their original or intended
  values. Unexpected changes by other installers stop recovery and require
  review. An original missing FxProperties key is deleted on successful
  rollback when safe.
- A successful registration is **not** treated as immediately active. A
  persisted marker keeps manual and automatic limit changes blocked until
  the next Windows boot; recovery-required state remains distinct and blocked.
  Neither the registration check nor the reboot heuristic proves that audio
  processing is actually active in `audiodg.exe`.
- The existing official Device Selector remains a fallback for unsupported
  driver layouts and specialized troubleshooting.

## Intentional limitations and release gates

1. The elevated helper **has been exercised against a disposable VB-CABLE
   playback endpoint inside Windows Sandbox**, but NOT against the user's
   normal audio devices or diverse third-party driver stacks. Read-only
   `--inspect` was also tested against the host's HS80 MAX and LG TV. The
   unit suite additionally tests registry writes/rollback under isolated
   **HKCU test keys**, never actual host MMDevices HKLM keys.
2. A Windows **restart is required** after our helper reports successful
   registration. We do not silently restart Windows Audio, disconnect a device,
   interrupt a call, or assume the live engine loaded the new effect. A future
   implementation should use a real audio-engine check (for example an audited
   equivalent of the upstream APO named-pipe test), not registry inference.
3. Driver-protected registry keys may reject an elevated write. The helper
   opens existing `FxProperties` with narrowly scoped query/set-value rights,
   without taking ownership or altering Windows ACLs. Endpoints with no
   `FxProperties` key or insufficient value-write rights fall back to the
   official advanced Device Selector until a separately tested path exists.
4. This implementation follows official Equalizer APO **1.4.2** slot and
   child-registration semantics only. Any newer version, fork, or unexpected
   registration fails preflight rather than assuming compatibility.
5. Before distributing an elevated executable, conduct a security review,
   including helper integrity and launch from per-user/writable install paths,
   backup-directory permissions, UAC denial, and arbitrary command arguments.
   A signed binary and a trustworthy installation path are strongly preferred.
6. Review GPL licensing and acknowledgments for source-level compatibility
   with official Equalizer APO. Do not publish a compiled release before
   license obligations for an implementation based on upstream semantics are
   understood and met.
7. Update release versioning and perform physical acceptance tests before
   publishing or replacing the installed 1.0.0.

## Required physical acceptance tests

- Use a disposable Windows VM or a low-risk non-default playback device first.
  Verify the installed helper registers exactly the selected endpoint and
  doesn't modify unrelated microphone, other playback, or manufacturer effects.
- Verify that Windows Audio loads Equalizer APO after the requested restart.
  Measure/observe the actual attenuation, rather than equating registry slots
  with successful audio-engine activation.
- Test untouched clean FxProperties, absent FxProperties, OEM SFX/MFX/EFX,
  legacy LFX/GFX, combined Bluetooth, disabled enhancements, unusual or
  malformed FxProperties, ACL denial, and missing EAPO registration.
- Interrupt the helper mid-write and test recovery from `Prepared` or
  `RollbackIncomplete` backups; verify that changed OEM entries cause
  recovery to refuse overwriting them.
- Reconnect the HS80 MAX in the same and different USB ports; run while
  AudioLimits is visible, minimized, and hidden to tray. Check single-instance
  relaunch, UAC denial, restart-required status, and no increase of Windows
  volume during remembered-limit restoration.
- Test the installer/ZIP packaging, update/repair, and per-user installation,
  including helper trust/permissions. The full packaging script intentionally
  stops installed AudioLimits processes and must not be run casually during
  development.

## Validation so far

- 113 Core tests pass (50 existing + 63 new), including recovery refusal after
  another installer changes relevant registry values, protected backup ACL
  checks, the official missing-FxProperties default install mode, and
  value-only Windows registry handle access.
- Full solution Release build succeeds with 0 warnings and 0 errors.
- The packaged helper's **read-only** mode returned AlreadyRegistered for both
  HS80 MAX and LG TV; invalid arguments rejected. A staging test verified the
  helper launches alongside the published WinUI application using shared DLLs
  instead of bundling a duplicate 25 MB Windows SDK runtime.
- MicaPC's actual HS80 MAX and LG TV audio registrations, Windows Audio
  service, and user volume settings were not changed during testing. All
  subsequent audio registry/service experiments occurred in a disposable
  Windows Sandbox guest.
- Build script now includes the helper's four small entry files in the canonical
  `app/` payload, but
  a complete installer/ZIP packaging run has **not** been executed.
- A live sandbox test with VB-Audio's signed virtual cable and hash-verified
  Equalizer APO 1.4.2 exposed a real Windows ACL failure: `OpenSubKey(...,
  writable:true)` requests too much access on `FxProperties`. The tested fix
  uses `RegOpenKeyEx` with only query/set-value rights. The experimental helper
  then successfully registered SFX/EFX for one active render endpoint, left a
  second render endpoint untouched, and saved a durable rollback record.
- After guest reboot a shared-mode stream initialized, `audiodg.exe` loaded
  `EqualizerAPO.dll`, and an upstream-compatible named-pipe diagnostic received
  `{deviceGuid, stage:PreMix, phase:Initialize}` for the intended endpoint.
  PostMix initialization was **not observed** in a valid single-listener run.
  An attempted parallel-listener version generated access-denied retries and
  is **not** valid proof about the absent stage.
- A signal test routed a 997 Hz sine into `CABLE Input` and sampled its paired
  `CABLE Output` capture endpoint. With Equalizer APO config set to -6 dB,
  -26 dB, then restored to -6 dB, captured RMS remained approximately
  0.0667, 0.0666 (0.0673 after AudioSrv restart), 0.0671 respectively;
  captured peaks remained approximately 0.1000. This **does not demonstrate
  effective attenuation**. The virtual-cable capture tap may precede effects,
  or the selected post-mix stage may not be initialized on this driver.
  Do not ship until processing-stage initialization and effective attenuation
  can be distinguished and the chosen stage verified.
- Source is committed on the development branch only when validated; neither
  the 1.0.0 tag nor installed application is changed.

Audio Limits remains a convenience aid; software and driver audio paths can
bypass Equalizer APO. It is not a hearing-protection guarantee.

## Upstream references

Official Equalizer APO mirror:
https://github.com/mirror/equalizerapo/blob/master/DeviceAPOInfo.cpp
https://github.com/mirror/equalizerapo/blob/master/DeviceSelector/main.cpp

Microsoft Windows APO architecture:
https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/windows-11-apis-for-audio-processing-objects
