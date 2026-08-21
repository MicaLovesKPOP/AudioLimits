# Changelog

## 1.0.0-rc.2

- Promote the validated WinUI codebase to a GitHub-ready release candidate without changing limiter/audio behavior.
- Standardize both supported distributions on one canonical application layout: user-facing `AudioLimits.exe` at the root and framework-dependent WinUI implementation files under `app\`.
- Keep `AudioLimits-Setup.exe` as the recommended normal installer and add one no-install `AudioLimits-1.0.0-rc.2-x64.zip`; retire public Portable/Standalone terminology and artifacts.
- Route routine launch paths directly to `app\AudioLimits.App.exe` after prerequisites are prepared: Setup-created Start-menu/desktop shortcuts, Setup's post-install launch, and Start with Windows no longer pay the bootstrapper cost on every launch.
- Migrate an existing Start-with-Windows registration that still points at the root prerequisite launcher to the direct app host when the app next starts.
- Keep root `AudioLimits.exe` as the obvious manual first-run/recovery entry point for freshly extracted/copied folders and missing-prerequisite repair.
- Keep Setup state-aware (fresh/update/repair/downgrade protection) and clean known pre.30 root payload files during upgrade to the subfolder layout.
- Keep the self-contained launcher prerequisite policy unchanged; healthy-machine launch and full-folder relocation have passed real-Windows testing.
- Explicitly defer only the clean-VM missing-prerequisite recovery path and mark rc.2 as a GitHub pre-release until that test is performed.
- Keep generated release output clean: `release\` contains only Setup and the no-install ZIP; intermediates/report move to `artifacts\`.

## 1.0.0-pre.30

- Replaced the confusing portable/standalone release experiment with a canonical self-bootstrapping application folder.
- Added a small self-contained `AudioLimits.exe` launcher that checks and repairs missing .NET 8 Desktop, VC++ x64, and Windows App Runtime 2.3.1 prerequisites before launching the WinUI host.
- Renamed the internal WinUI process to `AudioLimits.App.exe`; shortcuts and Start-with-Windows route through the launcher.
- Added Microsoft Authenticode validation for prerequisite installers downloaded by the launcher.
- `publish\` now mirrors the copyable installed application folder; normal Setup remains the primary public artifact.
- Setup removes legacy pre.30 `AudioLimits.dll`/deps/runtimeconfig sidecars during upgrade.
- No intentional limiter/backend/tray/UI behavior changes.

## 1.0.0-pre.29

- Disabled IL trimming for the self-contained portable fallback after the pre.28 trimmed WinUI build failed to launch on the Windows validation machine.
- Kept the portable build fully self-contained, single-file, and compressed; the compact web-prerequisite Setup remains the primary distribution.
- Retained source-generated System.Text.Json metadata for deterministic settings serialization.
- Removed the UTF-8 BOM from `Build-PreRelease.cmd`, fixing the garbled `@echo off` line and noisy echoed build commands.
- No limiter/backend, tray, installer-maintenance, or settings-schema behavior changes.


## 1.0.0-pre.28

- Make Setup state-aware instead of treating every rerun like a fresh installation.
- Fresh installs retain the passed dual-scope flow: **Just for me** by default, with **Anyone who uses this computer** available.
- Older installed versions now get a short **Update Audio Limits** flow that preserves the previous install scope, location, desktop-shortcut choice, limits, and settings while rechecking shared prerequisites.
- Re-running the same version now gets a **Repair Audio Limits** flow that reinstalls app files and rechecks prerequisites without deleting user data.
- An installed version newer than the installer is detected and blocked to prevent accidental downgrades.
- Do not add legacy-style Modify/Uninstall actions to Setup; Windows Settings > Apps remains the normal uninstall surface.
- Change the final fresh-install action from **Next** to **Install** when the Ready page is suppressed.
- Preserve pre.27 setup branding, trim-safe settings JSON, release sizes, tray/lifecycle behavior, and limiter/Equalizer APO behavior.

## 1.0.0-pre.27

- Record the real-Windows pre.26 installer pass: dual-scope installation, dark-mode setup, prerequisite acquisition, and the ~20 MB installer architecture work as intended.
- Set both normal and DynamicDark Inno Setup wizard-image directives so the custom Audio Limits artwork is used in dark mode instead of Inno Setup's built-in box/disc artwork.
- Add dedicated transparent Audio Limits small and large wizard assets for the interior and completion pages.
- Replace the stock completion-page copy with a concise `Audio Limits is ready` completion state.
- Make `SettingsStore` JSON serialization trim-safe by switching AppSettings and schema-1 migration DTOs to source-generated System.Text.Json metadata.
- Preserve the on-disk JSON shape and existing schema migration/backup/recovery behavior; existing SettingsStore tests remain the behavioral gate.
- Keep the proven pre.23 tray/lifecycle behavior, Equalizer APO behavior, installer prerequisite policy, and dual-scope install model unchanged.

## 1.0.0-pre.26

- Keep the pre.25 20.6 MB installer / 69.3 MB portable release architecture and focus on installer UX.
- Default to a per-user **Just for me** install while offering Inno Setup's native **Anyone who uses this computer** administrative install mode.
- Use auto Program Files / Start Menu / Desktop constants so paths and shortcuts follow the selected install scope.
- Follow Windows light/dark appearance in Setup and replace the generic wizard artwork with Audio Limits identity.
- Remove the redundant Ready-to-Install prerequisite summary; only show preparation UI when a required Microsoft component is genuinely missing.
- Fix .NET 8 Desktop Runtime x64 detection by checking .NET's registered 32-bit registry view explicitly, with a defensive 64-bit fallback.
- Read the x64 VC++ runtime from the explicit 64-bit registry view.
- Refuse to install over a detected Audio Limits source checkout.
- Keep Start-with-Windows per-user regardless of install scope.
- No intentional limiter/Core/tray behavior change.

## 1.0.0-pre.25

- Promotes the trimmed/compressed self-contained build to a full portable release-candidate validation path.
- Adds a framework-dependent installer payload intended to keep the normal GitHub download small.
- Adds an Inno Setup `AudioLimits-Setup.exe` prototype with prerequisite detection and conditional Microsoft web-installs for .NET 8 Desktop Runtime x64, Visual C++ v14 x64, and Windows App Runtime 2.3.1 x64.
- Adds a normal Program Files install, Start-menu shortcut, optional desktop shortcut, uninstall registration, and cleanup of Audio Limits' HKCU startup value on uninstall.
- Build script continues to close running Audio Limits instances automatically and now emits `release\RELEASE_REPORT.txt`.
- No intentional limiter/Core/tray behavior change.

## 1.0.0-pre.24

- Record the real-Windows pre.23 pass: build/tests are clean and a taskbar click now dismisses both the Audio Limits tray menu and Explorer notification-area overflow; tray/lifecycle behavior is frozen for the release-size pass.
- Move into release/distribution-size measurement without changing limiter, Equalizer APO, tray, lifecycle, or UI behavior.
- Automatically terminate any running `AudioLimits.exe` before cleaning publish output, preventing the invisible/background-process file-lock failure encountered during pre.23 testing.
- Produce five x64 publish candidates in one build so release size can be chosen from measured Windows output rather than guesses: current baseline, compressed fully self-contained, experimental trimmed+compressed fully self-contained, .NET-self-contained/shared-Windows-App-Runtime, and fully framework-dependent.
- Make the supported compressed fully self-contained candidate the provisional `publish\AudioLimits.exe` for low-risk runtime smoke testing.
- Generate `publish-candidates\SIZE_REPORT.txt` plus optimally-compressed comparison ZIPs with folder, EXE, and download sizes.
- Keep the trimmed candidate explicitly experimental until complete runtime verification; size alone is not evidence that trimming preserved WinUI/NAudio/dynamic behavior.
- Keep `AudioLimits.Core` byte-for-byte unchanged.

## 1.0.0-pre.23

- Record the real-Windows pre.22 result: neutral pointer-open focus, immediate arrow-key entry, placement/clamping and Escape/Alt+F4/outside-click dismissal are good; the remaining mismatch is that a taskbar click can dismiss the Audio Limits menu while leaving Explorer's notification-area overflow flyout open.
- Stop treating all menu dismissals as equivalent for shell-focus restoration.
- Carry an explicit tray-menu dismissal reason (`OutsidePointer`, `Deactivated`, `KeyboardCancel`, `CommandInvoked`) to the owner.
- Call `Shell_NotifyIcon(NIM_SETFOCUS)` only for keyboard cancellation (Escape / Alt+F4), where returning focus to the notification area is appropriate.
- Do not restore notification-area focus after an outside pointer click, deactivation, or a command invocation, so the user's clicked taskbar/foreground target keeps focus and Explorer can dismiss its own overflow surface naturally.
- Avoid brittle Explorer-specific window classes, synthetic mouse clicks, or undocumented shell manipulation.
- Preserve pre.22 keyboard behavior, pre.21 neutral focus, pre.20 placement/clamping/dismissal mechanics, pre.18 lifecycle and all `AudioLimits.Core` behavior unchanged.

## 1.0.0-pre.21

- Record the real-Windows pre.20 result: tray-menu anchoring, clamping, dark/z-order presentation and outside-click/Escape/Alt+F4 dismissal are good; the remaining defect is an intermittent white focus ring on a later pointer-open.
- Fix the actual focus-reset bug: the hidden pre.20 focus sink used `IsTabStop=False`, which prevents `Control.Focus(...)` from moving focus to it.
- Make the invisible sink a real focus target with its own system focus visual disabled, and reset focus to it both before hide and after activation/show.
- Place the sink outside a nested command-only `TabFocusNavigation=Cycle` scope so Tab/Shift+Tab remain among enabled visible commands after keyboard navigation enters the menu; the sink stays an implementation detail.
- Keep pre.20 placement/clamping/dismissal behavior, pre.18 lifecycle and all `AudioLimits.Core` behavior unchanged.

## 1.0.0-pre.20

- Record the real-Windows pre.19 tray-menu pass: the WinUI surface now renders dark and above the Windows 11 tray overflow as intended.
- Correct horizontal anchoring so the menu normally grows right from the invocation point, placing its bottom-left corner at the icon/cursor rather than its bottom-right corner.
- Clamp the final tray-menu rectangle to the current monitor work area so edge placement cannot push it off-screen.
- Remove the unwanted initial white focus outline by focusing an invisible non-tab-stop sink on pointer open; normal WinUI focus visuals still appear once keyboard navigation moves onto a command.
- Explicitly show/activate the tray menu as a foreground AppWindow and keep the existing deactivation light-dismiss path.
- Add short-lived low-level pointer observation while the menu is visible so clicking outside dismisses it even when Explorer's overflow surface does not take foreground activation; the outside click is not swallowed.
- Add short-lived keyboard dismissal handling so Escape and Alt+F4 reliably dismiss only the tray menu, consuming those keys so Alt+F4 cannot leak through to another window.
- Keep Shell_NotifyIcon, Explorer recovery, main-window lifecycle and all AudioLimits.Core behavior unchanged.

## 1.0.0-pre.19

- Record the user-reported pre.18 Windows lifecycle pass: tray presence, minimize/restore and normal lifecycle behavior were good; the remaining observed defect was the classic tray menu rendering light and behind the Windows 11 notification-area overflow.
- Keep native `Shell_NotifyIcon`, Explorer restart recovery, background startup, single-instance behavior, minimize-to-tray and safe exit unchanged.
- Replace only the classic Win32 `HMENU` presentation with a WinUI top-level context-menu surface using `OverlappedPresenter.CreateForContextMenu`.
- Keep the menu out of taskbar/Alt+Tab switchers and make it temporarily always-on-top while visible so it presents above the Windows tray overflow.
- Use WinUI theme resources and Desktop Acrylic for Light/Dark/High-Contrast-aware menu presentation without undocumented UxTheme dark-menu calls.
- Add light-dismiss behavior on deactivation/Escape, make Alt+F4 dismiss the tray menu rather than exit the app, and return focus to the notification-area icon after dismissal.
- Add Up/Down/Home/End keyboard navigation and cycle Tab focus among enabled tray commands.
- Recompute tray-menu size and work-area placement against the invocation monitor DPI on every open for mixed-DPI/taskbar scenarios.
- Keep `AudioLimits.Core` unchanged; this remains an App/shell accessibility and presentation pass.


## 1.0.0-pre.18

- Add a narrow native Win32 `TrayIconService` using `Shell_NotifyIcon` and a window subclass callback; keep all tray/window lifecycle out of `AudioLimits.Core`.
- Add a native notification-area menu with `Open Audio Limits`, `Settings`, and `Exit`; double-clicking the tray icon restores the existing WinUI window.
- Hide the main window to the tray on Minimize only when tray initialization succeeded; if tray setup fails, ordinary Windows minimization remains available so the app cannot disappear without a recovery surface.
- Preserve the last normal/maximized presenter state when restoring from the tray.
- Keep X / Alt+F4 as full application exit rather than close-to-tray.
- Route tray Exit through the same initialization/busy safety gate used by title-bar close, and bring a hidden window forward before explaining a blocked exit.
- Make `--background` startup a true tray-accessible background launch with no intentional main-window activation.
- Keep second-launch single-instance activation, now restoring a hidden/minimized primary instance instead of creating another tray icon.
- Recreate the notification icon when Explorer broadcasts the registered `TaskbarCreated` message after an Explorer restart.
- Clarify Start-with-Windows Settings copy: startup provides background reconciliation/checks and tray access, while configured Equalizer APO attenuation remains active after Audio Limits exits.
- Preserve all pre.17 setup/UI behavior and pre.16 limiter/hardware-volume behavior; no Core source file is intentionally changed in this stage.

## 1.0.0-pre.17

- Keep the pre.16 limiter/recovery behavior unchanged and make this a bounded setup-discovery/UI-polish candidate.
- Move the Settings command from the far-right TitleBar header to compact content immediately beside the `Audio Limits` title; reduce the visible/clickable command to a 20 px title-bar utility button with a 16 px native settings glyph.
- Keep `Same output without limit` present on hardware-volume endpoints and retain `Not available`; replace the repeated loudness-matching explanation with `Audio Limits can't calculate an exact equivalent.`
- Make the missing-Equalizer-APO state explicit on the Devices page, link directly to the official download page, and instruct users to reopen Audio Limits after installing it.
- Disable limit-setting actions while Equalizer APO is absent rather than letting those controls imply that a limit can currently be applied.
- Make saved-but-inactive device warnings distinguish missing Equalizer APO from a playback endpoint that is not enabled for Audio Limits.
- Make Settings audio-processing rows stateful: healthy installs stay quiet, missing Equalizer APO shows `Not installed`, and an installed copy with no Device Selector shows `Needs repair`.
- Improve setup dialogs so missing Device Selector state no longer says `Open audio setup` and then unexpectedly opens a download page.
- Rename `Apps that bypass Audio Limits` to `Audio processing limitations` while keeping the hearing-protection boundary unchanged.
- Add an About link and project metadata for the canonical GitHub repository at `https://github.com/MicaLovesKPOP/AudioLimits`, plus a repository-safe `.gitignore` for build/IDE output.

