# Audio Limits modern Windows migration plan

> **Historical design plan:** this document records the WinUI migration plan created earlier in development. The actual `1.0.0-rc.2` also includes the later validated tray, installer, and deployment work documented in `PROJECT_PROFILE.md` and `docs/VERIFICATION.md`; those files are authoritative for the current release candidate.


## Purpose

Audio Limits 1.0.0-rc.1 is the known-good WinForms functional baseline. The modernization work replaces the presentation and shell integration while preserving the proven audio limiting backend and safety contract.

The Windows Settings screenshots, WindBar, and Windows Native App Quality are references, not layout templates. Audio Limits should end up feeling like a small first-party-quality Windows utility with an information architecture designed specifically for per-device audio limiting.

## Decision order

1. Audio Limits' actual product needs and established UX contract.
2. Proven rc.1 audio/safety behavior.
3. Windows Native App Quality.
4. Current stable Microsoft Windows/WinUI guidance.
5. WindBar for architecture, shell integration and readiness discipline.
6. Windows screenshots for visual quality, hierarchy, density and restraint.

## Final architecture

- `AudioLimits.Core`: models, settings, volume curve, AudioDeviceService, EqualizerApoService, LimitService, LimitTransitionPlanner and logging.
- `AudioLimits.App`: WinUI 3 window/pages, dialogs, tray/shell lifecycle, startup integration and app identity.
- `AudioLimits.Core.Tests`: preserved backend tests plus new pure decision/projection tests.

Do not add a broad MVVM, DI, message-bus or navigation framework. Small typed view-state classes are allowed where they materially improve testability or binding.

## Final UI shell

- C# / .NET 8 / WinUI 3 / stable Windows App SDK.
- Unpackaged, self-contained, x64; single-file publish remains the distribution target when supported by the selected stable Windows App SDK.
- Mica primary backdrop with normal system fallback.
- WinUI `TitleBar` control with Audio Limits identity, native caption buttons, built-in Back button on Settings, and a compact `SubtleButtonStyle` Settings command using the native `Symbol.Setting` glyph in the right header on the Devices page.
- One main `Frame`: Devices page and Settings page. No sidebar or fake Settings navigation hierarchy.
- Normal resizable/maximizable desktop window, centered initially, with responsive content capped at a sensible readable width.

## Devices page

Hierarchy:

1. `Output devices` title.
2. Short explanation: Windows volume remains 0-100; the configured limit changes how loud 100 can be.
3. `InfoBar` only when setup/recovery/error state needs attention.
4. Vertically stacked device cards.

Each device card contains:

- small monochrome Fluent/system device glyph;
- friendly endpoint name;
- textual status (`No limit`, `Limited to N%`, `Limit inactive`);
- value rows: `Windows volume` and `Same output without limit`; the configured limit is shown once in the card header status (`No limit`, `Limited to N%`, `Limit inactive`) to avoid duplicate values;
- visible native Button actions: Set/Change limit, Set current output as limit, Remove limit as applicable;
- native progress state while an operation is running.

The card itself is custom composition, not a custom control imitation: `Border`/`Grid` plus native WinUI text, icons and buttons using theme resources such as card background/stroke brushes and system corner radii.

No dB in the normal UI. Do not add a slider that competes with the Windows volume slider. Unknown uncapped-equivalent values remain `—` rather than using a fake linear approximation.

Hardware-volume endpoints are a supported capability boundary, not an error state. When Core Audio reports hardware volume support, the card keeps the same action layout but shows `Same output without limit: Not available`, displays a concise explanation, and leaves `Set current output as limit` visible but disabled. Manual Set/Change/Remove remains available. These operations keep Windows hardware volume where the user left it and change only managed attenuation under the mute barrier. A weaker/removing limit warns that the current output may become louder.

## Limit dialog

Native WinUI `ContentDialog` with:

- device name;
- established explanatory copy;
- native `NumberBox`, whole percent, 1–100, with spin buttons hidden;
- short note that audio may briefly mute while a safe transition is applied;
- Cancel and accent/default Apply actions;
- Enter applies and Escape cancels.

100% is accepted as input but routes through the real Remove limit operation; a 100% active limit is never persisted.

## Healthy and problem states

Healthy state has no persistent setup banner.

Use native `InfoBar` for non-modal states:

- Equalizer APO/setup required;
- saved limit inactive for a device;
- pending recovery;
- state uncertain;
- normal operation failure after safe rollback.

