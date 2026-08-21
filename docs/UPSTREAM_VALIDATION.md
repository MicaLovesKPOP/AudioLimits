# Upstream validation notes

Validated through **2026-08-20**. These notes exist to prevent future work from rediscovering or accidentally reverting integration facts that were already checked against upstream source.

## Equalizer APO

Authoritative upstream for current behavior: SourceForge project/code, not the older public GitHub mirror.

- Project/releases: `https://sourceforge.net/projects/equalizerapo/`
- Current code tree: `https://sourceforge.net/p/equalizerapo/code/ci/main/tree/`
- Configuration reference: `https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/`

Validated facts used by Audio Limits:

- Equalizer APO 1.4 introduced the Qt-based **Device Selector** as the replacement for the legacy Configurator.
- Direct playback-stage registration in `DeviceAPOInfo.cpp` is detected through LFX/SFX for pre-mix and GFX/MFX/EFX for post-mix. The multi-effect slots 13/14/15 are used during install-mode selection but are not the direct installed-stage test used by `DeviceAPOInfo::load()`.
- For output devices, current installation-state logic maps LFX/SFX to pre-mix and GFX/MFX/EFX to post-mix.
- The configuration parser accepts `Stage: pre-mix post-mix`; managed per-device entries can then select one exact stage.
- `Device`, `If`, `Expression`, and `Include` factories run before the `Stage` factory. In particular, an **open false `If` before Audio Limits' include could suppress that include**. Audio Limits therefore refuses to append/change its managed include when the outer `If`/`EndIf` structure is unbalanced.
- Included files have their own condition/stage scope handling, so the managed file can reset/select its own Device/Channel/Stage state without leaking selectors back into the outer file.
- Equalizer APO watches its configuration directory for file changes and reloads configuration. It crossfades configuration transitions internally, but Audio Limits still uses conservative endpoint/config ordering because Equalizer APO exposes no acknowledgement that a particular managed attenuation has become active.
- ASIO and WASAPI exclusive mode can bypass the Windows system-effects infrastructure and therefore bypass Equalizer APO/Audio Limits.
- Equalizer APO 1.4.x deploys Qt runtime/plugin files beside its GUI tools. Audio Limits launches Device Selector through ShellExecute with Equalizer APO's installation directory as the working directory, matching normal shell launch context more closely than the v0.3 prototype.

## NAudio 2.3.0

Upstream source: `https://github.com/naudio/NAudio/tree/v2.3.0`

Validated facts used by Audio Limits:

- `MMDeviceEnumerator` provides endpoint enumeration and lookup.
- `MMDevice.AudioEndpointVolume` exposes scalar volume, dB volume, mute state, volume range, and notifications.
- `AudioEndpointVolume.NotificationGuid` is passed as the event context for changes made by Audio Limits; volume subscriptions ignore that context so internal calibration/transition writes do not churn the live UI.
- `AudioEndpointVolume` registers/unregisters Core Audio change notifications and implements `IDisposable`; Audio Limits owns and disposes long-lived device subscriptions.

## Windows endpoint volume

Microsoft documentation checked for `IAudioEndpointVolume::SetMasterVolumeLevelScalar`:

`https://learn.microsoft.com/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmastervolumelevelscalar`

The normalized scalar 0.0-1.0 control is audio-tapered/nonlinear and the curve is not a compatibility contract across Windows versions. Audio Limits therefore measures the endpoint's actual scalar-to-dB mapping rather than using a logarithmic percentage formula.

## Windows app theme / WinForms

Microsoft documentation checked for the app color preference and DWM frame behavior:

- `https://learn.microsoft.com/windows/apps/develop/settings/settings-common`
- `https://learn.microsoft.com/windows/apps/desktop/modernize/ui/apply-windows-themes`
- `https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute`
- `https://learn.microsoft.com/dotnet/api/microsoft.win32.systemevents.userpreferencechanged`

Validated facts used by Audio Limits:

- `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` is the documented per-app light/dark preference (1 light, 0 dark).
- Windows 11 build 22000+ supports `DWMWA_USE_IMMERSIVE_DARK_MODE = 20` for the standard window frame/title bar.
- .NET/WinForms `SystemEvents.UserPreferenceChanged` is available with a message pump and requires explicit unsubscription because it is a static event. Audio Limits also listens for display-setting changes and marshals theme updates back to its UI thread.
- The built-in WinForms `Application.SetColorMode` dark-mode API belongs to later Windows Desktop versions and is not available to the current self-contained .NET 8 target. pre.8 therefore keeps .NET 8 and applies a conservative client palette to standard WinForms controls rather than upgrading the user's SDK requirement solely for appearance.
- High Contrast is detected separately and takes precedence over custom light/dark client colors.
- Audio Limits does not use undocumented UxTheme dark-mode exports.

## WNAQ

Project quality standard: `MicaLovesKPOP/Windows-Native-App-Quality`.

Audio Limits intentionally remains WinForms for v1.0. WNAQ's framework-neutral rules apply to project discovery, reliability/lifecycle, persistence, accessibility/input, DPI, native integration, deployment, testing, and evidence provenance. WinUI-specific control guidance is not translated literally into WinForms.