## 1.0.0-pre.16

- Convert the pre.15 diagnostic finding into a first-class hardware-volume capability boundary instead of applying a device-specific correction factor.
- Detect Core Audio hardware-volume support per endpoint.
- On hardware-volume endpoints, keep the Windows volume position unchanged during Set/Change/Remove and change only managed attenuation under the existing transition-mute barrier.
- Keep `Set current output as limit` visible but disabled for hardware-volume endpoints; show a concise visible explanation and supplemental tooltip.
- Show `Same output without limit` as `Not available` while a hardware-volume endpoint is limited rather than presenting false precision.
- Add direct Remove-limit confirmation for hardware-volume devices and dynamic Set/Change copy that distinguishes quieter vs louder changes.
- Accept whole-number limits from 1–100; 100% invokes the real Remove-limit path and is never persisted as an active limit.
- Hide NumberBox spin buttons to avoid the native Compact flyout dominating the dialog.
- Persist the actual pre-operation managed attenuation in pending recovery state so rollback can restore external drift exactly.
- Make startup reconciliation hardware-aware: never move hardware volume automatically and never auto-relax a stronger live attenuation.
- Replace the placeholder Settings page with Community Toolkit SettingsCard rows for Start with Windows, audio processing/device management, bypass information, diagnostic logs, and About/version.
- Add a WinUI app-layer StartupService and background-launch path for startup checks.
- Add settings-store tests for the new durable previous-applied-attenuation field.

