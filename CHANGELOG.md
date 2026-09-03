# Changelog

All notable user-facing changes are documented here.

## [Unreleased]

## [0.5.13] - 2026-09-03

### Fixed

- **Startup crash:** Replaced two references to missing XAML styles in the new recording-efficiency settings, preventing Captail from failing while opening the settings window.
- **XAML resource validation:** Added regression coverage that rejects unresolved `StaticResource` keys before release.
- **Startup diagnostics:** Startup exceptions are now written to the Captail log with their full stack trace.

## [0.5.12] - 2026-09-03

### Added

- **Performance telemetry:** Built-in capture performance snapshots track rendered frames, lagged frames, encoded frames, output bytes, CPU time, and working set. A structured `PERF` log line is emitted per completed measurement sample without impacting the capture hot path.
- **Recording performance QA tool:** New `tools/ReplayPerformanceQa` console harness runs repeatable benchmark scenarios (baseline, replay, record, replay+record, save-replay, advanced-audio) with configurable warm-up, sample duration, and iteration count.
- **Suspend Instant Replay during manual recording:** New option pauses the replay buffer while a manual recording is active, reducing GPU encoder and disk I/O load. Replay resumes automatically with a fresh window after recording stops. The setting is enabled by default and clearly labeled in the UI.
- **Warm recording pipeline:** New option keeps the OBS pipeline initialized when Instant Replay is off, so pressing Record starts capturing without cold-start delay. Disabled by default until idle overhead is measured on target hardware.
- **Performance budgets:** Documented target thresholds for p99 frame time, lagged frames, CPU overhead, Record-to-first-byte latency, and stop/finalization time in `docs/PERFORMANCE_BUDGETS.md`.

### Improved

- **NVENC Low Overhead is now the default** for new configurations. Existing users who chose Balanced keep their setting. Invalid or missing modes normalize to Low Overhead. All 11 localizations updated.
- **Graceful recording output shutdown:** Dispose now attempts a clean `obs_output_stop` with a brief wait before falling back to `force_stop`, reducing the chance of truncated final MP4 fragments.
- **Earlier replay-suspension guard:** Saving a replay while the buffer is suspended now fails immediately with a clear message instead of waiting for a lock.
- **Recording output readiness check:** Manual recording waits for the first encoded packet before suspending the replay buffer, preventing a race where the replay stops before the recording encoder produces data.
- **Resilient OBS log bridge:** Native log handler install/remove is wrapped in exception handlers so a missing bridge DLL degrades gracefully instead of crashing.

### Known limitations

- Performance budgets are initial targets. Actual thresholds depend on GPU generation, driver version, capture resolution, and disk speed. Run the QA harness on your hardware to establish a local baseline.
- The warm recording pipeline keeps OBS sources and encoders allocated in memory. Idle GPU and CPU cost has not been measured across all supported hardware.

## [0.5.11] - 2026-09-03

### Improved

- **Tighter Indicator Margins**: Reduced corner border insets to 4 px so the recording indicator tucks neatly against the screen or window corner without distracting from game UI.

### Fixed

- **Exclusive Game Window Overlay**: Fixed an issue where recording indicators floated over the desktop and other applications (browser, Discord). The indicator now hides immediately whenever the captured game is minimized or out of focus, and instantly reappears when the game returns to the foreground.
- **Equal Window Border Insets**: Corrected an alignment issue on windowed games where DWM invisible resize margins skewed indicator positioning. Indicators now use Desktop Window Manager extended frame bounds, ensuring exact 4 px margins from all adjacent window borders.

## [0.5.10] - 2026-09-03

### Improved

- **Translucent & Compact Recording Indicators**: Reduced status indicators to a sleek 18×18 px footprint with 75% opacity, soft dark glass backing, and balanced 8 px margins from both edges in all four screen corners.
- **Dual Indicator Alignment**: Arranged simultaneous Instant Replay and manual recording indicators on a unified coordinate axis with a clean 4 px separation.

### Fixed

- **Game Window Indicator Anchoring**: Fixed an issue where the recording indicator jumped across monitors to foreground windows (Discord, browser) while capturing a game. Indicators now firmly resolve and lock to the target game window and its display.

## [0.5.9] - 2026-09-03

### Improved

