# Audio Limits continuation notes

## Current gate

`1.0.0` is the stable release. Avoid feature work unless a concrete regression or a deliberately scoped future release requires it. The owner has authorized post-1.0.0 fixes for USB headphone reconnects and confusing Equalizer APO setup. Work remains on the `fix/window-lifecycle-apo-refresh-20261111` development branch; do not replace an installed app, tag a release, or claim device portability is fully solved without the real-machine acceptance tests in `docs/DEVICE_ROAMING_AND_AUTOMATED_APO.md`. The branch now matches serialized USB hardware and restores caps automatically only where the exact new endpoint is already APO-ready. **The experimental endpoint-registration helper has now been tested against a disposable Windows Sandbox VB-CABLE render endpoint, including measured effective attenuation in SFX/MFX mode.** It uses a dedicated UAC-elevated executable, preserves OEM effect registrations, writes a durable backup before mutation, and supports guarded rollback. It never silently restarts Windows Audio; an OS restart is required before caps can be changed. `src/AudioLimits.EndpointSetup` is included in the canonical packaging build. Do not execute its `--install` or `--restore` commands against active user hardware until a separate physical acceptance test is authorized. The backup directory uses explicit admin-owned protected ACLs and refuses user-writable or reparse-point paths; keep this invariant. The sandbox result is conclusive that EAPO registration and DLL load are insufficient: the initial SFX/EFX mode showed no measured attenuation; a manually staged SFX/MFX mode reported both init stages and about -20.05 dB measured attenuation for a -20 dB requested change. See docs/SFX_MFX_SANDBOX_EVIDENCE_2026-11-11.md. **Runtime stage verification and a one-attempt reversible SFX/EFX-to-SFX/MFX fallback are implemented and passed sandbox acceptance testing; this is not yet broad-driver or release validation.** See docs/ONE_CLICK_FALLBACK_SANDBOX_2026-11-12.md for the forward switch, live attenuation, reboot gate, and crash-recovery test. Retain security/code-signing, GPL compatibility, general-driver, GUI/UAC, and packaging release gates. Automatic remembered-cap restoration must not raise Windows endpoint volume.

## Frozen behavior

- Preserve the limiter/Equalizer APO/Core behavior and its existing tests.
- Preserve the WinUI Devices/Settings design and hardware-volume fallback wording.
- Preserve title-bar layout, single-instance activation and Start-with-Windows semantics. The owner reported a tray-lifecycle usability regression on 2026-11-11: normal minimize must go to the taskbar; title-bar X must hide to tray; tray Exit must actually quit. Treat this as an explicit correction to the prior frozen behavior, not a reason to change unrelated audio logic.
- Preserve Setup fresh/update/repair behavior and prerequisite policy.

## Canonical app layout

```text
Audio Limits\
├─ AudioLimits.exe
└─ app\
   └─ AudioLimits.App.exe + supporting files
```

The root launcher is the obvious manual entry point for a freshly extracted/copied folder and for prerequisite recovery. Once prerequisites are prepared, routine Windows-created launch paths bypass the bootstrapper: Setup-created shortcuts/post-install launch and Start with Windows target `app\AudioLimits.App.exe` directly. `StartupService` also migrates the previous root-launcher startup registration when it encounters it.

## Public release artifacts

Only:

- `AudioLimits-Setup.exe`
- `AudioLimits-1.0.0-x64.zip`

Do not resurrect the old Portable/Standalone artifact. The ZIP is no-install, not portable-state software.

## 1.0.0 validation state

The final prerequisite bootstrap has been exercised on a clean Windows Sandbox through automatic Equalizer APO installation and app startup. Equalizer APO Device Selector can be opened explicitly afterward. Windows Sandbox remote audio does not expose a normal EAPO-selectable playback endpoint, so endpoint selection itself remains a real-PC path.