## 1.0.0-pre.15

- Add diagnostic-only endpoint/transition logging for the headset-specific loudness-preservation defect observed in pre.14.
- Record endpoint scalar, endpoint dB, dB range/increment, hardware-volume support flags, native volume step position/count, preferred Equalizer APO stage, managed attenuation, expected saved limit, and whether that expected limit is active.
- Log the transition plan plus snapshots before/after mute, endpoint movement, Equalizer APO reload, unmute, and final commit.
- Do not alter limiter mathematics, safe transition ordering, mute timing, APO write behavior, or the WinUI command wiring.
- Keep the settled NumberBox/100%-means-remove UI changes deferred until the headset diagnosis is complete, so this candidate changes observation rather than behavior.
- Record that disabling Windows Audio enhancements also disables the Equalizer APO stage on the affected headset; this is not a valid isolation test for downstream effects.

## 1.0.0-pre.14

- Restore WinUI Set/Change, Set-current and Remove limit mutations through Core `LimitService`.
- Run startup reconciliation before mutations become available.
- Add native ContentDialog/NumberBox limit editing, per-device ProgressRing state, and global mutation lockout while an operation is active.
- Remove the duplicate body Limit value and keep configured state in the card header.
- Replace the misaligned Settings glyph implementation with a centered native SymbolIcon/SubtleButtonStyle title-bar command.
- Preserve the pre.6 mute barrier; target-Windows testing confirmed clean mute transitions with no unexpected transient noises.
- Discover the headset-specific loudness-preservation mismatch that became the focus of pre.15 diagnostics.

