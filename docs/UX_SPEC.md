# Audio Limits v1 UX specification

## Mental model

The app communicates one rule:

> Set how loud each playback device is allowed to get. Windows volume still runs from 0–100%; the limit changes the device's maximum output.

The normal card terminology is:

- **Windows volume** — the current Windows endpoint slider position.
- **Same output without limit** — for endpoints where exact matching is available, the unrestricted Windows percentage that would produce the current audible output.
- **Limited to N% / No limit / Limit inactive** — the configured state, shown once in the card header rather than repeated as a value row.

No dB/APO/Core Audio terminology appears on the normal Devices page.

## Precision and capability

- Display and manual input use whole percentages only.
- Internal attenuation and sampled volume curves retain full precision.
- Software-volume endpoints support exact dB compensation and current-output matching.
- Hardware-volume endpoints are supported but do not claim exact loudness equivalence. While limited they show `Same output without limit: Not available`.
- On hardware-volume endpoints, `Set current output as limit` remains visible but disabled so the capability difference is discoverable rather than mysterious.

## Device without a limit

- Status: `No limit`.
- `Windows volume` and `Same output without limit` match, including on hardware-volume devices because no Audio Limits attenuation is active.
- Actions: `Set limit`, `Set current output as limit` (disabled on hardware-volume endpoints).

No pre-filled numeric control is shown on the card.

## Device with an active limit

Software-volume endpoint:

- Status: `Limited to N%`.
- Show both live values.
- Actions: `Change limit`, `Set current output as limit`, `Remove limit`.

Hardware-volume endpoint:

- Status: `Limited to N%`.
- `Same output without limit`: `Not available`.
- Visible secondary explanation: `This device controls Windows volume in hardware, so Audio Limits can't calculate an exact equivalent.`
- Actions use the same layout, but `Set current output as limit` is disabled with supplemental help text.

## Device with a saved but inactive limit

- Status: `Limit inactive`.
- Never calculate live output as if the attenuation were active.
- Show an actionable setup explanation.
- Removing the inactive saved limit is allowed and does not require the hardware-volume louder-output confirmation because it is not currently affecting audio.

## Limit dialog

Use a native WinUI `ContentDialog` and `NumberBox`.

- Label: `Output limit (%)`.
- Whole-number range: 1–100.
- Spin buttons hidden; keyboard increment/decrement remains native NumberBox behavior.
- Entering the existing value disables the primary action.
- 100% means no limit and routes through the real Remove operation; a 100% active limit is never stored.

Software-volume explanation:

> At Windows volume 100%, this device will be as loud as unrestricted Windows volume at N%.

Hardware-volume explanation:

> This device controls Windows volume in hardware. The actual loudness may not match unrestricted Windows volume at the same percentage.

Hardware transition copy is directional where known: applying/lowering may make output quieter; raising/removing may make output louder. When a louder result is possible, tell the user they can lower Windows volume first if needed.

## Applying changes

Software-volume endpoints:

- preserve current audible output whenever the new limit allows it;
- if the current output exceeds a new lower limit, reduce safely to that limit;
- never create a temporary louder state while reordering endpoint volume and attenuation.

Hardware-volume endpoints:

- keep Windows volume exactly where the user left it;
- change only Audio Limits attenuation under the transition-mute barrier;
- do not promise current-loudness preservation;
- direct Remove asks for confirmation because the final output may become louder;
- never hide the capability limitation behind false numerical precision.

## Settings

Settings is an in-window page reached through the compact native Settings command in the TitleBar right header. The TitleBar's built-in Back button returns to Devices; the Settings command is hidden while already on Settings.

Use Community Toolkit `SettingsCard` rows rather than manually imitating Windows Settings controls. Sections:

- **General** — Start Audio Limits with Windows.
- **Audio processing** — playback-device management and the shared-audio bypass boundary.
- **Diagnostics** — open log folder.
- **About** — app identity/version.

Settings apply immediately; there is no Save button.

## Backend exposure and compatibility disclosure

Do not show `Equalizer APO: found` during normal healthy operation. Backend/setup details belong in Settings or an actionable problem state.

Settings must state that ASIO and exclusive-mode apps can bypass Windows shared audio processing and therefore Audio Limits. Audio Limits must not be presented as hearing protection.

## Tray/lifecycle target

- Minimize main window -> hide to tray.
- Close with X / Alt+F4 -> exit through the safe-exit gate.
- Double-click tray icon -> show main window.
- Launch again while running -> show the existing window.
- Explorer restart recreates the tray icon.
- Exiting does not remove configured attenuation.
- Start with Windows is optional because healthy configured limits remain active independently through Equalizer APO.

Tray/minimize lifecycle is implemented in pre.18. pre.19 replaced the defective classic tray-menu presentation with a themed WinUI surface. pre.20 corrects its runtime menu semantics: bottom-left/right-growing anchoring with work-area clamping and deterministic outside-click/Escape/Alt+F4 dismissal. pre.21 fixes the remaining intermittent stale command focus visual by using a genuinely focusable neutral sink and explicit command-only Tab cycling. pre.22 restores immediate Up/Down/Home/End entry from that neutral sink by handling tray-menu keys at the shared parent level. pre.23 makes shell-focus restoration dismissal-aware so taskbar/outside pointer clicks do not hand focus back to the notification area after the menu closes; keyboard cancellation still returns focus to the tray icon.

## Theme/accessibility

- Follow Windows light/dark preference through WinUI theme resources; no separate Audio Limits theme selector.
- High Contrast must remain fully usable.
- Keyboard focus and dialog semantics are correctness requirements.
- Important explanations cannot exist only in hover tooltips; pointer help may supplement visible text.
