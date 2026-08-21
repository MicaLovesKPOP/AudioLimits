# Audio Limits project profile

## Current gate

**1.0.0-rc.2 — GitHub-ready release candidate.**

The limiter/backend, Equalizer APO integration, WinUI UI, tray/lifecycle, state-aware Setup, and healthy/relocated launcher paths have passed real-Windows validation. rc.2 freezes that behavior and normalizes the public distribution layout.

## Canonical distribution layout

Both supported binary distributions contain the same application tree:

```text
Audio Limits\
├─ AudioLimits.exe
└─ app\
   ├─ AudioLimits.App.exe
   └─ ...
```

- `AudioLimits.exe` is the only public application entry point. It is a compressed self-contained launcher with no WinUI dependency.
- `app\AudioLimits.App.exe` is the framework-dependent WinUI host and is implementation detail.
- Start-with-Windows routes through the root launcher.
- An intact copied/extracted folder remains runnable without pretending to be a Windows installation.

## Public GitHub binary policy

Exactly two release assets are intentional:

1. `AudioLimits-Setup.exe` — recommended normal installation.
2. `AudioLimits-1.0.0-rc.2-x64.zip` — extract-and-run alternative containing the canonical `Audio Limits` folder.

Do not publish a separate Portable, Standalone, or ad-hoc raw EXE. The ZIP is not portable-state software: settings remain per-user and normal Windows integration can still be enabled.

## Deferred validation

The only architectural validation intentionally left for later is a clean Windows VM with missing Microsoft prerequisites. The root launcher already detects/downloads/signature-validates/installs them after consent, but that recovery path has not yet been exercised end-to-end on a genuinely clean machine.