## 1.0.0-pre.13

- Complete the Stage 2 read-only main-page visual pass without reconnecting any audio mutation command.
- Remove the permanent migration-preview InfoBar from the healthy state so the page now resembles the intended v1 surface.
- Split endpoint friendly names into a primary device name plus secondary parenthesized device/driver description where possible.
- Refine card hierarchy with system card brushes, a subtle divider, secondary value labels, improved spacing, and a more polished empty/loading state.
- Surface naturally occurring missing-APO and inactive-saved-limit states through native WinUI InfoBars.
- Keep live Windows-volume and uncapped-equivalent projection read-only and preserve the rc.1 backend source behavior.
- Record the user-provided pre.12 dark-theme screenshot as visual evidence of successful WinUI shell/device-card rendering; nonvisual pre.12 checks remain separate evidence.


## 1.0.0-pre.12

- Begin the bounded WinUI 3 modernization instead of continuing to style WinForms.
- Split the known-good backend into `AudioLimits.Core` and a new `AudioLimits.App` WinUI project.
- Pin Windows App SDK 2.3.1 stable for the migration preview.
- Add Mica, the native WinUI TitleBar control, in-window Devices/Settings navigation and the existing Audio Limits identity.
- Add a modern read-only device-card surface that shows saved/active limit status, live Windows volume and calibrated uncapped-equivalent output.
- Preserve single-instance activation without keeping a WinForms dependency.
- Intentionally do not construct/call LimitService startup repair or expose any limit mutation command in this stage.
- Preserve the backend source behavior; backend files were moved with namespace/using changes only.
- Add `docs/MODERN_UI_PLAN.md` describing the final layout, native/native-imitation inventory, lifecycle, accessibility and staged path to rc.2/final 1.0.


