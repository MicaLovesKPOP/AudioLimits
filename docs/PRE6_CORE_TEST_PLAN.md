# Audio Limits v1.0 pre-release test run — 1.0.0-pre.6

> **pre.6 note:** pre.5 successfully removed the headset limit after the atomic-write retry fix, but the user heard a brief louder spike during removal before the compensated Windows volume took effect. That is a release-blocking safety defect. pre.6 performs every real gain transition behind a temporary endpoint mute, while retaining the previous endpoint/config ordering as defense in depth. Build pre.6, confirm startup does not change loudness, then repeat the Remove-limit test before continuing.
This is the first controlled test of the v1 candidate on real Windows audio hardware.

**Stop immediately if any transition is unexpectedly louder.** For the first add/change/remove tests, take the headset off your ears and use ordinary shared-mode Windows audio (for example browser/system audio), not ASIO or WASAPI exclusive mode.

## 0. Preserve the prototype state

If v0.3 currently has a limit applied, do not manually remove it first. The v1 candidate is designed to migrate that state without a louder transition.

Exit every running Audio Limits window/tray instance before building. The build script deliberately refuses to terminate `AudioLimits.exe` itself because doing so could interrupt an audio-state change.

## 1. Build gate

Double-click `Build-PreRelease.cmd` in the 1.0.0-pre.6 project folder. It will run the tests and publish step, then open the `publish` folder only after success.

If you prefer PowerShell, the equivalent command is:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Continue only if:

- all tests pass;
- publishing succeeds;
- the script prints `Pre-release build succeeded`;
- `publish\AudioLimits.exe` exists.

If compilation or tests fail, stop and send the complete PowerShell output.

## 2. First launch / prototype migration

Take the headset off your ears before first launch if an old prototype limit may still be active.

1. Run `publish\AudioLimits.exe`.
2. Confirm the window appears after the brief startup check.
3. Confirm neither device becomes unexpectedly louder during launch.
4. If a v0.x limit existed, confirm it appears as a saved/active limit rather than silently disappearing.
5. If a migrated old limit has no stored full volume curve, an intermediate `Same output without limit` value may temporarily show `—`; at Windows 100% the stored limit percentage is still shown exactly. Setting/changing that limit once records the full v1 curve.

## 3. Single instance

1. With Audio Limits already running, launch `publish\AudioLimits.exe` again.
2. Confirm the existing window is brought forward.
3. Confirm there is still only one `AudioLimits.exe` process and one tray icon.
4. Repeat once more.

This closes the prototype defect where three independent instances could run.

## 4. Live display with no limit

Use a device that currently has no limit, or remove one later after the initial safety tests.

1. Change Windows volume several times.
2. Confirm `Windows volume` updates without pressing Refresh.
3. Confirm `Same output without limit` matches Windows volume when no limit is active.
4. Test mute and unmute.
5. Confirm normal UI shows whole percentages only.

## 5. Audio setup launcher

Open **Settings -> Manage audio devices…**.

Expected:

- Equalizer APO Device Selector opens normally.
- The previous Qt platform-plugin error must not appear.
- Close Device Selector without changing anything.

If it still fails, capture the exact error and stop this subtest; other safe tests may continue.

## 6. Headset — first limit

Take the headset off your ears.

1. Ensure the Corsair endpoint is enabled in Equalizer APO Device Selector.
2. Set Windows headset volume to 5%.
3. Play steady, non-startling audio.
4. Set a 10% limit.
5. Applying the limit must **not** make the current output louder.
6. Windows volume may move upward to preserve the previous audible output.
7. Slowly raise Windows volume to 100%.
8. At Windows 100%:
   - `Limit` = 10%
   - `Same output without limit` = 10%
   - output should match the old uncapped 10% level, not uncapped 100%.
9. Move Windows volume through several positions. `Same output without limit` should change plausibly and never exceed 10%.

Only put the headset back on after the first transition has behaved safely.

## 7. Stronger limit

With audio at a moderate level:

1. Change 10% -> 5%.
2. No loud transient is acceptable.
3. If the current output is above the new ceiling, becoming quieter is expected.
4. At Windows 100%, `Same output without limit` should read 5%.

## 8. Weaker limit