Use `ContentDialog` only when a modal decision is genuinely required.

## Settings page

Final settings use Microsoft-recommended Windows Community Toolkit `SettingsCard`/`SettingsExpander` where they are a better native-looking settings-row implementation than rebuilding the pattern manually. These are Toolkit controls, not OS-native controls, and must be documented honestly.

Sections:

### General
- Start Audio Limits with Windows — native `ToggleSwitch` inside SettingsCard.
- Explanation that startup is optional; limits remain active while Audio Limits is closed, while startup provides reconciliation/checks. Tray availability is added with the later tray-lifecycle stage.

### Audio processing
- processing status (Ready/setup required);
- Manage audio devices action;
- persistent boundary note that ASIO/exclusive modes can bypass processing and Audio Limits is not hearing protection.

### About
- app identity and version;
- diagnostic logs action.

Settings apply immediately. No Save button.

## Native / native-imitation inventory

- Window/AppWindow: native Windows App SDK.
- MicaBackdrop: native.
- WinUI TitleBar and Back: native.
- Windows caption buttons: native OS.
- ScrollViewer, TextBlock, Button, NumberBox, ContentDialog, InfoBar, ProgressRing, ToggleSwitch: native WinUI controls.
- Fluent FontIcon/system glyph resources: native visual resources.
- Device card: custom composition using native primitives; not imitation of a standard control.
- SettingsCard/SettingsExpander: Microsoft-recommended Community Toolkit Windows-11 settings-pattern implementation; native-looking, not an OS control.
- Tray: native Win32 `Shell_NotifyIcon` behind a narrow TrayIconService.
- Tray context menu: native Win32 shortcut menu.
- Core audio: existing NAudio/Core Audio integration.

## Theming and accessibility

- App theme remains Default and follows Windows Light/Dark/High Contrast.
- Remove the old custom WinForms theme palette once WinUI parity is reached.
- Use theme resources rather than hand-coded client RGB values except branding assets.
- Required actions must be keyboard reachable with logical visible focus.
- Long device names wrap.
- One vertical page scroll boundary; no nested vertical scrollers.
- Verify 100%, 150%, 200% display scaling; increased text; minimum/restored/maximized sizes; High Contrast; active/inactive windows; long device names; and mixed-DPI/multi-monitor movement where available.

## Tray/lifecycle target

- Minimize hides to tray.
- Tray restore reuses the existing window and restores its prior normal/maximized state.
- X / Alt+F4 exits the process when safe.
- Exit never removes limits.
- Exit is blocked while a safety-critical audio transition/reconciliation is in progress.
- Second launch activates the existing instance.
- Explorer restart (`TaskbarCreated`) recreates the tray icon.
- Start-with-Windows can launch in background mode without flashing the main window.

## Migration stages

### pre.12 — architecture + WinUI shell (read-only)
- Extract backend to `AudioLimits.Core` without behavioral changes.
- Create WinUI App with Mica, TitleBar, Devices/Settings navigation and app identity.
- Enumerate real endpoints and display existing saved/APO state read-only.
- Subscribe to live Windows endpoint-volume changes.
- Preserve single-instance behavior.
- Intentionally do not instantiate/call LimitService repair or expose mutation actions.

### pre.13 — complete read-only visual main page
- final device card hierarchy;
- responsive/reflow behavior;
- setup/recovery/error/empty states;
- device glyph selection;
- screenshot-driven visual quality pass across Light/Dark/High Contrast and scaling.

### pre.14 — restore limit mutations
- native ContentDialog/NumberBox;
- Set/Change/Set-current/Remove actions;
- operation progress and disabled mutation state;
- repeat the critical real-audio safety matrix despite backend source parity because the invocation layer changed.

### pre.15 — hardware-volume diagnosis
- structured endpoint/transition logging only;
- prove why the headset and TV differ before changing the safety model.

### pre.16 — hardware-volume capability + Settings
- hardware-aware transition/recovery policy with no hidden hardware-volume movement;
- honest card capability state and disabled current-output matching on hardware endpoints;
- 1–100 editor with 100% routed to Remove;
- final SettingsCard information architecture, startup toggle, Device Selector and logs access.

### pre.17 — setup discovery + UI polish
- explicit missing/incomplete Equalizer APO states without healthy-state backend clutter;
- accurate setup/download/repair actions;
- final hardware-volume wording and unavailable-value treatment;
- compact Settings command beside the app title;
- GitHub project link and Settings terminology cleanup.

