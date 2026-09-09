# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows-desktop

## Users

Captail serves Windows players who want to preserve recent gameplay without running a full streaming suite. They use it while gaming, often with the main window minimized to the system tray, and need to trust that capture is still healthy before a moment worth saving happens.

## Product Purpose

Captail keeps a rolling local replay buffer and saves the most recent seconds or minutes on demand. Success means that recording state is immediately understandable, saving a replay is effortless, and recoverable capture failures never remain silent.

## Positioning

Captail is a focused, open-source instant-replay tool: one supervised capture pipeline, automatic desktop/game switching, a local replay library, and trimming without the scenes, streaming setup, accounts, cloud upload, analytics, or telemetry of a broader capture suite.

## Operating Context

The application runs on Windows 10 and 11, stays available from the system tray, records in the background, and is commonly used alongside fullscreen games. Users configure video, audio, storage, replay behavior, hotkeys, and notifications; they save clips from a global hotkey or the main window, then play, rename, reveal, delete, or trim them locally.

## Capabilities and Constraints

- Native WPF application targeting .NET 9 and x64 Windows.
- Hardware H.264, HEVC, and AV1 encoding through supported NVIDIA, AMD, or Intel GPUs.
- Desktop and direct game-capture workflows with watchdog recovery.
- Mixed, separate, and per-application audio routing.
- Local replay library, playback, and stream-copy trimming.
- Eleven localization dictionaries and layouts that must tolerate translated copy.
- The recording path is performance-sensitive; decorative UI work must not add capture-time GPU or CPU load.
- Existing behavior, automation identifiers, keyboard workflows, and native Windows affordances must remain intact during the redesign.

## Brand Commitments

The confirmed product name is Captail. The user explicitly delegated a replacement visual identity and is not attached to the inherited dark-and-mint design. Existing icon artwork may be retained where technically required, but the prior UI is not a visual constraint.

## Evidence on Hand

- Product and workflow documentation: `README.md`, `CONTEXT.md`.
- Current interface implementation: `src/Captail/*.xaml`, `src/Captail/Themes/*.xaml`.
- Current screenshots: `docs/captail-*.png` and `store-listing/images/captail-*.png`.
- Existing automated UI/layout checks: `tools/TestStartupUiSurfaces.ps1`, `tools/TestLocalizedLayout.ps1`, and `tests/Captail.Tests/XamlResourceTests.cs`.
- No external user research, analytics, testimonials, or benchmark evidence is available and none should be fabricated.

## Product Principles

- Make capture health unmistakable at a glance.
- Keep saving a replay the fastest and most visually dominant action.
- Hide complexity until the user enters settings, then group it by task rather than implementation detail.
- Prefer durable native controls and immediate feedback over decorative effects.
- Keep recordings, settings, and diagnostics transparent and local-first.

## Accessibility & Inclusion

The redesign must preserve keyboard operation, automation names, visible focus, readable localized text, non-color state cues, and Windows scaling behavior. Motion must be brief, purposeful, and safe to disable.