- **Quieter Recording Indicators**: Replaced continuously rotating and pulsing status indicators with compact static states, moved the screen overlay closer to the selected edge, and arranged simultaneous Instant Replay and manual recording indicators without overlap.
- **Clearer Capture Summary**: The dashboard status now prioritizes the replay duration and the executable name of the game actually being captured, omitting redundant audio details so the summary remains visible.
- **Smaller Replay Card Actions**: Reduced the hover action tray, controls, spacing, and shadow again so it covers less of each replay title while keeping all four actions and their tooltips available.

## [0.5.8] - 2026-09-03

### Improved

- **Compact Replay Card Actions**: Reduced the hover action tray and its four controls so replay titles stay visible longer while preview, folder, trim, and delete remain immediately accessible.

## [0.5.7] - 2026-09-03

### Improved

- **Overlay Notification Layout**: Kept the notification lifetime indicator inside the card instead of overlapping its content and escaping the rounded boundary, while refining spacing, typography, state colors, and empty-detail handling to match Captail's visual language.

## [0.5.6] - 2026-09-03

### Improved

- **Replay Library Duration Caching**: Added persistent disk-based `.meta` file and in-memory caching for clip durations in the replay library, eliminating up to 128 redundant `ffprobe` and `ffmpeg` process executions when opening or scrolling clip pages.
- **Hot Path Native Interop Optimization**: Optimized libobs status and procedure callbacks (`ReadBoolProcedure`, `ReadStringProcedure`, `TryReadProcessAudioStatus`) by switching from repeated unmanaged heap allocations and manual zeroing loops to zero-allocation stack buffers (`stackalloc`), eliminating micro-stutter and memory churn during active gameplay.
- **Lightweight Process Inspection**: Replaced heavyweight `Process.GetProcessById` instantiations in foreground detection and audio monitoring with direct Win32 `QueryFullProcessImageNameW` P/Invoke calls using stack-allocated buffers, significantly reducing garbage collection pressure.
- **Display Window Manager (DWM) Compositor Decoupling**: Added coordinate and dimension change tracking to the replay status indicator window, eliminating redundant Win32 `SetWindowPos` calls every 750ms and preventing unnecessary compositor invalidations.
- **Log File Rotation & Size Protection**: Enforced an automatic 10 MB file rotation threshold on `log.txt` with graceful rollover (`log.old.txt`) and concurrency lock backoff, protecting disk space from unbounded log accumulation during extended tray uptime.

## [0.5.5] - 2026-09-03

### Fixed

- **Replay Library Full Title & Metadata Visibility**: Removed an excessive static right margin (`135px`) on replay library cards that prematurely truncated clip titles and date/time metadata when resting, restored full-width text rendering across the card with hover tooltips, and elevated the floating action button tray with a refined drop shadow.

## [0.5.4] - 2026-09-03

### Added

- **Open / Hide Global Hotkey**: Added a customizable global hotkey (`Ctrl+Shift+F8` by default) to quickly show and focus Captail or minimize it back to the system tray, complete with Settings capture UI, collision resolution, and tray menu integration.

### Improved

- **Compact & Restrained Overlay Notifications**: Redesigned overlay notifications according to antislop principles—reduced window size from 384×110 to 310×68 px, refined 9px corner radius, subtle drop shadow, 22×22 icon badge, 1.5px flush lifebar, and snappier 160–200ms slide-in animation.

### Fixed

- **Action Buttons Text Trimming & Unit Formatting**: Replaced unconstrained horizontal button layouts with a 3-column grid (`Auto`, `*`, `Auto`), preventing text overflow and clipping in the Save Replay button, normalized Ukrainian seconds abbreviation to standard `с`, and added hover tooltips showing the full action title and keybinding.

## [0.5.3] - 2026-09-03

### Added

- **Ukrainian Installer Localization & Polished Wizard**: Replaced legacy installer language files with complete Ukrainian translations (`Ukrainian.isl`), streamlined the setup wizard flow, and added quiet relaunch behavior after update installations.
- **Silent Automatic Updates**: Added a background update monitor and user toggle (`AutoUpdate`) in General Settings to seamlessly download and apply updates in the background when not recording.

### Fixed

- **Replay Library Title Contrast**: Fixed a bug where recent replay titles in `RecentReplaysList` inherited black text (`#000000`) on dark surfaces and dimmed on hover; explicitly bound `Foreground` to `TextPrimaryBrush` and preserved full opacity.
- **Dashboard Action Buttons Alignment**: Unified `SaveReplayButton` and `RecordButton` layout using constrained 2-column grids, centered 16×16 icon boxes, identical 18px hotkey badge baselines, matching corner radii (`11`), and text trimming to prevent overlapping in localized interfaces.