1. Put Windows volume somewhere below the current ceiling.
2. Change 5% -> 15%.
3. Current audible output should remain essentially unchanged.
4. Windows volume should move downward if needed before attenuation is relaxed.
5. No loud transient is acceptable.
6. At Windows 100%, `Same output without limit` should read 15%.

## 9. Set current output as limit

1. Establish a comfortable output below full uncapped output.
2. Note `Same output without limit`.
3. Click `Set current output as limit`.
4. Audible output should remain essentially unchanged.
5. Windows volume should become or approach 100%.
6. The new whole-number `Limit` should match the previous uncapped-equivalent output rounded to the nearest percent.

At Windows 100% with an already-active limit, this command should be unavailable/no-op because the current output is already the existing ceiling.

## 10. Remove limit

Take the headset off your ears again for the first removal.

1. Put Windows volume below 100%.
2. Remove the limit.
3. A short intentional mute/dropout is expected while Audio Limits changes the Windows endpoint and Equalizer APO state.
4. The device must remain muted across the gain transition; there must be **no louder spike at any point**.
5. After the transition, output should return at essentially the same perceived level unless the endpoint floor makes exact preservation impossible. In that edge case Audio Limits should remain muted and explain why.
6. `Limit` becomes `Off`.
7. `Windows volume` and `Same output without limit` match again.

## 11. LG TV

Repeat the manual-limit test on:

`LG TV SSCR2 (HD Audio Driver for Display Audio)`

Use a 33% limit.

At Windows 100%:

- `Limit` = 33%
- `Same output without limit` = 33%

Confirm normal TV/system volume controls still behave as expected.

## 12. Persistence and app lifecycle

With at least one limit active:

1. Close the main window using X.
2. Confirm it hides to tray.
3. Confirm the limit remains active.
4. Reopen from the tray.
5. Exit from the tray menu.
6. Confirm the limit remains active after the process exits.
7. Relaunch Audio Limits.
8. Confirm startup does not produce a louder jump and the saved limit returns as active.

## 13. Managed-state drift safety

This intentionally simulates a manual Equalizer APO edit after Audio Limits is already running. Keep the headset off your ears.

1. With a 10% headset limit active, open Equalizer APO's `AudioLimits.txt`.
2. Note the headset `Preamp` value, then make it **more negative** by 3 dB and save.
3. Wait a moment for Equalizer APO to reload. The output should become quieter.
4. In Audio Limits, change the limit to 15%.
5. Audio Limits must first reconcile the drifted managed state safely; there must be no louder transient.
6. Confirm the managed file returns to an Audio Limits-generated value and the final UI reads `Limited to 15%`.

Do not test by making the preamp less negative for this first pre-release run; the purpose is to prove Audio Limits does not mistake a drifted active entry for an inactive one.

## 14. Device lifecycle

1. Disconnect/disable the Corsair endpoint.
2. Wait up to 5 seconds.
3. Confirm the app does not crash.
4. Reconnect it.
5. Confirm it returns.
6. If Windows assigns a new endpoint GUID, Audio Limits must not silently attach the old saved limit to the new endpoint or claim it is active.

## 15. Startup setting

1. Enable `Start Audio Limits with Windows`.
2. Confirm Settings shows it enabled after closing/reopening Settings.
3. If convenient, test the next sign-in: Audio Limits should start in the tray without forcing the main window open.
4. Disable startup again and confirm the choice persists.

## 16. Basic native-UI checks

Without changing audio state:

1. Navigate the main window and limit dialog using Tab/Shift+Tab.
2. Open a limit dialog and use Enter/Escape.
3. Resize the main window to its minimum and larger sizes.
4. If convenient, test 125%/150% display scaling.
5. If convenient, briefly enable Windows high contrast and confirm text/buttons remain usable.

These are pre-release observations; do not infer they passed merely because the app compiled.

## Known enforcement boundary

Do not use ASIO or WASAPI exclusive mode to judge the limiter itself. Equalizer APO depends on the Windows system-effects path; those modes can bypass it. Audio Limits is not a hearing-protection control.

## Report back

For any failure, capture:

- exact step number;
- what you clicked;
- Windows volume before/after;
- `Same output without limit` before/after;
- whether audible output became quieter, unchanged, or louder;
- screenshot/error text;
- `%LOCALAPPDATA%\AudioLimits\logs\AudioLimits.log` if an error occurred.

If every safety-critical step passes, report that explicitly and include any cosmetic/UX issues separately.