## 1.0.0-rc.1

- Promote the pre.11 codebase to the first final v1 release candidate with no limiter, Equalizer APO, persistence, UI, theme, lifecycle, or icon implementation changes.
- Record the user-reported pre.11 Windows runtime icon pass: the custom Audio Limits identity is now visible in the running application and notification area.
- Keep the already-passed pre.6 safety-critical audio behavior and pre.8 theme behavior as prior runtime evidence rather than needlessly repeating those matrices.
- Narrow the final RC test to build/test cleanliness, release identity, close/minimize persistence, keyboard/dialog sanity, startup-setting persistence, and a normal-theme spot-check.
- Keep actual boot-time startup execution and mixed-DPI/multi-monitor behavior explicitly unverified unless exercised on the target machine.


## 1.0.0-pre.11

- Record the user-reported pre.10 runtime failure: the intended Audio Limits identity did not visibly appear in either the running app/window or notification area.
- Replace the runtime ICO-stream path with embedded 32-bit PNG artwork converted to owned native HICONs at runtime. The executable still keeps `Assets\AudioLimits.ico` through `ApplicationIcon` for shell identity.
- Re-apply the window icon after HWND creation so title-bar identity does not depend on constructor-time handle timing.
- Initialize `NotifyIcon` with an icon in its object initializer and use a fresh owned HICON whenever the shell-theme glyph changes.
- Add icon initialization/change logging.
- Strengthen icon tests: runtime icons must be 32x32 and contain visible pixels, rather than merely being non-null.
- Keep limiter, Equalizer APO, calibration, persistence, lifecycle, and theme logic unchanged.