## [0.5.2] - 2026-09-03

### Improved

- **Replay Library Virtualization & Pagination**: Switched the replay library to incrementally load pages of 64 items with native WPF container recycling and thumbnail decoding at 224px display width, reducing memory overhead by up to 95% in large libraries.
- **Responsive Background Scanning**: Replaced full-directory enumeration and sorting with a min-heap candidate queue executed on a background thread, preventing UI stutters when opening Settings or scrolling the clip history.
- **Bounded Thumbnail Cache**: Capped generated preview and thumbnail cache storage at 512 MB with periodic background cleanup of entries older than 30 days.

### Fixed

- **Dashboard Record Button Layout**: Corrected text and hotkey pill alignment within subtle action buttons so the manual recording button stretches symmetrically across the dashboard.
- **Timeline Trimming Alignment**: Positioned range trim handles cleanly outside the active selection track, aligning cut boundaries exactly to the handles' edges rather than their centers to avoid obscuring video frames.
- **Replay Refresh Concurrency**: Added queued refresh handling to prevent lost clip updates when file modifications or deletions occur during scroll-based pagination.

## [0.5.1] - 2026-09-02

### Improved

- **WCAG AA Contrast Calibration**: Calibrated `ColorTextMuted` to `#7A878F` (4.6:1+ contrast ratio) and `ColorRingIdle` to `#606C74` (3.1:1+ ratio), significantly improving readability of section headers, hints, and idle status indicators on dark surfaces.
- **Keyboard Navigation & Modern Focus**: Introduced unified Windows 11-style focus indicators (`CaptailFocusVisual`, `CaptailFocusVisualRound`, `CaptailFocusVisualSmall`) with mint accent borders across header buttons, link buttons, menu popups, switches, and route check boxes, eliminating invisible keyboard tab states.
- **Interactive Control States**: Added tactile `IsPressed`, `Hover`, and `Disabled` visual states to accent buttons, subtle action buttons, toggle switches, audio chips, and editable comboboxes.
- **Dashboard Symmetry & Card Feedback**: Balanced primary action buttons with explicit geometry and high-contrast keycap hotkey badges, added cohesive hover states to the folder path button and library clip cards, and introduced native Windows close-button styling.

## [0.5.0] - 2026-09-02

### Added

- **Standard / Manual Recording**: Start and stop standard recording at any time using global hotkey `Ctrl+Shift+F11`, the tray menu, or the dashboard. Records directly to MP4/MKV without duplicate encoder load alongside Instant Replay.
- **Dual-Action Dashboard**: Side-by-side Save Replay and Record buttons with a live duration stopwatch (`00:01:23`).
- **HUD Indicator & Clip Badges**: Pulsating red overlay indicator during active recording and distinct `REC` / `REPLAY` badges in recent replays.
- **Customizable 3-Way Hotkeys**: Dedicated hotkey configuration for save (`Ctrl+Shift+F10`), toggle (`Ctrl+Shift+F9`), and manual recording (`Ctrl+Shift+F11`) with interactive capture and collision prevention.

## [0.4.0] - 2026-09-02

### Added

- **Resilient Replay Runtime**: Encapsulated the recording and OBS lifecycle into a dedicated state-managed module with serialized command processing, graceful shutdown prioritization, and automatic configuration rollback on failure.
- **Automated Test Suite**: Added an xUnit test project (`Captail.Tests`) covering runtime state transitions, configuration transactions, encoding policies, and audio routing.
- **Immutable Dependency Pins**: Pinned external FFmpeg runtimes to immutable versions and SHA-256 digests with automated CI verification gates.

### Improved

- **Settings Maintainability**: Replaced duplicate settings extraction and manual property comparisons with centralized configuration equality checks.
- **Watchdog Recovery**: Watchdog recovery now coalesces concurrent recovery signals to prevent overlapping pipeline restarts during driver or game crashes.

## [0.3.3] - 2026-09-02

### Fixed

- Replaced free-form bitrate entry with Auto and a broader set of reliable presets from 5 to 100 Mbps; legacy custom values now select the nearest preset.

## [0.3.2] - 2026-09-02

### Fixed

- Kept the bitrate selector the same height as the other video controls.
- Added the missing NVENC help affordance and made tooltip spacing and hover behavior consistent across the settings window.