### pre.18 — tray and lifecycle
- native tray service and Explorer restart recovery;
- minimize-to-tray / X-exit / background startup / tray settings / second-launch lifecycle.

### pre.19 — accessibility/DPI/polish only
- replace the user-observed wrong-theme/behind-overflow classic tray menu with a WinUI context-menu surface while preserving `Shell_NotifyIcon`;
- keyboard-only workflow and context-menu arrow/Escape/Tab behavior;
- focus entry/return, including notification-area focus restoration;
- text/display scaling;
- High Contrast;
- long content;
- mixed DPI/monitor movement and tray-menu work-area placement;
- final spacing, typography, icon sizing, disabled/busy/error states.

### pre.20 — tray-menu behavior correction
- correct bottom-left/right-growing anchoring and clamp the popup to the monitor work area;
- remove pointer-open focus-ring artifact while retaining keyboard focus visuals;
- make outside-click, Escape and Alt+F4 dismissal deterministic;
- preserve the pre.19 visual surface and all earlier lifecycle/audio behavior.

### pre.21 — tray-menu retained-focus correction

- Fix the intermittent second/later-open white focus outline left by pre.20.
- Make the hidden neutral focus sink actually focusable; `IsTabStop=False` prevented the pre.20 `Focus(...)` call from working.
- Reset neutral focus across hide/show activation and keep the sink outside the nested command-only Tab cycle so it remains an implementation detail.
- Preserve all pre.20 placement, clamping, dismissal and lifecycle behavior.

### pre.23 — tray-overflow pointer-dismissal correction

- Keep the WinUI tray-menu surface and all pre.22 keyboard/focus behavior.
- Track why the tray menu is dismissed.
- Use `NIM_SETFOCUS` only after Escape/Alt+F4 cancellation.
- Do not steal focus back from an outside pointer target or command activation.
- Verify that clicking the taskbar closes both Audio Limits' menu and Explorer's open notification-area overflow.
- Do not depend on undocumented Explorer window classes or synthetic clicks.

### pre.24 — release/distribution-size comparison
- freeze the passed pre.23 application behavior;
- auto-close any running Audio Limits process before publish cleanup;
- measure the existing fully self-contained baseline, supported single-file compression, experimental trimming+compression, shared-Windows-App-Runtime/.NET-self-contained, and fully framework-dependent outputs;
- report folder, EXE and compressed-download sizes from the real Windows toolchain;
- do not accept the current ~300 MB development publish as the release artifact.

### pre.25 — release deployment selection
- choose the deployment model from pre.24 measurements plus runtime/startup results;
- design clean-machine runtime acquisition if a framework-dependent component is selected;
- fully revalidate any trimmed/AOT-like optimization before trusting it;
- produce the normal-sized GitHub-ready release shape without feature work.

### pre.26 — installer scope and UX polish
- preserve the selected release model from pre.25;
- offer current-user install by default and optional all-users install;
- route install paths/shortcuts through Inno Setup auto constants;
- fix .NET runtime detection and remove happy-path prerequisite clutter;
- follow Windows light/dark appearance and brand the setup wizard;
- block accidental installation over a source checkout.

### pre.27 — release branding + trim safety
- explicitly provide Audio Limits wizard artwork for both dynamic light and dynamic dark setup modes;
- simplify the finished-page copy;
- replace reflection-based SettingsStore JSON calls with System.Text.Json source-generated metadata;
- preserve the existing settings JSON contract and migration behavior;
- repeat installed + portable regression checks before moving to release-candidate work.

### pre.28 — installer maintenance flow
- distinguish fresh install, update, same-version repair, and downgrade states;
- preserve existing scope/location/task choices during update and repair;
- skip irrelevant fresh-install pages for maintenance operations;
- recheck prerequisites during update/repair without deleting Audio Limits settings or limits;
- keep uninstall in Windows Settings rather than adding a legacy Modify/Remove setup mode.

### rc.2
No feature work. Full release matrix only. Only release blockers may change.

### 1.0.0
Exact rc.2 implementation with version/docs/changelog promotion only. No last-minute feature additions.

## Safety rule during migration

The Equalizer APO and limit-transition backend is more trusted than the new UI. If UI modernization requires a backend behavioral change, stop treating that area as carried-forward evidence and reopen its targeted safety tests.