## 1.0.0-pre.10

- Integrate the final Audio Limits visual identity: speaker -> sound wave -> hard stop.
- Add a multi-size executable/window icon with dedicated small-size artwork.
- Replace the generic WinForms notification icon with transparent monochrome Audio Limits tray glyphs.
- Follow the Windows shell/taskbar light-dark preference for tray-glyph contrast, with High Contrast and missing-preference fallbacks.
- Refresh the tray glyph when Windows theme/display preferences change.
- Embed all runtime ICO resources so the self-contained single-EXE publish does not need loose icon files.
- Keep the editable SVG masters in `Assets\`.
- Add tray-glyph selection tests.
- No limiter, Equalizer APO, calibration, or audio-transition behavior changed.

## 1.0.0-pre.9

- Record the user-reported pre.8 focused Windows theme/accessibility pass: Light, Dark, runtime theme switching, High Contrast, dialog/control readability, and tray-menu theming all behaved as expected in the requested test run.
- Change the main-window lifecycle contract: X / Alt+F4 now requests a full safe application exit instead of hiding to tray.
- Change Minimize to hide the main window to the tray; reopening normalizes a previously minimized window before showing it.
- Route title-bar close through the existing `ExitApplication` busy/reconciliation guard so closing cannot interrupt calibration or a transactional limit change.
- Clarify Settings startup copy: Start with Windows is optional because Equalizer APO keeps configured limits active when Audio Limits is not running; startup is for reconciliation/checks and tray availability.
- Leave the pre.6-tested audio transition/backend logic and the pre.8-tested theme implementation unchanged.

## 1.0.0-pre.8

- Fix the pre.7 Windows compile failure in `ThemeManager.ApplyControl`: `TableLayoutPanel` and `FlowLayoutPanel` derive from `Panel`, so their pattern-matching cases must appear before the general `Panel` case.
- Preserve the intended theme behavior for layout panels rather than deleting the specific cases.
- Record the first pre.7 Windows build attempt as a compile-gate failure before publishing or runtime theme testing; no pre.7 runtime behavior is treated as verified.
- Keep the pre.6 audio/runtime pass unchanged; pre.8 remains a theme/accessibility-only candidate.

## 1.0.0-pre.7

- Record the user-reported pre.6 pass for the headset mute-barrier Remove-limit retest: the transition completed as mute -> compensated output with no louder spike.
- Record the subsequent user-reported passes for the LG TV 33% limit, tray/persistence lifecycle, single-instance activation, and Equalizer APO Device Selector launch without the former Qt error.
- Add system-following light/dark/high-contrast behavior as the next v1 release gate without changing the already-passed audio transition logic.
- Read the documented Windows `AppsUseLightTheme` app preference; High Contrast takes precedence, and missing/unreadable preference data safely falls back to light mode.
- Use the supported `DWMWA_USE_IMMERSIVE_DARK_MODE` frame attribute on Windows 11 for the standard title bar while leaving older Windows frame behavior to the platform.
- Theme WinForms client surfaces, standard controls, modal forms, and the tray context menu for .NET 8 without relying on undocumented UxTheme dark-mode entry points.
- Listen for Windows user-preference/display changes and re-apply the current theme to every open Audio Limits form and the tray menu at runtime.
- Add pure theme-selection tests covering dark/light fallback and High Contrast precedence.

## 1.0.0-pre.6

- Treat the brief louder spike observed during the otherwise-successful pre.5 Remove-limit test as a release-blocking safety defect.
- Temporarily mute any previously-unmuted endpoint before every real effective-gain transition, wait briefly for the mute to settle, keep the existing safe endpoint/config ordering underneath it, and restore the prior unmuted state only after both subsystems have settled.
- Apply the same mute barrier to startup reconciliation and interrupted-change rollback, not only the explicit Remove-limit path.
- Preserve fail-quieter behavior: endpoint-floor cases and incomplete recovery remain muted rather than risking a louder output.
- Add planner regression coverage asserting that stronger, weaker, and removal transitions require the mute barrier while no-op attenuation changes do not.
- Record that pre.5 fixed the transient atomic-file replacement failure and completed Remove-limit, but still produced the audible spike that pre.6 is intended to eliminate.
- Keep system-following dark/light/high-contrast behavior as a separate unresolved v1 release requirement.

## 1.0.0-pre.5

- Add bounded retry handling around the atomic replacement of Equalizer APO configuration files when Windows reports a transient `UnauthorizedAccessException` or `IOException`.
- Preserve the atomic temp-file + replace design; persistent permission failures still fail closed after six attempts rather than falling back to an in-place partial write.
- Add four unit tests for transient access-denied retry, transient I/O retry, non-transient failure pass-through, and bounded retry exhaustion.
- Record the pre.4 runtime evidence: full-width device cards and Settings layout are correct; headset calibration/re-apply behaved as expected with no louder transient; `Set current output as limit` produced the expected new limit; the first `Remove limit` attempt failed safely because the managed Equalizer APO file was momentarily unavailable for atomic replacement, and automatic rollback restored the previous limit.
- Keep system-following light/dark/high-contrast behavior as an unresolved v1 release requirement; this safety fix is intentionally isolated from theme work.

## 1.0.0-pre.4

- Replace the vertical `FlowLayoutPanel` device host with a one-column `TableLayoutPanel` so each device card fills a real table cell instead of depending on FlowLayoutPanel's implicit-column width rules.
- Remove timing-sensitive manual card-width reconciliation entirely.
- Keep the pre.3 Settings group-box layout fix; the pre.4 Windows screenshot confirms those sections render correctly.
- Record system-following light/dark theme behavior as a v1 release requirement rather than optional cosmetic polish. Theme work will include the window frame, client controls/dialogs, tray menu, and high-contrast fallback.

## 1.0.0-pre.3

- Fix first-launch device cards collapsing to a narrow empty strip because the initial resize ran before the `FlowLayoutPanel` had its real client width.
- Re-size device cards whenever the device host client size changes, with a re-entrancy guard.
- Fix the General and About group-box contents overlapping their captions/borders by placing them in DPI-aware layout containers.
- Record the first successful Windows build/test/publish report for pre.2 and the observed pre.2 UI-layout failure.

## 1.0.0-pre.2

- Fix managed `Include: AudioLimits.txt` detection for normal Windows CRLF `config.txt` line endings. The pre.1 regex used multiline `$` without accounting for the carriage return, causing the first Windows test run to fail its dedicated regression test.
- Add explicit CRLF/LF regression coverage for the managed include detector.
- Clean up the three xUnit collection assertions reported by the pre.1 Windows build so the next test run should be warning-free for those analyzer findings.
- Record the first real Windows build evidence: production and test projects compiled, 41 tests were discovered, 40 passed, and the single CRLF detector regression test failed before publishing. No audio runtime behavior was exercised.

## 1.0.0-pre.1

- Normalize the first Windows-test handoff candidate to semantic pre-release version `1.0.0-pre.1` instead of continuing the internal `rc` sequence.
- Add a fail-closed Equalizer APO conditional-structure preflight so Audio Limits will not add or change its managed include inside an unclosed outer `If` block.
- Recover a missing primary settings file from a validated backup, while treating an invalid orphaned backup as non-authoritative and leaving existing audio processing unchanged.
- Keep managed-config changes behind strict ownership/structure validation, including orphaned/duplicate include detection and exact command validation.
- Clarify build documentation: the pre-release build gate refuses to run while Audio Limits is active rather than terminating it automatically.
- Record the upstream Equalizer APO, NAudio, Windows endpoint-volume, and WNAQ assumptions used by the implementation.
- Retain the existing safety-first transition/recovery model; no Windows runtime behavior is claimed as verified until `PRE_RELEASE_TEST.md` passes on the target machine.

## 1.0.0-rc4

- Reconcile the committed Equalizer APO state before every user-initiated limit change, so a manually drifted active attenuation cannot be mistaken for an inactive limit.
- Add a 30-day validity window for automatic interrupted-change recovery; stale recovery intent is preserved but is not automatically applied.
- Tighten pending-change validation for processing phase, nested endpoint identity, and implausible future timestamps.
- Launch Equalizer APO Device Selector through Windows ShellExecute from Equalizer APO's own working directory, matching normal Explorer/Start-menu launch behavior and avoiding an Audio Limits-specific Qt plugin environment.
- Improve device-card accessibility grouping and clarify the Settings backend status as `Installed` rather than implying every endpoint is ready.
- Treat orphaned/duplicate managed include lines as owned state, strictly reject unexpected commands in `AudioLimits.txt`, and normalize to exactly one managed include.
- When Windows cannot lower an endpoint enough to preserve an extremely quiet output during attenuation relaxation/removal, leave the device muted and tell the user instead of automatically ending louder.
- Fail closed on duplicate/orphaned/damaged `AudioLimits.txt` include references so normalization cannot accidentally relax multiplied or ambiguous attenuation.
- Restore the saved previous limit intent as well as external processing during interrupted-change rollback.
- Add a double-clickable `Build-PreRelease.cmd` test-build launcher.

## 1.0.0-rc3

- Finalized the v1 UI language around `Windows volume`, `Same output without limit`, and `Limit`.
- Kept whole percentages in normal UI while retaining full dB precision internally.
- Added card-based device UI, dedicated whole-percent limit dialog, tray lifecycle, Settings, and optional Start with Windows.
- Added single-instance behavior; a second launch activates the existing process instead of creating another tray icon.
- Replaced prototype polling for volume display with NAudio endpoint-volume notifications.
- Added sampled per-endpoint Windows scalar-to-dB curves for exact uncapped-equivalent display.
- Added safe transition planning for add/change/remove operations.
- Added durable pending-change state with phase, retry metadata, and deterministic rollback to previously committed intent.
- Changed recovery to establish the strictest plausible attenuation first and preserve the current recovery-time output instead of blindly restoring stale endpoint volume.
- Added startup reconciliation that cannot relax stronger managed attenuation before lowering the endpoint.
- Added authoritative settings-load states so corrupt/missing state cannot be silently interpreted as “remove all attenuation”.
- Added validated backup recovery, strict settings validation, quarantine of corrupt state, and crash-safer settings replacement.
- Added strict validation/parsing of Audio Limits' managed Equalizer APO file before startup reconciliation.
- Added Equalizer APO stage-aware detection using the actual LFX/SFX pre-mix and GFX/MFX/EFX post-mix slots used by Equalizer APO.
- Prefer post-mix; support pre-mix when Device Selector installs only that stage.
- Added per-device Stage directives and reset Device/Channel/Stage selectors at the end of the managed include.
- Preserved migration compatibility with v0.x `AudioLimits` managed headers/markers.
- Fixed Equalizer APO 1.4.x Device Selector launching with its own working directory and Qt platform-plugin path when present.
- Added bounded logging.
- Added WNAQ-aligned project instructions, project profile, UX specification, verification ledger, and risk-ordered pre-release test plan.
- Added unit tests for transition safety invariants, settings recovery/migration, volume curves, and managed APO config parsing.

## Prototype history

### v0.3
- Equalizer APO 1.4.x detection.
- Live Current display.
- Initial wording cleanup.
- Known defects: duplicate processes were allowed; Device Selector launched incorrectly; uncapped-equivalent output was not visible after limiting.

### v0.2
- Replaced handwritten Core Audio COM layer with NAudio and successfully enumerated the target playback devices.

### v0.1
- First functional source prototype.
- Known defect: handwritten Core Audio COM declarations failed with `Specified cast is not valid`.