## [0.3.1] - 2026-09-02

### Fixed

- Restored visible and editable bitrate values in compact dark-theme combo boxes.
- Made NVENC adaptive-quantization checkbox text readable and moved the low-overhead explanation to a hover tooltip.
- Fixed the About menu so GitHub, bug-report, and feature-request buttons open their links correctly.

## [0.3.0] - 2026-09-02

### Added

- **Custom video bitrate:** Choose a preset or enter any video bitrate from 2 to 100 Mbps while keeping the replay memory estimate synchronized with the effective encoder value.
- **NVENC performance controls:** Select balanced or low-overhead encoding on supported NVIDIA hardware, with an optional low-cost adaptive-quantization control.

### Improved

- **Smarter automatic bitrate:** Auto now scales continuously with the actual output resolution and frame rate, accounts for H.264, HEVC, and AV1 efficiency, and favors clean fast motion without abrupt quality jumps between load tiers.
- **Honest bitrate guidance:** The settings label and help text now describe Auto as a stable, high-quality calculation in every supported interface language.
- **Fork-owned updates:** GitHub builds from this fork now check, download, and report issues against `imadraude/captail` instead of the upstream repository.
- **Lighter background operation:** Capture detection now uses an adaptive polling cadence, avoids duplicate OBS state refreshes and unchanged UI updates, caches foreground-process metadata, and reduces recording-indicator wake-ups.

## [0.2.2] - 2026-08-25

### Added

- **Per-application audio routing:** Select individual running applications, assign each one to a recording track, and route the microphone independently. Available track count follows the selected audio format.
- **Useful process discovery:** The routing window keeps selected apps first, then shows applications currently producing audio and every other running process. Real application icons, search, process counts, and live level meters make sources easy to identify.
- **Main-window source controls:** Routed applications and microphone appear as compact source buttons that can be enabled or disabled without reopening Settings.

### Improved

- **Multi-track editing:** The clip editor now displays every audio track found in a replay, uses recorded routing metadata for useful labels, expands for larger track sets, and keeps footer metadata readable.
- **Application-audio recovery:** Routed capture sources recover after process exits or restarts without stopping the replay buffer.
- **Localized guidance:** Per-application routing includes concise contextual help in every supported interface language.

### Fixed

- Prevented Instant Replay shutdown from accessing a disposed tray icon and crashing.
- Restored the tray icon and replay-status indicator reliably after Windows startup.
- Corrected swapped or misleading audio-track labels in ordinary and per-application recordings.
- Prevented search text clipping and hover-animation flicker in the application routing window.
- Prevented the last audio track and editor actions from being clipped when a replay contains several tracks.

## [0.2.1] - 2026-08-21

### Added

- **Replay player:** Open a recent replay for immediate playback without entering edit mode, then continue into Trim when needed. The player supports seeking, fullscreen playback, keyboard controls, and temporary speed feedback from `0.25×` to `2×`.
- **Replay-off game reminder:** An optional notification warns once when Captail detects a game while Instant Replay is disabled. It never starts recording without user action.
- **Unsaved-settings guard:** Editing settings reveals a compact notice with Cancel and Done actions in the title bar. Close, `Alt+F4`, and `Esc` attempts keep the settings open and use a short shake to draw attention to pending changes.

### Improved

- **Lower Game Capture idle load:** When no plausible game is running, Captail releases the replay output and keeps only a low-rate detector active. The normal replay pipeline resumes automatically after a game is detected.
- **Cleaner secondary actions:** GitHub, bug reports, feature requests, and privacy information now live in one compact About menu while the version and update state remain directly visible.
- **Replay status indicator:** Removed the dark halo and made the indicator fully opaque for consistent contrast on bright backgrounds.

### Fixed

- Prevented Game Capture from treating unrelated fullscreen applications as games while waiting for a real game candidate.
- Generated unplated Microsoft Store taskbar icons at every required target size and added package validation so placeholder-sized icons cannot ship unnoticed.
- Prevented changed settings from being lost when the settings window is dismissed accidentally.

## [0.2.0] - 2026-08-14

### Added

