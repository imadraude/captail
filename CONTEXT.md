# Captail

Captail keeps a recent window of gameplay or desktop media available for an on-demand save while making recording state and failures visible.

## Language

**Instant Replay**:
The user-facing capability that continuously retains recent media and saves that retained window on request.
_Avoid_: Recording, capture buffer

**Replay runtime**:
The active lifetime of Instant Replay, including its requested configuration, current state, recovery, and save operations.
_Avoid_: OBS lifecycle, recording service

**Replay pipeline**:
The capture, encoding, audio-routing, and rolling-buffer resources used by a running Replay runtime.
_Avoid_: OBS instance, recorder

**Replay save**:
A request that commits the currently retained media to a durable replay file.
_Avoid_: Recording export, buffer dump