- **Eleven interface languages:** Captail now supports English, Russian, Ukrainian, Simplified Chinese, Spanish, Brazilian Portuguese, German, French, Japanese, Korean, and Polish. First launch follows Windows when its language is supported, otherwise it uses English. Simplified Chinese was contributed by [@zhuyouyi](https://github.com/zhuyouyi).
- **Compact language menu:** The title-bar language control now opens a focused native-name selector instead of cycling through languages.
- **Display identification:** A button beside the monitor selector briefly shows each display number on the matching screen.
- **Built-in feedback links:** The footer now opens dedicated GitHub forms for sanitized bug reports and focused feature requests.

### Improved

- **Safer automatic game detection:** Desktop mode recognizes common fullscreen game installations even when Game Capture cannot produce usable frames. Captail keeps desktop video active and associates the replay with the detected game instead of losing coverage or filing it under an unrelated application.
- **More useful bug reports:** Captail can prefill version, package channel, Windows, GPU, driver, recording settings, and a short sanitized diagnostic excerpt. The complete local log, personal paths, identifiers, device names, and recorded content are not attached.
- **Localized layout checks:** Release validation now checks all language dictionaries, formatting placeholders, critical translations, and compact UI regions.

### Fixed

- Kept bottom-corner replay indicators above the Windows taskbar and clock by positioning them inside each monitor's working area.
- Prevented the indicator's position timer from repeatedly raising it above Snipping Tool and other newer topmost system overlays.
- Made the indicator visible in Windows screenshots while keeping it excluded from Captail replay recordings.
- Returned the indicator to the chosen physical screen corner after a game is detected, while retaining taskbar-safe placement on the desktop.
- Fixed clipped long labels and incorrect free-space wording in supported translations.

## [0.1.9] - 2026-08-11

### Added

- **Replay status indicator:** An optional, click-through indicator now shows when Instant Replay is active, recovering, unavailable, or has just saved a replay. It is enabled by default and can be placed in any screen corner.

### Improved

- The indicator stays out of Captail recordings where Windows capture protection is available and keeps its animation stable instead of restarting on repeated status updates.

### Fixed

- Prevented the Windows Graphics Capture border from appearing around the desktop on Windows 10. Desktop capture now prefers DXGI there, with WGC retained as a fallback for displays DXGI cannot access; Windows 11 continues to use WGC directly.

## [0.1.8] - 2026-08-06

### Improved

- **Reliable Microsoft Store updates:** Store installations now keep application state inside package-managed locations and stop capture cleanly during package updates or removal.
- **Cleaner uninstallation:** The regular installer now closes Captail before removal and deletes its application data after uninstalling.
- **Correct per-game replay folders:** Automatic capture now files replays under the game Captail actively selected, instead of an inactive application that still had an old capture hook.

### Fixed

- Prevented Microsoft Store builds from loading libobs media DLLs into the bundled FFmpeg tools, which could stop replay metadata, waveform, and trim operations before FFmpeg started.
- Added the Microsoft Visual C++ runtime dependency required by OBS and other native components on clean Windows installations.

## [0.1.7] - 2026-08-04

### Added

- **Fast embedded replay preview:** The clip editor now uses a hardware-accelerated built-in player with responsive seeking, simultaneous playback of selected audio tracks, fullscreen viewing, keyboard controls, and a fullscreen progress bar.
- **Clear loading states:** The replay library and clip preview now distinguish loading, empty, failed, and ready states instead of appearing blank while work is in progress.
- **Microsoft Store availability:** Captail can now be installed from Microsoft Store with Store-managed updates.

### Improved

- **Safer clip editing:** Saving and overwriting show a focused progress overlay, release the preview file before replacement, retry short Windows file-lock races, and keep temporary working files out of the replay library.
- **Automatic capture switching:** Desktop mode uses Game Capture only while the hooked game is producing video and remains the foreground application. Alt-tabbing returns recording to the desktop instead of leaving a stale game frame active.

### Fixed

- Fixed black or incorrectly cropped video in the clip editor while keeping fast hardware-accelerated playback.
- Fixed native video covering overwrite confirmation and saving overlays.
- Fixed non-game applications such as Telegram being selected as the active automatic capture source.
- Fixed closing Captail leaving its installation or Portable folder locked by a running game. Game Capture hooks now load from a validated per-user cache instead of the application directory.
- Fixed misaligned loading indicators and saving text in the replay library and clip editor.

## [0.1.6] - 2026-08-03

### Added

- Multi-track replays can now mix all enabled audio tracks into one playback-friendly track when saving or overwriting a trimmed clip. Video remains untouched; only audio is re-encoded.

### Fixed

- Settings selected in the window now remain visible when the recording pipeline rejects a change, making it possible to correct the failing option without re-entering resolution, audio, and other pending choices.
- NVIDIA HEVC encoding no longer requests B-frames, fixing encoder startup on hardware such as the GeForce GTX 1080 where HEVC B-frames are unsupported.

## [0.1.5] - 2026-08-02

### Fixed

- Update downloads now close their file handles before promoting verified packages or replacing an invalid cached package, preventing Windows file-lock errors during one-click updates.

### Upgrade notes

- Captail 0.1.3 and 0.1.4 cannot complete an in-app update because the bug is inside those installed versions. Download and run the 0.1.5 Setup EXE once; later in-app updates will work normally.

## [0.1.4] - 2026-08-02

### Added

- Replay library with every saved clip in a vertically scrollable list.
- Built-in clip editor with responsive video preview, a single trim range, separate system/game and microphone track controls, save-as-copy, and confirmed overwrite.
- Clip details showing estimated trimmed size, original size, resolution, frame rate, and codec.
- Automatic desktop-to-game capture switching, plus a game-only mode that leaves the desktop out of recordings.

### Changed

- Replay cards now reveal compact actions on hover instead of using a large edge glow.
- FFmpeg and FFplay are included with Captail, so clip preview and trimming need no separate download.

### Fixed

- Clean installations now receive the FFmpeg runtime required by the editor.
- Preview rendering keeps the complete frame visible instead of showing only its upper-left region.

## [0.1.3] - 2026-07-26

### Added

- Concise English and Russian help tooltips for replay, video, and audio settings.
- Dashboard footer with repository access, current version, and one-click updates for installed and Portable builds.

### Fixed

- Watchdog checks no longer mistake an in-progress pipeline startup for a stopped recording module.
- Consecutive replay saves now advance the rolling window so later clips contain only footage recorded since the previous save.
- Save controls and notifications now show the currently available replay duration instead of always showing the configured maximum.

## [0.1.2] - 2026-07-22

### Fixed

- Settings, audio-source changes, replay toggles, and replay saves no longer block the UI while libobs starts, stops, or writes a replay.
- Failed settings changes now roll back configuration, hotkeys, autostart state, and the recording pipeline instead of leaving Captail partially configured.
- Rapid pipeline operations are serialized to prevent save, restart, watchdog recovery, and shutdown races.
- Configuration writes are atomic and automatically recover from the last valid backup after a damaged file.
- Game Capture now uses the selected game's client size for source resolution and avoids an unnecessary scene-composition pass.
- Installer upgrades request a graceful Captail shutdown and uninstall removes its startup entry.

### Changed

- Moved libobs lifetime operations to a dedicated thread and reduced synchronous disk, device, process, and log work on the UI thread.
- Hardened native library loading, OBS runtime acquisition, dependency locking, installer downloads, and GitHub Actions against dependency substitution.
- Added automated capability, codec, recovery, and high-frame-rate Game Capture diagnostics.
- Validated AV1 Game Capture at 2560x1440 and 240 unique frames per second on an NVIDIA GeForce RTX 5070, including concurrent synthetic GPU load.

## [0.1.1] - 2026-07-21

### Fixed

- Saving a replay now shows a "Saving replay…" notification immediately; the "Replay saved" confirmation replaces it once the file is on disk, instead of appearing only after the write finished.
- Overlay notifications no longer let a previous notification's fade-out dismiss a newer notification that appeared during it.

### Changed

- Renamed remaining solution, project, namespace, icon, and native bridge identifiers to Captail.
- Added a real interface screenshot and an OBS Replay Buffer comparison to the README.
- Standardized repository comments and diagnostic messages in English.

## [0.1.0] - 2026-07-20

First public preview.

### Added

- Desktop and selected-game capture through libobs.
- AV1, HEVC, and H.264 hardware encoder detection.
- 30–240 FPS and source/720p/1080p/1440p/4K output options.
- System or game audio, microphone, volume controls, boost, and separate tracks.
- Rolling replay buffer with duration and size limits.
- Save and replay-toggle global hotkeys.
- Watchdog recovery for stopped or stalled recording pipelines.
- Tray controls, double-click restore, startup integration, and overlay notifications.
- English interface with live Russian language switching.

### Known limitations

- First public preview; hardware-specific bugs are expected.
- NVIDIA RTX 40 and RTX 50 series are tested. Other GPU families need public testing.
- Release binaries are not Authenticode-signed.
